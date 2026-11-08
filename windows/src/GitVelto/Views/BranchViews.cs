using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

public enum RemoteOpMode { Pull, Fetch, Push }
public enum CreateRefKind { Branch, Tag }

/// <summary>Layout helpers shared by the dialogs in this file.</summary>
file static class BV
{
    /// <summary>Bottom bar: optional left element, Cancel + primary button on the right.</summary>
    public static (DockPanel Bar, Button Ok) Footer(GVView view, string okTitle, Action onOk, UIElement? left = null, string cancel = "キャンセル")
    {
        var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var c = new Button { Content = cancel, MinWidth = 96, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        c.Click += (_, _) => view.Handle.Close();
        var ok = Ui.Accent(new Button { Content = okTitle, MinWidth = 96, IsDefault = true });
        ok.Click += (_, _) => onOk();
        right.Children.Add(c);
        right.Children.Add(ok);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        if (left != null)
        {
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
        }
        return (bar, ok);
    }

    /// <summary>Small secondary "xx とは" label with a glossary "?" button.</summary>
    public static StackPanel TermCaption(string text, string key)
    {
        var p = TermHelp.Label(text, key);
        if (p.Children[0] is TextBlock t)
        {
            t.FontSize = 12;
            t.Foreground = Ui.Secondary();
        }
        p.VerticalAlignment = VerticalAlignment.Center;
        return p;
    }

    /// <summary>Header on top, scrolling form in the middle, learning preview + footer at the bottom.</summary>
    public static DockPanel Layout(Repo repo, UIElement body, CommandPreview? preview, UIElement footer, double margin = 16)
    {
        var root = new DockPanel { Margin = new Thickness(margin) };
        var header = new RepoHeader(repo);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        if (preview != null)
        {
            DockPanel.SetDock(preview, Dock.Bottom);
            root.Children.Add(preview);
        }
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return root;
    }

    /// <summary>Two-column form grid (label / control).</summary>
    public static Grid FormGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        return g;
    }

    /// <summary>Adds a row; an empty label lets the control span both columns. Returns the label (null when empty).</summary>
    public static TextBlock? AddRow(Grid g, string label, UIElement control)
    {
        int row = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock? l = null;
        if (label.Length > 0)
        {
            l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            Grid.SetRow(l, row);
            g.Children.Add(l);
        }
        if (control is FrameworkElement f && f.Margin == default) f.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, label.Length > 0 ? 1 : 0);
        if (label.Length == 0) Grid.SetColumnSpan(control, 2);
        g.Children.Add(control);
        return l;
    }

    public static void Show(bool visible, params UIElement?[] elements)
    {
        foreach (var e in elements)
            if (e != null) e.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool On(CheckBox c) => c.IsChecked == true;

    /// <summary>Calls <paramref name="a"/> when the text of an editable combo box changes.</summary>
    public static void OnTextChanged(ComboBox c, Action a) =>
        c.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => a()));

    /// <summary>Check box followed by a "?" help button.</summary>
    public static StackPanel Help(CheckBox c, string key)
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        return TermHelp.With(c, key);
    }

    /// <summary>Selects <paramref name="value"/> in a plain combo box of strings, adding it when missing.</summary>
    public static void Select(ComboBox c, string value)
    {
        if (value.Length == 0) { c.SelectedIndex = -1; return; }
        if (!c.Items.Cast<object>().Any(i => i as string == value)) c.Items.Add(value);
        c.SelectedItem = value;
    }

    public static string Value(ComboBox c) => c.IsEditable ? c.Text.Trim() : c.SelectedItem as string ?? "";

    public static void Fill(ComboBox c, IEnumerable<string> items)
    {
        c.Items.Clear();
        foreach (var i in items) c.Items.Add(i);
    }

    public static TextBox MultiLine(int minLines, int maxLines) => new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = minLines, MaxLines = maxLines,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
}

// MARK: - Pull / Fetch / Push

public sealed class RemoteOpView : GVView
{
    private readonly Repo repo;
    private readonly RemoteOpMode mode;
    private List<string> remotes = [];
    private List<RefInfo> refs = [];
    private bool loaded, suppress;
    private int upstreamVersion;

