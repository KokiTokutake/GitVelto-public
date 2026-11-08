using System.Windows.Controls;
using GitVelto.Core;

namespace GitVelto.UI;

// PLACEHOLDER: replaced by the real implementation.

public sealed class CommitView : GVView
{
    public CommitView(Repo repo, IReadOnlyList<string> paths)
    {
        Content = new TextBlock { Text = "未実装: CommitView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class StatusView : GVView
{
    public StatusView(Repo repo, IReadOnlyList<string> paths)
    {
        Content = new TextBlock { Text = "未実装: StatusView", Margin = new System.Windows.Thickness(20) };
    }
}
