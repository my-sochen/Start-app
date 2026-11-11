namespace StartApp.Core;

/// <summary>In-memory mutations over a Store. Every method leaves the store consistent (no dangling refs, no orphans, no duplicate membership).</summary>
public sealed class Catalog
{
    private readonly Store _s;
    private readonly Func<string> _rand;
    /// <param name="randomPart">Test hook for deterministic id parts; default is random.</param>
    public Catalog(Store s, Func<string>? randomPart = null) { _s = s; _rand = randomPart ?? (() => Guid.NewGuid().ToString("N")[..8]); }
    public Store Store => _s;

    // Identity key: type + exact target. No trim, case folding, slash rewriting or URL normalization, so distinct
    // targets are never merged. Equivalent spellings stay separate items (identity decision still open).
    public static string Key(ItemType type, string target) => type + "\u0000" + target;

    /// <summary>Data validity only, not an execution-safety guarantee. Whitespace at the edges is rejected, never silently trimmed.</summary>
    public static string? ValidateTarget(ItemType type, string target)
    {
        if (!Enum.IsDefined(type)) return "unknown item type";
        if (string.IsNullOrEmpty(target)) return "target is empty";
        if (char.IsWhiteSpace(target[0]) || char.IsWhiteSpace(target[^1])) return "target has leading/trailing whitespace";
        if (target.Any(char.IsControl)) return "target has control characters";
        if (type == ItemType.Link && !Uri.TryCreate(target, UriKind.Absolute, out _)) return "link must be an absolute URI";
        return null;
    }

    /// <summary>Explicit absolute Windows path (drive or UNC share). Pure string rule; no filesystem access.</summary>
    public static bool IsAbsoluteWindowsPath(string v) => AbsWin.IsMatch(v);

    private static readonly System.Text.RegularExpressions.Regex AbsWin = new(@"^([A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\).+", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Executable identity = one explicit absolute Windows path chosen by the user. No aliases, no arguments, no command-line parsing
    /// (characters like &amp; are legal in paths and are not special here). Existence/authorization is a future platform-adapter concern.
    /// Browser: links only. OpenWith: files/folders only.</summary>
    public static string? ValidateLaunch(ItemType type, LaunchConfig? l)
    {
        if (l is null) return null;
        foreach (var v in new[] { l.OpenWith, l.Browser })
            if (v is not null && (!AbsWin.IsMatch(v) || v.Any(char.IsControl) || char.IsWhiteSpace(v[^1]))) return "launch executable must be an explicit absolute Windows path";
        if (l.OpenWith is not null && type is ItemType.Link or ItemType.Program) return $"openWith not allowed for {type}";
        if (l.Browser is not null && type != ItemType.Link) return $"browser not allowed for {type}";
        return null;
    }

    private string NewId(string prefix, Func<string, bool> exists)
    {
        for (var i = 0; i < 100; i++) { var id = prefix + _rand(); if (!exists(id)) return id; }
        throw new InvalidOperationException("could not generate a unique id");
    }

    public Item GetOrAddItem(ItemType type, string name, string target)
    {
        var te = ValidateTarget(type, target); if (te != null) throw new ArgumentException(te);
        var key = Key(type, target);
        var existing = _s.Items.Values.FirstOrDefault(i => Key(i.Type, i.Target) == key);
        if (existing != null) return existing;
        var item = new Item(NewId("it_", id => _s.Items.ContainsKey(id)), type, name, target);
        if (!_s.Items.TryAdd(item.Id, item)) throw new InvalidOperationException("item id collision");
        return item;
    }

    public Workspace CreateWorkspace(string name, string? description = null)
    {
        var w = new Workspace { Id = NewId("ws_", id => _s.Workspaces.Any(w => w.Id == id)), Name = name, Description = description };
        _s.Workspaces.Add(w);
        return w;
    }

    private Workspace Ws(string id) => _s.Workspaces.FirstOrDefault(w => w.Id == id) ?? throw new KeyNotFoundException(id);

    /// <summary>Adds an item reference. Existing membership is kept as is (no duplicate, launch config not overwritten).</summary>
    public bool AddToWorkspace(string workspaceId, string itemId, LaunchConfig? launch = null)
    {
        var w = Ws(workspaceId);
        if (!_s.Items.ContainsKey(itemId)) throw new KeyNotFoundException(itemId);
        if (w.Items.Any(m => m.ItemId == itemId)) return false;
        var le = ValidateLaunch(_s.Items[itemId].Type, launch); if (le != null) throw new ArgumentException(le);
        w.Items.Add(new Membership(itemId, launch));
        return true;
    }

    public void RemoveFromWorkspace(string workspaceId, string itemId)
    {
        Ws(workspaceId).Items.RemoveAll(m => m.ItemId == itemId);
        RemoveOrphans();
    }

    public void DeleteWorkspace(string workspaceId)
    {
        _s.Workspaces.Remove(Ws(workspaceId));
        RemoveOrphans();
    }

    /// <summary>Local edit: only this workspace's reference changes. Reuses a matching catalog item or creates one; old item removed if orphaned. Launch config kept.</summary>
    public Item ReplaceTargetLocal(string workspaceId, string itemId, string newTarget, string? newName = null)
    {
        var w = Ws(workspaceId);
        var idx = w.Items.FindIndex(m => m.ItemId == itemId);
        if (idx < 0) throw new KeyNotFoundException(itemId);
        var old = _s.Items[itemId];
        var item = GetOrAddItem(old.Type, newName ?? old.Name, newTarget);
        if (item.Id == itemId) return item;
        var launch = w.Items[idx].Launch;
        if (w.Items.Any(m => m.ItemId == item.Id)) w.Items.RemoveAt(idx); // target already present in this workspace: keep one membership
        else w.Items[idx] = new Membership(item.Id, launch);
        RemoveOrphans();
        return item;
    }

    public int RemoveOrphans()
    {
        var used = _s.Workspaces.SelectMany(w => w.Items).Select(m => m.ItemId).ToHashSet();
        var orphans = _s.Items.Keys.Where(k => !used.Contains(k)).ToList();
        foreach (var k in orphans) _s.Items.Remove(k);
        return orphans.Count;
    }

    /// <summary>Reverse lookup by scan (no persisted counter/index, per decision).</summary>
    public IEnumerable<Workspace> WorkspacesContaining(string itemId) => _s.Workspaces.Where(w => w.Items.Any(m => m.ItemId == itemId));

    public IReadOnlyList<string> Validate()
    {
        var errs = new List<string>();
        foreach (var it in _s.Items.Values) { var te = ValidateTarget(it.Type, it.Target); if (te != null) errs.Add($"{it.Id}: {te}"); }
        foreach (var w in _s.Workspaces)
        {
            if (w.Items.GroupBy(m => m.ItemId).Any(g => g.Count() > 1)) errs.Add($"{w.Id}: duplicate membership");
            foreach (var m in w.Items)
            {
                if (!_s.Items.TryGetValue(m.ItemId, out var it)) { errs.Add($"{w.Id}: dangling {m.ItemId}"); continue; }
                var le = ValidateLaunch(it.Type, m.Launch); if (le != null) errs.Add($"{w.Id}/{m.ItemId}: {le}");
            }
        }
        if (_s.Workspaces.GroupBy(w => w.Id).Any(g => g.Count() > 1)) errs.Add("duplicate workspace id");
        return errs;
    }
}
