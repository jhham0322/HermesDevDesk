using System.Text.Json;
using Hermes.Core;
internal static class StatePersistenceTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        static AppState State(string value) => new() { Desktop = new() { Drafts = new() { ["owned"] = value } } };
        static string Value(string path) => JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))!.Desktop.Drafts["owned"];
        var transient = Path.Combine(root, "transient"); var store = new StateStore(transient); store.Save(State("old")); var path = Path.Combine(transient, "state.json");
        var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var saving = Task.Run(() => store.Save(State("new")));
        await Task.Delay(75); handle.Dispose(); await saving.WaitAsync(TimeSpan.FromSeconds(4));
        check(Value(path) == "new" && Value(path + ".backup") == "old", "state replacement recovers from a short-lived reader lock without losing backup");
        File.WriteAllText(path + ".new", "KEEP_INTERRUPTED_SAVE"); store.Save(State("next"));
        check(File.ReadAllText(path + ".new") == "KEEP_INTERRUPTED_SAVE", "unique snapshot does not overwrite an interrupted legacy save");
        var oldBytes = File.ReadAllBytes(path); var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        try
        {
            try { store.Save(State("readonly-pending")); throw new Exception("Read-only save unexpectedly succeeded"); }
            catch (HermesException ex) { check(ex.InnerException is UnauthorizedAccessException && ex.Message.Contains("보존"), "read-only state reports access failure without bypassing attributes"); }
            check(File.ReadAllBytes(path).SequenceEqual(oldBytes) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "read-only failure preserves original bytes and attribute");
            check(Directory.GetFiles(transient, "state.json.*.pending").Any(p => Value(p) == "readonly-pending"), "read-only failure preserves complete recoverable latest snapshot");
        }
        finally { File.SetAttributes(path, attributes); }
        var conflict = Path.Combine(root, "directory"); var dirStore = new StateStore(conflict); Directory.CreateDirectory(Path.Combine(conflict,"state.json"));
        try { dirStore.Save(State("directory-pending")); throw new Exception("Directory save unexpectedly succeeded"); }
        catch(HermesException ex) { check(ex.InnerException is IOException && Directory.Exists(Path.Combine(conflict,"state.json")), "directory/file conflict is preserved and reported without replacement"); }
        var locked = Path.Combine(root,"locked"); var lockedStore = new StateStore(locked); lockedStore.Save(State("locked-old")); var lockedPath=Path.Combine(locked,"state.json");
        using(var permanent=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try { lockedStore.Save(State("locked-new")); throw new Exception("Locked save unexpectedly succeeded"); }
            catch(HermesException ex) { check(ex.InnerException is IOException or UnauthorizedAccessException, "persistent file lock returns original I/O cause after bounded retry"); }
            check(Value(lockedPath)=="locked-old" && Directory.GetFiles(locked,"state.json.*.pending").Any(p=>Value(p)=="locked-new"), "persistent lock keeps old state and complete new snapshot");
        }
        var parallel=Path.Combine(root,"parallel");var first=new StateStore(parallel);var second=new StateStore(parallel);
        await Task.WhenAll(Task.Run(()=>{for(int i=0;i<12;i++)first.Save(State("A"+i));}),Task.Run(()=>{for(int i=0;i<12;i++)second.Save(State("B"+i));}));
        check(first.SaveCount+second.SaveCount==24 && new StateStore(parallel).Load().Desktop.Drafts.ContainsKey("owned"), "independent StateStore instances serialize the same canonical file path");
        check(Directory.GetFiles(parallel,"*.pending").Length==0 && File.Exists(Path.Combine(parallel,"state.json.backup")), "successful concurrent saves leave valid backup and no staging files");
    }
}
