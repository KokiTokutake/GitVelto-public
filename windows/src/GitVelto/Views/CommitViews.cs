using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>TortoiseGit-style commit dialog: only the checked files are committed.</summary>
public sealed class CommitView : GVView
{
    private readonly StatusModel model;
    private readonly FileList files = new(checkable: true);
    private readonly TextBox message = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, Height = 150,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, SpellCheck = { IsEnabled = false },
    };
    private readonly CheckBox amend = new() { Content = "直前のコミットをやり直す (amend)" };
    private readonly CheckBox push = new() { Content = "コミット後にプッシュ" };
    private readonly CheckBox setAuthor = new() { Content = "作成者を指定" };
    private readonly TextBox author = new() { Width = 240, Visibility = Visibility.Collapsed, ToolTip = "名前 <メール>" };
    private readonly CheckBox setDate = new() { Content = "日時を指定" };
    private readonly DatePicker datePicker = new() { Visibility = Visibility.Collapsed, SelectedDate = DateTime.Today };
    private readonly TextBox timeBox = new() { Width = 64, Visibility = Visibility.Collapsed, Text = DateTime.Now.ToString("HH:mm") };
    private readonly CheckBox newBranch = new() { Content = "新しいブランチを作成してコミット" };
    private readonly TextBox branchName = new() { Width = 220, Visibility = Visibility.Collapsed };
    private readonly TextBlock firstLineInfo = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock countText = new() { Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Width = 60, Height = 4, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly CheckBox showUntracked = new() { Content = "未追跡を表示", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox wholeProject = new() { Content = "プロジェクト全体", Margin = new Thickness(0, 0, 10, 0) };
    private readonly Border identityBox = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox userName = new() { MinWidth = 200 };
    private readonly TextBox userEmail = new() { MinWidth = 240 };
    private readonly CommandPreview preview = new();
    private readonly Button commitButton = Ui.Accent(new Button { Content = "コミット", MinWidth = 120 });
    private readonly Button recentButton = new() { Content = "最近のメッセージ ▾" };
    private bool needsIdentity;

    public CommitView(Repo repo, IReadOnlyList<string> paths)
    {
        model = new StatusModel(repo, paths);
        push.IsChecked = AppSettings.Shared.PushAfterCommit;

        // --- message ---
        message.FontFamily = Ui.Mono;
        message.FontSize = 13;
        var msgHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(recentButton, Dock.Right);
        msgHeader.Children.Add(recentButton);
        msgHeader.Children.Add(Ui.Heading("メッセージ:"));
        recentButton.Click += (_, _) => ShowRecent();

        var signOff = new Button { Content = "Signed-off-by を追加" };
        signOff.Click += (_, _) => AddSignOff();
        var opts1 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        opts1.Children.Add(Ui.Row(signOff, new TermHelp("signoff")));
        opts1.Children.Add(Ui.Row(setAuthor, author));
        opts1.Children.Add(Ui.Row(setDate, datePicker, timeBox));
        setAuthor.Click += (_, _) => { author.Visibility = setAuthor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; UpdatePreview(); };
        setDate.Click += (_, _) =>
        {
            var v = setDate.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            datePicker.Visibility = v; timeBox.Visibility = v;
        };
        author.TextChanged += (_, _) => UpdatePreview();

        var opts2 = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(firstLineInfo, Dock.Right);
        opts2.Children.Add(firstLineInfo);
        var left2 = new WrapPanel();
        left2.Children.Add(Ui.Row(newBranch, branchName));
        left2.Children.Add(Ui.Row(amend, new TermHelp("amend")));
        opts2.Children.Add(left2);
        newBranch.Click += (_, _) => { branchName.Visibility = newBranch.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; UpdatePreview(); };
        branchName.TextChanged += (_, _) => UpdatePreview();
        amend.Click += (_, _) =>
        {
            if (amend.IsChecked == true && message.Text.Trim().Length == 0)
                message.Text = model.Repo.Git(["log", "-1", "--format=%B"]).Out.Trim();
            UpdatePreview();
        };
        message.TextChanged += (_, _) => { UpdateFirstLine(); UpdatePreview(); UpdateCommitEnabled(); };

        // --- identity ---
        var idPanel = new StackPanel();
        idPanel.Children.Add(new TextBlock { Text = "作成者情報が未設定です (git config --global)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        idPanel.Children.Add(Ui.Row(new TextBlock { Text = "名前 (user.name)" }, userName, new TextBlock { Text = "メール (user.email)" }, userEmail));
        identityBox.Child = idPanel;
        identityBox.Padding = new Thickness(10);
        identityBox.CornerRadius = new CornerRadius(6);
        identityBox.Background = (System.Windows.Media.Brush)FindResource("LearningBg");

        // --- files ---
        var fileHeader = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        // Bulk check: header check box + "選択 ▾" (by kind); clicking one check box applies to all selected rows.
        var selectMenu = files.SelectButton();
        right.Children.Add(showUntracked);
        if (model.HasPathFilter) right.Children.Add(wholeProject);
        right.Children.Add(selectMenu);
        right.Children.Add(Ui.IconButton("", "再読み込み", Reload));
        DockPanel.SetDock(right, Dock.Right);
        fileHeader.Children.Add(right);
        fileHeader.Children.Add(Ui.Row(Ui.Heading("変更されたファイル"), new TermHelp("stage"), countText, loading));
        showUntracked.Click += (_, _) => { model.ShowUntracked = showUntracked.IsChecked == true; Reload(); };
        wholeProject.Click += (_, _) => { model.WholeProject = wholeProject.IsChecked == true; Reload(); };
        files.MinHeight = 160;
        files.Open += model.OpenDiff;
        files.MenuBuilder = sel => model.WorkingTreeMenu(sel, Reload);
        files.CheckedChanged += () => { UpdateCount(); UpdatePreview(); UpdateCommitEnabled(); };

        // --- bottom ---
        var cancel = new Button { Content = "キャンセル", MinWidth = 96, IsCancel = true, Margin = new Thickness(8, 0, 8, 0) };
        cancel.Click += (_, _) => Handle.Close();
        commitButton.Click += (_, _) => Commit();
        commitButton.ToolTip = "Ctrl+Enter";
        push.Click += (_, _) => { commitButton.Content = push.IsChecked == true ? "コミットしてプッシュ" : "コミット"; UpdatePreview(); };
        commitButton.Content = push.IsChecked == true ? "コミットしてプッシュ" : "コミット";
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var bRight = new StackPanel { Orientation = Orientation.Horizontal };
        bRight.Children.Add(TermHelp.Label("コミットとは", "commit"));
        bRight.Children.Add(cancel);
        bRight.Children.Add(commitButton);
        DockPanel.SetDock(bRight, Dock.Right);
        bottom.Children.Add(bRight);
        bottom.Children.Add(Ui.Row(push, new TermHelp("push")));

        // --- layout: message area / splitter / files ---
        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(msgHeader);
        top.Children.Add(message);
        top.Children.Add(opts1);
        top.Children.Add(opts2);
        top.Children.Add(identityBox);

        var lower = new DockPanel();
        DockPanel.SetDock(fileHeader, Dock.Top);
        lower.Children.Add(fileHeader);
        var lowerBottom = new StackPanel();
        lowerBottom.Children.Add(preview);
        lowerBottom.Children.Add(bottom);
        DockPanel.SetDock(lowerBottom, Dock.Bottom);
        lower.Children.Add(lowerBottom);
        lower.Children.Add(files);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 200 });
        Grid.SetRow(lower, 1);
        grid.Children.Add(top);
        grid.Children.Add(lower);
        Content = grid;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; if (commitButton.IsEnabled) Commit(); }
        };
        OnRepoChanged(repo.Root, Reload);
        UpdateFirstLine();
        UpdateCommitEnabled();
    }

    protected internal override void OnShown()
    {
        message.Focus();
        Reload();
        var repo = model.Repo;
        if (repo.OperationState() != RepoOperationState.None && repo.MergeMessage() is { } m)
            message.Text = string.Join("\n", m.Replace("\r\n", "\n").Split('\n').Where(l => !l.StartsWith('#'))).Trim();
        var n = repo.Config("user.name");
        var e = repo.Config("user.email");
        needsIdentity = n.Length == 0 || e.Length == 0;
        userName.Text = n;
        userEmail.Text = e;
        identityBox.Visibility = needsIdentity ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Reload()
    {
        loading.Visibility = Visibility.Visible;
        try { await model.Reload(files); }
        finally { loading.Visibility = Visibility.Collapsed; }
        UpdateCount();
        UpdatePreview();
    }

    private void UpdateCount() => countText.Text = $"{files.CheckedEntries.Count} / {files.Entries.Count} 選択";

    private void UpdateFirstLine()
    {
        var first = message.Text.Replace("\r", "").Split('\n')[0];
        firstLineInfo.Text = $"1行目: {first.Length} 文字";
        firstLineInfo.Foreground = first.Length > 72 ? System.Windows.Media.Brushes.DarkOrange : Ui.Secondary();
    }

    private void UpdateCommitEnabled() => commitButton.IsEnabled = message.Text.Trim().Length > 0;

    private void ShowRecent()
    {
        var menu = new ContextMenu { PlacementTarget = recentButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var list = AppSettings.Shared.RecentMessages;
        if (list.Count == 0) menu.Items.Add(new MenuItem { Header = "(履歴はありません)", IsEnabled = false });
        foreach (var m in list)
        {
            var title = m.Replace("\r", "").Replace("\n", " ");
            if (title.Length > 80) title = title[..80] + "…";
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => message.Text = m;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>Readable equivalent of the commit steps for the learning-mode preview.</summary>
    private void UpdatePreview()
    {
        var sel = files.CheckedEntries;
        var o = new List<IReadOnlyList<string>>();
        if (newBranch.IsChecked == true) o.Add(["switch", "-c", branchName.Text.Trim().Length == 0 ? "<ブランチ名>" : branchName.Text.Trim()]);
        var addPaths = sel.Where(e => e.Y != ' ' || e.State == FileState.Untracked).Select(e => e.Path).ToList();
        if (addPaths.Count > 0) o.Add(["add", "-A", "--", .. addPaths]);
        var firstLine = message.Text.Replace("\r", "").Split('\n')[0];
        var commit = new List<string> { "commit", "-m", firstLine.Length == 0 ? "<メッセージ>" : firstLine };
        if (amend.IsChecked == true) commit.Add("--amend");
        if (setAuthor.IsChecked == true && author.Text.Length > 0) commit.Add($"--author={author.Text}");
        if (sel.Count == 0 && amend.IsChecked != true) commit.Add("--allow-empty");
        if (model.OpState != RepoOperationState.Merging && sel.Count > 0)
        {
            commit.Add("--");
            foreach (var e in sel) { commit.Add(e.Path); if (e.OrigPath != null) commit.Add(e.OrigPath); }
        }
        o.Add(commit);
        if (push.IsChecked == true) o.Add(["push"]);
        preview.Set(o, "実際には、ファイル一覧やメッセージは一時ファイル経由で渡します (--pathspec-from-file / -F)。");
    }

    private void AddSignOff()
    {
        var n = model.Repo.Config("user.name");
        var e = model.Repo.Config("user.email");
        var line = $"Signed-off-by: {n} <{e}>";
        if (message.Text.Contains(line)) return;
        var m = message.Text.TrimEnd('\r', '\n');
        m += m.Length == 0 ? line : (m.Contains("Signed-off-by:") ? "\n" : "\n\n") + line;
        message.Text = m;
    }

    private DateTimeOffset? SelectedDate()
    {
        if (setDate.IsChecked != true) return null;
        var d = datePicker.SelectedDate ?? DateTime.Today;
        if (TimeSpan.TryParse(timeBox.Text, out var t)) d = d.Date + t;
        return new DateTimeOffset(d);
    }

    private void Commit()
    {
        var repo = model.Repo;
        var selected = files.CheckedEntries;
        var text = message.Text.Replace("\r\n", "\n");
        if (text.Trim().Length == 0) return;
        if (selected.Count == 0 && amend.IsChecked != true && model.OpState != RepoOperationState.Merging)
        {
            if (!WindowManager.Confirm("ファイルが選択されていません", "変更を含まない空のコミットを作成しますか?", "空のコミットを作成")) return;
        }
        if (newBranch.IsChecked == true && branchName.Text.Trim().Length == 0)
        {
            WindowManager.Alert("ブランチ名を入力してください");
            return;
        }
        if (needsIdentity)
        {
            if (userName.Text.Trim().Length == 0 || userEmail.Text.Trim().Length == 0)
            {
                WindowManager.Alert("作成者の名前とメールアドレスを入力してください");
                return;
            }
            repo.Git(["config", "--global", "user.name", userName.Text.Trim()]);
            repo.Git(["config", "--global", "user.email", userEmail.Text.Trim()]);
        }
        var opts = new GitOps.CommitOptions(text)
        {
            Amend = amend.IsChecked == true,
            Author = setAuthor.IsChecked == true && author.Text.Trim().Length > 0 ? author.Text.Trim() : null,
            Date = SelectedDate(),
            NewBranch = newBranch.IsChecked == true ? branchName.Text.Trim() : null,
            Push = push.IsChecked == true,
        };
        var steps = GitOps.CommitSteps(repo, selected, opts);
        AppSettings.Shared.Remember(text);
        AppSettings.Shared.PushAfterCommit = push.IsChecked == true;
        var root = repo.Root;
        WindowManager.Shared.RunProgress(opts.Push ? "コミットしてプッシュ" : "コミット", root, steps,
            [new ProgressAction("ログを表示", () => WindowManager.Shared.Perform(GitAction.Log, [root]))]);
        Handle.Close();
    }
}

/// <summary>"Check for modifications" window.</summary>
public sealed class StatusView : GVView
{
    private readonly StatusModel model;
    private readonly FileList files = new(checkable: false);
    private readonly TextBlock count = new();
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Width = 60, Height = 4, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };

    public StatusView(Repo repo, IReadOnlyList<string> paths)
    {
        model = new StatusModel(repo, paths);
        count.Style = (Style)FindResource("Heading");
        var showUntracked = new CheckBox { Content = "未追跡を表示", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
        var showIgnored = new CheckBox { Content = "無視を表示", Margin = new Thickness(0, 0, 10, 0) };
        var whole = new CheckBox { Content = "プロジェクト全体", Margin = new Thickness(0, 0, 10, 0) };
        showUntracked.Click += (_, _) => { model.ShowUntracked = showUntracked.IsChecked == true; Reload(); };
        showIgnored.Click += (_, _) => { model.ShowIgnored = showIgnored.IsChecked == true; Reload(); };
        whole.Click += (_, _) => { model.WholeProject = whole.IsChecked == true; Reload(); };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(showUntracked);
        right.Children.Add(showIgnored);
        if (model.HasPathFilter) right.Children.Add(whole);
        right.Children.Add(Ui.IconButton("", "再読み込み", Reload));
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(Ui.Row(count, loading));

        files.Open += model.OpenDiff;
        files.MenuBuilder = sel => model.WorkingTreeMenu(sel, Reload);

        var patch = new Button { Content = "パッチとして保存…", Margin = new Thickness(0, 0, 8, 0) };
        patch.Click += (_, _) => SavePatch();
        var stash = new Button { Content = "変更を退避…", Margin = new Thickness(0, 0, 8, 0) };
        stash.Click += (_, _) => WindowManager.Shared.Perform(GitAction.StashSave, [repo.Root]);
        var commit = new Button { Content = "コミット…", Margin = new Thickness(0, 0, 8, 0) };
        commit.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Commit, model.Paths.Count == 0 ? [repo.Root] : model.Paths.ToList());
        var close = new Button { Content = "閉じる", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Handle.Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var br = new StackPanel { Orientation = Orientation.Horizontal };
        br.Children.Add(patch); br.Children.Add(stash); br.Children.Add(commit); br.Children.Add(close);
        DockPanel.SetDock(br, Dock.Right);
        bottom.Children.Add(br);
        bottom.Children.Add(Ui.Caption("ダブルクリックで差分を表示 / 右クリックで操作"));

        var root = new DockPanel { Margin = new Thickness(14) };
        var rh = new RepoHeader(repo);
        DockPanel.SetDock(rh, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(rh);
        root.Children.Add(header);
        root.Children.Add(bottom);
        root.Children.Add(files);
        Content = root;
        OnRepoChanged(repo.Root, Reload);
    }

    protected internal override void OnShown() => Reload();

    private async void Reload()
    {
        loading.Visibility = Visibility.Visible;
        try { await model.Reload(files); }
        finally { loading.Visibility = Visibility.Collapsed; }
        count.Text = $"{files.Entries.Count} 件の変更";
    }

    private void SavePatch()
    {
        var repo = model.Repo;
        if (Shell.SaveFile("パッチとして保存", repo.Name + ".diff", "パッチ (*.diff;*.patch)|*.diff;*.patch|すべてのファイル|*.*", owner: OwnerWindow) is not { } file) return;
        var rels = model.Paths.Select(repo.Relative).Where(r => r.Length > 0).ToList();
        var r = repo.Git(["diff", "HEAD", "--binary", "--", .. rels]);
        try { File.WriteAllBytes(file, r.Stdout); }
        catch (Exception e) { WindowManager.Alert("保存できません", e.Message); }
    }
}