    private readonly ComboBox localBranch = new() { MinWidth = 220 };
    private readonly ComboBox remote = new() { MinWidth = 180 };
    // Editable with the remote's branches as suggestions (the macOS version uses a text field + menu).
    private readonly ComboBox remoteBranch = new() { IsEditable = true, IsTextSearchEnabled = false, MinWidth = 220 };
    private readonly CheckBox allRemotes = new() { Content = "すべてのリモート (--all)" };
    private readonly CheckBox rebase = new() { Content = "リベースする (--rebase)" };
    private readonly CheckBox autostash = new() { Content = "ローカルの変更を自動退避 (--autostash)", IsChecked = true };
    private readonly CheckBox prune = new() { Content = "削除されたリモートブランチを整理 (--prune)" };
    private readonly CheckBox tags = new();
    private readonly CheckBox setUpstream = new() { Content = "上流ブランチとして設定 (-u)" };
    private readonly CheckBox force = new() { Content = "強制プッシュ (--force-with-lease)" };
    private readonly CheckBox ffOnly = new() { Content = "早送りのみ (--ff-only)" };
    private readonly CheckBox allBranches = new() { Content = "すべてのブランチをプッシュ (--all)" };
    private readonly TextBox newRemoteName = new() { Text = "origin" };
    private readonly TextBox newRemoteUrl = new();
    private readonly Button addRemote = new() { Content = "リモートを追加", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly GroupBox noRemoteBox;
    private readonly Grid form;
    private readonly CommandPreview preview = new();
    private readonly Button ok;

    public RemoteOpView(Repo repo, RemoteOpMode mode)
    {
        this.repo = repo;
        this.mode = mode;
        tags.Content = mode == RemoteOpMode.Fetch ? "すべてのタグを取得 (--tags)" : "タグもプッシュ (--tags)";

        // --- no remote yet ---
        var addGrid = BV.FormGrid();
        BV.AddRow(addGrid, "名前", newRemoteName);
        BV.AddRow(addGrid, "URL (https://… / git@…)", newRemoteUrl);
        BV.AddRow(addGrid, "", addRemote);
        noRemoteBox = new GroupBox { Header = "リモートが登録されていません", Content = addGrid, Padding = new Thickness(8), Visibility = Visibility.Collapsed };
        newRemoteName.TextChanged += (_, _) => UpdateUi();
        newRemoteUrl.TextChanged += (_, _) => UpdateUi();
        addRemote.Click += (_, _) => AddRemote();

        // --- form ---
        form = BV.FormGrid();
        form.Visibility = Visibility.Collapsed;
        if (mode == RemoteOpMode.Push) BV.AddRow(form, "ローカルブランチ", localBranch);
        var manage = new Button { Content = "管理…", Margin = new Thickness(8, 0, 0, 0) };
        manage.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Remotes, [repo.Root]);
        var remoteRow = new DockPanel();
        DockPanel.SetDock(manage, Dock.Right);
        remoteRow.Children.Add(manage);
        remoteRow.Children.Add(remote);
        BV.AddRow(form, "リモート", remoteRow);
        if (mode != RemoteOpMode.Fetch) BV.AddRow(form, "リモートブランチ", remoteBranch);
        var opts = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Opt(UIElement e) { if (e is FrameworkElement f) f.Margin = new Thickness(0, 3, 0, 3); opts.Children.Add(e); }
        switch (mode)
        {
            case RemoteOpMode.Pull:
                Opt(BV.Help(rebase, "rebase"));
                Opt(BV.Help(ffOnly, "fast-forward"));
                Opt(BV.Help(autostash, "autostash"));
                Opt(BV.Help(prune, "prune"));
                break;
            case RemoteOpMode.Fetch:
                Opt(allRemotes);
                Opt(BV.Help(prune, "prune"));
                Opt(BV.Help(tags, "tag"));
                break;
            case RemoteOpMode.Push:
                Opt(allBranches);
                Opt(BV.Help(setUpstream, "upstream"));
                Opt(BV.Help(force, "force-push"));
                Opt(BV.Help(tags, "tag"));
                break;
        }
        BV.AddRow(form, "", opts);

        foreach (var c in new[] { allRemotes, rebase, autostash, prune, tags, setUpstream, force, ffOnly, allBranches })
            c.Click += (_, _) => UpdateUi();
        localBranch.SelectionChanged += (_, _) => { if (!suppress) UpdateRemoteBranchDefault(); };
        remote.SelectionChanged += (_, _) => { if (!suppress) { FillRemoteBranches(); UpdateUi(); } };
        BV.OnTextChanged(remoteBranch, UpdateUi);

        var body = new StackPanel();
        body.Children.Add(noRemoteBox);
        body.Children.Add(form);

        var (label, key) = mode switch
        {
            RemoteOpMode.Pull => ("プルとは", "pull"),
            RemoteOpMode.Fetch => ("フェッチとは", "fetch"),
            _ => ("プッシュとは", "push"),
        };
        (var footer, ok) = BV.Footer(this, OkTitle, Run, BV.TermCaption(label, key));
        ok.IsEnabled = false;
        Content = BV.Layout(repo, body, preview, footer);
        // Pick up a remote added from the "管理…" window.
        OnRepoChanged(repo.Root, () => { if (loaded && remotes.Count == 0) Load(); });
    }

