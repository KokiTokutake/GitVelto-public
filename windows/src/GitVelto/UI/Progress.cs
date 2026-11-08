using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using GitVelto.Core;

namespace GitVelto.UI;

public sealed record ProgressAction(string Title, Action Action);

public enum OutputKind { Command, Normal, Error, Warning, Success }

public sealed class OutputLine(string raw, OutputKind kind, IReadOnlyList<string>? args = null)
{
    public string Raw { get; } = raw;
    public OutputKind Kind { get; } = kind;
    /// <summary>git arguments for command lines (used by the learning mode to explain them).</summary>
    public IReadOnlyList<string>? Args { get; } = args;

    public string Translated => Kind is OutputKind.Normal or OutputKind.Error or OutputKind.Warning
        ? GitMessages.TranslateLine(Raw) : Raw;
}

/// <summary>Runs progress steps one after another, streaming output (UI thread events).</summary>
public sealed class CommandRunner
{
    public enum RunState { Running, Success, Failed, Cancelled }

    public ObservableCollection<OutputLine> Lines { get; } = [];
    public string Partial { get; private set; } = "";
    public RunState State { get; private set; } = RunState.Running;

    /// <summary>Raised on the UI thread when <see cref="Partial"/> or <see cref="State"/> change.</summary>
    public event Action? Changed;
    /// <summary>Raised on the UI thread once all steps finished (true = success).</summary>
    public event Action<bool>? Finished;

    private readonly string dir;
    private readonly Queue<ProgressStep> steps;
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private StreamingProcess? process;
    private bool cancelled, pendingCR;
    private readonly StringBuilder partial = new();

    public CommandRunner(string dir, IEnumerable<ProgressStep> steps)
    {
        this.dir = dir;
        this.steps = new Queue<ProgressStep>(steps);
    }

    public void Start() => RunNext();

    public void Cancel()
    {
        cancelled = true;
        process?.Cancel();
    }

    private void Add(string text, OutputKind kind, IReadOnlyList<string>? args = null) => Lines.Add(new OutputLine(text, kind, args));

    private static OutputKind Classify(string line)
    {
        var l = line.ToLowerInvariant();
        if (l.StartsWith("error") || l.StartsWith("fatal") || l.Contains("conflict") || l.Contains("! [rejected]") || l.Contains("! [remote rejected]"))
            return OutputKind.Error;
        if (l.StartsWith("warning") || l.StartsWith("hint")) return OutputKind.Warning;
        return OutputKind.Normal;
    }

    /// <summary>Handles \r progress updates the way a terminal would.</summary>
    private void Consume(string chunk)
    {
        foreach (var ch in chunk)
        {
            if (ch == '\n')
            {
                var s = partial.ToString();
                Add(s, Classify(s));
                partial.Clear();
                pendingCR = false;
            }
            else if (ch == '\r')
            {
                if (partial.Length > 0) pendingCR = true;
            }
            else
            {
                if (pendingCR) { partial.Clear(); pendingCR = false; }
                partial.Append(ch);
            }
        }
        Partial = partial.ToString();
        Changed?.Invoke();
    }

    private void FlushPartial()
    {
        if (partial.Length == 0) return;
        var s = partial.ToString();
        Add(s, Classify(s));
        partial.Clear();
        Partial = "";
    }

    private void Finish(bool ok)
    {
        FlushPartial();
        if (cancelled)
        {
            State = RunState.Cancelled;
            Add("中止しました", OutputKind.Error);
        }
        else
        {
            State = ok ? RunState.Success : RunState.Failed;
            Add(ok ? "成功" : "失敗しました", ok ? OutputKind.Success : OutputKind.Error);
        }
        Changed?.Invoke();
        Finished?.Invoke(ok && !cancelled);
    }

