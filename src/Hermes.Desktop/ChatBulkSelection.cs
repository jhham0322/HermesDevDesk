using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private readonly HashSet<string> selectedChatIds = [];
    private readonly List<(string Id, CheckBox Box)> chatSelectionRows = [];
    private bool synchronizingChatChecks;

    private bool CanDeleteChat(WorkItem task) => !DeskController.IsRunning(task) &&
        (!browserLocks.TryGetValue(task.Id, out var gate) || gate.CurrentCount > 0);

    private IEnumerable<WorkItem> BulkChatCandidates() =>
        desk.Search(Search.Text, null, ArchivedOnly.IsChecked == true).Where(CanDeleteChat);

    private CheckBox BuildChatSelectionRow(WorkItem task)
    {
        var box = new CheckBox
        {
            Content = new TextBlock { Text = ShortTitle(task.Title), TextTrimming = TextTrimming.CharacterEllipsis },
            Margin = new Thickness(20, 5, 8, 5),
            ToolTip = task.Title + " · " + task.Status,
            IsEnabled = CanDeleteChat(task),
            IsChecked = selectedChatIds.Contains(task.Id)
        };
        System.Windows.Automation.AutomationProperties.SetName(box, task.Title);
        box.Checked += (_, _) => ToggleChatSelection(task.Id, true);
        box.Unchecked += (_, _) => ToggleChatSelection(task.Id, false);
        chatSelectionRows.Add((task.Id, box));
        return box;
    }

    private void ToggleChatSelection(string id, bool selected)
    {
        if (synchronizingChatChecks) return;
        if (selected) selectedChatIds.Add(id);
        else selectedChatIds.Remove(id);
        UpdateChatSelectionControls();
    }

    private void UpdateChatSelectionControls()
    {
        synchronizingChatChecks = true;
        try
        {
            foreach (var (id, box) in chatSelectionRows) box.IsChecked = selectedChatIds.Contains(id);
            DeleteSelectedChats.IsEnabled = selectedChatIds.Count > 0;
            DeleteSelectedChats.Content = selectedChatIds.Count > 0 ? $"삭제 ({selectedChatIds.Count})" : "삭제";
        }
        finally { synchronizingChatChecks = false; }
    }

    private void ChatSelectionMode_Click(object sender, RoutedEventArgs e)
    {
        selectedChatIds.Clear();
        ChatBulkActions.Visibility = ChatSelectionMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        sidebarStamp = "";
        RenderSidebar();
    }

    private void SelectAllChats_Click(object sender, RoutedEventArgs e)
    {
        selectedChatIds.UnionWith(BulkChatCandidates().Select(task => task.Id));
        UpdateChatSelectionControls();
    }

    private void ClearChatSelection_Click(object sender, RoutedEventArgs e)
    {
        selectedChatIds.Clear();
        UpdateChatSelectionControls();
    }

    private void DeleteSelectedChats_Click(object sender, RoutedEventArgs e) =>
        _ = Guard(() =>
        {
            DeleteChatBatch(desk.State.Tasks.Where(task => selectedChatIds.Contains(task.Id)).ToArray(), true);
            return Task.CompletedTask;
        });

    private async Task VerifyChatSelectionAndGit(string data, Action<bool, string> check)
    {
        var root = Path.Combine(data, "registered-git");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "소스.txt"), "initial source");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored.txt\n");
        File.WriteAllText(Path.Combine(root, "ignored.txt"), "ignored");
        File.WriteAllText(Path.Combine(root, ".env"), "fixture only");
        var project = await desk.RegisterWithGitAsync(root);
        var paths = (await GitRunner.Run(root, "ls-tree", "-r", "--name-only", "-z", "HEAD")).Output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        check(paths.ToHashSet().SetEquals([".gitignore", "소스.txt"]), "registration creates a local initial commit and respects exclusions");
        var before = (await GitRunner.Run(root, "rev-parse", "HEAD")).Output;
        File.WriteAllText(Path.Combine(root, "소스.txt"), "uncommitted change");
        check(await desk.RegisterWithGitAsync(root) == project &&
            (await GitRunner.Run(root, "rev-parse", "HEAD")).Output == before, "registration preserves a project's existing Git history");

        var first = desk.NewTask(project, "selection-fixture first");
        var second = desk.NewTask(project, "selection-fixture second");
        var running = desk.NewTask(project, "selection-fixture running");
        running.Status = "실행 중";
        Search.Text = "selection-fixture";
        ChatSelectionMode.IsChecked = true;
        ChatSelectionMode_Click(this, new());
        SelectAllChats_Click(this, new());
        check(selectedChatIds.SetEquals([first.Id, second.Id]) &&
            chatSelectionRows.Where(row => row.Id == running.Id).All(row => !row.Box.IsEnabled),
            "select all selects filtered idle chats and skips running chats");
        chatSelectionRows.First(row => row.Id == first.Id).Box.IsChecked = false;
        check(selectedChatIds.SetEquals([second.Id]), "checkbox supports partial selection");
        DeleteChatBatch(desk.State.Tasks.Where(task => selectedChatIds.Contains(task.Id)).ToArray(), false);
        check(!desk.Store.Load().Tasks.Any(task => task.Id == second.Id) &&
            desk.State.Tasks.Contains(first) && desk.State.Tasks.Contains(running), "bulk delete persists only selected chats");
        SelectAllChats_Click(this, new());
        DeleteChatBatch(desk.State.Tasks.Where(task => selectedChatIds.Contains(task.Id)).ToArray(), false);
        check(selectedChatIds.Count == 0 && desk.State.Tasks.Contains(running) &&
            File.ReadAllText(Path.Combine(root, "소스.txt")) == "uncommitted change", "select all preserves running chat and project files");
        running.Status = "중단됨";
        ChatSelectionMode.IsChecked = false;
        ChatSelectionMode_Click(this, new());
        Search.Clear();
    }
}