    protected internal override void OnShown() => Load();

    private string OkTitle => mode switch { RemoteOpMode.Pull => "プル", RemoteOpMode.Fetch => "フェッチ", _ => "プッシュ" };

    private string RemoteName => remote.SelectedItem as string ?? "";
    private string LocalName => localBranch.SelectedItem as string ?? "";
    private string RemoteBranchName => remoteBranch.Text.Trim();

    /// <summary>The git command this dialog runs (also shown in the learning-mode preview).</summary>
    private List<string> CommandArgs()
    {
        List<string> args;
        string rb = RemoteBranchName;
        switch (mode)
        {
            case RemoteOpMode.Pull:
                args = ["pull", "--progress", BV.On(rebase) ? "--rebase" : "--no-rebase"];
                if (BV.On(autostash)) args.Add("--autostash");
                if (BV.On(prune)) args.Add("--prune");
                if (BV.On(ffOnly) && !BV.On(rebase)) args.Add("--ff-only");
                args.Add(RemoteName);
                if (rb.Length > 0) args.Add(rb);
                break;
            case RemoteOpMode.Fetch:
                args = ["fetch", "--progress"];
                if (BV.On(prune)) args.Add("--prune");
                if (BV.On(tags)) args.Add("--tags");
                args.Add(BV.On(allRemotes) ? "--all" : RemoteName);
                break;
            default:
                args = ["push", "--progress"];
                if (BV.On(setUpstream)) args.Add("-u");
                if (BV.On(force)) args.Add("--force-with-lease");
                if (BV.On(tags)) args.Add("--tags");
                if (BV.On(allBranches)) args.AddRange(["--all", RemoteName]);
                else args.AddRange([RemoteName, $"{LocalName}:{(rb.Length == 0 ? LocalName : rb)}"]);
                break;
        }
        return args;
    }

    private async void Load()
    {
        var (rs, rf, cur, up) = await Task.Run(() =>
        {
            var rs = repo.Remotes();
            var rf = repo.Refs();
            var cur = repo.CurrentBranch() ?? "";
            var up = cur.Length > 0 ? repo.Upstream(cur) : null;
            return (rs, rf, cur, up);
        });
        remotes = rs;
        refs = rf;
        suppress = true;
        BV.Fill(localBranch, refs.Where(r => r.Kind == RefKind.Local).Select(r => r.Name));
        BV.Fill(remote, remotes);
        BV.Select(localBranch, cur);
        if (up is { } u)
        {
            BV.Select(remote, u.Remote);
            FillRemoteBranches();
            remoteBranch.Text = u.Branch;
        }
        else
        {
            BV.Select(remote, remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault() ?? "");
            FillRemoteBranches();
            remoteBranch.Text = cur;
            setUpstream.IsChecked = true;
        }
        suppress = false;
        loaded = true;
        UpdateUi();
    }

    /// <summary>Suggestions for the remote branch: branches of the selected remote.</summary>
    private void FillRemoteBranches()
    {
        var text = remoteBranch.Text;
        var prefix = RemoteName + "/";
        BV.Fill(remoteBranch, refs.Where(r => r.Kind == RefKind.Remote && r.Name.StartsWith(prefix, StringComparison.Ordinal)).Select(r => r.Name[prefix.Length..]));
        remoteBranch.Text = text;
    }

    private async void UpdateRemoteBranchDefault()
    {
        var lb = LocalName;
        int v = ++upstreamVersion;
        var up = await Task.Run(() => lb.Length > 0 ? repo.Upstream(lb) : null);
        if (v != upstreamVersion) return;
        suppress = true;
        if (up is { } u)
        {
            BV.Select(remote, u.Remote);
            FillRemoteBranches();
            remoteBranch.Text = u.Branch;
            setUpstream.IsChecked = false;
        }
        else
        {
            remoteBranch.Text = lb;
            setUpstream.IsChecked = true;
        }
        suppress = false;
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (!loaded) return;
        bool has = remotes.Count > 0;
        BV.Show(!has, noRemoteBox);
        BV.Show(has, form);
        addRemote.IsEnabled = newRemoteName.Text.Length > 0 && newRemoteUrl.Text.Length > 0;
        ffOnly.IsEnabled = !BV.On(rebase);
        remote.IsEnabled = !(mode == RemoteOpMode.Fetch && BV.On(allRemotes));
        ok.IsEnabled = has;
        preview.Set(has ? new List<IReadOnlyList<string>> { CommandArgs() } : []);
    }

