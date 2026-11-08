using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using GitVelto.Core;
using GitVelto.UI;

namespace GitVelto;

/// <summary>A request to perform an action (from the command line, the shell extension or a second instance).</summary>
public sealed record CommandRequest(string Action, List<string> Paths);

public partial class App : Application
{
    private Mutex? instanceMutex;
    private TrayIcon? tray;
    private CancellationTokenSource? pipeCts;

    public static App Instance => (App)Current;

    /// <summary>
    /// Command line:
    ///   GitVelto.exe &lt;action&gt; [path...] [--pathfile file [--deletepathfile]]
    ///   GitVelto.exe --background        (start resident with the tray icon only, e.g. at sign-in)
    ///   GitVelto.exe "Password for ...:" (GIT_ASKPASS / SSH_ASKPASS, detected via GITVELTO_ASKPASS=1)
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        if (Environment.GetEnvironmentVariable("GITVELTO_ASKPASS") == "1" && IsAskPassInvocation(args))
        {
            int code = AskPassDialog.Run(args.Length > 0 ? args[0] : "");
            Shutdown(code);
            return;
        }

        Git.AskPassExecutable = Environment.ProcessPath;
        var request = ParseArgs(args, out bool background);

        instanceMutex = new Mutex(true, $@"Local\GitVeltoApp-{Environment.UserName}", out bool first);
        if (!first)
        {
            if (TryForward(request ?? new CommandRequest(background ? "" : "settings", [])))
            {
                Shutdown(0);
                return;
            }
            // The other instance does not answer (hung or shutting down): continue as a normal instance.
        }

        DispatcherUnhandledException += (_, ev) =>
        {
            WindowManager.Alert("予期しないエラーが発生しました", ev.Exception.Message);
            ev.Handled = true;
        };

        StartPipeServer();
        UpdateTrayIcon();
        AppSettings.Shared.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName is nameof(AppSettings.ShowTrayIcon) or null) UpdateTrayIcon();
        };

        if (request != null) Handle(request);
        else if (!background) WindowManager.Shared.ShowSettings();
        ExitIfIdle();
    }

    private static bool IsAskPassInvocation(string[] args) =>
        args.Length == 0 || (args.Length == 1 && GitActionExt.Parse(args[0]) == null && !args[0].StartsWith("--"));

    internal static CommandRequest? ParseArgs(string[] args, out bool background)
    {
        background = false;
        string? action = null;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--background":
                    background = true;
                    break;
                case "--pathfile" when i + 1 < args.Length:
                    var file = args[++i];
                    try
                    {
                        paths.AddRange(File.ReadAllLines(file, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0));
                    }
                    catch { }
                    if (args.Contains("--deletepathfile")) try { File.Delete(file); } catch { }
                    break;
                case "--deletepathfile":
                    break;
                default:
                    if (action == null && !a.StartsWith("--") && GitActionExt.Parse(a) != null) action = a;
                    else if (!a.StartsWith("--")) paths.Add(a);
                    break;
            }
        }
        return action == null ? null : new CommandRequest(action, paths);
    }

    public void Handle(CommandRequest r)
    {
        if (r.Action.Length == 0) return;
        if (GitActionExt.Parse(r.Action) is not { } action) return;
        try { WindowManager.Shared.Perform(action, r.Paths); }
        catch (Exception ex) { WindowManager.Alert("操作を開始できません", ex.Message); }
    }

    // ---- single instance ----

    private static string PipeName => $"{KG.AppPipe}-{Environment.UserName}";

    private static bool TryForward(CommandRequest r)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                c.Connect(500);
                var json = JsonSerializer.SerializeToUtf8Bytes(r);
                c.Write(json);
                c.Flush();
                // Let the running instance bring its window to the front.
                AllowSetForegroundWindow(-1);
                return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { Thread.Sleep(100); }
        }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private void StartPipeServer()
    {
        pipeCts = new CancellationTokenSource();
        var token = pipeCts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var ms = new MemoryStream();
                    await server.CopyToAsync(ms, token);
                    var req = JsonSerializer.Deserialize<CommandRequest>(ms.ToArray());
                    if (req != null) await Dispatcher.InvokeAsync(() => { Handle(req); ExitIfIdle(); });
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(200); }
            }
        });
    }

    // ---- lifetime ----

    public void UpdateTrayIcon()
    {
        if (AppSettings.Shared.ShowTrayIcon)
        {
            tray ??= new TrayIcon();
        }
        else
        {
            tray?.Dispose();
            tray = null;
            ExitIfIdle();
        }
    }

    /// <summary>Without the tray icon the app quits when its last window closes.</summary>
    public void ExitIfIdle()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (tray == null && Windows.Count == 0) Shutdown();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        pipeCts?.Cancel();
        tray?.Dispose();
        CommandHistory.Shared.Save();
        try { instanceMutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
