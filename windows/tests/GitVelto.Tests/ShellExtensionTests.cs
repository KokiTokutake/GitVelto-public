using System.Runtime.InteropServices;
using System.Text;

namespace GitVelto.Tests;

/// <summary>
/// Loads GitVeltoShell.dll directly (without registering it) and drives its COM objects the way Explorer does:
/// overlay handlers (IShellIconOverlayIdentifier) and the context menu (IShellExtInit + IContextMenu).
/// Needs the C++ build (windows\build.ps1 or MSBuild); skipped when the DLL is missing.
/// </summary>
public class ShellExtensionTests
{
    // ---- COM interop ----

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr outer, ref Guid riid, out IntPtr obj);
        [PreserveSig] int LockServer(bool fLock);
    }

    [ComImport, Guid("0C6C4200-C589-11D0-999A-00C04FD655E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellIconOverlayIdentifier
    {
        [PreserveSig] int IsMemberOf([MarshalAs(UnmanagedType.LPWStr)] string path, uint attrib);
        [PreserveSig] int GetOverlayInfo([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconFile, int cchMax, out int index, out uint flags);
        [PreserveSig] int GetPriority(out int priority);
    }

    [ComImport, Guid("000214E8-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellExtInit
    {
        [PreserveSig] int Initialize(IntPtr pidlFolder, IntPtr dataObject, IntPtr hkeyProgId);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(IntPtr pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr reserved, StringBuilder name, uint cchMax);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectFn(ref Guid clsid, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("user32")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32")] private static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32")] private static extern int GetMenuItemCount(IntPtr m);
    [DllImport("user32")] private static extern IntPtr GetSubMenu(IntPtr m, int pos);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetMenuStringW(IntPtr m, uint item, StringBuilder s, int max, uint flags);
    [DllImport("shell32", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray array);
    [DllImport("ole32")] private static extern void CoTaskMemFree(IntPtr p);

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    }

    private const uint MF_BYPOSITION = 0x400;
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IClassFactory = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    private static readonly Guid BHID_DataObject = new("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");
    private static readonly Guid ClsidMenu = new("6E7A9B2C-3F1D-4C8E-9A51-7D2B0C4E8F10");
    private static Guid OverlayClsid(int kind) => new($"6E7A9B2C-3F1D-4C8E-9A51-7D2B0C4E8F1{kind + 1}");
    private static readonly string[] Kinds = ["normal", "modified", "added", "conflict", "untracked", "ignored"];

    private static string? FindDll()
    {
        var root = Path.Combine(TestEnv.RepoRootDir(), "windows", "src", "GitVeltoShell", "bin", "x64");
        foreach (var cfg in new[] { "Release", "Debug" })
        {
            var p = Path.Combine(root, cfg, "GitVeltoShell.dll");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static T Create<T>(IntPtr module, Guid clsid)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectFn>(GetProcAddress(module, "DllGetClassObject"));
        var iid = IID_IClassFactory;
        Assert.Equal(0, fn(ref clsid, ref iid, out var pf));
        var factory = (IClassFactory)Marshal.GetObjectForIUnknown(pf);
        Marshal.Release(pf);
        var unk = IID_IUnknown;
        Assert.Equal(0, factory.CreateInstance(IntPtr.Zero, ref unk, out var obj));
        var o = (T)Marshal.GetObjectForIUnknown(obj);
        Marshal.Release(obj);
        return o;
    }

    private static List<(string Title, List<string>? Sub)> ReadMenu(IntPtr menu)
    {
        var items = new List<(string, List<string>?)>();
        int n = GetMenuItemCount(menu);
        for (int i = 0; i < n; i++)
        {
            var sb = new StringBuilder(512);
            GetMenuStringW(menu, (uint)i, sb, sb.Capacity, MF_BYPOSITION);
            var sub = GetSubMenu(menu, i);
            items.Add((sb.ToString(), sub == IntPtr.Zero ? null : ReadMenu(sub).Select(x => x.Item1).ToList()));
        }
        return items;
    }

    private static List<(string Title, List<string>? Sub)> MenuFor(IntPtr module, params string[] paths)
    {
        var menuObj = Create<IContextMenu>(module, ClsidMenu);
        var init = (IShellExtInit)menuObj;
        var pidls = paths.Select(p =>
        {
            Assert.Equal(0, SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _));
            return pidl;
        }).ToArray();
        Assert.Equal(0, SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var array));
        var bhid = BHID_DataObject; var iid = IID_IDataObject;
        Assert.Equal(0, array.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var dataObject));
        Assert.Equal(0, init.Initialize(IntPtr.Zero, dataObject, IntPtr.Zero));
        Marshal.Release(dataObject);
        foreach (var p in pidls) CoTaskMemFree(p);
        var hmenu = CreatePopupMenu();
        try
        {
            int hr = menuObj.QueryContextMenu(hmenu, 0, 1, 0x7FFF, 0);
            Assert.True(hr >= 0, $"QueryContextMenu 0x{hr:X8}");
            return ReadMenu(hmenu).Where(i => i.Title.Length > 0 || i.Sub != null).ToList();
        }
        finally { DestroyMenu(hmenu); }
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void OverlaysAndContextMenu()
    {
        var src = FindDll();
        if (src == null) return; // C++ part not built
        // The DLL starts GitVeltoCache.exe from its own folder: put both side by side.
        var dir = Path.Combine(TestEnv.Temp, "shell");
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, "GitVeltoShell.dll");
        File.Copy(src, dll, true);
        foreach (var f in Directory.GetFiles(AppContext.BaseDirectory))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("GitVeltoCache") || name.StartsWith("GitVelto.Core")) File.Copy(f, Path.Combine(dir, name), true);
        }
        var repos = TestEnv.MakeTestRepos();
        var work = Path.Combine(repos, "work");

        RunSta(() =>
        {
            var module = LoadLibraryW(dll);
            Assert.NotEqual(IntPtr.Zero, module);

            // ---- overlays ----
            var handlers = Enumerable.Range(0, 6).Select(k => Create<IShellIconOverlayIdentifier>(module, OverlayClsid(k))).ToArray();
            string? Badge(string path)
            {
                // The first query may start the cache and come back empty: retry briefly.
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    for (int k = 0; k < 6; k++)
                        if (handlers[k].IsMemberOf(path, 0) == 0) return Kinds[k];
                    Thread.Sleep(250);
                }
                return null;
            }
            Assert.Equal("modified", Badge(Path.Combine(work, "README.md")));
            Assert.Equal("normal", Badge(Path.Combine(work, ".gitignore")));
            Assert.Equal("ignored", Badge(Path.Combine(work, "debug.log")));
            Assert.Equal("untracked", Badge(Path.Combine(work, "日本語メモ.txt")));
            Assert.Equal("added", Badge(Path.Combine(work, "src", "new_feature.swift")));
            Assert.Equal("modified", Badge(Path.Combine(work, "src")));
            Assert.Equal("conflict", Badge(Path.Combine(repos, "conflict")));
            var icon = new StringBuilder(1024);
            Assert.Equal(0, handlers[3].GetOverlayInfo(icon, icon.Capacity, out _, out var flags));
            Assert.EndsWith(@"overlays\conflict.ico", icon.ToString());

            // ---- context menu ----
            var rootMenu = MenuFor(module, work);
            var titles = rootMenu.Select(i => i.Title).ToList();
            Assert.Contains("Git 同期…", titles);
            Assert.Contains("Git コミット -> \"main\"…", titles);
            var sub = rootMenu.First(i => i.Title == "GitVelto").Sub!;
            Assert.Contains("ログを表示", sub);
            Assert.Contains("二分探索を開始 (bisect)…", sub);
            Assert.DoesNotContain("名前の変更 (git mv)…", sub);

            var fileMenu = MenuFor(module, Path.Combine(work, "README.md"));
            var fsub = fileMenu.First(i => i.Title == "GitVelto").Sub!;
            Assert.Contains("差分", fsub);
            Assert.Contains("元に戻す…", fsub);
            Assert.Contains("Blame", fsub);

            var outside = MenuFor(module, repos);
            Assert.Contains("Git クローン…", outside.Select(i => i.Title));
            Assert.Contains("Git ここにリポジトリを作成…", outside.Select(i => i.Title));
        });
    }
}