    private async void AddRemote()
    {
        List<string> args = ["remote", "add", newRemoteName.Text, newRemoteUrl.Text];
        var r = await Task.Run(() => repo.Git(args));
        CommandHistory.Shared.Record(args, repo.Root, r.Ok);
        if (!r.Ok) WindowManager.Alert("リモートを追加できません", r.Message);
        Load();
        if (r.Ok) WindowManager.NotifyRepoChanged(repo.Root);
    }

    private void Run()
    {
        if (remotes.Count == 0) return;
        if (mode == RemoteOpMode.Push)
        {
            if (LocalName.Length == 0) return;
            if (BV.On(force) && !WindowManager.Confirm("強制プッシュしますか?", "リモートの履歴が上書きされる可能性があります。", "強制プッシュ", destructive: true)) return;
        }
        var args = CommandArgs();
        var root = repo.Root;
        WindowManager.Shared.RunProgress(OkTitle, root, [ProgressStep.Git(args)],
            [new ProgressAction("ログを表示", () => WindowManager.Shared.Perform(GitAction.Log, [root]))]);
        Handle.Close();
    }
}

// MARK: - Switch / Checkout

public sealed class SwitchView : GVView
{
    private enum Kind { Branch, Tag, Commit }

    private readonly Repo repo;
    private List<RefInfo> refs = [];
    private Kind kind = Kind.Branch;
    private bool loaded;

    private readonly RefPicker branchPicker = new([], tags: false);
    private readonly RefPicker tagPicker = new([], local: false, remote: false);
    private readonly TextBox commit = new();
    private readonly TextBlock? branchLabel, tagLabel, commitLabel, newBranchLabel;
    private readonly CheckBox createBranch = new() { Content = "新しいブランチを作成" };
    private readonly TextBox newBranch = new();
    private readonly CheckBox force = new() { Content = "ローカルの変更を破棄して切り替え (--discard-changes)" };
    private readonly StackPanel detachedHint;
    private readonly CommandPreview preview = new();

    public SwitchView(Repo repo)
    {
        this.repo = repo;
        var g = BV.FormGrid();
        var kinds = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (k, title) in new[] { (Kind.Branch, "ブランチ"), (Kind.Tag, "タグ"), (Kind.Commit, "コミット") })
        {
            var rb = new RadioButton { Content = title, GroupName = "switch-kind", IsChecked = k == Kind.Branch, Margin = new Thickness(0, 0, 16, 0), MinWidth = 0 };
            rb.Checked += (_, _) => { kind = k; UpdateUi(); };
            kinds.Children.Add(rb);
        }
        BV.AddRow(g, "種類", kinds);
        branchLabel = BV.AddRow(g, "切り替え先", branchPicker);
        tagLabel = BV.AddRow(g, "タグ", tagPicker);
        commitLabel = BV.AddRow(g, "コミット (ハッシュ等)", commit);
        BV.AddRow(g, "", BV.Help(createBranch, "branch"));
        newBranchLabel = BV.AddRow(g, "新しいブランチ名", newBranch);
        BV.AddRow(g, "", force);
        var hint = Ui.Caption("ブランチを作らない場合は HEAD 切り離しになります");
        hint.VerticalAlignment = VerticalAlignment.Center;
        detachedHint = new StackPanel { Orientation = Orientation.Horizontal, Children = { hint, new TermHelp("detached") } };
        BV.AddRow(g, "", detachedHint);

        branchPicker.SelectionChanged += (_, _) => { if (loaded) { UpdateDefaults(); UpdateUi(); } };
        BV.OnTextChanged(branchPicker, () => { if (loaded) UpdateUi(); });
        tagPicker.SelectionChanged += (_, _) => UpdateUi();
        BV.OnTextChanged(tagPicker, UpdateUi);
        commit.TextChanged += (_, _) => UpdateUi();
        createBranch.Click += (_, _) => UpdateUi();
        newBranch.TextChanged += (_, _) => UpdateUi();
        force.Click += (_, _) => UpdateUi();

