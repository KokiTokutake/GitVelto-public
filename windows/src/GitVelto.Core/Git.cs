using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace GitVelto.Core;

public sealed class GitResult(int status, byte[] stdout, byte[] stderr)
{
    public int Status { get; } = status;
    public byte[] Stdout { get; } = stdout;
    public byte[] Stderr { get; } = stderr;
    public bool Ok => Status == 0;
    public string Out => Encoding.UTF8.GetString(Stdout);
    public string Err => Encoding.UTF8.GetString(Stderr);

    public string RawMessage
    {
        get
        {
            var e = Err.Trim();
            return e.Length == 0 ? Out.Trim() : e;
        }
    }

    /// <summary>Message translated to Japanese where possible.</summary>
    public string Message => GitMessages.Translate(RawMessage);
}

public sealed class GitException(string message) : Exception(message);

/// <summary>Temporary files used for pathspecs, messages, exported blobs, rebase todo lists.</summary>
public static class KGTemp
{
    public static string Dir { get; } = Create();

    private static string Create()
    {
        var d = Path.Combine(Path.GetTempPath(), "GitVelto");
        try { Directory.CreateDirectory(d); } catch { return Path.GetTempPath(); }
        return d;
    }

    public static string PathFor(string name) => Path.Combine(Dir, name);

    public static string NewId(int length = 32) => Guid.NewGuid().ToString("N")[..length];

    public static string EmptyFile()
    {
        var p = PathFor("empty-" + NewId(6));
        File.WriteAllBytes(p, []);
        return p;
    }
}

public static class Git
{
    /// <summary>Executable used as GIT_ASKPASS / SSH_ASKPASS (GitVelto.exe). Null = not set.</summary>
    public static string? AskPassExecutable { get; set; }

    private static string? detected;

    /// <summary>Path of git.exe (settings → Git for Windows registry → PATH → default install folder).</summary>
    public static string Executable
    {
        get
        {
            var custom = AppSettings.Shared.GitPath.Trim().Trim('"');
            if (custom.Length > 0 && File.Exists(custom)) return custom;
            return detected ??= Detect();
        }
    }

