using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Helpers local to the tool dialogs.</summary>
file static class ToolKit
{
    /// <summary>Cancel + primary button bar (Ui.Buttons) that also hands back the primary button.</summary>
    public static StackPanel Buttons(GVView view, string primary, Action onPrimary, out Button ok)
    {
        var p = Ui.Buttons(view, primary, onPrimary);
        ok = (Button)p.Children[^1];
        return p;
    }

    /// <summary>Text box + "選択…" button.</summary>
    public static DockPanel PathField(TextBox box, Action choose)
    {
        var d = new DockPanel();
        var b = new Button { Content = "選択…", Margin = new Thickness(8, 0, 0, 0) };
        b.Click += (_, _) => choose();
        DockPanel.SetDock(b, Dock.Right);
        d.Children.Add(b);
        d.Children.Add(box);
        return d;
    }

    public static TextBlock Secondary(string text) => new() { Text = text, Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center };

    public static T Spaced<T>(T e, double top = 8) where T : FrameworkElement
    {
        e.Margin = new Thickness(0, top, 0, 0);
        return e;
    }

    public static string Downloads()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var d = Path.Combine(home, "Downloads");
        return Directory.Exists(d) ? d : home;
    }
}

// MARK: - Bisect

/// <summary>Starts git bisect between a good and a bad commit.</summary>
public sealed class BisectStartView : GVView
{
    private readonly Repo repo;
    private readonly string good;
    private readonly RefPicker goodPicker = new([]);
    private readonly RefPicker badPicker = new([]);
    private readonly CommandPreview preview = new();
    private readonly Button startButton;

    public BisectStartView(Repo repo, string good = "", string bad = "HEAD")
    {
        this.repo = repo;
        this.good = good;
        goodPicker.Text = good;
        badPicker.Text = bad;
        foreach (var p in new[] { goodPicker, badPicker })
        {
            p.HorizontalAlignment = HorizontalAlignment.Stretch;
            p.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
            p.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        }

        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(Ui.Row(Ui.Heading("不具合が入り込んだコミットを二分探索で特定します。"), new TermHelp("bisect")));
        top.Children.Add(ToolKit.Spaced(Ui.Form(("良い (問題のない) コミット", goodPicker), ("悪い (問題のある) コミット", badPicker))));
        var note = Ui.Caption("開始後は、チェックアウトされたコミットを確認し、エクスプローラーの右クリックメニュー「二分探索: 良い / 悪い」で判定を進めます。");
        note.TextWrapping = TextWrapping.Wrap;
        top.Children.Add(ToolKit.Spaced(note));

        var buttons = ToolKit.Buttons(this, "開始", Start, out startButton);
        Content = BrowserToolKit.Layout(16, [top], null, [preview, buttons]);
        Update();
    }

    protected internal override async void OnShown()
    {
        var refs = await Task.Run(repo.Refs);
        string goodText = goodPicker.Text, badText = badPicker.Text;
        goodPicker.SetRefs(refs);
        badPicker.SetRefs(refs);
        goodPicker.Text = good.Length == 0 && goodText.Length == 0 ? (refs.FirstOrDefault(r => r.Kind == RefKind.Tag)?.Name ?? "") : goodText;
        badPicker.Text = badText;
        Update();
    }

    private string Good => goodPicker.Value;
    private string Bad => badPicker.Value;

    private void Update()
    {
        startButton.IsEnabled = Good.Length > 0 && Bad.Length > 0;
        preview.Set(new List<IReadOnlyList<string>>
        {
            new[] { "bisect", "start", Bad, Good }, new[] { "bisect", "good" }, new[] { "bisect", "bad" }, new[] { "bisect", "reset" },
        }, "1 行目を実行した後、確認結果に応じて good / bad を繰り返し、最後に reset で終了します。");
    }

    private void Start()
    {
        if (Good.Length == 0 || Bad.Length == 0) return;
        WindowManager.Shared.RunProgress("二分探索を開始", repo.Root, [ProgressStep.Git("bisect", "start", Bad, Good)]);
        Handle.Close();
    }
}

// MARK: - Export

/// <summary>git archive (ZIP / tar.gz / tar) of a revision.</summary>
public sealed class ExportView : GVView
{
    private static readonly string[] Formats = ["zip", "tar.gz", "tar"];
    private readonly Repo repo;
    private readonly TextBox rev = new();
    private readonly CheckBox prefix;
    private readonly TextBox output = new();
    private readonly CommandPreview preview = new();
    private readonly Button exportButton;
    private string format = "zip";

