using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace GitVelto.Core;

/// <summary>
/// User settings, stored under HKCU\Software\GitVelto so that the shell extension (C++) can read
/// the values it needs (menu layout, overlay switches) without talking to the app.
/// Set the environment variable GITVELTO_REGKEY to use another key (tests).
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public static AppSettings Shared { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly string keyPath;

    private AppSettings()
    {
        keyPath = Environment.GetEnvironmentVariable("GITVELTO_REGKEY") is { Length: > 0 } k ? k : KG.RegistryKey;
        Reload();
    }

    // ---- values ----

    private string gitPath = "";
    public string GitPath { get => gitPath; set => Set(ref gitPath, value, v => WriteString("GitPath", v)); }

    private bool badgesEnabled = true;
    public bool BadgesEnabled { get => badgesEnabled; set => Set(ref badgesEnabled, value, v => WriteBool("BadgesEnabled", v)); }

    private bool showTrayIcon = true;
    public bool ShowTrayIcon { get => showTrayIcon; set => Set(ref showTrayIcon, value, v => WriteBool("ShowTrayIcon", v)); }

    private int logLimit = 500;
    public int LogLimit { get => logLimit; set => Set(ref logLimit, Math.Max(50, value), v => WriteInt("LogLimit", v)); }

    private bool diffSideBySide;
    public bool DiffSideBySide { get => diffSideBySide; set => Set(ref diffSideBySide, value, v => WriteBool("DiffSideBySide", v)); }

    private List<string> recentMessages = [];
    public List<string> RecentMessages { get => recentMessages; set => Set(ref recentMessages, value, v => WriteList("RecentMessages", v)); }

    private bool pushAfterCommit;
    public bool PushAfterCommit { get => pushAfterCommit; set => Set(ref pushAfterCommit, value, v => WriteBool("PushAfterCommit", v)); }

    private bool cleanToTrash = true;
    public bool CleanToTrash { get => cleanToTrash; set => Set(ref cleanToTrash, value, v => WriteBool("CleanToTrash", v)); }

    private MenuConfig menuConfig = new();
    /// <summary>Assign a new instance to save (mutating the lists in place is not detected).</summary>
    public MenuConfig MenuConfig
    {
        get => menuConfig;
        set => Set(ref menuConfig, value, v =>
        {
            WriteList("MenuTopLevel", v.TopLevel);
            WriteList("MenuHidden", v.Hidden);
            WriteBool("MenuShowBranch", v.ShowBranch);
        });
    }

    /// <summary>External diff command. $LOCAL / $REMOTE are replaced with file paths. Empty = built-in viewer.</summary>
    private string externalDiff = "";
    public string ExternalDiff { get => externalDiff; set => Set(ref externalDiff, value, v => WriteString("ExternalDiff", v)); }

    /// <summary>External merge command. $BASE / $LOCAL / $REMOTE / $MERGED. Empty = built-in conflict editor.</summary>
    private string externalMerge = "";
    public string ExternalMerge { get => externalMerge; set => Set(ref externalMerge, value, v => WriteString("ExternalMerge", v)); }

    private List<string> badgeExcludes = [];
    public List<string> BadgeExcludes { get => badgeExcludes; set => Set(ref badgeExcludes, value, v => WriteList("BadgeExcludes", v)); }

    /// <summary>Terminal used by "ターミナルで開く": wt / pwsh / powershell / cmd / gitbash (empty = auto).</summary>
    private string terminal = "";
    public string Terminal { get => terminal; set => Set(ref terminal, value, v => WriteString("Terminal", v)); }

    private bool signOff;
    public bool SignOff { get => signOff; set => Set(ref signOff, value, v => WriteBool("SignOff", v)); }

    /// <summary>Learning mode: show the git commands (with explanations) that each dialog runs.</summary>
    private bool learningMode = true;
    public bool LearningMode { get => learningMode; set => Set(ref learningMode, value, v => WriteBool("LearningMode", v)); }

    private bool progressShowRaw;
    public bool ProgressShowRaw { get => progressShowRaw; set => Set(ref progressShowRaw, value, v => WriteBool("ProgressShowRaw", v)); }

    public void Remember(string message)
    {
        var m = message.Trim();
        if (m.Length == 0) return;
        var list = RecentMessages.Where(x => x != m).ToList();
        list.Insert(0, m);
        RecentMessages = list.Take(20).ToList();
    }

    public bool IsBadgeExcluded(string path) =>
        BadgeExcludes.Any(ex => ex.Trim().Length > 0 && PathUtil.IsUnder(path, Environment.ExpandEnvironmentVariables(ex.Trim())));

    // ---- persistence ----

    public void Reload()
    {
        using var k = Registry.CurrentUser.OpenSubKey(keyPath);
        gitPath = ReadString(k, "GitPath", "");
        badgesEnabled = ReadBool(k, "BadgesEnabled", true);
        showTrayIcon = ReadBool(k, "ShowTrayIcon", true);
        logLimit = Math.Max(50, ReadInt(k, "LogLimit", 500));
        diffSideBySide = ReadBool(k, "DiffSideBySide", false);
        recentMessages = ReadList(k, "RecentMessages");
        pushAfterCommit = ReadBool(k, "PushAfterCommit", false);
        cleanToTrash = ReadBool(k, "CleanToTrash", true);
        menuConfig = new MenuConfig
        {
            TopLevel = k?.GetValue("MenuTopLevel") is string[] top ? top.ToList() : ["sync", "commit"],
            Hidden = ReadList(k, "MenuHidden"),
            ShowBranch = ReadBool(k, "MenuShowBranch", true),
        };
        externalDiff = ReadString(k, "ExternalDiff", "");
        externalMerge = ReadString(k, "ExternalMerge", "");
        badgeExcludes = ReadList(k, "BadgeExcludes");
        terminal = ReadString(k, "Terminal", "");
        signOff = ReadBool(k, "SignOff", false);
        learningMode = ReadBool(k, "LearningMode", true);
        progressShowRaw = ReadBool(k, "ProgressShowRaw", false);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private void Set<T>(ref T field, T value, Action<T> save, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value) && field is not System.Collections.IList && field is not Core.MenuConfig) return;
        field = value;
        try { save(value); } catch { /* registry unavailable: keep in memory */ }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private RegistryKey OpenWrite() => Registry.CurrentUser.CreateSubKey(keyPath, true);
    private void WriteString(string n, string v) { using var k = OpenWrite(); k.SetValue(n, v, RegistryValueKind.String); }
    private void WriteBool(string n, bool v) { using var k = OpenWrite(); k.SetValue(n, v ? 1 : 0, RegistryValueKind.DWord); }
    private void WriteInt(string n, int v) { using var k = OpenWrite(); k.SetValue(n, v, RegistryValueKind.DWord); }
    private void WriteList(string n, List<string> v) { using var k = OpenWrite(); k.SetValue(n, v.ToArray(), RegistryValueKind.MultiString); }

    private static string ReadString(RegistryKey? k, string n, string d) => k?.GetValue(n) as string ?? d;
    private static bool ReadBool(RegistryKey? k, string n, bool d) => k?.GetValue(n) is int i ? i != 0 : d;
    private static int ReadInt(RegistryKey? k, string n, int d) => k?.GetValue(n) is int i ? i : d;
    private static List<string> ReadList(RegistryKey? k, string n) => k?.GetValue(n) is string[] a ? a.ToList() : [];
}
