using System.Windows;
using GitVelto.Core;
using WinForms = System.Windows.Forms;

namespace GitVelto.UI;

/// <summary>Notification-area icon (the macOS version's menu bar item).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon icon;

    public TrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        void Add(string text, Action a) => menu.Items.Add(text, null, (_, _) => Application.Current.Dispatcher.BeginInvoke(a));
        Add("リポジトリのログを開く…", () => Choose("ログを表示するリポジトリを選択", GitAction.Log));
        Add("変更をチェック…", () => Choose("変更をチェックするリポジトリを選択", GitAction.Status));
        Add("同期…", () => Choose("同期するリポジトリを選択", GitAction.Sync));
        Add("クローン…", () => Choose("クローン先の親フォルダを選択", GitAction.Clone));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        Add("設定…", () => WindowManager.Shared.ShowSettings());
        Add("Git 用語集", () => WindowManager.Shared.ShowGlossary());
        Add("実行したコマンドの履歴", () => WindowManager.Shared.ShowCommandHistory());
        Add("ヘルプ", () => WindowManager.Shared.Perform(GitAction.Help, []));
        Add("GitVelto について", () => WindowManager.Shared.Perform(GitAction.About, []));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        Add("終了", () => Application.Current.Shutdown());

        System.Drawing.Icon? ico = null;
        try
        {
            var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"))?.Stream;
            if (s != null) ico = new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
        }
        catch { }
        icon = new WinForms.NotifyIcon
        {
            Icon = ico ?? System.Drawing.SystemIcons.Application,
            Text = "GitVelto",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) Application.Current.Dispatcher.BeginInvoke(() => WindowManager.Shared.ShowSettings());
        };
    }

    private static void Choose(string title, GitAction action)
    {
        if (Shell.ChooseFolder(title) is { } p) WindowManager.Shared.Perform(action, [p]);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