    public ExportView(Repo repo, string? revision = null)
    {
        this.repo = repo;
        var revisionText = string.IsNullOrWhiteSpace(revision) ? "HEAD" : revision;
        rev.Text = revisionText;
        output.Text = Path.Combine(ToolKit.Downloads(), $"{repo.Name}-{revisionText.Replace("/", "-")}.zip");
        prefix = new CheckBox { Content = $"フォルダ名 ({repo.Name}/) を付ける", IsChecked = true };

        var formats = BrowserToolKit.Segmented(["ZIP", "tar.gz", "tar"], 0, i =>
        {
            format = Formats[i];
            var o = output.Text;
            var ext = Path.GetExtension(o);
            var bas = ext.Length > 0 ? o[..^ext.Length] : o;
            if (bas.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) bas = bas[..^4];
            output.Text = bas + "." + format;
            Update();
        });
        formats.HorizontalAlignment = HorizontalAlignment.Left;
        rev.TextChanged += (_, _) => Update();
        output.TextChanged += (_, _) => Update();
        prefix.Click += (_, _) => Update();

        var outField = ToolKit.PathField(output, () =>
        {
            var name = $"{repo.Name}-{rev.Text.Trim().Replace("/", "-")}.{format}";
            var filter = format == "zip" ? "ZIP (*.zip)|*.zip" : format == "tar" ? "tar (*.tar)|*.tar" : "tar.gz (*.tar.gz)|*.tar.gz";
            var dir = Path.GetDirectoryName(output.Text);
            if (Shell.SaveFile("エクスポート先", name, filter + "|すべてのファイル|*.*", dir, OwnerWindow) is { } f) output.Text = f;
        });

        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(Ui.Form(("リビジョン", rev), ("形式", formats), ("", prefix), ("出力先", outField)));
        var buttons = ToolKit.Buttons(this, "エクスポート", Export, out exportButton);
        Content = BrowserToolKit.Layout(16, [top], null, [preview, buttons]);
        Update();
    }

    private List<string> Args()
    {
        var args = new List<string> { "archive", $"--format={format}", "-o", output.Text.Trim() };
        if (prefix.IsChecked == true) args.Add($"--prefix={repo.Name}/");
        args.Add(rev.Text.Trim());
        return args;
    }

    private void Update()
    {
        exportButton.IsEnabled = output.Text.Trim().Length > 0 && rev.Text.Trim().Length > 0;
        preview.Set(Args());
    }

    private void Export()
    {
        if (!exportButton.IsEnabled) return;
        var o = output.Text.Trim();
        WindowManager.Shared.RunProgress("エクスポート", repo.Root, [ProgressStep.Git(Args())],
            [new ProgressAction("エクスプローラーで表示", () => Shell.Reveal([o]))]);
        Handle.Close();
    }
}

// MARK: - Patches

/// <summary>Creates patch files (format-patch) or a diff of the uncommitted changes.</summary>
public sealed class CreatePatchView : GVView
{
    private readonly Repo repo;
    private readonly IReadOnlyList<string> commits;
    private int mode; // 0 last N, 1 since ref, 2 selected commits, 3 working tree diff
    private readonly TextBox countBox = new() { Text = "1", Width = 64, TextAlignment = TextAlignment.Right };
    private readonly TextBox since = new();
    private readonly CheckBox single = new() { Content = "1 つのファイルにまとめる" };
    private readonly TextBox outDir = new();
    private readonly CommandPreview preview = new();
    private readonly FrameworkElement countRow, sinceRow;
    private readonly Button createButton;

