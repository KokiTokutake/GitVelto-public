using System.Windows.Controls;
using GitVelto.Core;

namespace GitVelto.UI;

// PLACEHOLDER: replaced by the real implementation.

public sealed class CloneView : GVView
{
    public CloneView(string parentDir)
    {
        Content = new TextBlock { Text = "未実装: CloneView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class InitRepoView : GVView
{
    public InitRepoView(string dir)
    {
        Content = new TextBlock { Text = "未実装: InitRepoView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class BlameView : GVView
{
    public BlameView(Repo repo, string path, string? revision)
    {
        Content = new TextBlock { Text = "未実装: BlameView", Margin = new System.Windows.Thickness(20) };
    }
}