        var (footer, _) = BV.Footer(this, "切り替え", Run, BV.TermCaption("切り替えとは", "switch"));
        Content = BV.Layout(repo, g, preview, footer);
        UpdateUi();
    }

    protected internal override async void OnShown()
    {
        refs = await Task.Run(repo.Refs);
        branchPicker.SetRefs(refs, tags: false);
        tagPicker.SetRefs(refs, local: false, remote: false);
        branchPicker.Value = refs.FirstOrDefault(r => r.Kind == RefKind.Local && !r.IsHead)?.Name ?? refs.FirstOrDefault()?.Name ?? "";
        loaded = true;
        UpdateDefaults();
        UpdateUi();
    }

    private string Target => kind switch
    {
        Kind.Branch => branchPicker.Value,
        Kind.Tag => tagPicker.Value,
        _ => commit.Text.Trim(),
    };

    private bool IsRemote
    {
        get
        {
            if (kind != Kind.Branch) return false;
            var t = Target;
            return refs.FirstOrDefault(r => r.Name == t)?.Kind == RefKind.Remote;
        }
    }

    private void UpdateDefaults()
    {
        if (IsRemote)
        {
            var target = branchPicker.Value;
            var short_ = string.Join("/", target.Split('/').Skip(1));
            bool exists = refs.Any(r => r.Kind == RefKind.Local && r.Name == short_);
            createBranch.IsChecked = !exists;
            newBranch.Text = short_;
        }
        else
        {
            createBranch.IsChecked = false;
        }
    }

    private List<string>? CommandArgs()
    {
        var reference = Target;
        if (reference.Length == 0) return null;
        var args = new List<string> { "switch" };
        if (BV.On(force)) args.Add("--discard-changes");
        if (BV.On(createBranch))
        {
            var n = newBranch.Text.Trim();
            args.AddRange(["-c", n.Length == 0 ? "<新しいブランチ名>" : n]);
            if (IsRemote) args.Add("--track");
            args.Add(reference);
        }
        else if (kind == Kind.Branch && !IsRemote)
        {
            args.Add(reference);
        }
        else
        {
            args.AddRange(["--detach", reference]);
        }
        return args;
    }

    private void UpdateUi()
    {
        BV.Show(kind == Kind.Branch, branchLabel, branchPicker);
        BV.Show(kind == Kind.Tag, tagLabel, tagPicker);
        BV.Show(kind == Kind.Commit, commitLabel, commit);
        BV.Show(BV.On(createBranch), newBranchLabel, newBranch);
        BV.Show((kind != Kind.Branch || IsRemote) && !BV.On(createBranch), detachedHint);
        preview.Set(CommandArgs() is { } a ? new List<IReadOnlyList<string>> { a } : []);
    }

    private void Run()
    {
        if (BV.On(createBranch) && newBranch.Text.Trim().Length == 0) { WindowManager.Alert("ブランチ名を入力してください"); return; }
        if (CommandArgs() is not { } args) return;
        WindowManager.Shared.RunProgress("切り替え", repo.Root, [ProgressStep.Git(args)]);
        Handle.Close();
    }
}

// MARK: - Create branch / tag

public sealed class CreateRefView : GVView
{
    private readonly Repo repo;
    private readonly CreateRefKind kind;
    private readonly string? baseRef;
    private List<RefInfo> refs = [];
    private string? firstRemote;
    private int baseKind; // 0 HEAD, 1 ref, 2 commit

    private readonly TextBox name = new();
    private readonly RadioButton[] baseRadios;
    private readonly RefPicker refPicker = new([]);
    private readonly TextBox baseCommit = new();
    private readonly TextBlock? refLabel, commitLabel;
    private readonly CheckBox switchAfter = new() { Content = "作成後に新しいブランチへ切り替え", IsChecked = true };
    private readonly CheckBox force = new();
    private readonly TextBox message = BV.MultiLine(2, 4);
    private readonly CheckBox pushTag = new() { Content = "作成後にリモートへプッシュ" };
    private readonly CommandPreview preview = new();
    private readonly Button ok;

    public CreateRefView(Repo repo, CreateRefKind kind, string? baseRef)
    {
        this.repo = repo;
        this.kind = kind;
        this.baseRef = baseRef;
        bool isBranch = kind == CreateRefKind.Branch;
        force.Content = isBranch ? "既存のブランチを上書き (-f)" : "既存のタグを上書き (-f)";

        var g = BV.FormGrid();
        var nameRow = new DockPanel();
        var help = new TermHelp(isBranch ? "branch" : "tag");
        DockPanel.SetDock(help, Dock.Right);
        nameRow.Children.Add(help);
        nameRow.Children.Add(name);
        BV.AddRow(g, isBranch ? "ブランチ名" : "タグ名", nameRow);

        var radios = new StackPanel();
        baseRadios = new[] { "HEAD (現在の作業コピー)", "ブランチ/タグ", "コミット" }
            .Select((t, i) =>
            {
                var rb = new RadioButton { Content = t, GroupName = "create-ref-base", IsChecked = i == 0, Margin = new Thickness(0, 2, 0, 2) };
                rb.Checked += (_, _) => { baseKind = i; UpdateUi(); };
                radios.Children.Add(rb);
                return rb;
            }).ToArray();
        BV.AddRow(g, "作成元", radios);
        refLabel = BV.AddRow(g, "参照", refPicker);
        commitLabel = BV.AddRow(g, "コミット", baseCommit);
        if (isBranch)
        {
            BV.AddRow(g, "", BV.Help(switchAfter, "switch"));
            BV.AddRow(g, "", force);
        }
        else
        {
            BV.AddRow(g, "メッセージ (入力すると注釈付きタグ)", message);
            BV.AddRow(g, "", force);
            BV.AddRow(g, "", pushTag);
        }

        name.TextChanged += (_, _) => UpdateUi();
        refPicker.SelectionChanged += (_, _) => UpdateUi();
        BV.OnTextChanged(refPicker, UpdateUi);
        baseCommit.TextChanged += (_, _) => UpdateUi();
        message.TextChanged += (_, _) => UpdateUi();
        foreach (var c in new[] { switchAfter, force, pushTag }) c.Click += (_, _) => UpdateUi();

        (var footer, ok) = BV.Footer(this, "作成", Run);
        Content = BV.Layout(repo, g, preview, footer);
        if (baseRef != null)
        {
            baseCommit.Text = baseRef;
            baseRadios[2].IsChecked = true;
        }
        UpdateUi();
        Loaded += (_, _) => name.Focus();
    }

