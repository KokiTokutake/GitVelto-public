namespace GitVelto.Core;

public enum DiffLineKind { Meta, Hunk, Context, Add, Del, Note }

public sealed record DiffLine(int Id, DiffLineKind Kind, int? OldNo, int? NewNo, string Text);

public sealed record SideRow(int Id, DiffLine? Left, DiffLine? Right, DiffLine? Header);

public static class DiffParser
{
    public static List<DiffLine> Parse(string text)
    {
        var lines = new List<DiffLine>();
        int oldNo = 0, newNo = 0, idx = 0;
        bool inHunk = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.StartsWith("@@"))
            {
                inHunk = true;
                var parts = line.Split(' ');
                if (parts.Length >= 3)
                {
                    oldNo = int.TryParse(parts[1].TrimStart('-').Split(',')[0], out var a) ? a : 0;
                    newNo = int.TryParse(parts[2].TrimStart('+').Split(',')[0], out var b) ? b : 0;
                }
                lines.Add(new(idx, DiffLineKind.Hunk, null, null, line));
            }
            else if (line.StartsWith("diff "))
            {
                inHunk = false;
                lines.Add(new(idx, DiffLineKind.Meta, null, null, line));
            }
            else if (inHunk && line.StartsWith('+'))
            {
                lines.Add(new(idx, DiffLineKind.Add, null, newNo, line[1..]));
                newNo++;
            }
            else if (inHunk && line.StartsWith('-'))
            {
                lines.Add(new(idx, DiffLineKind.Del, oldNo, null, line[1..]));
                oldNo++;
            }
            else if (inHunk && line.StartsWith(' '))
            {
                lines.Add(new(idx, DiffLineKind.Context, oldNo, newNo, line[1..]));
                oldNo++; newNo++;
            }
            else if (line.StartsWith('\\'))
            {
                lines.Add(new(idx, DiffLineKind.Note, null, null, line));
            }
            else if (inHunk && line.Length == 0)
            {
                continue;
            }
            else
            {
                inHunk = false;
                lines.Add(new(idx, DiffLineKind.Meta, null, null, line));
            }
            idx++;
        }
        while (lines.Count > 0 && lines[^1].Kind == DiffLineKind.Meta && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public static List<SideRow> SideBySide(IEnumerable<DiffLine> lines)
    {
        var rows = new List<SideRow>();
        var dels = new List<DiffLine>();
        var adds = new List<DiffLine>();
        int n = 0;
        void Flush()
        {
            int c = Math.Max(dels.Count, adds.Count);
            for (int i = 0; i < c; i++)
                rows.Add(new(n++, i < dels.Count ? dels[i] : null, i < adds.Count ? adds[i] : null, null));
            dels.Clear(); adds.Clear();
        }
        foreach (var l in lines)
        {
            switch (l.Kind)
            {
                case DiffLineKind.Del:
                    if (adds.Count > 0) Flush();
                    dels.Add(l);
                    break;
                case DiffLineKind.Add:
                    adds.Add(l);
                    break;
                case DiffLineKind.Context:
                    Flush();
                    rows.Add(new(n++, l, l, null));
                    break;
                default:
                    Flush();
                    rows.Add(new(n++, null, null, l));
                    break;
            }
        }
        Flush();
        return rows;
    }

    /// <summary>Ids of the first line of each change block (for "next/previous change").</summary>
    public static List<int> ChangeStarts(IReadOnlyList<DiffLine> lines)
    {
        var o = new List<int>();
        bool prev = false;
        foreach (var l in lines)
        {
            bool change = l.Kind is DiffLineKind.Add or DiffLineKind.Del;
            if (change && !prev) o.Add(l.Id);
            prev = change;
        }
        return o;
    }
}

// ---- commit graph ----

public sealed record GraphRow(int Node, IReadOnlyList<string?> Top, IReadOnlyList<string?> Bottom, IReadOnlyList<string> Parents, string Hash);

