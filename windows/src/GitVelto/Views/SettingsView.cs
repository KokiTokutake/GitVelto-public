using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using GitVelto.Core;
using Microsoft.Win32;

namespace GitVelto.UI;

/// <summary>Settings window (TortoiseGit "Settings"): general, context menu, overlays, git, external tools.</summary>
public sealed class SettingsView : GVView
{
    private readonly AppSettings s = AppSettings.Shared;
    private readonly TextBlock shellState = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox userName = new(), userEmail = new(), defaultBranch = new();
    private readonly ComboBox autocrlf = Combo(("", "未設定"), ("false", "false"), ("input", "input"), ("true", "true"));
    private readonly ComboBox pullRebase = Combo(("", "未設定"), ("false", "マージ (false)"), ("true", "リベース (true)"));
    private readonly ComboBox credential = Combo(("", "未設定"), ("manager", "Git Credential Manager (manager)"), ("wincred", "Windows 資格情報 (wincred)"), ("cache", "メモリに一時保存 (cache)"));
    private readonly TextBlock gitVersion = new() { Foreground = Ui.Secondary() };
    private readonly TextBlock gitInUse = new() { Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap };

    public SettingsView()
    {
        var tabs = new TabControl { Margin = new Thickness(10) };
        tabs.Items.Add(new TabItem { Header = "一般", Content = Scroll(General()) });
        tabs.Items.Add(new TabItem { Header = "コンテキストメニュー", Content = Scroll(MenuLayout()) });
        tabs.Items.Add(new TabItem { Header = "アイコンオーバーレイ", Content = Scroll(Overlays()) });
        tabs.Items.Add(new TabItem { Header = "Git", Content = Scroll(GitTab()) });
        tabs.Items.Add(new TabItem { Header = "外部ツール", Content = Scroll(Tools()) });
        Content = tabs;
    }

    protected internal override async void OnShown()
    {
        shellState.Text = ShellIntegration.StatusText();
        var (name, email, crlf, rebase, cred, branch, version) = await Task.Run(() => (
            Git.GlobalConfig("user.name"), Git.GlobalConfig("user.email"), Git.GlobalConfig("core.autocrlf"),
            Git.GlobalConfig("pull.rebase"), Git.GlobalConfig("credential.helper"), Git.GlobalConfig("init.defaultBranch"), Git.Version()));
        userName.Text = name; userEmail.Text = email; defaultBranch.Text = branch;
        Select(autocrlf, crlf); Select(pullRebase, rebase); Select(credential, cred);
        gitVersion.Text = version.Length > 0 ? version : "git が見つかりません";
        gitInUse.Text = Git.Executable;
    }

    // ---- helpers ----

