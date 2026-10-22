using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using Hermes.Core;

namespace Hermes.Desktop;

internal static class OfficeAddinNative
{
    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr window);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    }
    internal static readonly Dictionary<string, string> Applications = new(StringComparer.OrdinalIgnoreCase)
    { ["EXCEL"] = "Excel", ["WINWORD"] = "Word", ["POWERPNT"] = "PowerPoint", ["OUTLOOK"] = "Outlook", ["MSACCESS"] = "Access", ["VISIO"] = "Visio", ["WINPROJ"] = "MSProject", ["MSPUB"] = "Publisher" };

    [DllImport("oleaut32.dll", PreserveSig = false)] private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr parameter);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);

    internal static bool CheckWindowEnumeration() => EnumWindows((window, _) => { EnumChildWindows(window, (_, _) => true, IntPtr.Zero); return true; }, IntPtr.Zero);

    internal static object? Get(object target, string property) => target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
    private static void Set(object target, string property, object value) => target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, [value]);
    private static object? Item(object target, int index)
    {
        try { return target.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, target, [index]); }
        catch (Exception ex) when (ex is MissingMethodException || ex.GetBaseException().HResult == unchecked((int)0x80020003))
        { return target.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, target, [index]); }
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    private static bool Matches(object app, string application, int pid)
    {
        object? explorer = null;
        try
        {
            object target = app;
            if (application == "Outlook") { explorer = app.GetType().InvokeMember("ActiveExplorer", BindingFlags.InvokeMethod, null, app, null); if (explorer == null) return false; target = explorer; }
            if (application == "Word") { explorer = Get(app, "ActiveWindow"); if (explorer == null) return false; target = explorer; }
            IntPtr hwnd;
            try { hwnd = new IntPtr(Convert.ToInt64(Get(target, application == "Access" ? "hWndAccessApp" : application == "Visio" ? "WindowHandle32" : "Hwnd"))); }
            catch { if (target is not IOleWindow window || window.GetWindow(out hwnd) != 0) return false; }
            GetWindowThreadProcessId(hwnd, out var actual); return actual == pid;
        }
        catch { return false; }
        finally { Release(explorer); }
    }

    private static object? FindApplication(string application, int pid)
    {
        // NativeOM also works before a workbook/document has registered in the ROT.
        object? found = null;
        bool Inspect(IntPtr handle, IntPtr _)
        {
            if (found != null) return false;
            GetWindowThreadProcessId(handle, out var actual); if (actual != pid) return true;
            object? native = null; object? app = null;
            try
            {
                var iid = new Guid("00020400-0000-0000-C000-000000000046");
                if (AccessibleObjectFromWindow(handle, 0xFFFFFFF0, ref iid, out native) != 0 || native == null) return true;
                app = Get(native, "Application");
                if (app != null && Matches(app, application, pid)) { found = app; app = null; }
            }
            catch { }
            finally { Release(app); Release(native); }
            return found == null;
        }
        EnumWindows((handle, _) => { GetWindowThreadProcessId(handle, out var actual); if (actual == pid) { Inspect(handle, IntPtr.Zero); if (found == null) EnumChildWindows(handle, Inspect, IntPtr.Zero); } return found == null; }, IntPtr.Zero);
        if (found != null) return found;

        var type = Type.GetTypeFromProgID(application + ".Application");
        if (type != null)
        {
            object? active = null;
            try { var id = type.GUID; GetActiveObject(ref id, IntPtr.Zero, out active); if (Matches(active, application, pid)) { found = active; active = null; } }
            catch (COMException) { }
            finally { Release(active); }
        }
        if (found != null) return found;
        if (GetRunningObjectTable(0, out var table) != 0) return null;
        IEnumMoniker? enumeration = null;
        try
        {
            table.EnumRunning(out enumeration); var monikers = new IMoniker[1];
            while (enumeration.Next(1, monikers, IntPtr.Zero) == 0)
            {
                object? document = null; object? app = null;
                try { table.GetObject(monikers[0], out document); if (document != null) app = Get(document, "Application"); if (app != null && Matches(app, application, pid)) { found = app; app = null; break; } }
                catch { }
                finally { Release(app); Release(document); Release(monikers[0]); }
            }
        }
        finally { Release(enumeration); Release(table); }
        return found;
    }

    private sealed class Addin(object value, string kind, string name, string property) : IOfficeAddin
    {
        public string Kind => kind;
        public string Name => name;
        public bool Loaded => Convert.ToBoolean(Get(value, property));
        public void Load() => Set(value, property, true);
    }

    private sealed class ExcelAddin(object app, object value, string name) : IOfficeAddin
    {
        private bool registered;
        public string Kind => "Excel XLL / XLA / XLAM";
        public string Name => name;
        public bool Loaded
        {
            get { if (registered) return true; try { return Convert.ToBoolean(Get(value, "IsOpen")); } catch { return Convert.ToBoolean(Get(value, "Installed")); } }
        }
        public void Load()
        {
            try { Set(value, "Installed", true); }
            catch (Exception ex) when (ex.GetBaseException() is COMException) { /* Some Excel versions reject Installed; try the documented direct load API. */ }
            if (Loaded) return;
            var path = Convert.ToString(Get(value, "FullName")) ?? throw new IOException("Excel 추가 기능 경로가 없습니다.");
            if (Path.GetExtension(path).Equals(".xll", StringComparison.OrdinalIgnoreCase))
            {
                registered = Convert.ToBoolean(app.GetType().InvokeMember("RegisterXLL", BindingFlags.InvokeMethod, null, app, [path]));
                if (!registered) throw new IOException("Excel RegisterXLL이 로드를 거부했습니다: " + name);
            }
            else
            {
                object? workbooks = null; object? workbook = null;
                try { workbooks = Get(app, "Workbooks"); if (workbooks == null) throw new IOException("Excel 통합 문서 목록에 연결하지 못했습니다."); workbook = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod | BindingFlags.OptionalParamBinding, null, workbooks, [path]); }
                finally { Release(workbook); Release(workbooks); }
            }
        }
    }

    internal static List<OfficeAddinResult> LoadApplication(object app, string application, int pid)
    {
        var results = new List<OfficeAddinResult>();
        void LoadCollection(string property, string kind, string loaded)
        {
            object? collection = null;
            try
            {
                collection = Get(app, property); if (collection == null) return;
                var count = Convert.ToInt32(Get(collection, "Count"));
                for (var index = 1; index <= count; index++)
                {
                    object? item = null;
                    try
                    {
                        item = Item(collection, index); if (item == null) continue;
                        string name;
                        try { name = Convert.ToString(Get(item, property == "COMAddIns" ? "ProgId" : "Name")) ?? $"{kind} #{index}"; }
                        catch { name = $"{kind} #{index}"; }
                        IOfficeAddin addin = application == "Excel" && property == "AddIns" ? new ExcelAddin(app, item, name) : new Addin(item, kind, name, loaded);
                        results.AddRange(OfficeAddins.Load(application, pid, [addin]));
                    }
                    catch (Exception ex) { results.Add(new(application, pid, kind, $"#{index}", "실패", Safety.Redact(ex.GetBaseException().Message))); }
                    finally { Release(item); }
                }
            }
            catch (Exception ex) { results.Add(new(application, pid, kind, property, "실패", Safety.Redact(ex.GetBaseException().Message))); }
            finally { Release(collection); }
        }
        LoadCollection("COMAddIns", "COM / VSTO", "Connect");
        if (application is "Excel" or "Word") LoadCollection("AddIns", application == "Excel" ? "Excel XLL / XLA / XLAM" : "Word 템플릿", "Installed");
        if (application == "PowerPoint") LoadCollection("AddIns", "PowerPoint PPA / PPAM", "Loaded");
        return results;
    }

    internal static int Run(string application, int pid, long startTicks, string report)
    {
        var results = new List<OfficeAddinResult>(); object? app = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks != startTicks || !Applications.TryGetValue(process.ProcessName, out var actual) || actual != application) return 3;
            app = FindApplication(application, pid);
            if (app == null) return 2; // Office is still starting or busy; monitor retries later.
            results.AddRange(LoadApplication(app, application, pid));
            File.WriteAllText(report, JsonSerializer.Serialize(results)); return 0;
        }
        catch (Exception ex) { results.Add(new(application, pid, "Office", "연결", "실패", Safety.Redact(ex.GetBaseException().Message))); File.WriteAllText(report, JsonSerializer.Serialize(results)); return 1; }
        finally { Release(app); }
    }
}
