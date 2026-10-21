using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal Task RunLearningPanelChecks(string data)
    {
        var root = Path.Combine(data, "learning-project"); Directory.CreateDirectory(root); var project = desk.Register(root); Projects.SelectedItem = project;
        var scope = ExecutionMemory.Scope(root); var proof = desk.Memory.Record(scope, "fixture", "success", "업무 보고서 작성", "command", "Write-Output VALIDATED_REPORT", true, "VALIDATED_REPORT");
        var tab = InspectorTabs.Items.OfType<TabItem>().Single(t => t.Header.ToString() == "업무·실행 RAG"); InspectorTabs.SelectedItem = tab;
        if (learningMethods.Items.Count != 1) throw new IOException("RAG panel did not load project methods");
        learningMethods.SelectedIndex = 0;
        if (!learningDetail.Text.Contains("VALIDATED_REPORT")) throw new IOException("RAG panel did not show executed method");
        var guide = desk.Memory.Search(scope, "보고서").Single();
        desk.Memory.Improve(scope, guide.Id, proof.Id, "보고서를 작성한 뒤 출력 결과를 검증한다.");
        if (!new ExecutionMemory(Path.Combine(desk.DataDirectory, "knowledge")).Search(scope, "보고서").Single().Notes.Contains("검증")) throw new IOException("RAG panel store does not persist revisions");
        if (!File.Exists(desk.Memory.DatabasePath) || !desk.Memory.Context(scope, "보고서").Contains("VALIDATED_REPORT")) throw new IOException("Local DB retrieval missing");
        File.WriteAllText(Path.Combine(data, "learning-panel-report.json"), JsonSerializer.Serialize(new { passed = true, version = AppVersion.Current, checks = 4, database = desk.Memory.DatabasePath, modelCalls = 0 }));
        return Task.CompletedTask;
    }
}
