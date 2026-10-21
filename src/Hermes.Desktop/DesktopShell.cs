using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Hermes.Core;
using Microsoft.Win32;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private string? draftTaskId;
    private bool switchingDraft;
    private string conversationStamp = "";
    private readonly List<string> navigation = [];
    private int navigationIndex = -1;
    private bool navigating;
    private readonly HashSet<string> collapsedProjects = [];
    private readonly System.Windows.Threading.DispatcherTimer draftSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly System.Windows.Threading.DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string sidebarStamp = "";
    private void QueueRefresh()
    {
        if (closing) return;
        if (!refreshTimer.IsEnabled) refreshTimer.Start();
    }

    private void InitializeShell()
    {
        ApplyTheme(); SetSidebar(desk.State.Desktop.SidebarVisible); SetInspector(desk.State.Desktop.InspectorVisible);
        EnterSendMenu.IsChecked = desk.State.Desktop.EnterToSend;
        Effort.ItemsSource = new[] { "기본" }; Effort.SelectedIndex = 0;
        InitializeBrowserPanel();
        InitializeComputerPanel();
        InitializeLearningPanel();
        GitRunner.Executable = desk.State.Desktop.GitExecutable;
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); Refresh(); };
        InspectorSplitter.DragCompleted += (_, _) => SaveInspectorWidth();
        InspectorTabs.SelectedIndex = Math.Clamp(desk.State.Desktop.InspectorTabIndex, 0, InspectorTabs.Items.Count - 1);
        InspectorTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, InspectorTabs) && InspectorTabs.SelectedIndex >= 0) { desk.State.Desktop.InspectorTabIndex = InspectorTabs.SelectedIndex; desk.Store.Save(desk.State); } };
        draftSaveTimer.Tick += (_, _) => { draftSaveTimer.Stop(); desk.Store.Save(desk.State); };
        Closed += (_, _) => { draftSaveTimer.Stop(); refreshTimer.Stop(); };
    }
    private Brush Brush(string key) => (Brush)FindResource(key);
    private UIElement MakeIcon(string name, double size = 19) => new System.Windows.Shapes.Path { Data = (Geometry)FindResource("Icon." + name), Fill = Brush("Ink"), Width = size, Height = size, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) };
    private void ApplyTheme()
    {
        var keys = new[] { "Canvas", "Panel", "Rail", "Ink", "Muted", "Line", "Hover", "Selected", "Accent", "Code" };
        var light = new[] { "#FFFFFF", "#FAF9F8", "#F2F1F0", "#202020", "#777777", "#E8E7E6", "#F1F0EF", "#EEEDEB", "#222222", "#F5F5F5" };
        var dark = new[] { "#191919", "#202020", "#252525", "#ECECEC", "#A1A1A1", "#353535", "#2B2B2B", "#363636", "#ECECEC", "#242424" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString((desk.State.Desktop.DarkTheme ? dark : light)[i]));
        conversationStamp = "";
        sidebarStamp = "";
        renderedConversation = null; renderedEntries.Clear();
    }
    private void Theme_Click(object sender, RoutedEventArgs e) { desk.State.Desktop.DarkTheme = !desk.State.Desktop.DarkTheme; ApplyTheme(); desk.Save(); }
    private void SetSidebar(bool show) { SidebarPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed; SidebarColumn.Width = new GridLength(show ? 358 : 0); }
    private void Sidebar_Click(object sender, RoutedEventArgs e) { desk.State.Desktop.SidebarVisible = !desk.State.Desktop.SidebarVisible; SetSidebar(desk.State.Desktop.SidebarVisible); desk.Save(); }
    private void SetInspector(bool show)
    {
        var alreadyOpen = InspectorPane.Visibility == Visibility.Visible && InspectorColumn.Width.Value > 0;
        InspectorPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed; InspectorSplitColumn.Width = new GridLength(show ? 4 : 0);
        InspectorColumn.MinWidth = show ? 200 : 0;
        if (!show) InspectorColumn.Width = new GridLength(0);
        else if (!alreadyOpen) InspectorColumn.Width = new GridLength(double.IsFinite(desk.State.Desktop.InspectorWidth) ? Math.Max(200, desk.State.Desktop.InspectorWidth) : 400);
        if (show && ActualWidth < 1400) SetSidebar(false);
        else SetSidebar(desk.State.Desktop.SidebarVisible);
    }
    private void SaveInspectorWidth()
    {
        if (InspectorPane.Visibility != Visibility.Visible || !double.IsFinite(InspectorColumn.ActualWidth) || InspectorColumn.ActualWidth < 200) return;
        desk.State.Desktop.InspectorWidth = InspectorColumn.ActualWidth;
        ContentColumn.Width = new GridLength(1, GridUnitType.Star);
        desk.Store.Save(desk.State);
    }
    private void Inspector_Click(object sender, RoutedEventArgs e) { desk.State.Desktop.InspectorVisible = !desk.State.Desktop.InspectorVisible; SetInspector(desk.State.Desktop.InspectorVisible); desk.Save(); }
    private void OpenPanel(int index) { desk.State.Desktop.InspectorVisible = true; SetInspector(true); InspectorTabs.SelectedIndex = index; desk.Save(); }
    private void ReviewPanel_Click(object sender, RoutedEventArgs e) { OpenPanel(1); if (CurrentProject != null) _ = Guard(RefreshGitChanges); }
    private void ApprovalPanel_Click(object sender, RoutedEventArgs e) => OpenPanel(3);
    private void Home_Click(object sender, RoutedEventArgs e) { Search.Clear(); ArchivedOnly.IsChecked = false; ConversationScroll.ScrollToTop(); }
    private void SearchFocus_Click(object sender, RoutedEventArgs e) { SetSidebar(true); desk.State.Desktop.SidebarVisible = true; SearchPanel.Visibility = Visibility.Visible; Search.Focus(); }
    private void ArchiveView_Click(object sender, RoutedEventArgs e) { SearchPanel.Visibility = Visibility.Visible; ArchivedOnly.IsChecked = !ArchivedOnly.IsChecked; }
    private void EnterSend_Click(object sender, RoutedEventArgs e) { desk.State.Desktop.EnterToSend = EnterSendMenu.IsChecked; desk.Save(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        for (DependencyObject? element = e.OriginalSource as DependencyObject; element != null && element != sender; element = VisualTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Primitives.ButtonBase or MenuItem or Menu or TextBox) return;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        e.Handled = true;
    }
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void UpdateShellState()
    {
        if (desk == null) return;
        var running = CurrentTask != null && DeskController.IsRunning(CurrentTask);
        StartButton.IsEnabled = CanSubmit();
        browserEnable.IsChecked = CurrentProject?.BrowserEnabled == true;
        computerEnable.IsChecked = CurrentProject?.ComputerEnabled == true;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StartButton.ToolTip = CurrentTask?.Status == "실행 중" ? "실행 중 추가 지시 보내기" : "전송 · Enter";
        var approvals = desk.Approvals.Count(a => a.Params.Text("threadId") == CurrentTask?.ThreadId);
        ApprovalBadge.Visibility = approvals > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApprovalBadge.Content = $"승인 {approvals}개 검토";
        var changed = CurrentTask?.ChangedFiles.Count ?? 0;
        ChangeSummary.Visibility = changed > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChangeSummary.Content = $"{changed}개 파일 변경됨 · 검토";
    }
    private void Model_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (desk == null || Effort == null) return;
        var model = Model.SelectedItem as string ?? "";
        var options = new List<string> { "기본" }; if (desk.ModelEfforts.TryGetValue(model, out var supported)) options.AddRange(supported);
        Effort.ItemsSource = options; Effort.SelectedIndex = 0;
    }
    private void Draft_Changed(object sender, TextChangedEventArgs e)
    {
        if (PromptPlaceholder != null) PromptPlaceholder.Visibility = Prompt.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (desk == null || switchingDraft || draftTaskId == null) return;
        desk.State.Desktop.Drafts[draftTaskId] = Safety.Redact(Prompt.Text); draftSaveTimer.Stop(); draftSaveTimer.Start();
    }
    private readonly Dictionary<string, List<ProjectImage>> imageDrafts = [];
    private readonly Dictionary<string, List<ProjectFileAttachment>> fileDrafts = [];
    private readonly Dictionary<string, List<ProjectSkill>> skillDrafts = [];
    private readonly Dictionary<string, string> modelDrafts = [];
    private void SwitchDraft()
    {
        var id = CurrentTask?.Id;
        if (id == draftTaskId) return;
        if (draftTaskId != null)
        {
            desk.State.Desktop.Drafts[draftTaskId] = Safety.Redact(Prompt.Text);
            imageDrafts[draftTaskId] = pendingImages.ToList(); fileDrafts[draftTaskId] = pendingFiles.ToList(); skillDrafts[draftTaskId] = pendingSkills.ToList();
            modelDrafts[draftTaskId] = Model.SelectedItem as string ?? "";
        }
        pendingImages.Clear(); pendingFiles.Clear(); pendingSkills.Clear();
        if (id != null && imageDrafts.TryGetValue(id, out var images)) pendingImages.AddRange(images);
        if (id != null && fileDrafts.TryGetValue(id, out var files)) pendingFiles.AddRange(files);
        if (id != null && skillDrafts.TryGetValue(id, out var skills)) pendingSkills.AddRange(skills);
        RefreshAttachments(); SkillHint.Text = string.Join(", ", pendingSkills.Select(s => s.Name));
        var selectedModel = id != null && modelDrafts.TryGetValue(id, out var savedModel) ? savedModel : CurrentTask?.Model;
        if (selectedModel != null && Model.Items.Contains(selectedModel)) Model.SelectedItem = selectedModel;
        draftTaskId = id; switchingDraft = true;
        Prompt.Text = id != null && desk.State.Desktop.Drafts.TryGetValue(id, out var text) ? text : "";
        switchingDraft = false;
        if (id != null && !navigating && (navigationIndex < 0 || navigation[navigationIndex] != id))
        {
            if (navigationIndex + 1 < navigation.Count) navigation.RemoveRange(navigationIndex + 1, navigation.Count - navigationIndex - 1);
            navigation.Add(id); navigationIndex = navigation.Count - 1;
        }
    }
    private void Navigate(int offset)
    {
        var index = navigationIndex + offset; if (index < 0 || index >= navigation.Count) return;
        var task = desk.State.Tasks.FirstOrDefault(t => t.Id == navigation[index]); if (task == null) return;
        navigationIndex = index; navigating = true; try { SelectTask(task); } finally { navigating = false; }
    }
    private void Back_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Forward_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void SelectTask(WorkItem task)
    {
        closedChats.Remove(task.Id);
        ArchivedOnly.IsChecked = task.Archived; Search.Clear();
        Projects.SelectedItem = desk.State.Projects.First(p => p.Id == task.ProjectId); Refresh(); Tasks.SelectedItem = task;
        RenderTask(); RenderSidebar(); Prompt.Focus();
    }
    private void RenderSidebar()
    {
        var stamp = Search.Text + "|" + ArchivedOnly.IsChecked + "|" + CurrentTask?.Id + "|" + string.Join('|', desk.State.Projects.Select(p => p.Id + p.Name)) + "|" + string.Join('|', desk.State.Tasks.Select(t => t.Id + t.Title + t.Status + t.Pinned + t.Archived));
        if (stamp == sidebarStamp) return; sidebarStamp = stamp;
        SidebarGroups.Children.Clear();
        IEnumerable<WorkItem> Visible(ProjectEntry? project = null) => desk.Search(Search.Text, project?.Id, ArchivedOnly.IsChecked == true);
        var pins = Visible().Where(t => t.Pinned).ToList();
        if (pins.Count > 0) { SidebarGroups.Children.Add(new TextBlock { Text = "고정됨", Foreground = Brush("Muted"), FontSize = 12, Margin = new Thickness(10, 8, 0, 6) }); foreach (var pin in pins) SidebarGroups.Children.Add(TaskButton(pin)); }
        foreach (var project in desk.State.Projects)
        {
            var tasks = Visible(project).ToList(); if (Search.Text.Length > 0 && tasks.Count == 0) continue;
            var header = new StackPanel { Orientation = Orientation.Horizontal }; header.Children.Add(MakeIcon("folder"));
            header.Children.Add(new TextBlock { Text = project.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, FontSize = 18 });
            var panel = new StackPanel();
            var select = new Button { Content = "이 프로젝트에서 새 채팅", HorizontalContentAlignment = HorizontalAlignment.Left, Foreground = Brush("Muted"), FontSize = 12, Padding = new Thickness(30, 7, 8, 7) };
            select.Click += (_, _) => { Projects.SelectedItem = project; NewTask_Click(this, new RoutedEventArgs()); Prompt.Focus(); }; panel.Children.Add(select);
            foreach (var task in tasks) panel.Children.Add(TaskButton(task));
            var group = new Expander { Header = header, Content = panel, IsExpanded = !collapsedProjects.Contains(project.Id), Margin = new Thickness(4, 8, 0, 4) };
            group.Collapsed += (_, _) => collapsedProjects.Add(project.Id); group.Expanded += (_, _) => collapsedProjects.Remove(project.Id);
            SidebarGroups.Children.Add(group);
        }
        if (desk.State.Projects.Count == 0) SidebarGroups.Children.Add(new TextBlock { Text = "프로젝트를 추가하여 시작하세요", Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 20, 10, 0) });
    }
    private readonly List<string> openChats = [];
    private readonly HashSet<string> closedChats = [];
    private string chatTabsStamp = "";
    private void RenderChatTabs()
    {
        if (CurrentTask is { } current && !openChats.Contains(current.Id) && !closedChats.Contains(current.Id)) openChats.Add(current.Id);
        var stamp = CurrentTask?.Id + "|" + string.Join('|', openChats.Select(id => { var task = desk.State.Tasks.FirstOrDefault(t => t.Id == id); return id + task?.Title + task?.Status + task?.Archived; }));
        if (stamp == chatTabsStamp) return;
        chatTabsStamp = stamp;
        ChatTabs.Children.Clear();
        foreach (var id in openChats.ToList())
        {
            var task = desk.State.Tasks.FirstOrDefault(t => t.Id == id); if (task == null || task.Archived) { openChats.Remove(id); continue; }
            var tab = new StackPanel { Orientation = Orientation.Horizontal };
            var select = new Button { Content = (DeskController.IsRunning(task) ? "● " : "") + (task.Title.Length > 18 ? task.Title[..18] + "…" : task.Title), ToolTip = task.Title + " · " + task.Status, Background = task == CurrentTask ? Brush("Selected") : Brushes.Transparent };
            select.Click += (_, _) => SelectTask(task); tab.Children.Add(select);
            var close = new Button { Content = "×", ToolTip = "탭 닫기 · 작업은 계속 실행됩니다" };
            close.Click += (_, _) => { closedChats.Add(id); openChats.Remove(id); if (CurrentTask == task && openChats.LastOrDefault() is { } next) SelectTask(desk.State.Tasks.First(t => t.Id == next)); else if (CurrentTask == task) NewTask_Click(this, new RoutedEventArgs()); RenderChatTabs(); };
            tab.Children.Add(close); ChatTabs.Children.Add(tab);
        }
        var add = new Button { Content = "+", ToolTip = "새 채팅" }; add.Click += NewTask_Click; ChatTabs.Children.Add(add);
    }
    private Button TaskButton(WorkItem task)
    {
        var row = new DockPanel();
        if (DeskController.IsRunning(task)) { var state = new TextBlock { Text = "진행", Foreground = Brush("Muted"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }; DockPanel.SetDock(state, Dock.Right); row.Children.Add(state); }
        row.Children.Add(new TextBlock { Text = task.Title, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 18 });
        var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(28, 9, 10, 9), Margin = new Thickness(0), ToolTip = task.Title + " · " + task.Status, Background = task == CurrentTask ? Brush("Selected") : Brushes.Transparent };
        button.Click += (_, _) => SelectTask(task); button.ContextMenu = ChatMenu(task); return button;
    }
    private ContextMenu ChatMenu(WorkItem task)
    {
        var menu = new ContextMenu();
        void Item(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item(task.Pinned ? "고정 해제" : "고정", () => { task.Pinned = !task.Pinned; desk.Save(); });
        Item("이름 변경", () => Rename(task));
        Item("대화 복사", () => Clipboard.SetText(ChatText(task)));
        Item("대화 내보내기", () => Export(task));
        Item("대화 분기", () => { SelectTask(task); Fork_Click(this, new RoutedEventArgs()); });
        Item(task.Archived ? "복원" : "보관", () => { SelectTask(task); if (task.Archived) Restore_Click(this, new RoutedEventArgs()); else Archive_Click(this, new RoutedEventArgs()); });
        menu.Items.Add(new Separator());
        Item("채팅 삭제", () => _ = Guard(() => { DeleteChat(task, true); return Task.CompletedTask; }));
        return menu;
    }
    private void DeleteChat(WorkItem task, bool confirm)
    {
        if (DeskController.IsRunning(task)) throw new IOException("삭제할 채팅을 먼저 중단하세요.");
        if (confirm && MessageBox.Show(this, "이 채팅의 대화 기록과 초안을 삭제할까요?\n프로젝트 파일, Git 이력과 사용량은 유지됩니다.", "채팅 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        openChats.Remove(task.Id); closedChats.Remove(task.Id); navigation.RemoveAll(id => id == task.Id); navigationIndex = navigation.Count - 1;
        imageDrafts.Remove(task.Id); fileDrafts.Remove(task.Id); skillDrafts.Remove(task.Id); modelDrafts.Remove(task.Id);
        if (draftTaskId == task.Id) { draftTaskId = null; switchingDraft = true; Prompt.Clear(); switchingDraft = false; pendingImages.Clear(); pendingFiles.Clear(); pendingSkills.Clear(); Attachments.ItemsSource = null; }
        if (chatBrowsers.Remove(task.Id, out var view))
        {
            if (ReferenceEquals(activeBrowserView, view)) activeBrowserView = null;
            if (ReferenceEquals(browserView, view)) { browserView = null; browserInitialization = null; }
            browserSurface.Children.Remove(view); view.Dispose(); ShowChatBrowser();
        }
        browserLocks.Remove(task.Id);
        desk.DeleteTask(task); Footer.Text = "채팅 삭제됨 · 프로젝트 파일과 Git 이력은 유지됨";
    }
    private void DeleteChat_Click(object sender, RoutedEventArgs e) => _ = Guard(() => { if (CurrentTask is { } task) DeleteChat(task, true); return Task.CompletedTask; });
    private void ChatMenu_Click(object sender, RoutedEventArgs e) { if (CurrentTask is { } task) { var menu = ChatMenu(task); menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true; } }
    private void AddMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Item(string label, RoutedEventHandler action) { var item = new MenuItem { Header = label }; item.Click += action; menu.Items.Add(item); }
        Item("이미지 첨부", ImageAdd_Click); Item("파일 첨부", FileAdd_Click); Item("클립보드 붙여넣기", PasteAttachment_Click); Item("첨부 비우기", ImageClear_Click); Item("프로젝트 스킬 선택", Skills_Click); Item("스킬 비우기", SkillClear_Click);
        menu.Items.Add(new Separator()); Item("MCP 연결", Mcp_Click); Item("격리 worktree", IsolatedTask_Click); Item("AI 변경 검토", AiReview_Click); Item("초안 예약", Schedule_Click);
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void Rename(WorkItem task)
    {
        var dialog = new Window { Owner = this, Title = "채팅 이름 변경", Width = 430, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(18) }; var input = new TextBox { Text = task.Title, MaxLength = 200 }; var button = new Button { Content = "저장", IsDefault = true, Margin = new Thickness(0, 15, 0, 0) }; panel.Children.Add(input); panel.Children.Add(button); dialog.Content = panel;
        button.Click += (_, _) => { try { ChatMessages.Rename(desk.State, task, input.Text); desk.Save(); dialog.DialogResult = true; } catch (IOException ex) { MessageBox.Show(dialog, ex.Message); } }; dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; dialog.ShowDialog();
    }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (CurrentTask is { } task) Rename(task); }
    private string ChatText(WorkItem task) => "# " + task.Title + "\n\n" + string.Join("\n\n", task.Transcript.Select(entry => $"### {entry.Kind}\n\n{entry.Text}"));
    private void CopyChat_Click(object sender, RoutedEventArgs e) { if (CurrentTask is { } task) Clipboard.SetText(ChatText(task)); }
    private void Export(WorkItem task)
    {
        var dialog = new SaveFileDialog { Filter = "Markdown|*.md", FileName = "Hermes-chat.md" }; if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Safety.Redact(ChatText(task)), new UTF8Encoding(false));
    }
    private void ExportChat_Click(object sender, RoutedEventArgs e) { if (CurrentTask is { } task) Export(task); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.N: NewTask_Click(this, new RoutedEventArgs()); Prompt.Focus(); break;
                case Key.K: Palette_Click(this, new RoutedEventArgs()); break;
                case Key.F: SearchFocus_Click(this, new RoutedEventArgs()); break;
                case Key.B: Sidebar_Click(this, new RoutedEventArgs()); break;
                case Key.J: Inspector_Click(this, new RoutedEventArgs()); break;
                default: return;
            }
            e.Handled = true; return;
        }
        if (e.Key == Key.Enter && Prompt.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && desk.State.Desktop.EnterToSend)
        { e.Handled = true; if (StartButton.IsEnabled) Start_Click(this, new RoutedEventArgs()); }
    }
    private void Shortcuts_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Ctrl+N 새 채팅\nCtrl+K 명령 팔레트\nCtrl+F 채팅 검색\nCtrl+B 사이드바\nCtrl+J 오른쪽 패널\nEnter 전송 · Shift+Enter 줄바꿈\n실행 중 Enter: 추가 지시", "단축키");
    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        var commands = new (string Name, Action Run)[] { ("새 채팅", () => NewTask_Click(this, new())), ("프로젝트 열기", () => Register_Click(this, new())), ("엔진 연결", () => Connect_Click(this, new())), ("설정", () => Settings_Click(this, new())), ("로컬 Git 및 원격 서버", () => GitSettings_Click(this, new())), ("테마 변경", () => Theme_Click(this, new())), ("파일", () => OpenPanel(0)), ("변경 검토", () => ReviewPanel_Click(this, new())), ("터미널 명령", () => OpenPanel(6)), ("브라우저", () => OpenPanel(8)), ("Windows 컴퓨터 제어", () => OpenPanel(9)), ("사용량", () => OpenPanel(5)), ("승인", () => OpenPanel(3)), ("스킬", () => Skills_Click(this, new())), ("MCP", () => Mcp_Click(this, new())), ("초안 예약", () => Schedule_Click(this, new())), ("기능 및 연결 상태", () => Capabilities_Click(this, new())) };
        var window = new Window { Owner = this, Title = "명령 팔레트", Width = 540, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) }; var input = new TextBox(); DockPanel.SetDock(input, Dock.Top); panel.Children.Add(input); var list = new ListBox(); panel.Children.Add(list); window.Content = panel;
        void Filter() { list.ItemsSource = commands.Where(c => c.Name.Contains(input.Text, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).ToList(); list.SelectedIndex = 0; }
        void Execute() { if (list.SelectedItem is not string name) return; window.Close(); commands.First(c => c.Name == name).Run(); }
        input.TextChanged += (_, _) => Filter(); list.MouseDoubleClick += (_, _) => Execute(); window.PreviewKeyDown += (_, key) => { if (key.Key == Key.Enter) { key.Handled = true; Execute(); } else if (key.Key == Key.Down) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); key.Handled = true; } else if (key.Key == Key.Escape) window.Close(); }; Filter(); window.Loaded += (_, _) => input.Focus(); window.ShowDialog();
    }
    private void Capabilities_Click(object sender, RoutedEventArgs e) => new CapabilitiesWindow(desk) { Owner = this }.ShowDialog();
    private void GitSettings_Click(object sender, RoutedEventArgs e) => new GitSettingsWindow(desk, CurrentProject) { Owner = this }.ShowDialog();
}