    private void RunNext()
    {
        if (cancelled) { Finish(false); return; }
        if (steps.Count == 0) { Finish(true); return; }
        switch (steps.Dequeue())
        {
            case ProgressStep.GitCmd g:
                RunGit(g.Args, g.Env);
                break;
            case ProgressStep.Custom c:
                Add(c.Title, OutputKind.Command);
                Task.Run(() =>
                {
                    try
                    {
                        var o = c.Work();
                        ui.BeginInvoke(() =>
                        {
                            if (o.Length > 0) Consume(o + "\n");
                            RunNext();
                        });
                    }
                    catch (Exception e)
                    {
                        ui.BeginInvoke(() => { Add(e.Message, OutputKind.Error); Finish(false); });
                    }
                });
                break;
        }
    }

    private void RunGit(IReadOnlyList<string> args, Dictionary<string, string>? env)
    {
        Add(CommandExplainer.Display(args), OutputKind.Command, args);
        var historyId = CommandHistory.Shared.Record(args, dir);
        var p = new StreamingProcess(args, dir, env);
        p.Output += s => ui.BeginInvoke(() => Consume(s));
        p.Exited += code =>
        {
            CommandHistory.Shared.Finish(historyId, code == 0);
            // Queue after the output callbacks already posted.
            ui.BeginInvoke(DispatcherPriority.Background, () =>
            {
                FlushPartial();
                if (code == 0) RunNext();
                else
                {
                    Add($"(終了コード {code})", OutputKind.Error);
                    Finish(false);
                }
            });
        };
        process = p;
        try { p.Start(); }
        catch (Exception e)
        {
            Add("git を起動できません: " + e.Message, OutputKind.Error);
            Finish(false);
        }
    }
}

