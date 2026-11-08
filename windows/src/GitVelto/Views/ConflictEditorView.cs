using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Built-in 2-way conflict editor (a light-weight TortoiseGitMerge).</summary>
public sealed class ConflictEditorView : GVView
{
    private readonly Repo repo;
    private readonly string path;
    private List<ConflictSegment> segments = [];
    private readonly List<ConflictBlockView> blocks = [];
    private Encoding encoding = new UTF8Encoding(false);
    private string eol = "\n";

    private readonly TextBlock countText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button nextButton = new() { Content = "次の未解決へ", Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
    private readonly Button allOurs = new() { Content = "すべて自分の変更", Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
    private readonly Button allTheirs = new() { Content = "すべて相手の変更", IsEnabled = false };
    private readonly Button saveButton = new() { Content = "保存", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private readonly Button resolveButton = Ui.Accent(new Button { Content = "保存して解決済みにする", MinWidth = 120, IsEnabled = false, ToolTip = "Ctrl+Enter" });
    private readonly StackPanel content = new() { Margin = new Thickness(8) };
    private readonly ScrollViewer scroller;
    private readonly Grid body = new();

    internal static Brush OursColor => new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    internal static Brush TheirsColor => new SolidColorBrush(Color.FromRgb(0x2F, 0x7B, 0xD9));
    internal static Brush Orange => (Brush)Application.Current.FindResource("StateConflict");

    public ConflictEditorView(Repo repo, string path)
    {
        this.repo = repo;
        this.path = path;

        // --- header ---
        var header = new DockPanel { Margin = new Thickness(10) };
        var hRight = new StackPanel { Orientation = Orientation.Horizontal };
        hRight.Children.Add(nextButton);
        hRight.Children.Add(allOurs);
        hRight.Children.Add(allTheirs);
        DockPanel.SetDock(hRight, Dock.Right);
        header.Children.Add(hRight);
        var hLeft = new StackPanel { Orientation = Orientation.Horizontal };
        hLeft.Children.Add(Ui.Glyph("", 15, Orange));
        hLeft.Children.Add(new TextBlock
        {
            Text = repo.Relative(path), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        hLeft.Children.Add(countText);
        hLeft.Children.Add(new TermHelp("conflict"));
        header.Children.Add(hLeft);
        nextButton.Click += (_, _) => JumpToNextUnresolved();
        allOurs.Click += (_, _) => ResolveAll(ConflictResolution.Ours);
        allTheirs.Click += (_, _) => ResolveAll(ConflictResolution.Theirs);

        // --- body ---
        scroller = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        body.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        // --- bottom ---
        var bottom = new DockPanel { Margin = new Thickness(10) };
        var bRight = new StackPanel { Orientation = Orientation.Horizontal };
        if (AppSettings.Shared.ExternalMerge.Length > 0)
        {
            var ext = new Button { Content = "外部マージツール", Margin = new Thickness(0, 0, 8, 0) };
            ext.Click += (_, _) => { ExternalTools.Merge(repo, path); Handle.Close(); };
            bRight.Children.Add(ext);
        }
        var cancel = new Button { Content = "キャンセル", MinWidth = 96, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Handle.Close();
        bRight.Children.Add(cancel);
        bRight.Children.Add(saveButton);
        bRight.Children.Add(resolveButton);
        saveButton.Click += (_, _) => Save(false);
        resolveButton.Click += (_, _) => Save(true);
        DockPanel.SetDock(bRight, Dock.Right);
        bottom.Children.Add(bRight);
        var note = Ui.Caption("「自分」= 現在のブランチ (HEAD)、「相手」= マージ/適用しようとしている変更");
        note.VerticalAlignment = VerticalAlignment.Center;
        var bLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bLeft.Children.Add(note);
        bLeft.Children.Add(new TermHelp("ours-theirs"));
        bottom.Children.Add(bLeft);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(new Separator { Margin = new Thickness(0) });
        DockPanel.SetDock(root.Children[^1], Dock.Top);
        root.Children.Add(bottom);
        var sep2 = new Separator { Margin = new Thickness(0) };
        DockPanel.SetDock(sep2, Dock.Bottom);
        root.Children.Add(sep2);
        root.Children.Add(body);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (resolveButton.IsEnabled) Save(true);
                e.Handled = true;
            }
        };
        UpdateCounts();
    }

    protected internal override async void OnShown()
    {
        var file = path;
        var (segs, enc, lineEnd, error) = await Task.Run(() => Load(file));
        body.Children.Clear();
        if (error != null)
        {
            body.Children.Add(new TextBlock
            {
                Text = error, Foreground = Brushes.Crimson, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            UpdateCounts();
            return;
        }
        segments = segs;
        encoding = enc;
        eol = lineEnd;
        foreach (var s in segments)
        {
            if (s.IsConflict)
            {
                var b = new ConflictBlockView(s, eol, UpdateCounts) { Margin = new Thickness(0, 6, 0, 6) };
                blocks.Add(b);
                content.Children.Add(b);
            }
            else content.Children.Add(new PlainBlock(s.Text));
        }
        body.Children.Add(scroller);
        saveButton.IsEnabled = true;
        UpdateCounts();
    }

    /// <summary>Reads the file (detecting the encoding by BOM, otherwise strict UTF-8) and parses the conflict markers.</summary>
    private static (List<ConflictSegment> Segments, Encoding Encoding, string Eol, string? Error) Load(string file)
    {
        byte[] data;
        try { data = File.ReadAllBytes(file); }
        catch { return ([], Encoding.UTF8, "\n", "ファイルを読み込めません"); }
        const string notText = "テキストファイルではありません (バイナリの競合は「競合を解決」で自分/相手を選んでください)";
        string text;
        Encoding enc;
        try
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                enc = new UTF8Encoding(true, true);
                text = enc.GetString(data, 3, data.Length - 3);
            }
            else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                enc = new UnicodeEncoding(false, true, true);
                text = enc.GetString(data, 2, data.Length - 2);
            }
            else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                enc = new UnicodeEncoding(true, true, true);
                text = enc.GetString(data, 2, data.Length - 2);
            }
            else
            {
                if (Array.IndexOf(data, (byte)0) >= 0) return ([], Encoding.UTF8, "\n", notText);
                enc = new UTF8Encoding(false, true);
                text = enc.GetString(data);
            }
        }
        catch (DecoderFallbackException) { return ([], Encoding.UTF8, "\n", notText); }

        var segs = ConflictParser.Parse(text);
        if (!segs.Any(s => s.IsConflict)) return (segs, enc, "\n", "競合マーカーが見つかりません");
        // Dominant line ending, used for text typed by the user and for re-written markers.
        int crlf = 0, lf = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++; else lf++;
        }
        return (segs, enc, crlf > lf ? "\r\n" : "\n", null);
    }

    private int ConflictCount => blocks.Count;
    private int Unresolved => blocks.Count(b => b.Segment.Resolution == null);

    private void UpdateCounts()
    {
        var unresolved = Unresolved;
        countText.Text = $"競合 {ConflictCount} 箇所 / 未解決 {unresolved}";
        countText.Foreground = unresolved > 0 ? Orange : OursColor;
        nextButton.IsEnabled = unresolved > 0;
        allOurs.IsEnabled = allTheirs.IsEnabled = blocks.Count > 0;
        resolveButton.IsEnabled = blocks.Count > 0 && unresolved == 0;
    }

    private void JumpToNextUnresolved()
    {
        var target = blocks.FirstOrDefault(b => b.Segment.Resolution == null);
        if (target == null) return;
        var y = target.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
        scroller.ScrollToVerticalOffset(Math.Max(0, y));
    }

    private void ResolveAll(ConflictResolution r)
    {
        foreach (var b in blocks) b.Apply(r);
        UpdateCounts();
    }

    /// <summary>Like <see cref="ConflictSegment.Output"/>, but writes markers with the file's line ending.</summary>
    private string Output(ConflictSegment s)
    {
        if (!s.IsConflict || s.Resolution != null) return s.Output;
        var sb = new StringBuilder();
        sb.Append("<<<<<<< ").Append(s.OursLabel).Append(eol).Append(s.Ours);
        if (s.Base != null) sb.Append("||||||| base").Append(eol).Append(s.Base);
        sb.Append("=======").Append(eol).Append(s.Theirs).Append(">>>>>>> ").Append(s.TheirsLabel).Append(eol);
        return sb.ToString();
    }

    private async void Save(bool markResolved)
    {
        if (blocks.Count == 0) return;
        var text = string.Concat(segments.Select(Output));
        try
        {
            var preamble = encoding.GetPreamble();
            var bytes = encoding.GetBytes(text);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            fs.Write(preamble);
            fs.Write(bytes);
        }
        catch (Exception e)
        {
            WindowManager.Alert("保存できません", e.Message);
            return;
        }
        if (!markResolved)
        {
            WindowManager.NotifyRepoChanged(path);
            return;
        }

        var rel = repo.Relative(path);
        var r = await Task.Run(() =>
        {
            var id = CommandHistory.Shared.Record(["add", "--", rel], repo.Root);
            var res = repo.Git(["--literal-pathspecs", "add", "--", rel]);
            CommandHistory.Shared.Finish(id, res.Ok);
            return res;
        });
        if (!r.Ok)
        {
            WindowManager.Alert("解決済みにできません", r.Message);
            return;
        }
        WindowManager.NotifyRepoChanged(path);
        Handle.Close();
        var root = repo.Root;
        var (noConflicts, st) = await Task.Run(() =>
        {
            var rr = new Repo(root);
            return (rr.Status([], "no").All(e => e.State != FileState.Conflict), rr.OperationState());
        });
        if (!noConflicts) return;
        if (!WindowManager.Confirm("すべての競合が解決されました", "続けてコミット (またはリベースの続行) を行いますか?", "開く")) return;
        if (st is RepoOperationState.Rebasing or RepoOperationState.ApplyingPatches or RepoOperationState.CherryPicking)
            WindowManager.Shared.Perform(GitAction.Resolve, [root]);
        else
            WindowManager.Shared.Perform(GitAction.Commit, [root]);
    }

    // ---- blocks ----

    private static string StripEol(string s)
    {
        s = s.Replace("\r\n", "\n");
        return s.EndsWith('\n') ? s[..^1] : s;
    }

    private static TextBox MonoText(string text) => new()
    {
        Text = text, IsReadOnly = true, FontFamily = Ui.Mono, FontSize = 13, BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, Padding = new Thickness(0), TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        MinHeight = 0,
    };

    /// <summary>Unchanged text between conflicts (long runs are collapsed).</summary>
    private sealed class PlainBlock : StackPanel
    {
        public PlainBlock(string text)
        {
            var lines = StripEol(text).Split('\n');
            Opacity = 0.72;
            if (lines.Length > 12)
            {
                Children.Add(MonoText(string.Join("\n", lines.Take(4))));
                var link = new Hyperlink(new Run($"… {lines.Length - 8} 行を表示 …"));
                var linkText = new TextBlock(link) { FontSize = 12, Margin = new Thickness(0, 2, 0, 2) };
                Children.Add(linkText);
                Children.Add(MonoText(string.Join("\n", lines.TakeLast(4))));
                link.Click += (_, _) =>
                {
                    Children.Clear();
                    Children.Add(MonoText(string.Join("\n", lines)));
                };
            }
            else Children.Add(MonoText(string.Join("\n", lines)));
        }
    }

    /// <summary>One conflict: both sides, resolution buttons and the (editable) result.</summary>
    private sealed class ConflictBlockView : Border
    {
        public ConflictSegment Segment { get; }
        private readonly string eol;
        private readonly Action changed;
        private readonly StackPanel status = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel resultPanel = new();
        private readonly TextBox result = new()
        {
            AcceptsReturn = true, AcceptsTab = true, FontSize = 12, TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1), Padding = new Thickness(4), MaxHeight = 600,
        };
        private bool updating;

        public ConflictBlockView(ConflictSegment seg, string eol, Action changed)
        {
            Segment = seg;
            this.eol = eol;
            this.changed = changed;
            Padding = new Thickness(8);
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0xE0, 0x8A, 0x00));
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xE0, 0x8A, 0x00));

            var sides = new Grid();
            sides.ColumnDefinitions.Add(new ColumnDefinition());
            sides.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sides.ColumnDefinitions.Add(new ColumnDefinition());
            var ours = Side($"自分: {seg.OursLabel}", seg.Ours, Color.FromRgb(0x2E, 0xA0, 0x43));
            var theirs = Side($"相手: {seg.TheirsLabel}", seg.Theirs, Color.FromRgb(0x2F, 0x7B, 0xD9));
            Grid.SetColumn(theirs, 2);
            sides.Children.Add(ours);
            sides.Children.Add(theirs);

            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            buttons.Children.Add(Small("自分を使用", () => Resolve(ConflictResolution.Ours)));
            buttons.Children.Add(Small("相手を使用", () => Resolve(ConflictResolution.Theirs)));
            buttons.Children.Add(Small("両方 (自分 → 相手)", () => Resolve(ConflictResolution.OursThenTheirs)));
            buttons.Children.Add(Small("両方 (相手 → 自分)", () => Resolve(ConflictResolution.TheirsThenOurs)));
            buttons.Children.Add(Small("手動で編集", () =>
            {
                Resolve(ConflictResolution.Custom);
                result.Focus();
            }));
            var buttonRow = new DockPanel();
            DockPanel.SetDock(status, Dock.Right);
            buttonRow.Children.Add(status);
            buttonRow.Children.Add(buttons);

            result.FontFamily = Ui.Mono;
            result.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x2E, 0xA0, 0x43));
            result.TextChanged += (_, _) =>
            {
                if (updating) return;
                // Text typed in a WPF TextBox uses CRLF: normalize to the file's line ending.
                var t = result.Text.Replace("\r\n", "\n");
                if (eol != "\n") t = t.Replace("\n", eol);
                Segment.Resolved = t;
                Segment.Resolution = ConflictResolution.Custom;
            };
            var resultLabel = new TextBlock { Text = "結果:", FontSize = 12, Foreground = Ui.Secondary(), Margin = new Thickness(0, 6, 0, 2) };
            resultPanel.Children.Add(resultLabel);
            resultPanel.Children.Add(result);

            var root = new StackPanel();
            root.Children.Add(sides);
            root.Children.Add(buttonRow);
            root.Children.Add(resultPanel);
            Child = root;
            Render();
        }

        private static Button Small(string title, Action a)
        {
            var b = new Button { Content = title, Padding = new Thickness(8, 3, 8, 3), FontSize = 12, Margin = new Thickness(0, 0, 6, 4), MinWidth = 0 };
            b.Click += (_, _) => a();
            return b;
        }

        private static StackPanel Side(string title, string text, Color color)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock
            {
                Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var box = MonoText(text.Length == 0 ? "(なし)" : StripEol(text));
            p.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B)),
                Padding = new Thickness(6), Child = box,
            });
            return p;
        }

        public void Apply(ConflictResolution r)
        {
            Segment.Apply(r);
            if (r == ConflictResolution.Custom && Segment.Resolved.Length == 0) Segment.Resolved = Segment.Ours;
            Render();
        }

        private void Resolve(ConflictResolution r)
        {
            Apply(r);
            changed();
        }

        private void Render()
        {
            status.Children.Clear();
            if (Segment.Resolution != null)
            {
                status.Children.Add(Ui.Glyph("", 13, OursColor));
                status.Children.Add(new TextBlock { Text = "解決済み", Foreground = OursColor, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                var undo = Small("元に戻す", () =>
                {
                    Segment.Resolution = null;
                    Segment.Resolved = "";
                    Render();
                    changed();
                });
                undo.Margin = new Thickness(0);
                status.Children.Add(undo);
                updating = true;
                // Show "\n" line breaks; the TextBox renders them and keeps them unless the user edits.
                result.Text = Segment.Resolved.Replace("\r\n", "\n");
                updating = false;
                var lines = Math.Max(3, Segment.Resolved.Split('\n').Length);
                result.MinHeight = lines * 16 + 10;
                resultPanel.Visibility = Visibility.Visible;
            }
            else
            {
                status.Children.Add(Ui.Glyph("", 13, Orange));
                status.Children.Add(new TextBlock { Text = "未解決", Foreground = Orange, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                resultPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
