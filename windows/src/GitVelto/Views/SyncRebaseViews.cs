using System.Windows.Controls;
using GitVelto.Core;

namespace GitVelto.UI;

// PLACEHOLDER: replaced by the real implementation.

public sealed class SyncView : GVView
{
    public SyncView(Repo repo)
    {
        Content = new TextBlock { Text = "未実装: SyncView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class RebaseView : GVView
{
    public RebaseView(Repo repo, string? initialUpstream)
    {
        Content = new TextBlock { Text = "未実装: RebaseView", Margin = new System.Windows.Thickness(20) };
    }
}
