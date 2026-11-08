using System.Windows.Controls;
using GitVelto.Core;

namespace GitVelto.UI;

// PLACEHOLDER: replaced by the real implementation.
public enum RemoteOpMode { Pull, Fetch, Push }
public enum CreateRefKind { Branch, Tag }

public sealed class RemoteOpView : GVView
{
    public RemoteOpView(Repo repo, RemoteOpMode mode)
    {
        Content = new TextBlock { Text = "未実装: RemoteOpView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class SwitchView : GVView
{
    public SwitchView(Repo repo)
    {
        Content = new TextBlock { Text = "未実装: SwitchView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class CreateRefView : GVView
{
    public CreateRefView(Repo repo, CreateRefKind kind, string? baseRef)
    {
        Content = new TextBlock { Text = "未実装: CreateRefView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class MergeView : GVView
{
    public MergeView(Repo repo)
    {
        Content = new TextBlock { Text = "未実装: MergeView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class StashSaveView : GVView
{
    public StashSaveView(Repo repo)
    {
        Content = new TextBlock { Text = "未実装: StashSaveView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class StashListView : GVView
{
    public StashListView(Repo repo)
    {
        Content = new TextBlock { Text = "未実装: StashListView", Margin = new System.Windows.Thickness(20) };
    }
}