    protected internal override async void OnShown()
    {
        var (rf, rm) = await Task.Run(() => (repo.Refs(), repo.Remotes().FirstOrDefault()));
        refs = rf;
        firstRemote = rm;
        refPicker.SetRefs(refs);
        refPicker.Value = refs.FirstOrDefault()?.Name ?? "";
        UpdateUi();
    }

    private List<List<string>> CommandList()
    {
        var typed = name.Text.Trim();
        var n = typed.Length == 0 ? (kind == CreateRefKind.Branch ? "<ブランチ名>" : "<タグ名>") : typed;
        string? start = baseKind == 0 ? null : baseKind == 1 ? refPicker.Value : baseCommit.Text;
        List<string> startArg = start != null ? [start] : [];
        bool f = BV.On(force);
        var output = new List<List<string>>();
        if (kind == CreateRefKind.Branch)
        {
            if (BV.On(switchAfter)) output.Add(["switch", f ? "-C" : "-c", n, .. startArg]);
            else output.Add(["branch", .. (f ? new[] { "-f" } : []), n, .. startArg]);
        }
        else
        {
            var args = new List<string> { "tag" };
            if (f) args.Add("-f");
            if (message.Text.Length > 0) args.AddRange(["-a", "-m", message.Text]);
            args.Add(n);
            if (start != null) args.Add(start);
            output.Add(args);
            if (BV.On(pushTag) && firstRemote != null)
                output.Add(["push", "--progress", firstRemote, $"refs/tags/{n}", .. (f ? new[] { "--force" } : [])]);
        }
        return output;
    }

    private void UpdateUi()
    {
        BV.Show(baseKind == 1, refLabel, refPicker);
        BV.Show(baseKind == 2, commitLabel, baseCommit);
        ok.IsEnabled = name.Text.Trim().Length > 0;
        preview.Set(CommandList());
    }

    private void Run()
    {
        if (name.Text.Trim().Length == 0) return;
        var steps = CommandList().Select(c => ProgressStep.Git(c)).ToList();
        WindowManager.Shared.RunProgress(kind == CreateRefKind.Branch ? "ブランチを作成" : "タグを作成", repo.Root, steps);
        Handle.Close();
    }
}

// MARK: - Merge

public sealed class MergeView : GVView
{
    private readonly Repo repo;
    private readonly RefPicker target = new([]);
    private readonly CheckBox noFF = new() { Content = "早送りしない (--no-ff)" };
    private readonly CheckBox squash = new() { Content = "スカッシュ (--squash)" };
    private readonly CheckBox noCommit = new() { Content = "コミットしない (--no-commit)" };
    private readonly TextBox message = BV.MultiLine(2, 5);
    private readonly CommandPreview preview = new();
    private readonly Button ok;

    public MergeView(Repo repo)
    {
        this.repo = repo;
        var g = BV.FormGrid();
        BV.AddRow(g, "マージ元", target);
        BV.AddRow(g, "", BV.Help(noFF, "no-ff"));
        BV.AddRow(g, "", BV.Help(squash, "squash"));
        BV.AddRow(g, "", noCommit);
        BV.AddRow(g, "マージメッセージ (省略可)", message);
        // Inline "?" keeps the help button right after the (wrapping) text.
        var hint = Ui.Caption("競合が発生した場合は「競合を解決」から解決後にコミットしてください。");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 8, 0, 0);
        hint.Inlines.Add(new System.Windows.Documents.InlineUIContainer(new TermHelp("conflict")) { BaselineAlignment = BaselineAlignment.Center });
        BV.AddRow(g, "", hint);