    private static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 4, 10, 10) };

    private static ComboBox Combo(params (string value, string label)[] items)
    {
        var c = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, IsEditable = false };
        foreach (var (v, l) in items) c.Items.Add(new ComboBoxItem { Content = l, Tag = v });
        c.SelectedIndex = 0;
        return c;
    }

    private static void Select(ComboBox c, string value)
    {
        var item = c.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
        if (item == null && value.Length > 0) { item = new ComboBoxItem { Content = value, Tag = value }; c.Items.Add(item); }
        c.SelectedItem = item ?? c.Items[0];
    }

    private static string Value(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private static TextBlock Section(string text) => new() { Text = text, Style = (Style)Application.Current.FindResource("SectionHeader") };

    private static CheckBox Toggle(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        c.Click += (_, _) => set(c.IsChecked == true);
        return c;
    }

    private static Button Btn(string label, Action a)
    {
        var b = new Button { Content = label, Margin = new Thickness(0, 4, 8, 4) };
        b.Click += (_, _) => a();
        return b;
    }

    private static WrapPanel Wrap(params UIElement[] items)
    {
        var w = new WrapPanel();
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    // ---- tabs ----

    private StackPanel General()
    {
        var p = new StackPanel();
        p.Children.Add(Section("エクスプローラー統合"));
        p.Children.Add(Ui.Row(new TextBlock { Text = "状態:" }, shellState));
        p.Children.Add(Wrap(
            Btn("エクスプローラーに登録…", () => { ShellIntegration.Install(); shellState.Text = ShellIntegration.StatusText(); }),
            Btn("登録を解除…", () => { ShellIntegration.Uninstall(); shellState.Text = ShellIntegration.StatusText(); }),
            Btn("エクスプローラーを再起動", ShellIntegration.RestartExplorer)));
        p.Children.Add(Ui.Caption("エクスプローラーでファイルやフォルダを右クリック → 「Git 同期…」「Git コミット…」「GitVelto ▸」から操作できます。Windows 11 では「その他のオプションを確認」(Shift+F10) の中に表示されます。登録には管理者の許可が必要です。"));

        p.Children.Add(Section("学習"));
        p.Children.Add(Toggle("学習モード（実行される git コマンドとその意味を表示）", s.LearningMode, v => s.LearningMode = v));
        p.Children.Add(Ui.Caption("各ダイアログに「実行されるコマンド」とオプションの意味を表示し、進捗画面にも解説を出します。操作しながら git コマンドを覚えたい方向けです。"));
        p.Children.Add(Wrap(Btn("Git 用語集を開く", () => WindowManager.Shared.ShowGlossary()),
            Btn("実行したコマンドの履歴", () => WindowManager.Shared.ShowCommandHistory())));

        p.Children.Add(Section("一般"));
        p.Children.Add(Toggle("通知領域 (タスクトレイ) にアイコンを表示", s.ShowTrayIcon, v => s.ShowTrayIcon = v));
        p.Children.Add(Toggle("サインイン時に起動", StartupItem.Enabled, v => StartupItem.Enabled = v));
        var limit = new TextBox { Width = 80, Text = s.LogLimit.ToString() };
        limit.LostFocus += (_, _) => { if (int.TryParse(limit.Text, out var n)) s.LogLimit = Math.Clamp(n, 100, 5000); limit.Text = s.LogLimit.ToString(); };
        p.Children.Add(Ui.Row(new TextBlock { Text = "ログの読み込み件数 (100〜5000):" }, limit));
        p.Children.Add(Toggle("差分を左右に並べて表示", s.DiffSideBySide, v => s.DiffSideBySide = v));
        p.Children.Add(Toggle("クリーンアップ時にごみ箱へ移動", s.CleanToTrash, v => s.CleanToTrash = v));
        var term = Combo(("", "自動"), ("wt", "Windows ターミナル"), ("pwsh", "PowerShell 7"), ("powershell", "Windows PowerShell"), ("cmd", "コマンド プロンプト"), ("gitbash", "Git Bash"));
        Select(term, s.Terminal);
        term.SelectionChanged += (_, _) => s.Terminal = Value(term);
        p.Children.Add(Ui.Row(new TextBlock { Text = "ターミナル:" }, term));
        return p;
    }

    private StackPanel MenuLayout()
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Caption("各項目をトップレベル・サブメニュー・非表示から選べます。変更は次回メニューを開いたときに反映されます。"));
        p.Children.Add(Toggle("「Git コミット」にブランチ名を表示", s.MenuConfig.ShowBranch, v =>
        {
            var c = Clone(s.MenuConfig); c.ShowBranch = v; s.MenuConfig = c;
        }));
        var rows = new StackPanel();
        void Build()
        {
            rows.Children.Clear();
            foreach (var a in GitActionExt.MenuConfigurable)
            {
                var combo = Combo(("0", "トップレベル"), ("1", "サブメニュー"), ("2", "非表示"));
                combo.MinWidth = 150;
                Select(combo, s.MenuConfig.IsHidden(a) ? "2" : s.MenuConfig.IsTop(a) ? "0" : "1");
                combo.SelectionChanged += (_, _) =>
                {
                    var c = Clone(s.MenuConfig);
                    c.TopLevel.Remove(a.Id());
                    c.Hidden.Remove(a.Id());
                    if (Value(combo) == "0") c.TopLevel.Add(a.Id());
                    if (Value(combo) == "2") c.Hidden.Add(a.Id());
                    s.MenuConfig = c;
                };
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                DockPanel.SetDock(combo, Dock.Right);
                row.Children.Add(combo);
                row.Children.Add(Ui.Row(Ui.Glyph(a.Glyph(), 14), new TextBlock { Text = a.Title() }));
                rows.Children.Add(row);
            }
        }
        p.Children.Add(Wrap(Btn("TortoiseGit と同じ既定に戻す", () => { s.MenuConfig = new MenuConfig(); Build(); }),
            new TextBlock { Text = "(トップレベル: Git 同期 / Git コミット)", Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center }));
        p.Children.Add(Section("項目"));
        Build();
        p.Children.Add(rows);
        return p;
    }

    private static MenuConfig Clone(MenuConfig c) => new() { TopLevel = c.TopLevel.ToList(), Hidden = c.Hidden.ToList(), ShowBranch = c.ShowBranch };

    private StackPanel Overlays()
    {
        var p = new StackPanel();
        p.Children.Add(Section("アイコンオーバーレイ"));
        p.Children.Add(Toggle("エクスプローラーにアイコンオーバーレイ (状態バッジ) を表示", s.BadgesEnabled, v => { s.BadgesEnabled = v; CacheClient.SettingsChanged(); }));
        var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var k in Enum.GetValues<BadgeKind>())
        {
            var item = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            item.Children.Add(BadgeLegend.Create(k, 26));
            item.Children.Add(new TextBlock { Text = k.Label(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            legend.Children.Add(item);
        }
        p.Children.Add(legend);
        p.Children.Add(Ui.Caption(ShellIntegration.OverlaySlotText()));

        p.Children.Add(Section("オーバーレイを表示しないフォルダ"));
        var list = new StackPanel();
        void Build()
        {
            list.Children.Clear();
            foreach (var ex in s.BadgeExcludes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = Ui.IconButton("", "削除", () =>
                {
                    s.BadgeExcludes = s.BadgeExcludes.Where(x => x != ex).ToList();
                    CacheClient.SettingsChanged();
                    Build();
                });
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = ex, VerticalAlignment = VerticalAlignment.Center });
                list.Children.Add(row);
            }
        }
        Build();
        p.Children.Add(list);
        var input = new TextBox { MinWidth = 320, ToolTip = @"パス (例: %USERPROFILE%\AppData)" };
        p.Children.Add(Wrap(input,
            Btn("選択…", () => { if (Shell.ChooseFolder("除外するフォルダ", owner: OwnerWindow) is { } f) input.Text = f; }),
            Btn("追加", () =>
            {
                if (input.Text.Trim().Length == 0) return;
                s.BadgeExcludes = [.. s.BadgeExcludes, input.Text.Trim()];
                input.Text = "";
                CacheClient.SettingsChanged();
                Build();
            })));
        input.Margin = new Thickness(0, 4, 8, 4);
        p.Children.Add(Ui.Caption("ネットワークドライブや巨大なフォルダを除外すると動作が軽くなります。"));
        return p;
    }

    private StackPanel GitTab()
    {
        var p = new StackPanel();
        p.Children.Add(Section("Git 実行ファイル"));
        var path = new TextBox { Text = s.GitPath, MinWidth = 380, ToolTip = "空欄で自動検出" };
        path.LostFocus += (_, _) => { s.GitPath = path.Text.Trim(); gitInUse.Text = Git.Executable; };
        p.Children.Add(Ui.Form(("パス (空欄で自動検出)", path), ("使用中", gitInUse), ("バージョン", gitVersion)));

        p.Children.Add(Section("ユーザー情報 (git config --global)"));
        p.Children.Add(Ui.Form(("名前 (user.name)", userName), ("メール (user.email)", userEmail)));

        p.Children.Add(Section("グローバル設定"));
        p.Children.Add(Ui.Form(("改行コード変換 (core.autocrlf)", autocrlf), ("プル時の動作 (pull.rebase)", pullRebase),
            ("認証情報の保存 (credential.helper)", credential), ("新規リポジトリの既定ブランチ (init.defaultBranch)", defaultBranch)));

        var save = Ui.Accent(new Button { Content = "保存", MinWidth = 96, Margin = new Thickness(0, 4, 8, 4) });
        save.Click += (_, _) => SaveGlobal();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        p.Children.Add(Wrap(save,
            Btn("~/.gitconfig を編集", () =>
            {
                var f = Path.Combine(home, ".gitconfig");
                if (!File.Exists(f)) File.WriteAllText(f, "");
                Shell.OpenInTextEditor(f);
            }),
            Btn("グローバル .gitignore を編集", () =>
            {
                var f = Git.GlobalConfig("core.excludesfile");
                if (f.Length == 0)
                {
                    f = Path.Combine(home, ".gitignore_global");
                    Git.Run(["config", "--global", "core.excludesfile", f.Replace('\\', '/')], home);
                }
                f = f.StartsWith("~/") ? Path.Combine(home, f[2..]) : f;
                f = f.Replace('/', '\\');
                if (!File.Exists(f)) File.WriteAllText(f, "");
                Shell.OpenInTextEditor(f);
            })));
        return p;
    }

    private void SaveGlobal()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Set(string k, string v)
        {
            if (v.Length == 0) Git.Run(["config", "--global", "--unset", k], home);
            else Git.Run(["config", "--global", k, v], home);
        }
        Set("user.name", userName.Text.Trim());
        Set("user.email", userEmail.Text.Trim());
        Set("core.autocrlf", Value(autocrlf));
        Set("pull.rebase", Value(pullRebase));
        Set("credential.helper", Value(credential));
        Set("init.defaultBranch", defaultBranch.Text.Trim());
        WindowManager.Alert("保存しました", "", false);
    }

    private StackPanel Tools()
    {
        var p = new StackPanel();
        p.Children.Add(Section("差分ツール ($LOCAL / $REMOTE)"));
        var diff = new TextBox { Text = s.ExternalDiff, ToolTip = "コマンド (空欄で内蔵ビューア)" };
        diff.LostFocus += (_, _) => s.ExternalDiff = diff.Text.Trim();
        p.Children.Add(diff);
        p.Children.Add(Presets(t => { diff.Text = t; s.ExternalDiff = t; }, true, "内蔵ビューア"));

        p.Children.Add(Section("マージツール ($BASE / $LOCAL / $REMOTE / $MERGED)"));
        var merge = new TextBox { Text = s.ExternalMerge, ToolTip = "コマンド (空欄で内蔵の競合エディタ)" };
        merge.LostFocus += (_, _) => s.ExternalMerge = merge.Text.Trim();
        p.Children.Add(merge);
        p.Children.Add(Presets(t => { merge.Text = t; s.ExternalMerge = t; }, false, "内蔵の競合エディタ"));
        p.Children.Add(Ui.Caption("$LOCAL などはファイルパスに置き換わります。%ProgramFiles% などの環境変数も使えます。"));
        return p;
    }

    private static Button Presets(Action<string> set, bool diff, string builtIn)
    {
        var b = new Button { Content = "プリセット ▾", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        var menu = new ContextMenu { PlacementTarget = b };
        foreach (var (name, d, m) in ExternalTools.Presets)
            menu.Items.Add(FileList.Item(name, () => set(diff ? d : m)));
        menu.Items.Add(new Separator());
        menu.Items.Add(FileList.Item(builtIn, () => set("")));
        b.Click += (_, _) => menu.IsOpen = true;
        return b;
    }
}

