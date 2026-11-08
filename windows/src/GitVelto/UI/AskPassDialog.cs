using System.Windows.Controls;

namespace GitVelto.UI;

/// <summary>Credential prompt used as GIT_ASKPASS / SSH_ASKPASS (prints the answer to stdout).</summary>
public static class AskPassDialog
{
    public static int Run(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        bool secret = lower.Contains("password") || lower.Contains("passphrase") || lower.Contains("pin") || lower.Contains("token");
        bool confirm = lower.Contains("yes/no");
        var text = prompt.Length == 0 ? "入力してください" : prompt;
        string? value;
        if (secret)
        {
            var box = new PasswordBox { MinWidth = 340 };
            value = MessageDialog.Build(null, "GitVelto - 認証", text, MessageKind.Question, "OK", "キャンセル", box, out _) ? box.Password : null;
        }
        else
        {
            var box = new TextBox { MinWidth = 340, Text = confirm ? "yes" : "" };
            value = MessageDialog.Build(null, "GitVelto - 認証", text, MessageKind.Question, "OK", "キャンセル", box, out _) ? box.Text : null;
        }
        if (value == null) return 1;
        try
        {
            using var stdout = Console.OpenStandardOutput();
            var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\n");
            stdout.Write(bytes);
            stdout.Flush();
        }
        catch { return 1; }
        return 0;
    }
}