/// <summary>TortoiseGit-style progress dialog body.</summary>
public sealed class ProgressView : GVView
{
    private readonly string title;
    private readonly CommandRunner runner;
    private readonly IReadOnlyList<ProgressAction> successActions;
    private readonly TextBlock status = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock statusIcon = new() { FontSize = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar spinner = new() { IsIndeterminate = true, Width = 80, Height = 4, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly RichTextBox output;
    private readonly Paragraph doc = new() { Margin = new Thickness(0) };
    private readonly TextBlock partialText = new() { FontFamily = Ui.Mono, FontSize = 13, Margin = new Thickness(10, 0, 10, 4), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel actionButtons = new() { Orientation = Orientation.Horizontal };
    private readonly Button closeButton = new() { Content = "中止", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox rawToggle = new() { Content = "原文 (英語) を表示", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private int rendered;

    public ProgressView(string title, CommandRunner runner, IReadOnlyList<ProgressAction> successActions)
    {
        this.title = title;
        this.runner = runner;
        this.successActions = successActions;

        output = new RichTextBox
        {
            IsReadOnly = true, IsDocumentEnabled = true, FontFamily = Ui.Mono, FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Document = new FlowDocument(doc) { FontFamily = Ui.Mono, FontSize = 13, PagePadding = new Thickness(4), TextAlignment = TextAlignment.Left },
            Padding = new Thickness(6),
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(statusIcon);
        header.Children.Add(status);
        header.Children.Add(spinner);

        rawToggle.IsChecked = AppSettings.Shared.ProgressShowRaw;
        rawToggle.Click += (_, _) =>
        {
            AppSettings.Shared.ProgressShowRaw = rawToggle.IsChecked == true;
            RenderAll();
        };
        var copy = Ui.IconButton("", "出力をコピー", () =>
            Shell.Copy(string.Join("\n", runner.Lines.Select(l => ShowRaw ? l.Raw : l.Translated))));
        closeButton.Click += (_, _) =>
        {
            if (runner.State == CommandRunner.RunState.Running) runner.Cancel(); else Handle.Close();
        };

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(actionButtons, Dock.Left);
        bottom.Children.Add(actionButtons);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(rawToggle);
        right.Children.Add(copy);
        right.Children.Add(closeButton);
        bottom.Children.Add(right);

        var outputBox = new DockPanel();
        DockPanel.SetDock(partialText, Dock.Bottom);
        outputBox.Children.Add(partialText);
        outputBox.Children.Add(output);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(bottom);
        root.Children.Add(outputBox);
        Content = root;

        runner.Lines.CollectionChanged += (_, _) => RenderNew();
        runner.Changed += UpdateState;
        AppSettings.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.LearningMode)) Dispatcher.BeginInvoke(RenderAll);
        };
        UpdateState();
        RenderAll();
    }

    private bool ShowRaw => rawToggle.IsChecked == true;

    protected internal override bool CanClose()
    {
        if (runner.State != CommandRunner.RunState.Running) return true;
        if (!WindowManager.Confirm("実行中の処理を中止しますか？", "", "中止する", true)) return false;
        runner.Cancel();
        return true;
    }

    private void UpdateState()
    {
        switch (runner.State)
        {
            case CommandRunner.RunState.Running:
                statusIcon.Text = ""; status.Text = $"{title} を実行中…"; spinner.Visibility = Visibility.Visible; break;
            case CommandRunner.RunState.Success:
                Ui.SetGlyph(statusIcon, "", Brushes.SeaGreen); status.Text = $"{title}: 成功"; spinner.Visibility = Visibility.Collapsed; break;
            case CommandRunner.RunState.Failed:
                Ui.SetGlyph(statusIcon, "", Brushes.Crimson); status.Text = $"{title}: 失敗"; spinner.Visibility = Visibility.Collapsed; break;
            default:
                Ui.SetGlyph(statusIcon, "", Brushes.DarkOrange); status.Text = $"{title}: 中止"; spinner.Visibility = Visibility.Collapsed; break;
        }
        partialText.Text = runner.Partial.Length == 0 ? "" : (ShowRaw ? runner.Partial : GitMessages.TranslateLine(runner.Partial));
        partialText.Visibility = runner.Partial.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        bool done = runner.State != CommandRunner.RunState.Running;
        closeButton.Content = done ? "閉じる" : "中止";
        closeButton.IsDefault = done;
        actionButtons.Children.Clear();
        if (runner.State == CommandRunner.RunState.Success)
        {
            foreach (var a in successActions)
            {
                var b = new Button { Content = a.Title, Margin = new Thickness(0, 0, 8, 0) };
                b.Click += (_, _) => { a.Action(); Handle.Close(); };
                actionButtons.Children.Add(b);
            }
        }
        if (done) closeButton.Focus();
    }

    private Brush Color(OutputKind k) => k switch
    {
        OutputKind.Command => Ui.Accent(),
        OutputKind.Error => Brushes.Crimson,
        OutputKind.Warning => Brushes.DarkOrange,
        OutputKind.Success => Brushes.SeaGreen,
        _ => (Brush)FindResource(SystemColors.WindowTextBrushKey),
    };

    private void RenderAll()
    {
        doc.Inlines.Clear();
        rendered = 0;
        RenderNew();
        UpdateState();
    }

    private void RenderNew()
    {
        var learning = AppSettings.Shared.LearningMode;
        for (; rendered < runner.Lines.Count; rendered++)
        {
            var line = runner.Lines[rendered];
            if (doc.Inlines.Count > 0) doc.Inlines.Add(new LineBreak());
            var text = ShowRaw ? line.Raw : line.Translated;
            doc.Inlines.Add(new Run(text.Length == 0 ? " " : text)
            {
                Foreground = Color(line.Kind),
                FontWeight = line.Kind == OutputKind.Command ? FontWeights.SemiBold : FontWeights.Normal,
            });
            if (learning && line.Args != null)
            {
                var ex = CommandExplainer.Explain(line.Args);
                var opts = string.Join(" / ", ex.Options.Select(o => $"{o.Option}: {o.Meaning}"));
                var note = (ex.Summary.Length > 0 ? "\n  ↳ " + ex.Summary : "") + (opts.Length > 0 ? "\n    " + opts : "");
                if (note.Length > 0)
                    doc.Inlines.Add(new Run(note) { Foreground = Ui.Accent(), FontFamily = SystemFonts.MessageFontFamily, FontSize = 12 });
            }
        }
        output.ScrollToEnd();
    }
}