/// <summary>Badge drawing for the settings legend (same look as the overlay icons).</summary>
public static class BadgeLegend
{
    public static readonly Dictionary<BadgeKind, Color> Colors = new()
    {
        [BadgeKind.Normal] = Color.FromRgb(0x2E, 0xA1, 0x42),
        [BadgeKind.Modified] = Color.FromRgb(0xD6, 0x29, 0x2E),
        [BadgeKind.Added] = Color.FromRgb(0x0A, 0x69, 0xD9),
        [BadgeKind.Conflict] = Color.FromRgb(0xED, 0x9E, 0x00),
        [BadgeKind.Untracked] = Color.FromRgb(0x8C, 0x94, 0x9E),
        [BadgeKind.Ignored] = Color.FromRgb(0x57, 0x5E, 0x6B),
    };

    public static readonly Dictionary<BadgeKind, string> Glyphs = new()
    {
        [BadgeKind.Normal] = "",
        [BadgeKind.Modified] = "!",
        [BadgeKind.Added] = "",
        [BadgeKind.Conflict] = "",
        [BadgeKind.Untracked] = "?",
        [BadgeKind.Ignored] = "",
    };

    public static FrameworkElement Create(BadgeKind k, double size)
    {
        var g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = Brushes.White });
        g.Children.Add(new Ellipse { Fill = new SolidColorBrush(Colors[k]), Margin = new Thickness(size * 0.08) });
        var glyph = Glyphs[k];
        bool text = glyph is "!" or "?";
        g.Children.Add(new TextBlock
        {
            Text = glyph, Foreground = Brushes.White, FontWeight = FontWeights.Black,
            FontFamily = text ? new FontFamily("Segoe UI") : Ui.IconFont, FontSize = size * (text ? 0.62 : 0.46),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }
}

