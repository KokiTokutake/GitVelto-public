using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Windows shell helpers (open, reveal, clipboard, terminal, file dialogs, icons).</summary>
public static class Shell
{
    public static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { WindowManager.Alert("開けません", $"{path}\n{e.Message}"); }
    }

    /// <summary>Opens Explorer with the given items selected.</summary>
    public static void Reveal(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        try
        {
            if (Directory.Exists(list[0]) && list.Count == 1) Process.Start("explorer.exe", $"\"{list[0]}\"");
            else Process.Start("explorer.exe", $"/select,\"{list[0]}\"");
        }
        catch { }
    }

    public static void Copy(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch (COMException) { Thread.Sleep(50); }
        }
    }

    public static string? ClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { return null; }
    }

    /// <summary>Opens a terminal in <paramref name="dir"/> (Windows Terminal / PowerShell / cmd / Git Bash).</summary>
    public static void OpenTerminal(string dir)
    {
        var pref = AppSettings.Shared.Terminal;
        string? bash = Git.InstallRoot is { } r ? Path.Combine(r, "git-bash.exe") : null;
        var candidates = new List<(string id, Func<ProcessStartInfo?> psi)>
        {
            ("wt", () => FindOnPath("wt.exe") is { } wt ? new ProcessStartInfo(wt) { ArgumentList = { "-d", dir } } : null),
            ("pwsh", () => FindOnPath("pwsh.exe") is { } p ? new ProcessStartInfo(p) { WorkingDirectory = dir, UseShellExecute = true } : null),
            ("powershell", () => new ProcessStartInfo("powershell.exe") { WorkingDirectory = dir, UseShellExecute = true }),
            ("cmd", () => new ProcessStartInfo("cmd.exe") { WorkingDirectory = dir, UseShellExecute = true }),
            ("gitbash", () => bash != null && File.Exists(bash) ? new ProcessStartInfo(bash) { ArgumentList = { $"--cd={dir}" } } : null),
        };
        var ordered = candidates.Where(c => c.id == pref).Concat(candidates.Where(c => c.id != pref));
        foreach (var (_, make) in ordered)
        {
            try
            {
                if (make() is { } psi) { Process.Start(psi); return; }
            }
            catch { }
        }
        WindowManager.Alert("ターミナルを起動できません", dir);
    }

    public static string? FindOnPath(string exe)
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(d.Trim(), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", exe);
        return File.Exists(local) ? local : null;
    }

    public static string? ChooseFolder(string title, string? initial = null, Window? owner = null)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (initial != null && Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog(owner) == true ? d.FolderName : null;
    }

    public static string[]? ChooseFiles(string title, string filter = "すべてのファイル|*.*", string? initial = null, bool multi = true, Window? owner = null)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, Multiselect = multi };
        if (initial != null && Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog(owner) == true ? d.FileNames : null;
    }

    public static string? SaveFile(string title, string fileName, string filter = "すべてのファイル|*.*", string? initial = null, Window? owner = null)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Title = title, FileName = fileName, Filter = filter };
        if (initial != null && Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog(owner) == true ? d.FileName : null;
    }

    /// <summary>Opens a text file in the default editor (Notepad when there is no association).</summary>
    public static void OpenInTextEditor(string path)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path } }); }
        catch (Exception e) { WindowManager.Alert("開けません", e.Message); }
    }

    /// <summary>Moves files/folders to the Recycle Bin.</summary>
    public static void MoveToRecycleBin(string path)
    {
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else if (File.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    // ---- icons ----

    private static readonly Dictionary<string, ImageSource> IconCache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_ATTRIBUTE_DIRECTORY = 0x10;

    /// <summary>Small (16px) shell icon for a file name (by extension) or a folder.</summary>
    public static ImageSource? Icon(string name, bool isDir = false)
    {
        var key = isDir ? "<dir>" : Path.GetExtension(name);
        if (key.Equals(".exe", StringComparison.OrdinalIgnoreCase) || key.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) key = "x" + key;
        if (IconCache.TryGetValue(key, out var cached)) return cached;
        var info = new SHFILEINFO();
        var probe = isDir ? "folder" : "file" + Path.GetExtension(name);
        SHGetFileInfo(probe, isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            IconCache[key] = src;
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }
}
