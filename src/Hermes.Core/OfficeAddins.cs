namespace Hermes.Core;

public sealed record OfficeAddinResult(string Application, int ProcessId, string Kind, string Name, string Status, string Detail);
public interface IOfficeAddin
{
    string Kind { get; }
    string Name { get; }
    bool Loaded { get; }
    void Load();
}
public static class OfficeAddins
{
    // One broken extension must not prevent the remaining extensions from loading.
    // Never repeatedly reconnect a failing extension in the same Office session.
    public static List<OfficeAddinResult> Load(string application, int processId, IEnumerable<IOfficeAddin> addins)
    {
        var results = new List<OfficeAddinResult>();
        foreach (var addin in addins)
        {
            try
            {
                if (addin.Loaded) { results.Add(new(application, processId, addin.Kind, addin.Name, "이미 로드됨", "")); continue; }
                addin.Load();
                var loaded = addin.Loaded;
                results.Add(new(application, processId, addin.Kind, addin.Name, loaded ? "로드됨" : "실패", loaded ? "" : "Office가 로드 상태를 확인해 주지 않았습니다."));
            }
            catch (Exception ex) { results.Add(new(application, processId, addin.Kind, addin.Name, "실패", Safety.Redact(ex.GetBaseException().Message))); }
        }
        return results;
    }
}
