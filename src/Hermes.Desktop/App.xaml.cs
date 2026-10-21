using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hermes.Desktop;
public partial class App : Application
{
    private bool reportingError;
    internal Exception? ExpectedUiTestException { get; set; }
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Hermes.Core.AppLog.Write(e.ExceptionObject as Exception ?? new Exception("Unhandled runtime exception"), "runtime-unhandled");
        TaskScheduler.UnobservedTaskException += (_, e) => { Hermes.Core.AppLog.Write(e.Exception, "unobserved-task"); e.SetObserved(); };
        DispatcherUnhandledException += (_, e) =>
        {
            var path = Hermes.Core.AppLog.Write(e.Exception, "dispatcher-unhandled"); e.Handled = true;
            if (ReferenceEquals(e.Exception, ExpectedUiTestException)) { ExpectedUiTestException = null; return; }
            if (e.Exception is OutOfMemoryException or AccessViolationException) { Shutdown(1); return; }
            if (reportingError) return;
            reportingError = true;
            try { MessageBox.Show("작업 중 오류가 발생했습니다. 앱은 계속 사용할 수 있습니다.\n\n" + Hermes.Core.Safety.Redact(e.Exception.Message) + "\n\n오류 로그: " + path, "Hermes DevDesk 오류"); }
            finally { reportingError = false; }
        };
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataIndex = Array.IndexOf(e.Args, "--data-dir");
            var data = dataIndex >= 0 && e.Args.Length > dataIndex + 1 ? e.Args[dataIndex + 1] : Hermes.Core.AppProfiles.Resolve(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesDevDesk"));
            Hermes.Core.AppLog.Configure(data);
            var window = new MainWindow(data); MainWindow = window;
            if (e.Args.Contains("--local-browser-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunLocalBrowserChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "local-browser-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--learning-panel-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunLearningPanelChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "learning-panel-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--browser-experience-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunBrowserExperienceChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "browser-experience-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--conversation-performance-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunConversationPerformanceChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "conversation-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--window-state-report"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await Task.Delay(300); window.WriteWindowPlacementReport(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { Hermes.Core.AppLog.Write(ex, "window-report"); Shutdown(1); } }); }
            else if (e.Args.Contains("--window-state-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunWindowPersistenceChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "window-state-error.txt"), ex.ToString()); Shutdown(1); } }); }
            else if (e.Args.Contains("--relay-send-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunRelaySendChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "relay-send-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--simple-settings-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunSimpleSettingsChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "simple-settings-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--provider-settings-smoke"))
            { window.Show(); window.Dispatcher.BeginInvoke(async () => { try { await window.RunProviderSettingsChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "provider-settings-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } }); }
            else if (e.Args.Contains("--maximize-smoke"))
            { window.Show();window.Dispatcher.BeginInvoke(async()=>{try{await window.RunMaximizeChecks(data);await window.ShutdownController();Shutdown(0);}catch{await window.ShutdownController();Shutdown(1);}}); }
            else if (e.Args.Contains("--http-settings-smoke"))
            {
                window.Show();window.Dispatcher.BeginInvoke(async()=>
                { try { var settings=new SettingsWindow(window.HttpTestController);settings.Show();await settings.RunHttpSettingsChecks(data);settings.Close();await window.ShutdownController();Shutdown(0); } catch(Exception ex) { File.WriteAllText(Path.Combine(data,"http-settings-error.txt"),ex.GetType().Name);await window.ShutdownController();Shutdown(1); } });
            }
            else if (e.Args.Contains("--computer-panel-smoke"))
            {
                window.Show(); window.Dispatcher.BeginInvoke(async () =>
                { try { await window.RunComputerPanelChecks(data); await window.ShutdownController(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(data, "computer-panel-error.txt"), ex.ToString()); await window.ShutdownController(); Shutdown(1); } });
            }
            else if (e.Args.Contains("--ui-smoke"))
            {
                window.Width = 1280; window.Height = 1392;
                window.Show();
                window.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var checks = await window.RunProgrammaticUiChecks(data);
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(data, "ui-smoke.png")); encoder.Save(stream);
                        File.WriteAllText(Path.Combine(data, "ui-smoke.txt"), "WPF window loaded on .NET 10.\n" + checks);
                        await window.ShutdownController(); Shutdown(0);
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(data, "ui-smoke-error.txt"), ex.ToString()); Shutdown(1); }
                });
            }
            else window.Show();
        }
        catch (Exception ex) { var path = Hermes.Core.AppLog.Write(ex, "startup"); MessageBox.Show(Hermes.Core.Safety.Redact(ex.Message) + "\n\n오류 로그: " + path, "Hermes DevDesk 시작 실패"); Shutdown(1); }
    }
}