public static class CommitGraph
{
    public static List<GraphRow> Compute(IReadOnlyList<Commit> commits)
    {
        var lanes = new List<string?>();
        var rows = new List<GraphRow>(commits.Count);
        foreach (var c in commits)
        {
            var top = lanes.ToArray();
            int node = lanes.IndexOf(c.Hash);
            if (node < 0)
            {
                int free = lanes.IndexOf(null);
                if (free >= 0) node = free; else { node = lanes.Count; lanes.Add(null); }
            }
            for (int i = 0; i < lanes.Count; i++) if (i != node && lanes[i] == c.Hash) lanes[i] = null;
            if (c.Parents.Count == 0)
            {
                lanes[node] = null;
            }
            else
            {
                lanes[node] = c.Parents[0];
                foreach (var p in c.Parents.Skip(1))
                {
                    if (lanes.Contains(p)) continue;
                    int free = lanes.IndexOf(null);
                    if (free >= 0) lanes[free] = p; else lanes.Add(p);
                }
            }
            while (lanes.Count > 0 && lanes[^1] == null) lanes.RemoveAt(lanes.Count - 1);
            rows.Add(new GraphRow(node, top, lanes.ToArray(), c.Parents, c.Hash));
        }
        return rows;
    }
}

// ---- conflicts ----

public enum ConflictResolution { Ours, Theirs, OursThenTheirs, TheirsThenOurs, Custom }

/// <summary>One piece of a conflicted file.</summary>
public sealed class ConflictSegment
{
    public int Id { get; init; }
    public bool IsConflict { get; init; }
    public string Text { get; init; } = "";
    public string Ours { get; set; } = "";
    public string? Base { get; set; }
    public string Theirs { get; set; } = "";
    public string OursLabel { get; set; } = "";
    public string TheirsLabel { get; set; } = "";
    public ConflictResolution? Resolution { get; set; }
    public string Resolved { get; set; } = "";

    public string Output
    {
        get
        {
            if (!IsConflict) return Text;
            if (Resolution != null) return Resolved;
            var s = $"<<<<<<< {OursLabel}\n" + Ours;
            if (Base != null) s += "||||||| base\n" + Base;
            s += "=======\n" + Theirs + $">>>>>>> {TheirsLabel}\n";
            return s;
        }
    }

    public void Apply(ConflictResolution r)
    {
        Resolution = r;
        Resolved = r switch
        {
            ConflictResolution.Ours => Ours,
            ConflictResolution.Theirs => Theirs,
            ConflictResolution.OursThenTheirs => Ours + Theirs,
            ConflictResolution.TheirsThenOurs => Theirs + Ours,
            _ => Resolved,
        };
    }
}

public static class ConflictParser
{
    public static List<ConflictSegment> Parse(string text)
    {
        var segs = new List<ConflictSegment>();
        var plain = new System.Text.StringBuilder();
        int state = 0; // 0 plain, 1 ours, 2 base, 3 theirs
        string ours = "", theirs = "", oursLabel = "";
        string? bas = null;
        var lines = text.Split('\n').ToList();
        bool endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var l = line + (i < lines.Count - 1 || endsWithNewline ? "\n" : "");
            var bare = line.TrimEnd('\r');
            switch (state)
            {
                case 0:
                    if (bare.StartsWith("<<<<<<<"))
                    {
                        if (plain.Length > 0) { segs.Add(new ConflictSegment { Id = segs.Count, IsConflict = false, Text = plain.ToString() }); plain.Clear(); }
                        ours = ""; theirs = ""; bas = null;
                        oursLabel = bare[7..].Trim();
                        state = 1;
                    }
                    else plain.Append(l);
                    break;
                case 1:
                    if (bare.StartsWith("|||||||")) { bas = ""; state = 2; }
                    else if (bare.StartsWith("=======")) state = 3;
                    else ours += l;
                    break;
                case 2:
                    if (bare.StartsWith("=======")) state = 3; else bas += l;
                    break;
                default:
                    if (bare.StartsWith(">>>>>>>"))
                    {
                        segs.Add(new ConflictSegment
                        {
                            Id = segs.Count, IsConflict = true, Ours = ours, Base = bas, Theirs = theirs,
                            OursLabel = oursLabel, TheirsLabel = bare[7..].Trim(),
                        });
                        state = 0;
                    }
                    else theirs += l;
                    break;
            }
        }
        if (plain.Length > 0) segs.Add(new ConflictSegment { Id = segs.Count, IsConflict = false, Text = plain.ToString() });
        return segs;
    }
}