        target.SelectionChanged += (_, _) => UpdateUi();
        BV.OnTextChanged(target, UpdateUi);
        foreach (var c in new[] { noFF, squash, noCommit }) c.Click += (_, _) => UpdateUi();
        message.TextChanged += (_, _) => UpdateUi();

        (var footer, ok) = BV.Footer(this, "マージ", Run, BV.TermCaption("マージとは", "merge"));
        Content = BV.Layout(repo, g, preview, footer);
        UpdateUi();
    }

    protected internal override async void OnShown()
    {
        var refs = await Task.Run(repo.Refs);
        target.SetRefs(refs.Where(r => !r.IsHead));
        target.Value = refs.FirstOrDefault(r => !r.IsHead && r.Kind == RefKind.Local)?.Name ?? refs.FirstOrDefault(r => !r.IsHead)?.Name ?? "";
        UpdateUi();
    }

    private List<string> MergeArgs()
    {
        var args = new List<string> { "merge", "--progress", "--no-edit" };
        if (BV.On(noFF)) args.Add("--no-ff");
        if (BV.On(squash)) args.Add("--squash");
        if (BV.On(noCommit)) args.Add("--no-commit");
        if (message.Text.Length > 0) args.AddRange(["-m", message.Text]);
        args.Add(target.Value);
        return args;
    }

    private void UpdateUi()
    {
        ok.IsEnabled = target.Value.Length > 0;
        preview.Set(MergeArgs());
    }

    private void Run()
    {
        if (target.Value.Length == 0) return;
        var args = MergeArgs();
        var root = repo.Root;
        WindowManager.Shared.RunProgress("マージ", root, [ProgressStep.Git(args)],
            [new ProgressAction("ログを表示", () => WindowManager.Shared.Perform(GitAction.Log, [root]))],
            okResult =>
            {
                if (!okResult && new Repo(root).OperationState() == RepoOperationState.Merging &&
                    WindowManager.Confirm("マージで競合が発生しました", "競合の解決画面を開きますか?", "開く"))
                    WindowManager.Shared.Perform(GitAction.Resolve, [root]);
            });
        Handle.Close();
    }
}

// MARK: - Stash

public sealed class StashSaveView : GVView
{
    private readonly Repo repo;
    private readonly TextBox message = new();
    private readonly CheckBox untracked = new() { Content = "未追跡ファイルも含める (-u)" };
    private readonly CheckBox keepIndex = new() { Content = "ステージ済みの変更は残す (--keep-index)" };
    private readonly CommandPreview preview = new();

    public StashSaveView(Repo repo)
    {
        this.repo = repo;
        var g = BV.FormGrid();
        BV.AddRow(g, "メッセージ (省略可)", message);
        BV.AddRow(g, "", BV.Help(untracked, "untracked"));
        BV.AddRow(g, "", BV.Help(keepIndex, "stage"));
        message.TextChanged += (_, _) => UpdatePreview();
        untracked.Click += (_, _) => UpdatePreview();
        keepIndex.Click += (_, _) => UpdatePreview();

        var (footer, _) = BV.Footer(this, "退避", () =>
        {
            WindowManager.Shared.RunProgress("変更を退避", repo.Root, [ProgressStep.Git(StashArgs())]);
            Handle.Close();
        }, BV.TermCaption("退避 (stash) とは", "stash"));
        Content = BV.Layout(repo, g, preview, footer);
        UpdatePreview();
        Loaded += (_, _) => message.Focus();
    }

    private List<string> StashArgs()
    {
        var args = new List<string> { "stash", "push" };
        if (BV.On(untracked)) args.Add("-u");
        if (BV.On(keepIndex)) args.Add("--keep-index");
        if (message.Text.Length > 0) args.AddRange(["-m", message.Text]);
        return args;
    }

    private void UpdatePreview() => preview.Set(StashArgs());
}

public sealed class StashListView : GVView
{
    private sealed class StashRow(StashEntry s)
    {
        public string Ref => s.Ref;
        public string Message => s.Message;
        public string Sub => $"{s.Ref}  {Ui.FormatDate(s.Date)}";
    }

    private sealed class FileRow(ChangedFile f)
    {
        public ChangedFile File => f;
        public string StateText => f.State.Label();
        public Brush Color => Ui.StateBrush(f.State);
        public string Path => f.DisplayPath;
    }

