namespace GitVelto.Core;

public enum FileState { Modified, Added, Deleted, Renamed, Copied, TypeChanged, Untracked, Ignored, Conflict }

public static class FileStateExt
{
    public static string Label(this FileState s) => s switch
    {
        FileState.Modified => "変更",
        FileState.Added => "追加",
        FileState.Deleted => "削除",
        FileState.Renamed => "名前変更",
        FileState.Copied => "コピー",
        FileState.TypeChanged => "種類変更",
        FileState.Untracked => "未追跡",
        FileState.Ignored => "無視",
        _ => "競合",
    };

    public static bool IsTracked(this FileState s) => s != FileState.Untracked && s != FileState.Ignored;
}

public sealed record StatusEntry(string Path, string? OrigPath, char X, char Y)
{
    public string Id => Path;

    public FileState State
    {
        get
        {
            if (X == '?') return FileState.Untracked;
            if (X == '!') return FileState.Ignored;
            if (X == 'U' || Y == 'U' || (X == 'A' && Y == 'A') || (X == 'D' && Y == 'D')) return FileState.Conflict;
            if (X == 'R' || Y == 'R') return FileState.Renamed;
            if (X == 'C' || Y == 'C') return FileState.Copied;
            if (X == 'A') return Y == 'D' ? FileState.Deleted : FileState.Added;
            if (X == 'D' || Y == 'D') return FileState.Deleted;
            if (X == 'T' || Y == 'T') return FileState.TypeChanged;
            return FileState.Modified;
        }
    }

    public bool Staged => X != ' ' && X != '?' && X != '!' && State != FileState.Conflict;
    public string FileName => System.IO.Path.GetFileName(Path.TrimEnd('/'));
    public string Ext => System.IO.Path.GetExtension(Path.TrimEnd('/')).TrimStart('.');
    public string DisplayPath => OrigPath != null ? $"{OrigPath} → {Path}" : Path;
}

public sealed record Commit(
    string Hash, string Short, IReadOnlyList<string> Parents, string Author, string Email,
    DateTimeOffset Date, IReadOnlyList<string> Refs, string Subject, string Body)
{
    public string Id => Hash;
    public string DateText => Date.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
}

public sealed record ChangedFile(string Status, string Path, string? OldPath)
{
    public string Id => Path + (OldPath ?? "");

    public FileState State => Status.FirstOrDefault() switch
    {
        'A' => FileState.Added,
        'D' => FileState.Deleted,
        'R' => FileState.Renamed,
        'C' => FileState.Copied,
        'T' => FileState.TypeChanged,
        'U' => FileState.Conflict,
        '?' => FileState.Untracked,
        _ => FileState.Modified,
    };

    public string DisplayPath => OldPath != null ? $"{OldPath} → {Path}" : Path;
}

public enum RefKind { Local, Remote, Tag }

public sealed record RefInfo(string FullName, string Name, RefKind Kind, string Hash, string Upstream, bool IsHead)
{
    public string Id => FullName;
}

public sealed record StashEntry(string Ref, string Message, DateTimeOffset Date)
{
    public string Id => Ref;
}

public sealed record RefDetail(
    string FullName, string Name, RefKind Kind, string Hash, string Upstream, bool IsHead,
    DateTimeOffset Date, string Subject, string Track, string Author)
{
    public string Id => FullName;
    public string KindLabel => Kind switch { RefKind.Local => "ローカル", RefKind.Remote => "リモート", _ => "タグ" };
}

public sealed record ReflogEntry(string Hash, string Short, string Selector, string Subject, DateTimeOffset Date)
{
    public string Id => Selector;
}

public sealed record TreeEntry(string Mode, string Type, string Hash, long Size, string Name, string Path)
{
    public bool IsDir => Type == "tree";
    public bool IsSubmodule => Type == "commit";
}

public sealed record RemoteInfo(string Name, string FetchUrl, string PushUrl);

public sealed record SubmoduleInfo(char State, string Hash, string Path, string Describe)
{
    public string StateLabel => State switch
    {
        '-' => "未初期化",
        '+' => "コミットが異なる",
        'U' => "競合",
        _ => "最新",
    };
}

public enum RepoOperationState { None, Merging, Rebasing, CherryPicking, Reverting, ApplyingPatches, Bisecting }

public static class RepoOperationStateExt
{
    public static string Label(this RepoOperationState s) => s switch
    {
        RepoOperationState.Merging => "マージ中",
        RepoOperationState.Rebasing => "リベース中",
        RepoOperationState.CherryPicking => "チェリーピック中",
        RepoOperationState.Reverting => "リバート中",
        RepoOperationState.ApplyingPatches => "パッチ適用中",
        RepoOperationState.Bisecting => "二分探索中",
        _ => "",
    };

    /// <summary>git sub-command used for --continue / --abort.</summary>
    public static string? Command(this RepoOperationState s) => s switch
    {
        RepoOperationState.Merging => "merge",
        RepoOperationState.Rebasing => "rebase",
        RepoOperationState.CherryPicking => "cherry-pick",
        RepoOperationState.Reverting => "revert",
        RepoOperationState.ApplyingPatches => "am",
        _ => null,
    };
}
