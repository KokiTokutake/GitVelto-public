using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GitVelto.Setup;

/// <summary>
/// GitVelto installer: checks the prerequisites, extracts the embedded app (payload.zip) and runs its
/// install.ps1 (copy to %ProgramFiles%\GitVelto, shell extension, Windows 11 menu package, Start menu,
/// "Apps &amp; features"). Runs elevated (see setup.manifest).
///   GitVeltoSetup.exe          interactive
///   GitVeltoSetup.exe /quiet   no dialogs (exit code 0 = success)
/// </summary>
public static class Program
{
    private const string Title = "GitVelto セットアップ";
    private const string RuntimeUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";
    private const string GitUrl = "https://gitforwindows.org/";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0, MB_OKCANCEL = 0x1, MB_YESNO = 0x4, MB_ICONERROR = 0x10, MB_ICONQUESTION = 0x20,
        MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40, MB_SETFOREGROUND = 0x10000;
    private const int IDOK = 1, IDYES = 6;

    private static bool quiet;

    private static int Ask(string text, uint type) => quiet ? IDOK : MessageBoxW(IntPtr.Zero, text, Title, type | MB_SETFOREGROUND);

    [STAThread]
    public static int Main(string[] args)
    {
        quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("/q", StringComparison.OrdinalIgnoreCase));
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";

        using var payload = typeof(Program).Assembly.GetManifestResourceStream("payload.zip");
        if (payload == null)
        {
            Ask("インストールするファイルが含まれていません。windows\\build.ps1 -Setup で作り直してください。", MB_OK | MB_ICONERROR);
            return 2;
        }

        var runtime = DesktopRuntime();
        if (runtime == null || runtime < MinRuntime)
        {
            if (quiet) return 3;
            var what = runtime == null
                ? ".NET 10 デスクトップ ランタイムが見つかりません。GitVelto の実行に必要です。"
                : $".NET 10 デスクトップ ランタイム {runtime} には既知の脆弱性があります ({MinRuntime} 以降で修正済み)。";
            var r = MessageBoxW(IntPtr.Zero,
                what + "\n\nダウンロードページを開きますか？\n" +
                $"(「.NET デスクトップ ランタイム」{MinRuntime} 以降の x64 版をインストールしてから、もう一度セットアップを実行してください)",
                Title, MB_YESNO | MB_ICONWARNING | MB_SETFOREGROUND);
            if (r == IDYES) Open(RuntimeUrl);
            return 3;
        }

        if (!HasGit() && !quiet)
        {
            var r = MessageBoxW(IntPtr.Zero,
                "Git for Windows が見つかりません。GitVelto は Git for Windows を使って動作します。\n\n" +
                "インストールはこのまま続けます。Git for Windows のダウンロードページを開きますか？",
                Title, MB_YESNO | MB_ICONWARNING | MB_SETFOREGROUND);
            if (r == IDYES) Open(GitUrl);
        }

        if (Ask($"GitVelto {version} をインストールします。\n\n" +
                $"インストール先: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitVelto")}\n\n" +
                "エクスプローラーの右クリックメニューとアイコンオーバーレイを登録するため、最後にエクスプローラーが再起動します" +
                " (開いているエクスプローラーのウインドウは閉じられます)。",
                MB_OKCANCEL | MB_ICONQUESTION) != IDOK) return 1;

        var temp = Path.Combine(Path.GetTempPath(), "GitVeltoSetup-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ZipFile.ExtractToDirectory(payload, temp);
            var script = Path.Combine(temp, "install.ps1");
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Source", temp }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Ask("インストールに失敗しました。\n\n" + Tail(err.Result + "\n" + output), MB_OK | MB_ICONERROR);
                return 4;
            }
        }
        catch (Exception e)
        {
            Ask("インストールに失敗しました。\n\n" + e.Message, MB_OK | MB_ICONERROR);
            return 4;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }

        Ask("GitVelto をインストールしました。\n\n" +
            "エクスプローラーでファイルやフォルダを右クリックすると「GitVelto」メニューが表示されます。\n" +
            "スタートメニューの「GitVelto」から設定を開けます。アンインストールは「設定 > アプリ」から行えます。",
            MB_OK | MB_ICONINFORMATION);
        return 0;
    }

    /// <summary>Oldest .NET 10 Desktop Runtime accepted (10.0.0-10.0.13 have CVE-2026-111497).</summary>
    private static readonly Version MinRuntime = new(10, 0, 14);

    /// <summary>Newest installed .NET 10 Desktop Runtime (null = none).</summary>
    private static Version? DesktopRuntime()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "",
        };
        Version? best = null;
        foreach (var root in roots.Where(r => r.Length > 0))
        {
            var dir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(dir)) continue;
            foreach (var d in Directory.GetDirectories(dir, "10.*"))
                if (Version.TryParse(Path.GetFileName(d), out var v) && (best == null || v > best)) best = v;
        }
        return best;
    }

    private static bool HasGit()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                if (hive.OpenSubKey(@"SOFTWARE\GitForWindows")?.GetValue("InstallPath") is string p && Directory.Exists(p)) return true;
            }
            catch { }
        }
        return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 1500 ? "…" + s[^1500..] : s;
    }
}