/// <summary>"Start at sign-in" (HKCU Run key).</summary>
public static class StartupItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(KG.AppName) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (value) k.SetValue(KG.AppName, $"\"{Environment.ProcessPath}\" --background");
            else k.DeleteValue(KG.AppName, false);
        }
    }
}

public sealed class AboutView : GVView
{
    public AboutView()
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        p.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.png")), Width = 96, Height = 96 });
        p.Children.Add(new TextBlock { Text = "GitVelto", FontSize = 26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        var ver = typeof(AboutView).Assembly.GetName().Version;
        p.Children.Add(new TextBlock { Text = $"バージョン {ver?.ToString(3) ?? "-"}", Foreground = Ui.Secondary(), HorizontalAlignment = HorizontalAlignment.Center });
        p.Children.Add(new TextBlock { Text = "エクスプローラーに統合された Git クライアント", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        var git = new TextBlock { FontSize = 12, Foreground = Ui.Secondary(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        p.Children.Add(git);
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notices))
        {
            var b = new Button { Content = "ライセンス情報", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            b.Click += (_, _) => Shell.OpenInTextEditor(notices);
            p.Children.Add(b);
        }
        Content = p;
        Loaded += async (_, _) => git.Text = await Task.Run(Git.Version);
    }
}

public sealed class HelpView : GVView
{
    private static readonly (string Title, string Glyph, string Text)[] Topics =
    [
        ("基本的な使い方", "", "エクスプローラーでファイルやフォルダを右クリックすると「Git 同期…」「Git コミット…」と「GitVelto ▸」サブメニューが表示されます (Windows 11 では「その他のオプションを確認」の中)。リポジトリの外では「Git クローン…」「Git ここにリポジトリを作成…」が表示されます。表示する項目やトップレベルに出す項目は「設定 > コンテキストメニュー」で変更できます。"),
        ("状態アイコン (オーバーレイ)", "", "緑のチェック: 変更なし / 赤の !: 変更あり / 青の +: 追加 / 橙の稲妻: 競合 / 灰の ?: 未追跡 / 濃灰の −: 無視。フォルダには中のファイルの最も重要な状態が表示されます。状態は GitVeltoCache が計算し、変更を監視して更新します。"),
        ("コミット", "", "コミットしたいファイルにチェックを入れ、メッセージを入力して Ctrl+Enter でコミットします。ダブルクリックで差分、右クリックで元に戻す・無視などができます。「直前のコミットをやり直す」で amend、「Signed-off-by」で署名行、「作成者を指定」で別の作成者としてコミットできます。"),
        ("ログ", "", "コミットグラフ付きの履歴を表示します。Ctrl+クリック/Shift+クリックで複数選択し、2 つのコミットを比較したりパッチを作成できます。コミットを右クリックすると、ブランチ/タグの作成、リセット、リバート、チェリーピック、リベース、二分探索などが行えます。一番上の「作業ツリーの変更」で未コミットの変更を確認できます。"),
        ("同期 (プル/プッシュ)", "", "「Git 同期…」ではプル・フェッチ・プッシュ・フェッチ＆リベースを 1 つの画面で行え、受信予定/送信予定のコミットも確認できます。認証が必要な場合はダイアログが表示されます (HTTPS は Git Credential Manager を利用)。"),
        ("ブランチとマージ", "", "切り替え/チェックアウト、ブランチ・タグの作成、マージ、リベース (並べ替え・統合・削除・メッセージ変更が可能な対話的リベース) を行えます。「参照の一覧」ではブランチやタグの削除・名前変更・上流設定ができます。"),
        ("競合の解決", "", "マージやリベースで競合したファイルは橙色のアイコンになります。「競合を編集…」で各競合箇所ごとに自分/相手/両方を選んで保存できます。「競合を解決…」で解決済みにし、マージ・リベースの続行/中止もそこから行えます。"),
        ("その他の機能", "", "退避 (stash)、二分探索 (bisect)、Blame、リポジトリブラウザ、Reflog、サブモジュール、パッチの作成/適用、エクスポート (ZIP)、クリーンアップ (ごみ箱へ移動)、無視リストへの追加、リモートの管理など、TortoiseGit の主な機能を利用できます。"),
        ("用語がわからないとき", "", "各画面の用語の横にある「?」ボタンを押すと、その場で意味と使いどころが表示されます。すべての用語は「Git 用語集」（エクスプローラーの GitVelto メニューや通知領域のアイコンから開けます）で検索できます。"),
        ("コマンドを学ぶ（学習モード）", "", "学習モードがオンのとき、各ダイアログの下に「実行される git コマンド」とオプションの意味が表示され、進捗画面にも各コマンドの解説が出ます。「実行したコマンドの履歴」では、これまでの操作で実行されたコマンドを確認・コピーでき、ターミナルで同じ操作を試せます。設定 > 一般 > 学習 でオン/オフを切り替えられます。"),
        ("外部ツール", "", "設定 > 外部ツールで、差分/マージに VS Code や WinMerge などの外部アプリを指定できます。$LOCAL・$REMOTE・$BASE・$MERGED がファイルパスに置き換わります。"),
    ];

    public HelpView()
    {
        var p = new StackPanel { Margin = new Thickness(18) };
        foreach (var (title, glyph, text) in Topics)
        {
            p.Children.Add(Ui.Row(Ui.Glyph(glyph, 16, Ui.Accent()), new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold }));
            p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 4, 0, 16) });
        }
        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

/// <summary>Registration state of the Explorer shell extension (GitVeltoShell.dll).</summary>
public static class ShellIntegration
{
    public const string ClsidContextMenu = "{6E7A9B2C-3F1D-4C8E-9A51-7D2B0C4E8F10}";

    public static bool IsRegistered()
    {
        try
        {
            using var k = Registry.ClassesRoot.OpenSubKey($@"CLSID\{ClsidContextMenu}\InprocServer32");
            return k?.GetValue("") is string dll && File.Exists(dll);
        }
        catch { return false; }
    }

    public static string StatusText() => IsRegistered() ? "登録済み" : "未登録 (「エクスプローラーに登録…」を押してください)";

    /// <summary>Explains how many overlay slots are used (Windows shows only the first 15 handlers).</summary>
    public static string OverlaySlotText()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers");
            var names = k?.GetSubKeyNames().OrderBy(n => n, StringComparer.Ordinal).ToList() ?? [];
            var ours = names.Where(n => n.TrimStart().StartsWith("GitVelto")).ToList();
            var shown = names.Take(15).Count(n => n.TrimStart().StartsWith("GitVelto"));
            return $"Windows が表示できるオーバーレイは先頭 15 個までです。登録数 {names.Count} 個のうち、GitVelto は {ours.Count} 個中 {shown} 個が有効です。";
        }
        catch { return ""; }
    }

    private static string? Script(string name)
    {
        var dir = AppContext.BaseDirectory;
        var p = Path.Combine(dir, name);
        return File.Exists(p) ? p : null;
    }

    private static void RunElevated(string script, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {args}",
            };
            Process.Start(psi)?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception) { /* cancelled by the user */ }
        catch (Exception e) { WindowManager.Alert("実行できません", e.Message); }
    }

    public static void Install()
    {
        if (Script("register-shell.ps1") is { } s) RunElevated(s, "");
        else WindowManager.Alert("登録スクリプトが見つかりません", "インストーラーから GitVelto を入れ直してください。");
    }

    public static void Uninstall()
    {
        if (Script("register-shell.ps1") is { } s) RunElevated(s, "-Unregister");
    }

    public static void RestartExplorer()
    {
        if (!WindowManager.Confirm("エクスプローラーを再起動しますか？", "開いているエクスプローラーのウインドウは閉じられます。", "再起動")) return;
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer")) p.Kill();
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception e) { WindowManager.Alert("再起動できません", e.Message); }
    }
}
