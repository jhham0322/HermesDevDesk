using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
using Microsoft.Win32;

namespace Hermes.Desktop;
public partial class MainWindow : Window
{
    private readonly DeskController desk;
    private readonly LocalCommandSession terminal;
    private readonly List<ProjectImage> pendingImages = [];
    private readonly List<ProjectSkill> pendingSkills = [];
    private readonly System.Windows.Threading.DispatcherTimer draftTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private string fileDirectory = "";
    private bool refreshing;
    private bool closing;
    private bool savedChanges;
    private bool connectingCompany;
    private ProjectEntry? CurrentProject => Projects.SelectedItem as ProjectEntry;
    private WorkItem? CurrentTask => Tasks.SelectedItem as WorkItem;
    private ServerRequest? CurrentApproval => (ApprovalList.SelectedItem as ApprovalDisplay)?.Request;
    private sealed record ApprovalDisplay(ServerRequest Request) { public override string ToString() => Request.Method.Replace("item/", "") + "\n" + Request.Params.Text("threadId")[..Math.Min(8, Request.Params.Text("threadId").Length)]; }
    public MainWindow(string data)
    {
        InitializeComponent(); InitializeMaximizeBounds();
        desk = new(data, a => Dispatcher.BeginInvoke(a)); desk.Changed += Refresh; desk.StreamChanged += QueueRefresh;
        InitializeShell();
        terminal = new(Path.Combine(data, "terminal-home")); terminal.Output += text => Dispatcher.BeginInvoke(() => { CommandOutput.AppendText(text); if (CommandOutput.Text.Length > 300000) CommandOutput.Text = CommandOutput.Text[^200000..]; CommandOutput.ScrollToEnd(); });
        UsageStart.SelectedDate = DateTime.UtcNow.Date.AddDays(-6); UsageEnd.SelectedDate = DateTime.UtcNow.Date;
        desk.Diagnostic += s => { Logs.AppendText(s + "\n"); if (Logs.Text.Length > 200_000) Logs.Text = Logs.Text[^100_000..]; Logs.ScrollToEnd(); };
        Refresh(); RefreshFiles(); Closing += Window_Closing;
        draftTimer.Tick += (_, _) => ProcessDrafts(); draftTimer.Start(); ProcessDrafts();
    }
    private void Refresh()
    {
        if (refreshing) return; refreshing = true;
        try
        {
            var project = CurrentProject; var task = CurrentTask; var approval = CurrentApproval;
            Projects.ItemsSource = desk.State.Projects.ToList(); Projects.SelectedItem = project ?? desk.State.Projects.FirstOrDefault();
            var projectTasks = desk.Search(Search.Text, SearchAll.IsChecked == true ? null : CurrentProject?.Id, ArchivedOnly.IsChecked == true);
            Tasks.ItemsSource = projectTasks; Tasks.SelectedItem = projectTasks.Contains(task!) && task?.ProjectId == CurrentProject?.Id ? task : projectTasks.FirstOrDefault(t => t.ProjectId == CurrentProject?.Id);
            ApprovalList.ItemsSource = desk.Approvals.Select(r => new ApprovalDisplay(r)).ToList();
            ApprovalList.SelectedItem = ApprovalList.Items.Cast<ApprovalDisplay>().FirstOrDefault(d => d.Request == approval) ?? ApprovalList.Items.Cast<ApprovalDisplay>().FirstOrDefault();
            Connection.Text = desk.AccountStatus;
            var visibleModels = desk.DisplayModels.Count > 0 ? desk.DisplayModels.ToList() : new List<string> { "모델명을 입력하세요" };
            if (!Model.Items.Cast<string>().SequenceEqual(visibleModels)) { var selected = desk.HasPendingModel ? desk.State.PendingModel : Model.SelectedItem as string; Model.ItemsSource = visibleModels; Model.SelectedItem = visibleModels.Contains(selected!) ? selected : visibleModels.FirstOrDefault(); }
            Model.ToolTip = desk.HasPendingModel ? "입력한 모델 · URL/키를 저장하고 연결하세요" : "설정한 모델 · " + desk.AccountStatus;
            ProviderHint.Text = desk.State.Provider.Mode == ConnectionMode.CompanyApi ? desk.State.Provider.IsAnthropic ? "Anthropic Messages · " + desk.ProviderCheckStatus : "회사 API · " + desk.ProviderCheckStatus : "ChatGPT CLI · 구독 사용량 사용";
            StartButton.IsEnabled = !connectingCompany && !desk.HasPendingModel && (desk.SubscriptionReady || desk.CanConnectCompany) && CurrentTask != null && !CurrentTask.Archived && (CurrentTask.Status == "실행 중" || !DeskController.IsRunning(CurrentTask));
            RenderTask();
            RenderSidebar(); UpdateShellState();
        }
        finally { refreshing = false; }
    }
    private void RenderTask()
    {
        TaskTitle.Text = CurrentTask?.Title ?? "프로젝트를 등록하고 작업을 시작하세요";
        RootLabel.Text = CurrentProject?.Root ?? "폴더 등록은 Hermes 목록에만 저장됩니다.";
        TaskStatus.Text = CurrentTask == null ? "" : CurrentTask.Status + "  /  " + CurrentTask.Updated.ToLocalTime().ToString("g");
        TaskDetails.Text = CurrentTask == null ? "선택한 작업 없음" : Review.Detail(desk.State, CurrentTask);
        Writable.IsEnabled = CurrentTask?.ThreadId == null;
        if (CurrentTask?.ThreadId != null) Writable.IsChecked = CurrentTask.ThreadWritable == true;
        SwitchDraft(); RenderConversation(); UpdateShellState();
    }
    private async Task Guard(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Footer.Text = Safety.Redact(ex.Message); MessageBox.Show(this, Safety.Redact(ex.Message), "Hermes DevDesk", MessageBoxButton.OK, MessageBoxImage.Information); } }
    private void Settings_Click(object sender, RoutedEventArgs e) { var window = new SettingsWindow(desk) { Owner = this }; window.ShowDialog(); Refresh(); }
    private void Schedule_Click(object sender, RoutedEventArgs e) { if (CurrentProject is { } project) new ScheduleWindow(desk, project) { Owner = this }.ShowDialog(); }
    private void ProcessDrafts()
    {
        var created = desk.CreateDueDrafts(DateTimeOffset.UtcNow);
        if (created.Count > 0) { Footer.Text = $"예약 작업 초안 {created.Count}개 생성 · 모델 자동 호출 없음"; Refresh(); }
    }
    private void Skills_Click(object sender, RoutedEventArgs e) => _ = Guard(() =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트 선택 필요"); var cli = desk.State.CodexPath ?? AppServerClient.FindInstalledCodex() ?? throw new IOException("공식 CLI 필요");
        var window = new SkillsWindow(project.Root, cli, Path.Combine(desk.DataDirectory, "skill-inspection", project.Id)) { Owner = this };
        if (window.ShowDialog() == true) { pendingSkills.Clear(); pendingSkills.AddRange(window.Selected); SkillHint.Text = string.Join(", ", pendingSkills.Select(s => s.Name)); }
        return Task.CompletedTask;
    });
    private void SkillClear_Click(object sender, RoutedEventArgs e) { pendingSkills.Clear(); SkillHint.Text = "선택 스킬 없음"; }
    private async void Archive_Click(object sender, RoutedEventArgs e) => await Guard(async () => { await ArchiveCurrentAsync(true, true); });
    private async void Restore_Click(object sender, RoutedEventArgs e) => await Guard(async () => { await ArchiveCurrentAsync(false, true); });
    private async Task ArchiveCurrentAsync(bool archived, bool confirm)
    {
        var task = CurrentTask ?? throw new IOException("대화를 선택하세요.");
        if (confirm && MessageBox.Show(this, archived ? "선택 대화를 보관할까요? 대화·파일·사용량은 보존합니다." : "선택 대화를 복원할까요?", "대화 보관/복원", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await desk.SetArchivedAsync(task, archived); ArchivedOnly.IsChecked = archived; Refresh(); Tasks.SelectedItem = task;
    }
    private void Mcp_Click(object sender, RoutedEventArgs e) => _ = Guard(() =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요.");
        var cli = desk.State.CodexPath ?? AppServerClient.FindInstalledCodex() ?? throw new IOException("공식 CLI 필요");
        new McpWindow(desk, project, cli) { Owner = this }.ShowDialog(); return Task.CompletedTask;
    });
    private async void Fork_Click(object sender, RoutedEventArgs e) => await Guard(async () => { var task = await desk.ForkAsync(CurrentTask ?? throw new IOException("시작된 작업을 선택하세요.")); Search.Clear(); Tasks.SelectedItem = task; RenderTask(); });
    private async void AiReview_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var project = CurrentProject ?? throw new IOException("Git 프로젝트를 선택하세요.");
        if (MessageBox.Show(this, "현재 Git 프로젝트의 커밋하지 않은 변경을 선택한 provider로 검토합니다. 모델 사용량이 발생할 수 있습니다. 읽기 전용 새 검토 작업을 시작할까요?", "AI 변경 검토", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var task = await desk.StartReviewAsync(project, Model.SelectedItem as string ?? ""); Search.Clear(); Tasks.SelectedItem = task; RenderTask();
    });
    private void ImageAdd_Click(object sender, RoutedEventArgs e) => _ = Guard(() =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요.");
        var dialog = new OpenFileDialog { Filter = "Project image|*.png;*.jpg;*.jpeg", Multiselect = true, InitialDirectory = project.Root, Title = "선택 프로젝트의 이미지 · 모델로 전송할 파일" };
        if (dialog.ShowDialog(this) == true) foreach (var file in dialog.FileNames) AddImage(file);
        return Task.CompletedTask;
    });
    private void AddImage(string file)
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요.");
        if (!Safety.Within(project.Root, file)) throw new IOException("선택한 프로젝트 안의 이미지만 첨부할 수 있습니다.");
        var image = ImageAttachments.Read(project.Root, Path.GetRelativePath(project.Root, file));
        using (var stream = File.OpenRead(Safety.Resolve(project.Root, image.RelativePath)))
        {
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth != image.Width || decoder.Frames[0].PixelHeight != image.Height) throw new IOException("이미지 디코드/해상도 오류");
        }
        var selected = ImageAttachments.Validate(project.Root, pendingImages.Append(image)); pendingImages.Clear(); pendingImages.AddRange(selected); Attachments.ItemsSource = pendingImages.ToList();
        AttachmentHint.Text = $"{pendingImages.Count}개 · 선택 이미지가 현재 모델/회사 endpoint로 전송됨 · 회사 배포의 image 지원은 미검증";
    }
    private void ImageClear_Click(object sender, RoutedEventArgs e) { pendingImages.Clear(); Attachments.ItemsSource = null; AttachmentHint.Text = "PNG/JPEG · 최대 4개/합계 3MB · 선택 이미지가 모델로 전송됨"; }
    private List<UsageEntry> SelectedUsage()
    {
        var start = new DateTimeOffset(DateTime.SpecifyKind((UsageStart.SelectedDate ?? DateTime.UtcNow.Date).Date, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind((UsageEnd.SelectedDate ?? DateTime.UtcNow.Date).Date.AddDays(1), DateTimeKind.Utc));
        if (end <= start) throw new IOException("날짜 구간을 확인하세요.");
        return UsageLedger.Select(desk.State, start, end, UsageTaskOnly.IsChecked == true ? CurrentTask?.Id : null, string.IsNullOrWhiteSpace(UsageModel.Text) ? null : UsageModel.Text.Trim());
    }
    private void Usage_Click(object sender, RoutedEventArgs e) => _ = Guard(() => { UsageView.Text = UsageLedger.Summary(SelectedUsage()); return Task.CompletedTask; });
    private void UsageExport_Click(object sender, RoutedEventArgs e) => _ = Guard(() =>
    {
        var dialog = new SaveFileDialog { FileName = "Hermes-observed-usage.json", Filter = "JSON|*.json" }; if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Safety.Redact(JsonSerializer.Serialize(SelectedUsage(), new JsonSerializerOptions { WriteIndented = true }))); return Task.CompletedTask;
    });
    private async void CommandRun_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요."); var command = CommandText.Text;
        if (MessageBox.Show(this, $"경로: {project.Root}\n모드: {(CommandWritable.IsChecked == true ? "프로젝트 쓰기" : "읽기 전용")}\n\n{Safety.Redact(command)}\n\n이 명령을 공식 sandbox 안에서 실행할까요?", "명령 일회성 확인", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var exe = desk.State.CodexPath ?? AppServerClient.FindInstalledCodex() ?? throw new IOException("공식 codex.exe를 선택하세요."); CommandOutput.Clear(); CommandOutput.AppendText(await terminal.RunAsync(exe, project.Root, command, CommandWritable.IsChecked == true));
    });
    private async void CommandStop_Click(object sender, RoutedEventArgs e) => await Guard(() => terminal.StopAsync());
    private void Register_Click(object sender, RoutedEventArgs e)
    {
        _ = Guard(() => { var dialog = new OpenFolderDialog { Title = "독립 작업 프로젝트 폴더 선택" }; if (dialog.ShowDialog(this) == true) { var project = desk.Register(dialog.FolderName); Projects.SelectedItem = project; fileDirectory = ""; Refresh(); RefreshFiles(); } return Task.CompletedTask; });
    }
    private void NewTask_Click(object sender, RoutedEventArgs e)
    { if (CurrentProject == null) { Footer.Text = "프로젝트를 먼저 등록하세요."; return; } ArchivedOnly.IsChecked = false; Search.Clear(); var task = desk.NewTask(CurrentProject, "작업 " + DateTime.Now.ToString("MM-dd HH:mm")); Tasks.SelectedItem = task; RenderTask(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (desk != null && !refreshing) Refresh(); }
    private void SearchScope_Changed(object sender, RoutedEventArgs e) { if (desk != null && !refreshing) Refresh(); }
    private async void IsolatedTask_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var project = CurrentProject ?? throw new IOException("원본 Git 프로젝트를 선택하세요.");
        var picker = new OpenFolderDialog { Title = "등록 프로젝트와 겹치지 않는 빈 worktree 보관 폴더 선택" };
        if (picker.ShowDialog(this) != true) return;
        var result = await desk.NewIsolatedTaskAsync(project, picker.FolderName, "격리 작업 " + DateTime.Now.ToString("MM-dd HH:mm"));
        Search.Clear(); Projects.SelectedItem = result.Project; Refresh(); Tasks.SelectedItem = result.Task; RenderTask(); RefreshFiles();
        Footer.Text = "새 worktree 등록됨 · 원본 미커밋 변경은 복사하지 않음 · 자동 삭제 없음";
    });
    private void Project_Selected(object sender, SelectionChangedEventArgs e)
    { if (refreshing || desk == null) return; fileDirectory = ""; ImageClear_Click(this, new RoutedEventArgs()); SkillClear_Click(this, new RoutedEventArgs()); FilePreview.Clear(); ChangedFiles.ItemsSource = null; Diff.Clear(); Refresh(); RefreshFiles(); }
    private void Task_Selected(object sender, SelectionChangedEventArgs e) { if (!refreshing && desk != null) { if (CurrentTask != null && CurrentTask.ProjectId != CurrentProject?.Id) Projects.SelectedItem = desk.State.Projects.First(p => p.Id == CurrentTask.ProjectId); RenderTask(); ChangedFiles.ItemsSource = null; Diff.Clear(); ReviewStatus.Text = "작업이 바뀌었습니다. 변경 목록을 조회하세요."; StartButton.IsEnabled = !connectingCompany && !desk.HasPendingModel && (desk.SubscriptionReady || desk.CanConnectCompany) && CurrentTask != null && !CurrentTask.Archived && !DeskController.IsRunning(CurrentTask); } }
    private void ChooseCli_Click(object sender, RoutedEventArgs e)
    { var dialog = new OpenFileDialog { Filter = "Codex executable|codex.exe", Title = "공식 codex.exe 선택" }; if (dialog.ShowDialog(this) == true) { desk.State.CodexPath = dialog.FileName; desk.Save(); Footer.Text = "CLI 경로 저장됨. 엔진 연결을 누르세요."; } }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (desk.State.Tasks.Any(DeskController.IsRunning)) throw new IOException("실행 중인 작업을 먼저 중단하세요.");
        var exe = desk.State.CodexPath ?? AppServerClient.FindInstalledCodex() ?? throw new IOException("공식 Codex CLI를 설치하고 codex.exe를 선택하세요. 자동 설치/로그인은 수행하지 않습니다.");
        await desk.ConnectAsync(exe, CurrentProject?.Root ?? throw new IOException("엔진 연결 전에 프로젝트를 등록/선택하세요."));
        if (desk.State.Provider.Mode == ConnectionMode.CompanyApi) await desk.CheckProviderAsync();
        Refresh();
    });
    private async void Start_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (connectingCompany) return;
        var prompt = Prompt.Text; var task = CurrentTask;
        if (task == null) { NewTask_Click(this, new RoutedEventArgs()); task = CurrentTask ?? throw new IOException("프로젝트를 선택하세요."); }
        if (!desk.SubscriptionReady && desk.CanConnectCompany)
        {
            connectingCompany = true; Refresh();
            try
            {
                var configured = desk.State.CodexPath;
                var exe = configured != null && File.Exists(configured) ? configured : AppServerClient.FindInstalledCodex() ?? throw new IOException("동봉한 엔진을 찾지 못했습니다. 설치 폴더의 engine을 확인하세요.");
                await desk.ConnectAsync(exe, CurrentProject!.Root);
            }
            finally { connectingCompany = false; Refresh(); }
        }
        if (task.Status == "실행 중")
        {
            if (pendingImages.Count > 0 || pendingSkills.Count > 0) throw new IOException("추가 지시는 텍스트로 전송하세요. 첨부는 다음 실행에서 사용할 수 있습니다.");
            await desk.SteerAsync(task, prompt); Prompt.Clear(); return;
        }
        if (task.Transcript.Count == 0) task.Title = Safety.Redact(prompt.Length > 45 ? prompt[..45] + "…" : prompt);
        await desk.StartAsync(task, prompt, Model.SelectedItem as string ?? "", Writable.IsChecked == true, pendingImages.ToList(), skills: pendingSkills.ToList(), effort: Effort.SelectedIndex > 0 ? Effort.SelectedItem as string : null); Prompt.Clear(); ImageClear_Click(this, new RoutedEventArgs()); SkillClear_Click(this, new RoutedEventArgs());
    });
    private async void Stop_Click(object sender, RoutedEventArgs e) => await Guard(async () => { if (CurrentTask != null) await desk.StopAsync(CurrentTask); });
    private void RefreshFiles(bool clearPreview = true)
    {
        if (clearPreview) FilePreview.Clear();
        try { Files.ItemsSource = CurrentProject == null ? null : WorkspaceFiles.List(CurrentProject.Root, fileDirectory); FileDirectory.Text = "/" + fileDirectory; }
        catch (Exception ex) { Footer.Text = Safety.Redact(ex.Message); }
    }
    private void FilesRefresh_Click(object sender, RoutedEventArgs e) => RefreshFiles();
    private void FilesUp_Click(object sender, RoutedEventArgs e) { fileDirectory = Path.GetDirectoryName(fileDirectory) ?? ""; RefreshFiles(); }
    private void File_Open(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _ = Guard(() => { if (CurrentProject != null && Files.SelectedItem is FileEntry entry) { if (entry.Directory) { fileDirectory = entry.Relative; RefreshFiles(); } else FilePreview.Text = WorkspaceFiles.Read(CurrentProject.Root, entry.Relative); } return Task.CompletedTask; }); }
    private async void GitDiff_Click(object sender, RoutedEventArgs e) => await Guard(RefreshGitChanges);
    private async Task RefreshGitChanges()
    {
        var project = CurrentProject; if (project == null) return;
        Diff.Clear(); FilePreview.Clear(); ReviewStatus.Text = "Git 변경 조회 중…";
        try
        {
            var changes = await Review.ChangesAsync(project.Root);
            if (CurrentProject?.Id != project.Id) return;
            savedChanges = false; ChangedFiles.ItemsSource = changes; ReviewStatus.Text = $"현재 Git 변경 {changes.Count}개 · 비밀/링크 경로 제외";
            if (changes.Count > 0) ChangedFiles.SelectedIndex = 0; else Diff.Text = "변경 없음";
        }
        catch (Exception ex) { if (CurrentProject?.Id == project.Id) { ChangedFiles.ItemsSource = null; ReviewStatus.Text = Safety.Redact(ex.Message); } throw; }
    }
    private void TurnDiff_Click(object sender, RoutedEventArgs e)
    { savedChanges = true; ChangedFiles.ItemsSource = CurrentTask?.ChangedFiles.ToList(); ReviewStatus.Text = "저장된 엔진 변경 · 현재 파일 상태와 다를 수 있음"; Diff.Text = CurrentTask?.Diff ?? ""; }
    private async void Change_Selected(object sender, SelectionChangedEventArgs e) => await Guard(async () =>
    {
        if (ChangedFiles.SelectedItem is not ChangedFile change || CurrentProject is not { } project) return;
        var diff = savedChanges ? change.SavedDiff ?? "저장된 diff 없음" : await Review.FileDiffAsync(project.Root, change);
        if (CurrentProject?.Id == project.Id && Equals(ChangedFiles.SelectedItem, change)) Diff.Text = diff;
    });
    private void ChangeOpen_Click(object sender, RoutedEventArgs e) => _ = Guard(() =>
    {
        FilePreview.Clear();
        if (ChangedFiles.SelectedItem is ChangedFile change && CurrentProject is { } project)
        {
            var path = Safety.Resolve(project.Root, change.Path);
            if (!File.Exists(path)) { FilePreview.Text = "현재 파일이 없습니다 (삭제/이름 변경). diff에서 검토하세요."; }
            else { try { FilePreview.Text = WorkspaceFiles.Read(project.Root, change.Path); } catch (Exception ex) when (ex is IOException or DecoderFallbackException) { FilePreview.Text = Safety.Redact(ex.Message); } }
            fileDirectory = Path.GetDirectoryName(change.Path) ?? ""; RefreshFiles(false); InspectorTabs.SelectedIndex = 0;
        }
        return Task.CompletedTask;
    });
    private void Approval_Selected(object sender, SelectionChangedEventArgs e)
    {
        EscalationConsent.IsChecked = false;
        ApprovalDetail.Text = CurrentApproval is { } request ? Safety.Redact(request.Method + "\n\n" + JsonSerializer.Serialize(request.Params, new JsonSerializerOptions { WriteIndented = true })) + "\n\n변경 내용: diff 탭/대화의 파일 변경 항목을 검토하세요." : "대기 요청 없음";
    }
    private async void Accept_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var request = CurrentApproval ?? throw new IOException("대기 요청이 없습니다.");
        Dictionary<string, object>? answers = null;
        if (request.Method == "item/tool/requestUserInput")
        {
            answers = new();
            foreach (var q in request.Params.GetProperty("questions").EnumerateArray())
            {
                var dialog = new AnswerWindow(q) { Owner = this }; if (dialog.ShowDialog() != true) return;
                answers[q.Text("id")] = new { answers = new[] { dialog.Answer } };
            }
        }
        await desk.AnswerAsync(request, true, answers, EscalationConsent.IsChecked == true);
    });
    private async void Decline_Click(object sender, RoutedEventArgs e) => await Guard(async () => { if (CurrentApproval != null) await desk.AnswerAsync(CurrentApproval, false); });
    public async Task ShutdownController() { draftTimer.Stop(); await terminal.DisposeAsync(); await desk.DisposeAsync(); }
    public async Task<string> RunProgrammaticUiChecks(string data)
    {
        var root = Path.Combine(data, "ui-project"); var other = Path.Combine(data, "ui-other");
        if (Directory.Exists(root) || Directory.Exists(other)) throw new IOException("UI 검사에는 새 데이터 경로가 필요합니다.");
        Directory.CreateDirectory(root); Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(root, "review.txt"), "initial\n");
        var project = desk.Register(root); var project2 = desk.Register(other);
        var task = desk.NewTask(project, "Alpha UI fixture"); task.Transcript.Add(new("UI 검사", "fixture-needle", DateTime.UtcNow));
        desk.NewTask(project2, "Alpha second fixture"); desk.Save(); Projects.SelectedItem = project; Tasks.SelectedItem = task;
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new IOException("UI check failed: " + label); checks++; }
        Search.Text = "fixture-needle"; Check(Tasks.Items.Count == 1 && CurrentTask == task, "conversation search control");
        SearchAll.IsChecked = true; Search.Text = "Alpha"; Check(Tasks.Items.Count == 2, "all-project search control");
        Tasks.SelectedItem = task; Check(CurrentProject == project && TaskDetails.Text.Contains(root), "search result execution root");
        Search.Clear();
        foreach (var args in new[] { new[] { "init" }, new[] { "add", "review.txt" }, new[] { "-c", "user.name=Hermes UI Test", "-c", "user.email=hermes@example.invalid", "commit", "-m", "isolated UI fixture" } })
        { var result = await GitRunner.Run(root, args); if (result.Exit != 0) throw new IOException(result.Error); }
        File.WriteAllText(Path.Combine(root, "review.txt"), "initial\nupdated\n"); await RefreshGitChanges();
        Check(ChangedFiles.Items.Count == 1, "changed-file list control");
        var until = DateTime.UtcNow.AddSeconds(5); while (!Diff.Text.Contains("+updated") && DateTime.UtcNow < until) await Task.Delay(25);
        Check(Diff.Text.Contains("+updated"), "selected actual Git diff rendering");
        ChangeOpen_Click(this, new RoutedEventArgs()); Check(FilePreview.Text.Contains("updated"), "changed file open handler");
        File.WriteAllText(Path.Combine(root, "review.txt"), "initial\n"); await RefreshGitChanges();
        Check(ChangedFiles.Items.Count == 0 && Diff.Text == "변경 없음" && FilePreview.Text.Length == 0, "refresh removes stale content");
        File.Move(Path.Combine(root, "review.txt"), Path.Combine(root, "preserved-renamed.txt")); await RefreshGitChanges();
        ChangedFiles.SelectedItem = ChangedFiles.Items.Cast<ChangedFile>().First(f => f.Path == "review.txt");
        ChangeOpen_Click(this, new RoutedEventArgs()); Check(FilePreview.Text.Contains("현재 파일이 없습니다"), "deleted file handled without stale preview");
        Projects.SelectedItem = project2; Check(CurrentProject == project2 && CurrentTask?.ProjectId == project2.Id && FilePreview.Text.Length == 0 && ChangedFiles.Items.Count == 0, "project switch keeps task root aligned and clears review");
        Search.Text = "no-matches"; NewTask_Click(this, new RoutedEventArgs()); Check(Search.Text.Length == 0 && CurrentTask?.ProjectId == project2.Id, "new task remains visible after empty search result");
        var settings = new SettingsWindow(desk); settings.Show(); checks += settings.RunProgrammaticChecks(); settings.Close();
        desk.State.Usage.Add(new() { Id = "ui-usage", TaskId = CurrentTask!.Id, At = DateTimeOffset.UtcNow, Model = "fixture-model", Deployment = "fixture-model", Requests = 1, Tokens = new(10, 5, 2, 1, 15), Status = "completed" });
        UsageModel.Text = "fixture-model"; UsageTaskOnly.IsChecked = true; Usage_Click(this, new RoutedEventArgs());
        Check(UsageView.Text.Contains("fixture-model") && SelectedUsage().Count == 1, "usage filter and render");
        UsageModel.Text = "missing-model"; Check(SelectedUsage().Count == 0, "empty usage model filter");
        Projects.SelectedItem = project;
        var png = Path.Combine(root, "ui-image.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));
        AddImage(png); Check(Attachments.Items.Count == 1 && pendingImages.Single().Width == 1, "image selection/decode/list controls");
        ImageClear_Click(this, new RoutedEventArgs()); Check(Attachments.Items.Count == 0, "image clear control");
        var cli = AppServerClient.FindInstalledCodex() ?? throw new IOException("MCP UI smoke requires bundled official CLI");
        var mcp = new McpWindow(desk, project, cli) { Owner = this }; mcp.Show();
        checks += await mcp.RunProgrammaticChecks(); mcp.Close();
        var skillFolder = Path.Combine(root, ".agents", "skills", "ui-skill"); Directory.CreateDirectory(skillFolder);
        File.WriteAllText(Path.Combine(skillFolder, "SKILL.md"), "---\nname: ui-skill\ndescription: UI local skill fixture\n---\nUI_SKILL_FIXTURE\n");
        var skillWindow = new SkillsWindow(root, cli, Path.Combine(data, "skill-ui-inspection")) { Owner = this }; skillWindow.Show(); checks += skillWindow.RunProgrammaticSelectionChecks(); pendingSkills.AddRange(skillWindow.Selected); skillWindow.Close();
        SkillHint.Text = pendingSkills.Single().Name; Check(SkillHint.Text == "ui-skill", "selected skill summary"); SkillClear_Click(this, new RoutedEventArgs()); Check(pendingSkills.Count == 0, "skill clear control");
        Tasks.SelectedItem = task; await ArchiveCurrentAsync(true, false); Check(ArchivedOnly.IsChecked == true && task.Archived && Tasks.Items.Count == 1, "archive control/filter");
        await ArchiveCurrentAsync(false, false); Check(ArchivedOnly.IsChecked == false && !task.Archived, "restore control/filter");
        var reminder = desk.ScheduleDraft(project, "UI reminder fixture", "manual-only", DateTimeOffset.UtcNow.AddHours(1)); var dueTasks = desk.CreateDueDrafts(reminder.DueAtUtc);
        Check(dueTasks.Count == 1 && dueTasks.Single().Status == "예약 초안", "scheduled draft notification data");
        Check(desk.CreateDueDrafts(reminder.DueAtUtc).Count == 0, "scheduled draft duplicate protection");
        checks += await RunShellChecks();
        return $"PASS: {checks} programmatic WPF control/event checks; isolated fixtures. Not native mouse/keyboard automation.\n";
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        if (desk.State.Tasks.Any(DeskController.IsRunning) && MessageBox.Show(this, "Hermes의 실행 작업을 중단하고 종료할까요?", "종료", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        e.Cancel = true; closing = true; draftTimer.Stop();
        foreach (var task in desk.State.Tasks.Where(DeskController.IsRunning).ToList()) { try { await desk.StopAsync(task); } catch (IOException) { } }
        StopComputer(); draftSaveTimer.Stop(); browserView?.Dispose();
        await terminal.DisposeAsync(); await desk.DisposeAsync(); desk.Save(); Close();
    }
}
public sealed class AnswerWindow : Window
{
    public string Answer { get; private set; } = "";
    public AnswerWindow(JsonElement q)
    {
        Style = Application.Current.TryFindResource(typeof(Window)) as Style;
        Title = q.Text("header"); Width = 520; Height = 370; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = q.Text("question"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var options = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        if (q.Property("options") is { ValueKind: JsonValueKind.Array } list) foreach (var opt in list.EnumerateArray()) options.Items.Add(opt.Text("label"));
        if (options.Items.Count > 0) panel.Children.Add(options);
        var input = new TextBox { MinHeight = 60, TextWrapping = TextWrapping.Wrap }; var secret = new PasswordBox { Height = 40 };
        var isSecret = q.Property("isSecret") is { ValueKind: JsonValueKind.True }; panel.Children.Add(isSecret ? secret : input);
        var send = new Button { Content = "답변 전송", Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(send);
        send.Click += (_, _) => { Answer = isSecret ? secret.Password : string.IsNullOrWhiteSpace(input.Text) ? options.SelectedItem as string ?? "" : input.Text; if (Answer.Length > 0) DialogResult = true; };
    }
}
