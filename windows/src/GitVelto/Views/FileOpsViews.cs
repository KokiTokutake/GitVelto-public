using System.Windows.Controls;
using GitVelto.Core;

namespace GitVelto.UI;

// PLACEHOLDER: replaced by the real implementation.
public enum FileActionMode { Add, Revert, Resolve }

public sealed class FileActionView : GVView
{
    public FileActionView(Repo repo, IReadOnlyList<string> paths, FileActionMode mode)
    {
        Content = new TextBlock { Text = "未実装: FileActionView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class DeleteView : GVView
{
    public DeleteView(Repo repo, IReadOnlyList<string> paths, bool keepLocal)
    {
        Content = new TextBlock { Text = "未実装: DeleteView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class RenameView : GVView
{
    public RenameView(Repo repo, string path)
    {
        Content = new TextBlock { Text = "未実装: RenameView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class IgnoreView : GVView
{
    public IgnoreView(Repo repo, IReadOnlyList<string> paths)
    {
        Content = new TextBlock { Text = "未実装: IgnoreView", Margin = new System.Windows.Thickness(20) };
    }
}

public sealed class CleanView : GVView
{
    public CleanView(Repo repo, IReadOnlyList<string> paths)
    {
        Content = new TextBlock { Text = "未実装: CleanView", Margin = new System.Windows.Thickness(20) };
    }
}