    public static IEnumerable<string> Candidates()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            string? install = null;
            try { install = hive.OpenSubKey(@"SOFTWARE\GitForWindows")?.GetValue("InstallPath") as string; } catch { }
            if (!string.IsNullOrEmpty(install)) yield return Path.Combine(install, "cmd", "git.exe");
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string p;
            try { p = Path.Combine(dir.Trim().Trim('"'), "git.exe"); } catch { continue; }
            yield return p;
        }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "Git", "cmd", "git.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe");
    }

    private static string Detect() => Candidates().FirstOrDefault(File.Exists) ?? "git.exe";

    /// <summary>Folder of the Git for Windows installation (contains bin\bash.exe, usr\bin), if found.</summary>
    public static string? InstallRoot
    {
        get
        {
            var exe = Executable;
            var dir = Path.GetDirectoryName(exe);
            if (dir == null) return null;
            var name = Path.GetFileName(dir).ToLowerInvariant();
            if (name is "cmd" or "bin") dir = Path.GetDirectoryName(dir);
            if (dir != null && Path.GetFileName(dir).Equals("mingw64", StringComparison.OrdinalIgnoreCase)) dir = Path.GetDirectoryName(dir);
            return dir;
        }
    }

    public static Dictionary<string, string> BuildEnvironment(Dictionary<string, string>? extra = null)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            env[(string)kv.Key] = kv.Value as string ?? "";
        env["GIT_TERMINAL_PROMPT"] = "0";
        env["GIT_PAGER"] = "cat";
        env["PAGER"] = "cat";
        env["GIT_EDITOR"] = "true";
        env["GIT_MERGE_AUTOEDIT"] = "no";
        // git ships Japanese messages on some builds; GitVelto translates the English ones itself.
        env["LC_ALL"] = "en_US.UTF-8";
        env["LANG"] = "en_US.UTF-8";
        if (AskPassExecutable is { } exe)
        {
            env["GIT_ASKPASS"] = exe;
            env["SSH_ASKPASS"] = exe;
            env["SSH_ASKPASS_REQUIRE"] = "force";
            env["DISPLAY"] = env.TryGetValue("DISPLAY", out var d) && d.Length > 0 ? d : ":0";
            env["GITVELTO_ASKPASS"] = "1";
        }
        if (extra != null) foreach (var kv in extra) env[kv.Key] = kv.Value;
        return env;
    }

    public static readonly string[] BaseArgs = ["-c", "core.quotepath=off", "-c", "color.ui=false"];

    internal static ProcessStartInfo StartInfo(IEnumerable<string> args, string dir, Dictionary<string, string>? env)
    {
        var psi = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = dir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in BaseArgs) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Clear();
        foreach (var kv in BuildEnvironment(env)) psi.Environment[kv.Key] = kv.Value;
        return psi;
    }

    /// <summary>Runs git synchronously. Do not call on the UI thread for potentially slow commands.</summary>
    public static GitResult Run(IEnumerable<string> args, string dir, byte[]? stdin = null, Dictionary<string, string>? env = null)
    {
        Process p;
        try
        {
            p = Process.Start(StartInfo(args, dir, env)) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception e)
        {
            return new GitResult(-1, [], Encoding.UTF8.GetBytes("git を起動できません: " + e.Message));
        }
        using (p)
        {
            // Read both streams concurrently: reading only one can deadlock when the other fills up.
            var outTask = ReadAllAsync(p.StandardOutput.BaseStream);
            var errTask = ReadAllAsync(p.StandardError.BaseStream);
            try
            {
                if (stdin != null) p.StandardInput.BaseStream.Write(stdin);
                p.StandardInput.Close();
            }
            catch (IOException) { /* process exited early */ }
            Task.WaitAll(outTask, errTask);
            p.WaitForExit();
            return new GitResult(p.ExitCode, outTask.Result, errTask.Result);
        }
    }

    public static Task<GitResult> RunAsync(IEnumerable<string> args, string dir, byte[]? stdin = null, Dictionary<string, string>? env = null)
    {
        var list = args.ToList();
        return Task.Run(() => Run(list, dir, stdin, env));
    }

    private static async Task<byte[]> ReadAllAsync(Stream s)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    public static string Version() => Run(["--version"], Path.GetTempPath()).Out.Trim();

    public static string GlobalConfig(string key) =>
        Run(["config", "--global", "--get", key], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Out.Trim();
}

/// <summary>Streams the output of a running git process (used by the progress window).</summary>
public sealed class StreamingProcess
{
    private readonly ProcessStartInfo psi;
    private Process? process;
    private readonly object gate = new();

    public event Action<string>? Output;
    public event Action<int>? Exited;

    public StreamingProcess(IEnumerable<string> args, string dir, Dictionary<string, string>? env)
    {
        psi = Git.StartInfo(args, dir, env);
    }

    public void Start()
    {
        var p = Process.Start(psi) ?? throw new InvalidOperationException("git を起動できません");
        process = p;
        p.StandardInput.Close();
        var a = Pump(p.StandardOutput.BaseStream);
        var b = Pump(p.StandardError.BaseStream);
        Task.Run(async () =>
        {
            await Task.WhenAll(a, b).ConfigureAwait(false);
            p.WaitForExit();
            Exited?.Invoke(p.ExitCode);
        });
    }

    private Task Pump(Stream s) => Task.Run(async () =>
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var buf = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buf.Length)];
        int n;
        while ((n = await s.ReadAsync(buf).ConfigureAwait(false)) > 0)
        {
            int c = decoder.GetChars(buf, 0, n, chars, 0, false);
            if (c > 0) lock (gate) Output?.Invoke(new string(chars, 0, c));
        }
        int rest = decoder.GetChars([], 0, 0, chars, 0, true);
        if (rest > 0) lock (gate) Output?.Invoke(new string(chars, 0, rest));
    });

    public void Cancel()
    {
        try { if (process is { HasExited: false } p) p.Kill(entireProcessTree: true); } catch { }
    }
}