    public CreatePatchView(Repo repo, IReadOnlyList<string>? commits = null)
    {
        this.repo = repo;
        this.commits = commits ?? [];
        if (this.commits.Count > 0) mode = 2;
        outDir.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var modes = new List<(string label, int mode)> { ("直近のコミット", 0), ("指定の参照以降のコミット", 1) };
        if (this.commits.Count > 0) modes.Add(($"ログで選択したコミット ({this.commits.Count})", 2));
        modes.Add(("コミットされていない変更 (git diff)", 3));
        var radios = BrowserToolKit.RadioGroup(modes.Select(m => m.label).ToList(), modes.FindIndex(m => m.mode == mode),
            i => { mode = modes[i].mode; Update(); });

        var minus = new Button { Content = "−", MinWidth = 32, Margin = new Thickness(6, 0, 0, 0) };
        var plus = new Button { Content = "+", MinWidth = 32, Margin = new Thickness(4, 0, 0, 0) };
        minus.Click += (_, _) => countBox.Text = Math.Max(1, Count - 1).ToString();
        plus.Click += (_, _) => countBox.Text = Math.Min(500, Count + 1).ToString();
        countBox.TextChanged += (_, _) => Update();
        countRow = Ui.Row(new TextBlock { Text = "コミット数:" }, countBox, minus, plus);
        var sinceGrid = BrowserToolKit.Placeholder(since, "例: origin/main");
        sinceRow = Ui.Form(("参照 (例: origin/main)", sinceGrid));
        since.TextChanged += (_, _) => Update();
        single.Click += (_, _) => Update();
        outDir.TextChanged += (_, _) => Update();

        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(Ui.Row(ToolKit.Secondary("パッチ:"), new TermHelp("patch")));
        top.Children.Add(ToolKit.Spaced(new TextBlock { Text = "対象:" }));
        top.Children.Add(ToolKit.Spaced(radios, 4));
        top.Children.Add(ToolKit.Spaced(countRow));
        top.Children.Add(ToolKit.Spaced(sinceRow));
        top.Children.Add(ToolKit.Spaced(single));
        top.Children.Add(ToolKit.Spaced(Ui.Form(("保存先フォルダ", ToolKit.PathField(outDir, () =>
        {
            if (Shell.ChooseFolder("保存先フォルダ", outDir.Text, OwnerWindow) is { } d) outDir.Text = d;
        })))));

        var buttons = ToolKit.Buttons(this, "作成", Run, out createButton);
        Content = BrowserToolKit.Layout(16, [top], null, [preview, buttons]);
        Update();
    }

    private int Count => int.TryParse(countBox.Text.Trim(), out var n) ? Math.Clamp(n, 1, 500) : 1;

    private List<string> Range() => mode == 0 ? [$"-{Count}", "HEAD"] : [$"{since.Text.Trim()}..HEAD"];

    private void Update()
    {
        countRow.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        sinceRow.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        single.Visibility = mode != 3 ? Visibility.Visible : Visibility.Collapsed;
        createButton.IsEnabled = outDir.Text.Trim().Length > 0;

        var o = outDir.Text.Trim();
        List<IReadOnlyList<string>> cmds;
        switch (mode)
        {
            case 3: cmds = [new[] { "diff", "HEAD", "--binary" }]; break;
            case 2: cmds = commits.Reverse().Select(c => (IReadOnlyList<string>)new[] { "format-patch", "-1", c[..Math.Min(8, c.Length)], "-o", o }).ToList(); break;
            default:
                var range = mode == 0 ? [$"-{Count}", "HEAD"] : new List<string> { $"{(since.Text.Trim().Length == 0 ? "<参照>" : since.Text.Trim())}..HEAD" };
                cmds = [single.IsChecked == true ? ["format-patch", "--stdout", .. range] : (List<string>)["format-patch", "-o", o, .. range]];
                break;
        }
        preview.Set(cmds);
    }

    private void Run()
    {
        var repo = this.repo;
        var o = outDir.Text.Trim();
        if (o.Length == 0) return;
        var steps = new List<ProgressStep>();
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var oneFile = single.IsChecked == true;

        ProgressStep ToFile(List<string> args, string name) =>
            new ProgressStep.Custom("git " + string.Join(" ", args) + " > " + name, () =>
            {
                var r = repo.Git(args);
                if (!r.Ok) throw new GitException(r.Message);
                var file = Path.Combine(o, name);
                File.WriteAllBytes(file, r.Stdout);
                return "保存しました: " + file;
            });

        switch (mode)
        {
            case 3:
                steps.Add(ToFile(["diff", "HEAD", "--binary"], $"{repo.Name}-{stamp}.diff"));
                break;
            case 2:
                var ordered = commits.Reverse().ToList();
                if (oneFile)
                {
                    steps.Add(new ProgressStep.Custom($"パッチを作成 ({ordered.Count} コミット)", () =>
                    {
                        using var ms = new MemoryStream();
                        foreach (var h in ordered)
                        {
                            var r = repo.Git(["format-patch", "-1", "--stdout", h]);
                            if (!r.Ok) throw new GitException(r.Message);
                            ms.Write(r.Stdout);
                        }
                        var file = Path.Combine(o, $"{repo.Name}-{stamp}.patch");
                        File.WriteAllBytes(file, ms.ToArray());
                        return "保存しました: " + file;
                    }));
                }
                else
                {
                    for (int i = 0; i < ordered.Count; i++)
                        steps.Add(ProgressStep.Git("format-patch", "-1", ordered[i], "-o", o, $"--start-number={i + 1}"));
                }
                break;
            default:
                var range = Range();
                if (oneFile) steps.Add(ToFile(["format-patch", "--stdout", .. range], $"{repo.Name}-{stamp}.patch"));
                else steps.Add(ProgressStep.Git(new List<string> { "format-patch", "-o", o }.Concat(range)));
                break;
        }
        WindowManager.Shared.RunProgress("パッチを作成", repo.Root, steps,
            [new ProgressAction("エクスプローラーで表示", () => Shell.OpenFile(o))]);
        Handle.Close();
    }
}

