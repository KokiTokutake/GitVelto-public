using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using GitVelto.Core;

namespace GitVelto.Tests;

internal static class TestEnv
{
    public static string Temp { get; private set; } = "";

    /// <summary>Isolates settings, history and git global config from the user's environment.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        Temp = Path.Combine(Path.GetTempPath(), "GitVeltoTests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Temp);
        Environment.SetEnvironmentVariable("GITVELTO_REGKEY", @"Software\GitVelto.Tests");
        Environment.SetEnvironmentVariable("GITVELTO_DATA", Path.Combine(Temp, "data"));
        var gitconfig = Path.Combine(Temp, "gitconfig");
        File.WriteAllText(gitconfig, "[user]\n\tname = Test User\n\temail = test@example.com\n[core]\n\tautocrlf = false\n[init]\n\tdefaultBranch = main\n");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", gitconfig);
    }

    public static string RepoRootDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "shared", "make_test_repos.sh"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("shared/make_test_repos.sh not found");
    }

    public static string Bash =>
        Path.Combine(Git.InstallRoot ?? @"C:\Program Files\Git", "bin", "bash.exe");

    /// <summary>Runs a bash command (Git for Windows) and returns trimmed stdout+stderr with LF line endings.</summary>
    public static string Sh(string cmd, string dir)
    {
        var psi = new ProcessStartInfo(Bash)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = dir,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(cmd);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (o + err.Result).Replace("\r\n", "\n").Trim();
    }

    /// <summary>Creates the sample repositories (same script as the macOS version) in a new folder.</summary>
    public static string MakeTestRepos()
    {
        var dir = Path.Combine(Temp, "repos-" + Guid.NewGuid().ToString("N")[..6]);
        var script = Path.Combine(RepoRootDir(), "shared", "make_test_repos.sh").Replace('\\', '/');
        var o = Sh($"bash '{script}' '{dir.Replace('\\', '/')}'", Temp);
        if (!o.Contains("created test repositories")) throw new InvalidOperationException("make_test_repos failed: " + o);
        return dir;
    }
}

/// <summary>Collects soft assertion failures so one scenario reports every broken step.</summary>
internal sealed class Checks
{
    private readonly List<string> failures = [];
    public int Passed { get; private set; }

    public void Check(bool cond, string name, string detail = "")
    {
        if (cond) Passed++; else failures.Add($"{name} {detail}".Trim());
    }

    public void AssertAll()
    {
        if (failures.Count > 0) Assert.Fail($"{failures.Count} 件失敗 ({Passed} 件成功):\n" + string.Join("\n", failures));
    }
}
