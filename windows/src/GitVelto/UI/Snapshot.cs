using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>
/// Development aid: renders every dialog off screen into PNG files (port of scripts/snapshot.sh of the macOS version).
///   GitVelto.exe --snapshot &lt;output folder&gt; &lt;sample repositories folder (scripts/make_test_repos.sh)&gt; [light|dark]
/// </summary>
public static class Snapshot
{
    public static async Task Run(string outDir, string reposDir)
    {
        Directory.CreateDirectory(outDir);
        var work = new Repo(Path.Combine(reposDir, "work"));
        var conflict = new Repo(Path.Combine(reposDir, "conflict"));
        var readme = Path.Combine(work.Root, "README.md");
        var main = Path.Combine(work.Root, "src", "main.swift");
        var log = work.Log(20);
        var head = log.FirstOrDefault();

        var views = new List<(string Name, Func<GVView> Make, double W, double H)>
        {
            ("commit", () => new CommitView(work, [work.Root]), 820, 820),
            ("status", () => new StatusView(work, [work.Root]), 860, 580),
            ("diff-unified", () => { AppSettings.Shared.DiffSideBySide = false; return new DiffView(work, new DiffSource.WorkingTree("README.md", null, FileState.Modified)); }, 1040, 600),
            ("diff-side", () => { AppSettings.Shared.DiffSideBySide = true; return new DiffView(work, new DiffSource.WorkingTree("README.md", null, FileState.Modified)); }, 1040, 600),
            ("log", () => new LogView(work, []), 1120, 780),
            ("sync", () => new SyncView(work), 860, 620),
            ("pull", () => new RemoteOpView(work, RemoteOpMode.Pull), 560, 510),
            ("fetch", () => new RemoteOpView(work, RemoteOpMode.Fetch), 560, 480),
            ("push", () => new RemoteOpView(work, RemoteOpMode.Push), 580, 560),
            ("switch", () => new SwitchView(work), 560, 540),
            ("branch", () => new CreateRefView(work, CreateRefKind.Branch, null), 560, 520),
            ("tag", () => new CreateRefView(work, CreateRefKind.Tag, null), 560, 580),
            ("merge", () => new MergeView(work), 560, 600),
            ("rebase", () => new RebaseView(work, "v1.1"), 820, 580),
            ("stash-save", () => new StashSaveView(work), 520, 410),
            ("stash-list", () => new StashListView(work), 800, 540),
            ("ref-browser", () => new RefBrowserView(work), 1000, 580),
            ("reflog", () => new ReflogView(work), 820, 540),
            ("repo-browser", () => new RepoBrowserView(work, "src"), 800, 580),
            ("add", () => new FileActionView(work, [work.Root], FileActionMode.Add), 740, 650),
            ("revert", () => new FileActionView(work, [work.Root], FileActionMode.Revert), 740, 650),
            ("resolve", () => new FileActionView(conflict, [conflict.Root], FileActionMode.Resolve), 740, 650),
            ("conflict-editor", () => new ConflictEditorView(conflict, Path.Combine(conflict.Root, "src", "main.swift")), 1040, 780),
            ("delete", () => new DeleteView(work, [readme], false), 600, 530),
            ("rename", () => new RenameView(work, readme), 560, 350),
            ("ignore", () => new IgnoreView(work, [Path.Combine(work.Root, "debug.log")]), 600, 530),
            ("clean", () => new CleanView(work, [work.Root]), 740, 540),
            ("blame", () => new BlameView(work, main, null), 1040, 600),
            ("clone", () => new CloneView(reposDir), 600, 530),
            ("init", () => new InitRepoView(reposDir), 520, 430),
            ("bisect", () => new BisectStartView(work), 560, 470),
            ("export", () => new ExportView(work), 560, 470),
            ("create-patch", () => new CreatePatchView(work), 560, 550),
            ("apply-patch", () => new ApplyPatchView(work, []), 680, 480),
            ("submodule-add", () => new SubmoduleAddView(work, work.Root), 580, 450),
            ("submodule-update", () => new SubmoduleUpdateView(work), 680, 460),
            ("remotes", () => new RemotesView(work), 680, 400),
            ("repo-settings", () => new RepoSettingsView(work), 720, 520),
            ("settings", () => new SettingsView(), 720, 620),
            ("glossary", () => new GlossaryView("rebase"), 860, 580),
            ("history", () => new CommandHistoryView(), 780, 540),
            ("help", () => new HelpView(), 680, 640),
            ("about", () => new AboutView(), 440, 340),
            ("progress", () =>
            {
                var spec = work.PathspecFile(["README.md", "src/new_feature.swift"]) ?? "";
                var r = new CommandRunner(work.Root, [ProgressStep.Git("--literal-pathspecs", "add", "--dry-run", "-A", $"--pathspec-from-file={spec}", "--pathspec-file-nul"), ProgressStep.Git("status", "--short")]);
                r.Start();
                return new ProgressView("状態の確認", r, []);
            }, 680, 420),
        };
        if (head != null)
        {
            views.Add(("reset", () => new ResetView(work, head), 520, 430));
            views.Add(("changed-files", () => new ChangedFilesView(work, work.ChangedFiles(head), head.Parents.FirstOrDefault() ?? head.Hash, head.Hash), 640, 480));
        }

        var report = new List<string>();
        foreach (var (name, make, w, h) in views)
        {
            try
            {
                var view = make();
                var win = new Window
                {
                    Content = view, Width = w, Height = h, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                };
                view.Handle.Window = win;
                win.Show();
                view.OnShown();
                // Let async loads (git) finish and layout settle.
                for (int i = 0; i < 30; i++) await Task.Delay(100);
                await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var root = (FrameworkElement)win.Content;
                var dpi = VisualTreeHelper.GetDpi(win);
                var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen())
                {
                    // Fluent windows draw a Mica backdrop (transparent Background): use the theme's solid base color instead.
                    var brush = (Application.Current.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush) ?? Brushes.White;
                    dc.DrawRectangle(brush, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                }
                bmp.Render(bg);
                bmp.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                await using (var fs = File.Create(Path.Combine(outDir, name + ".png"))) enc.Save(fs);
                win.Close();
                report.Add($"ok   {name}");
            }
            catch (Exception e)
            {
                report.Add($"FAIL {name}: {e.GetType().Name}: {e.Message}");
            }
        }
        File.WriteAllLines(Path.Combine(outDir, "report.txt"), report);
    }
}