/// <summary>Applies patch files with git am (as commits) or git apply (to the work tree).</summary>
public sealed class ApplyPatchView : GVView
{
    private readonly Repo repo;
    private readonly List<string> files;
    private readonly ListBox list = new();
    private readonly CheckBox threeWay = new() { Content = "3-way マージ (-3)", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox signOff = new() { Content = "Signed-off-by を追加", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox check = new() { Content = "確認のみ (--check)", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CommandPreview preview = new();
    private readonly Button applyButton;
    private bool useAm = true;

    public ApplyPatchView(Repo repo, IReadOnlyList<string> files)
    {
        this.repo = repo;
        this.files = files.ToList();

        var add = new Button { Content = "追加…", Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) =>
        {
            if (Shell.ChooseFiles("パッチファイルを追加", "パッチ (*.patch;*.diff)|*.patch;*.diff|すべてのファイル|*.*", repo.Root, true, OwnerWindow) is { } chosen)
            {
                this.files.AddRange(chosen);
                Fill();
            }
        };
        var clear = new Button { Content = "クリア" };
        clear.Click += (_, _) => { this.files.Clear(); Fill(); };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var hr = new StackPanel { Orientation = Orientation.Horizontal };
        hr.Children.Add(add);
        hr.Children.Add(clear);
        DockPanel.SetDock(hr, Dock.Right);
        header.Children.Add(hr);
        header.Children.Add(Ui.Heading("パッチファイル"));

        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

        var method = BrowserToolKit.RadioGroup(
        [
            "コミットとして適用 (git am) — format-patch で作成したパッチ",
            "作業ツリーに適用 (git apply) — 差分のみ",
        ], 0, i => { useAm = i == 0; Update(); });
        var methodBox = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        methodBox.Children.Add(new TextBlock { Text = "適用方法:", Margin = new Thickness(0, 0, 0, 2) });
        methodBox.Children.Add(method);

        var toggles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        toggles.Children.Add(new TermHelp("patch") { Margin = new Thickness(0, 0, 8, 0) });
        toggles.Children.Add(threeWay);
        toggles.Children.Add(signOff);
        toggles.Children.Add(check);
        foreach (var c in new[] { threeWay, signOff, check }) c.Click += (_, _) => Update();

        var buttons = ToolKit.Buttons(this, "適用", Run, out applyButton);
        Content = BrowserToolKit.Layout(14, [new RepoHeader(repo), header], list, [methodBox, preview, toggles, buttons]);
        Fill();
    }

    private void Fill()
    {
        list.Items.Clear();
        foreach (var f in files)
        {
            var row = new DockPanel();
            var dir = new TextBlock
            {
                Text = Path.GetDirectoryName(f) ?? "", Foreground = Ui.Secondary(), TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360, ToolTip = f,
            };
            DockPanel.SetDock(dir, Dock.Right);
            row.Children.Add(dir);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(new Image { Source = Shell.Icon(f), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            left.Children.Add(new TextBlock { Text = Path.GetFileName(f), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(left);
            list.Items.Add(new ListBoxItem { Content = row });
        }
        Update();
    }

    private List<string> ApplyArgs()
    {
        List<string> args;
        if (useAm)
        {
            args = ["am"];
            if (threeWay.IsChecked == true) args.Add("-3");
            if (signOff.IsChecked == true) args.Add("--signoff");
        }
        else
        {
            args = ["apply", "-v"];
            if (threeWay.IsChecked == true) args.Add("--3way");
            if (check.IsChecked == true) args.Add("--check");
        }
        return args;
    }

    private void Update()
    {
        signOff.Visibility = useAm ? Visibility.Visible : Visibility.Collapsed;
        check.Visibility = useAm ? Visibility.Collapsed : Visibility.Visible;
        applyButton.IsEnabled = files.Count > 0;
        if (files.Count == 0) preview.Set(Array.Empty<IReadOnlyList<string>>());
        else preview.Set([.. ApplyArgs(), .. files.Select(Path.GetFileName).OfType<string>()]);
    }

    private void Run()
    {
        if (files.Count == 0) return;
        var root = repo.Root;
        WindowManager.Shared.RunProgress("パッチを適用", root, [ProgressStep.Git(ApplyArgs().Concat(files))], onFinish: ok =>
        {
            if (ok || new Repo(root).OperationState() != RepoOperationState.ApplyingPatches) return;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (WindowManager.Confirm("パッチの適用で競合が発生しました", "競合の解決画面を開きますか? (続行/中止もそこから行えます)", "開く"))
                    WindowManager.Shared.Perform(GitAction.Resolve, [root]);
            });
        });
        Handle.Close();
    }
}

// MARK: - Submodules

/// <summary>git submodule add.</summary>
public sealed class SubmoduleAddView : GVView
{
    private readonly TextBox url = new();
    private readonly TextBox path = new();
    private readonly TextBox branch = new();
    private readonly Button addButton;
    private bool edited, settingPath;

    public SubmoduleAddView(Repo repo, string dir)
    {
        url.TextChanged += (_, _) =>
        {
            if (!edited)
            {
                var rel = repo.Relative(dir);
                var name = RepoName(url.Text);
                settingPath = true;
                path.Text = rel.Length == 0 ? name : rel + "/" + name;
                settingPath = false;
            }
            Update();
        };
        path.TextChanged += (_, _) => { if (!settingPath) edited = true; Update(); };

        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(Ui.Row(ToolKit.Secondary("サブモジュール:"), new TermHelp("submodule")));
        top.Children.Add(ToolKit.Spaced(Ui.Form(("リポジトリ URL", url), ("パス", path), ("ブランチ (省略可)", branch))));

        var buttons = ToolKit.Buttons(this, "追加", () =>
        {
            var u = url.Text.Trim();
            if (u.Length == 0 || path.Text.Trim().Length == 0) return;
            bool local = u.StartsWith('/') || u.StartsWith("file://") || u.StartsWith('.') || u.StartsWith('~') || u.StartsWith(@"\\")
                || Regex.IsMatch(u, @"^[A-Za-z]:[\\/]");
            // Recent git refuses local-path submodules unless the file transport is allowed explicitly.
            var args = new List<string>();
            if (local) args.AddRange(["-c", "protocol.file.allow=always"]);
            args.AddRange(["submodule", "add", "--progress"]);
            if (branch.Text.Trim().Length > 0) args.AddRange(["-b", branch.Text.Trim()]);
            args.AddRange(["--", ExpandTilde(u), path.Text.Trim()]);
            WindowManager.Shared.RunProgress("サブモジュールを追加", repo.Root, [ProgressStep.Git(args)]);
            Handle.Close();
        }, out addButton);
        Content = BrowserToolKit.Layout(16, [top], null, [buttons]);
        Update();
    }

    protected internal override void OnShown() => url.Focus();

    private void Update() => addButton.IsEnabled = url.Text.Trim().Length > 0 && path.Text.Trim().Length > 0;

    private static string ExpandTilde(string p) =>
        p == "~" || p.StartsWith("~/") || p.StartsWith(@"~\")
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..]
            : p;

    /// <summary>"https://host/user/name.git" → "name" (also handles scp-like and Windows paths).</summary>
    private static string RepoName(string url)
    {
        var s = url.Trim().TrimEnd('/', '\\');
        var name = s.Split(['/', ':', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

/// <summary>git submodule update for the checked submodules.</summary>
public sealed class SubmoduleUpdateView : GVView
{
    private readonly Repo repo;
    private readonly ListBox list = new();
    private readonly TextBlock empty = new() { Text = "サブモジュールはありません", Foreground = Ui.Secondary(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly List<(SubmoduleInfo info, CheckBox box)> items = [];
    private readonly CheckBox initialize = new() { Content = "初期化 (--init)", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox recursive = new() { Content = "再帰的 (--recursive)", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox remote = new() { Content = "リモートの最新にする (--remote)", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox force = new() { Content = "強制 (--force)", Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button updateButton;

    public SubmoduleUpdateView(Repo repo)
    {
        this.repo = repo;
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var body = new Grid();
        body.Children.Add(list);
        body.Children.Add(empty);

        var heading = Ui.Row(Ui.Heading("更新するサブモジュール"), new TermHelp("submodule"));
        heading.Margin = new Thickness(0, 0, 0, 6);
        var toggles = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var c in new[] { initialize, recursive, remote, force }) toggles.Children.Add(c);

        var buttons = ToolKit.Buttons(this, "更新", Run, out updateButton);
        Content = BrowserToolKit.Layout(14, [new RepoHeader(repo), heading], body, [toggles, buttons]);
        updateButton.IsEnabled = false;
    }

    protected internal override async void OnShown()
    {
        var subs = await Task.Run(repo.Submodules);
        items.Clear();
        list.Items.Clear();
        foreach (var s in subs)
        {
            var box = new CheckBox { Content = s.Path, IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) => UpdateEnabled();
            var row = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(new TextBlock
            {
                Text = s.StateLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0),
                Foreground = s.State == ' ' ? Ui.Secondary() : Brushes.DarkOrange,
            });
            right.Children.Add(new TextBlock { Text = s.Hash[..Math.Min(8, s.Hash.Length)], FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(box);
            list.Items.Add(new ListBoxItem { Content = row });
            items.Add((s, box));
        }
        empty.Visibility = subs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEnabled();
    }

    private void UpdateEnabled() => updateButton.IsEnabled = items.Any(i => i.box.IsChecked == true);

    private void Run()
    {
        var selected = items.Where(i => i.box.IsChecked == true).Select(i => i.info.Path).ToList();
        if (selected.Count == 0) return;
        var args = new List<string> { "-c", "protocol.file.allow=always", "submodule", "update", "--progress" };
        if (initialize.IsChecked == true) args.Add("--init");
        if (recursive.IsChecked == true) args.Add("--recursive");
        if (remote.IsChecked == true) args.Add("--remote");
        if (force.IsChecked == true) args.Add("--force");
        args.Add("--");
        args.AddRange(selected);
        WindowManager.Shared.RunProgress("サブモジュールを更新", repo.Root, [ProgressStep.Git(args)]);
        Handle.Close();
    }
}

// MARK: - Remotes / repository settings

/// <summary>Add / edit / remove remotes. Also embedded in <see cref="RepoSettingsView"/>.</summary>
public sealed class RemotesView : GVView
{
    private readonly Repo repo;
    private readonly ListBox list = new();
    private readonly TextBox name = new();
    private readonly TextBox url = new();
    private readonly TextBox pushUrl = new();
    private readonly TextBlock heading = Ui.Heading("");
    private readonly Button saveButton, fetchButton, pruneButton, removeButton;
    private List<RemoteInfo> remotes = [];
    private string? selection;
    private bool isNew, suppress, loaded;

    public RemotesView(Repo repo)
    {
        this.repo = repo;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) =>
        {
            if (suppress || list.SelectedItem is not ListBoxItem { Tag: string s }) return;
            selection = s;
            FillFields();
        };
        var add = Ui.IconButton("", "リモートを追加", () =>
        {
            isNew = true;
            selection = null;
            suppress = true;
            list.SelectedItem = null;
            suppress = false;
            name.Text = remotes.Count == 0 ? "origin" : "";
            url.Text = "";
            pushUrl.Text = "";
            UpdateState();
            name.Focus();
        });
        add.Margin = new Thickness(0, 0, 4, 0);
        removeButton = Ui.IconButton("", "リモートを削除", Remove);
        removeButton.Margin = new Thickness(0);
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        leftButtons.Children.Add(add);
        leftButtons.Children.Add(removeButton);
        var left = new DockPanel();
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        left.Children.Add(list);

        foreach (var t in new[] { name, url, pushUrl }) t.TextChanged += (_, _) => UpdateState();
        saveButton = Ui.Accent(new Button { Content = "保存", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) });
        saveButton.Click += (_, _) => Save();
        fetchButton = new Button { Content = "フェッチ", Margin = new Thickness(0, 0, 8, 0) };
        fetchButton.Click += (_, _) =>
        {
            if (selection is { } s) WindowManager.Shared.RunProgress("フェッチ", repo.Root, [ProgressStep.Git("fetch", "--progress", s)]);
        };
        pruneButton = new Button { Content = "整理 (prune)" };
        pruneButton.Click += (_, _) =>
        {
            if (selection is { } s) WindowManager.Shared.RunProgress("リモートブランチの整理", repo.Root, [ProgressStep.Git("remote", "prune", s)]);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(saveButton);
        actions.Children.Add(fetchButton);
        actions.Children.Add(pruneButton);

        var right = new StackPanel { Margin = new Thickness(12) };
        right.Children.Add(Ui.Row(heading, new TermHelp("remote"), new TermHelp("origin")));
        right.Children.Add(ToolKit.Spaced(Ui.Form(
            ("名前", BrowserToolKit.Placeholder(name, "origin")),
            ("URL", BrowserToolKit.Placeholder(url, "https://… または git@…")),
            ("プッシュ URL", BrowserToolKit.Placeholder(pushUrl, "省略時は URL と同じ")))));
        right.Children.Add(actions);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190), MinWidth = 160, MaxWidth = 260 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        left.Margin = new Thickness(8, 8, 0, 8);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(splitter);
        grid.Children.Add(right);
        Content = grid;

        // Loaded (not OnShown): this view is also hosted inside RepoSettingsView.
        Loaded += (_, _) => { if (!loaded) { loaded = true; Load(); } };
        UpdateState();
    }

    private async void Load()
    {
        remotes = await Task.Run(repo.RemotesDetailed);
        suppress = true;
        list.Items.Clear();
        foreach (var r in remotes)
        {
            var p = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            p.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold });
            p.Children.Add(new TextBlock { Text = r.FetchUrl, FontSize = 11, Foreground = Ui.Secondary(), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = r.FetchUrl });
            list.Items.Add(new ListBoxItem { Content = p, Tag = r.Name });
        }
        selection ??= remotes.FirstOrDefault()?.Name;
        if (selection != null && !remotes.Any(r => r.Name == selection)) selection = remotes.FirstOrDefault()?.Name;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == selection);
        suppress = false;
        if (selection != null) FillFields();
        if (remotes.Count == 0) { isNew = true; name.Text = "origin"; }
        UpdateState();
    }

    private void FillFields()
    {
        if (remotes.FirstOrDefault(r => r.Name == selection) is not { } r) return;
        isNew = false;
        name.Text = r.Name;
        url.Text = r.FetchUrl;
        pushUrl.Text = r.PushUrl == r.FetchUrl ? "" : r.PushUrl;
        UpdateState();
    }

    private void UpdateState()
    {
        heading.Text = isNew ? "新しいリモート" : "リモートの編集";
        saveButton.Content = isNew ? "追加" : "保存";
        saveButton.IsEnabled = name.Text.Trim().Length > 0 && url.Text.Trim().Length > 0;
        var existing = !isNew && selection != null ? Visibility.Visible : Visibility.Collapsed;
        fetchButton.Visibility = existing;
        pruneButton.Visibility = existing;
        removeButton.IsEnabled = selection != null;
    }

    private async Task<bool> Check(IEnumerable<string> args)
    {
        var r = await repo.GitAsync(args);
        if (!r.Ok) WindowManager.Alert("失敗しました", r.Message);
        return r.Ok;
    }

    private async void Save()
    {
        string n = name.Text.Trim(), u = url.Text.Trim(), pu = pushUrl.Text.Trim();
        if (n.Length == 0 || u.Length == 0) return;
        if (isNew)
        {
            if (!await Check(["remote", "add", n, u])) return;
        }
        else if (selection is { } old)
        {
            if (old != n && !await Check(["remote", "rename", old, n])) return;
            if (!await Check(["remote", "set-url", n, u])) return;
        }
        if (pu.Length > 0) await Check(["remote", "set-url", "--push", n, pu]);
        else if (!isNew) await repo.GitAsync(["config", "--unset-all", $"remote.{n}.pushurl"]);
        isNew = false;
        selection = n;
        WindowManager.NotifyRepoChanged(repo.Root);
        Load();
    }

    private async void Remove()
    {
        if (selection is not { } s) return;
        if (!WindowManager.Confirm($"リモート {s} を削除しますか?", "リモート追跡ブランチも削除されます。", "削除", true)) return;
        await Check(["remote", "remove", s]);
        selection = null;
        WindowManager.NotifyRepoChanged(repo.Root);
        Load();
    }
}

/// <summary>Per-repository settings: remotes and local git config.</summary>
public sealed class RepoSettingsView : GVView
{
    private readonly Repo repo;
    private readonly TextBox userName = new();
    private readonly TextBox userEmail = new();
    private readonly ComboBox autocrlf = Choice(("既定", ""), ("false", "false"), ("input", "input"), ("true", "true"));
    private readonly ComboBox pullRebase = Choice(("既定", ""), ("マージ (false)", "false"), ("リベース (true)", "true"));
    private readonly ComboBox ignoreCase = Choice(("既定", ""), ("true", "true"), ("false", "false"));
    private readonly TextBox hooksPath = new();

    public RepoSettingsView(Repo repo)
    {
        this.repo = repo;
        var remotes = new RemotesView(repo) { Margin = new Thickness(8) };
        var gitPane = BuildGitPane();
        var host = new ContentControl { Content = remotes };

        static StackPanel Label(string glyph, string text)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return p;
        }
        // Swap the content (instead of collapsing) so only the visible pane's default button reacts to Enter.
        var tabs = BrowserToolKit.Segmented([Label("", "リモート"), Label("", "Git")], 0,
            i => host.Content = i == 0 ? remotes : gitPane);
        tabs.HorizontalAlignment = HorizontalAlignment.Center;
        tabs.Margin = new Thickness(10);
        foreach (var c in tabs.Children.OfType<FrameworkElement>()) c.MinWidth = 120;

        var root = new DockPanel();
        var sep = new Separator();
        DockPanel.SetDock(tabs, Dock.Top);
        DockPanel.SetDock(sep, Dock.Top);
        root.Children.Add(tabs);
        root.Children.Add(sep);
        root.Children.Add(host);
        Content = root;
    }

    private static ComboBox Choice(params (string label, string value)[] items)
    {
        var c = new ComboBox { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (label, value) in items) c.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        c.SelectedIndex = 0;
        return c;
    }

    private static string Value(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private static void SetValue(ComboBox c, string v)
    {
        var item = c.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, v, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            // Keep values GitVelto does not list (e.g. "merges" for pull.rebase).
            item = new ComboBoxItem { Content = v, Tag = v };
            c.Items.Add(item);
        }
        c.SelectedItem = item;
    }

    private static TextBlock Section(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 14, 0, 4), FontWeight = FontWeights.SemiBold };
        if (Application.Current.TryFindResource("SectionHeader") is Style s) t.Style = s;
        return t;
    }

    private FrameworkElement BuildGitPane()
    {
        var p = new StackPanel { Margin = new Thickness(18, 4, 18, 14) };
        p.Children.Add(Section("このリポジトリのユーザー情報 (空欄ならグローバル設定)"));
        p.Children.Add(Ui.Form(("名前 (user.name)", userName), ("メール (user.email)", userEmail)));
        p.Children.Add(Section("動作"));
        p.Children.Add(Ui.Form(
            ("改行コード変換 (core.autocrlf)", autocrlf),
            ("プル時の動作 (pull.rebase)", pullRebase),
            ("大文字小文字を無視 (core.ignorecase)", ignoreCase),
            ("フックのパス (core.hooksPath)", hooksPath)));

        var save = Ui.Accent(new Button { Content = "保存", MinWidth = 90, IsDefault = true });
        save.Click += (_, _) => Save();
        var config = new Button { Content = ".git/config を編集" };
        config.Click += (_, _) => Shell.OpenInTextEditor(Path.Combine(repo.GitDir, "config"));
        var ignore = new Button { Content = ".gitignore を編集" };
        ignore.Click += (_, _) =>
        {
            var f = Path.Combine(repo.Root, ".gitignore");
            try { if (!File.Exists(f)) File.WriteAllBytes(f, []); }
            catch (Exception e) { WindowManager.Alert("作成できません", e.Message); return; }
            Shell.OpenInTextEditor(f);
        };
        var folder = new Button { Content = "フォルダを開く" };
        folder.Click += (_, _) => Shell.OpenFile(repo.Root);
        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        foreach (var b in new[] { save, config, ignore, folder }) { b.Margin = new Thickness(0, 0, 8, 6); buttons.Children.Add(b); }
        p.Children.Add(buttons);
        return new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    protected internal override async void OnShown()
    {
        string[] keys = ["user.name", "user.email", "core.autocrlf", "pull.rebase", "core.ignorecase", "core.hooksPath"];
        var v = await Task.Run(() => keys.Select(k => repo.Git(["config", "--local", "--get", k]).Out.Trim()).ToArray());
        userName.Text = v[0];
        userEmail.Text = v[1];
        SetValue(autocrlf, v[2]);
        SetValue(pullRebase, v[3]);
        SetValue(ignoreCase, v[4]);
        hooksPath.Text = v[5];
    }

    private async void Save()
    {
        var values = new (string key, string value)[]
        {
            ("user.name", userName.Text.Trim()), ("user.email", userEmail.Text.Trim()),
            ("core.autocrlf", Value(autocrlf)), ("pull.rebase", Value(pullRebase)),
            ("core.ignorecase", Value(ignoreCase)), ("core.hooksPath", hooksPath.Text.Trim()),
        };
        await Task.Run(() =>
        {
            foreach (var (k, v) in values)
            {
                if (v.Length == 0) repo.Git(["config", "--local", "--unset", k]);
                else repo.Git(["config", "--local", k, v]);
            }
        });
        Handle.Close();
    }
}
