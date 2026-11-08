namespace GitVelto.Core;

/// <summary>Snapshot of one repository's working-tree state, used to compute overlay badges.</summary>
public sealed class RepoSnapshot
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    public string Root { get; }
    public bool Valid { get; }
    public BadgeKind Overall { get; }

    private readonly Dictionary<string, BadgeKind> files = new(Cmp);        // NFC rel path -> kind (tracked changes)
    private readonly Dictionary<string, BadgeKind> dirAggregate = new(Cmp); // NFC rel dir -> worst kind beneath
    private readonly HashSet<string> untrackedFiles = new(Cmp);
    private readonly HashSet<string> ignoredFiles = new(Cmp);
    private readonly List<string> untrackedDirs = [];                        // NFC rel, no trailing slash
    private readonly List<string> ignoredDirs = [];

    public RepoSnapshot(string root, IEnumerable<StatusEntry> entries, bool valid)
    {
        Root = root;
        Valid = valid;
        var worst = BadgeKind.Normal;
        foreach (var e in entries)
        {
            var rel = e.Path.Nfc();
            switch (e.State)
            {
                case FileState.Untracked:
                    if (rel.EndsWith('/')) untrackedDirs.Add(rel.TrimEnd('/')); else untrackedFiles.Add(rel);
                    break;
                case FileState.Ignored:
                    if (rel.EndsWith('/')) ignoredDirs.Add(rel.TrimEnd('/')); else ignoredFiles.Add(rel);
                    break;
                default:
                    var kind = e.State switch
                    {
                        FileState.Conflict => BadgeKind.Conflict,
                        FileState.Added => BadgeKind.Added,
                        _ => BadgeKind.Modified,
                    };
                    files[rel] = kind;
                    if (kind.Severity() > worst.Severity()) worst = kind == BadgeKind.Added ? BadgeKind.Modified : kind;
                    // Aggregate onto ancestor folders
                    var comps = rel.Split('/');
                    var acc = "";
                    var folderKind = kind == BadgeKind.Conflict ? BadgeKind.Conflict : BadgeKind.Modified;
                    for (int i = 0; i < comps.Length - 1; i++)
                    {
                        acc = acc.Length == 0 ? comps[i] : acc + "/" + comps[i];
                        if (!dirAggregate.TryGetValue(acc, out var cur) || cur.Severity() < folderKind.Severity())
                            dirAggregate[acc] = folderKind;
                    }
                    break;
            }
        }
        Overall = worst;
    }

    private static bool Under(string rel, List<string> dirs) =>
        dirs.Any(d => rel.Equals(d, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>Badge for a repository-relative path ("/" separators).</summary>
    public BadgeKind? Badge(string rel, bool isDir)
    {
        if (!Valid) return null;
        var r = rel.Nfc();
        if (ignoredFiles.Contains(r) || Under(r, ignoredDirs)) return BadgeKind.Ignored;
        if (untrackedFiles.Contains(r) || Under(r, untrackedDirs)) return BadgeKind.Untracked;
        if (isDir) return dirAggregate.TryGetValue(r, out var d) ? d : BadgeKind.Normal;
        return files.TryGetValue(r, out var f) ? f : BadgeKind.Normal;
    }

    /// <summary>Computes the snapshot by running git status (used by the cache and the tests).</summary>
    public static RepoSnapshot Compute(string root)
    {
        var r = Git.Run(["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignored=matching"], root);
        return new RepoSnapshot(root, r.Ok ? Repo.ParseStatus(r.Stdout) : [], r.Ok);
    }
}