    private readonly Repo repo;
    private readonly ListBox stashList = new() { MinWidth = 260 };
    private readonly DataGrid files = new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
        HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None,
        RowHeight = 24, EnableRowVirtualization = true, MinWidth = 280,
    };
    private readonly Button apply = new() { Content = "適用" };
    private readonly Button pop = new() { Content = "復元して削除 (pop)" };
    private readonly Button drop = new() { Content = "削除" };
    private string? selectedRef;
    private bool suppress;
    private int filesVersion;

    public StashListView(Repo repo)
    {
        this.repo = repo;

        // --- stash list ---
        var item = new FrameworkElementFactory(typeof(StackPanel));
        item.SetValue(MarginProperty, new Thickness(0, 2, 0, 2));
        var msg = new FrameworkElementFactory(typeof(TextBlock));
        msg.SetBinding(TextBlock.TextProperty, new Binding(nameof(StashRow.Message)));
        msg.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        item.AppendChild(msg);
        var sub = new FrameworkElementFactory(typeof(TextBlock));
        sub.SetBinding(TextBlock.TextProperty, new Binding(nameof(StashRow.Sub)));
        sub.SetValue(TextBlock.FontSizeProperty, 11.0);
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        item.AppendChild(sub);
        stashList.ItemTemplate = new DataTemplate { VisualTree = item };
        stashList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(stashList, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(stashList, true);
        stashList.SelectionChanged += (_, _) =>
        {
            if (suppress) return;
            selectedRef = (stashList.SelectedItem as StashRow)?.Ref;
            LoadFiles();
        };

        // --- files ---
        var st = new FrameworkElementFactory(typeof(TextBlock));
        st.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileRow.StateText)));
        st.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(FileRow.Color)));
        st.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        files.Columns.Add(new DataGridTemplateColumn { Header = "状態", Width = 80, CellTemplate = new DataTemplate { VisualTree = st }, SortMemberPath = nameof(FileRow.StateText) });
        files.Columns.Add(new DataGridTextColumn { Header = "パス", Binding = new Binding(nameof(FileRow.Path)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        VirtualizingPanel.SetIsVirtualizing(files, true);
        files.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<DataGridRow>(d) is { Item: FileRow fr }) OpenDiff(fr.File);
        };
        files.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && files.SelectedItem is FileRow fr) { OpenDiff(fr.File); e.Handled = true; }
        };

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 220 });
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(files, 2);
        split.Children.Add(stashList);
        split.Children.Add(splitter);
        split.Children.Add(files);

        // --- buttons ---
        apply.Click += (_, _) => Act(["stash", "apply"], "退避を適用");
        pop.Click += (_, _) => Act(["stash", "pop"], "退避を復元");
        drop.Click += (_, _) =>
        {
            if (selectedRef is { } s && WindowManager.Confirm($"{s} を削除しますか?", "", "削除", destructive: true))
                Act(["stash", "drop"], "退避を削除");
        };
        var close = new Button { Content = "閉じる", MinWidth = 96, IsCancel = true };
        close.Click += (_, _) => Handle.Close();
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        var left = Ui.Row(apply, pop, drop);
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);

        var root = new DockPanel { Margin = new Thickness(14) };
        var header = new RepoHeader(repo);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(split);
        Content = root;
        UpdateButtons();
        OnRepoChanged(repo.Root, Load);
    }

    protected internal override void OnShown() => Load();

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        DependencyObject? cur = d;
        while (cur != null && cur is not T) cur = cur is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur);
        return cur as T;
    }

    private void OpenDiff(ChangedFile f)
    {
        if (selectedRef is not { } s) return;
        WindowManager.Shared.ShowDiff(repo, new DiffSource.Range($"{s}^1", s, f.Path, f.OldPath));
    }

    private async void Load()
    {
        var stashes = await Task.Run(repo.Stashes);
        var rows = stashes.Select(s => new StashRow(s)).ToList();
        suppress = true;
        stashList.ItemsSource = rows;
        var sel = rows.FirstOrDefault(r => r.Ref == selectedRef) ?? rows.FirstOrDefault();
        stashList.SelectedItem = sel;
        suppress = false;
        selectedRef = sel?.Ref;
        LoadFiles();
    }

    private async void LoadFiles()
    {
        UpdateButtons();
        int v = ++filesVersion;
        if (selectedRef is not { } s) { files.ItemsSource = null; return; }
        var list = await Task.Run(() => repo.ChangedFiles($"{s}^1", s));
        if (v != filesVersion) return;
        files.ItemsSource = list.Select(f => new FileRow(f)).ToList();
    }

    private void UpdateButtons()
    {
        bool has = selectedRef != null;
        apply.IsEnabled = has;
        pop.IsEnabled = has;
        drop.IsEnabled = has;
    }

    private void Act(List<string> args, string title)
    {
        if (selectedRef is not { } s) return;
        WindowManager.Shared.RunProgress(title, repo.Root, [ProgressStep.Git([.. args, s])], onFinish: _ =>
        {
            selectedRef = null;
            Load();
        });
    }
}
