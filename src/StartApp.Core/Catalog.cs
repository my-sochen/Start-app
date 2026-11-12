namespace StartApp.Core;

/// <summary>In-memory mutations over a Store. Every method leaves the store consistent (no dangling refs, no orphans, no duplicate membership).</summary>
public sealed class Catalog
{
    private readonly Store _s;
    private readonly Func<string> _rand;
    /// <param name="randomPart">Test hook for deterministic id parts; default is random.</param>
    public Catalog(Store s, Func<string>? randomPart = null)
    {
        _s = s; _rand = randomPart ?? (() => Guid.NewGuid().ToString("N")[..8]);
        // Instances in older files have no id yet: assign one in memory. It persists only if a later save happens (the normal upgrade path).
        foreach (var w in _s.Workspaces)
            for (var i = 0; i < w.Items.Count; i++)
                if (w.Items[i].Id is null) w.Items[i] = w.Items[i] with { Id = NewId("mi_", id => _s.Workspaces.SelectMany(x => x.Items).Any(m => m.Id == id)) };
        // Schema-2 legacy: a shared description on the item. Move it onto the existing instances that have none, then clear it from the
        // item, so it can never leak into a NEW instance or reappear after an instance description is explicitly cleared.
        foreach (var w in _s.Workspaces)
            for (var i = 0; i < w.Items.Count; i++)
                if (w.Items[i].Description is null && _s.Items.TryGetValue(w.Items[i].ItemId, out var legacy) && legacy.Description is not null)
                    w.Items[i] = w.Items[i] with { Description = legacy.Description };
        foreach (var id in _s.Workspaces.SelectMany(w => w.Items).Select(m => m.ItemId).ToHashSet())
            if (_s.Items.TryGetValue(id, out var it) && it.Description is not null)
                _s.Items[id] = it with { Description = null };
    }
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

    public const int MaxDescriptionLength = 500;

    /// <summary>Description is optional. Null, empty or whitespace-only means none. A real description is stored exactly as typed (not trimmed). Limit: 500 UTF-16 code units (the same unit the text box counts; an emoji counts as 2). Control characters other than line breaks and tabs are rejected.</summary>
    public static string? ValidateDescription(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        if (d.Length > MaxDescriptionLength) return $"description is longer than {MaxDescriptionLength} characters";
        if (d.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t')) return "description has control characters";
        return null;
    }

    /// <summary>Instance/workspace names are stored exactly as typed (never trimmed). Null means "no local name" (fall back to the
    /// original); a whitespace-only name is rejected, not silently dropped.</summary>
    public static string? ValidateInstanceName(string? name)
    {
        if (name is null) return null;
        if (string.IsNullOrWhiteSpace(name)) return "name is empty";
        return null;
    }

    /// <summary>Reuses the catalog item with the same type and target. A given description never overwrites an existing non-empty one; it only fills an empty one.</summary>
    public Item GetOrAddItem(ItemType type, string name, string target, string? description = null)
    {
        var te = ValidateTarget(type, target); if (te != null) throw new ArgumentException(te);
        var de = ValidateDescription(description); if (de != null) throw new ArgumentException(de);
        if (string.IsNullOrWhiteSpace(description)) description = null;
        var key = Key(type, target);
        var existing = _s.Items.Values.FirstOrDefault(i => Key(i.Type, i.Target) == key);
        if (existing != null)
        {
            if (description != null && string.IsNullOrEmpty(existing.Description)) { existing = existing with { Description = description }; _s.Items[existing.Id] = existing; }
            return existing;
        }
        var item = new Item(NewId("it_", id => _s.Items.ContainsKey(id)), type, name, target, description);
        if (!_s.Items.TryAdd(item.Id, item)) throw new InvalidOperationException("item id collision");
        return item;
    }

    public Workspace CreateWorkspace(string name, string? description = null, string? category = null)
    {
        ValidateWorkspaceFields(name, description, category);
        var w = new Workspace { Id = NewId("ws_", id => _s.Workspaces.Any(w => w.Id == id)), Name = name, Description = string.IsNullOrWhiteSpace(description) ? null : description, Category = string.IsNullOrWhiteSpace(category) ? null : category, CreatedAt = DateTime.UtcNow };
        _s.Workspaces.Add(w);
        return w;
    }

    /// <summary>Direct-edit save from the details screen: replaces name, description and category with the edited values.
    /// Empty description/category mean none/default. Items are not touched.</summary>
    public Workspace UpdateWorkspace(string id, string name, string? description, string? category)
    {
        ValidateWorkspaceFields(name, description, category);
        var w = Ws(id);
        w.Name = name; w.Description = string.IsNullOrWhiteSpace(description) ? null : description; w.Category = string.IsNullOrWhiteSpace(category) ? null : category;
        return w;
    }

    /// <summary>Records a real open (שגר) of the workspace for the card's "last opened" display.</summary>
    public void TouchLastOpened(string workspaceId) => Ws(workspaceId).LastOpenedAt = DateTime.UtcNow;

    // ---- managed categories (פופאפ עריכת קטגוריה, settled Nov 11): built-ins are defined in code and can never be edited or
    // deleted; the user creates, renames and deletes custom ones. כללי is the default and the one guaranteed built-in; the exact
    // contents of the built-in base list beyond it are still an open product choice, so the list holds only what is settled. ----

    /// <summary>Built-in base categories. Only כללי is settled by the user's originals; more can be added here once decided.</summary>
    public static readonly IReadOnlyList<string> BuiltInCategories = new[] { Workspace.DefaultCategory };
    /// <summary>Exact-match rule for category identity (Hebrew names; case-insensitive duplicate check for Latin letters).</summary>
    private static bool SameCategory(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    public static bool IsBuiltInCategory(string name) => BuiltInCategories.Any(b => SameCategory(b, name));
    public bool IsCustomCategory(string name) => _s.CustomCategories.Any(c => SameCategory(c, name));
    /// <summary>Built-ins first, then the user's custom categories in creation order.</summary>
    public IReadOnlyList<string> AllCategories() => BuiltInCategories.Concat(_s.CustomCategories).ToList();
    /// <summary>Category names are stored exactly as typed (never trimmed). Whitespace-only is rejected, control characters are rejected, limit 100.</summary>
    public static string? ValidateCategoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "category name is empty";
        if (name.Length > 100) return "category name is longer than 100 characters";
        if (name.Any(char.IsControl)) return "category name has control characters";
        return null;
    }
    /// <summary>Adds a custom category. Built-ins and existing customs (compared case-insensitively) are rejected as duplicates.</summary>
    public string AddCustomCategory(string name)
    {
        var e = ValidateCategoryName(name); if (e != null) throw new ArgumentException(e);
        if (AllCategories().Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("category already exists");
        _s.CustomCategories.Add(name);
        return name;
    }
    /// <summary>Renames a custom category; built-ins cannot be renamed. Every workspace assigned to it shows the new name.</summary>
    public void RenameCustomCategory(string oldName, string newName)
    {
        if (!IsCustomCategory(oldName)) throw new ArgumentException("only a custom category can be renamed");
        var e = ValidateCategoryName(newName); if (e != null) throw new ArgumentException(e);
        if (!SameCategory(oldName, newName) && AllCategories().Any(c => string.Equals(c, newName, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("category already exists");
        var i = _s.CustomCategories.FindIndex(c => SameCategory(c, oldName));
        _s.CustomCategories[i] = newName;
        foreach (var w in _s.Workspaces) if (w.Category is not null && SameCategory(w.Category, oldName)) w.Category = newName;
    }
    /// <summary>Workspaces currently assigned to this category (for the deletion warning). Comparison is exact.</summary>
    public IReadOnlyList<Workspace> WorkspacesInCategory(string name) =>
        _s.Workspaces.Where(w => SameCategory(w.EffectiveCategory, name)).ToList();
    /// <summary>Deletes a custom category; built-ins cannot be deleted. Workspaces assigned to it are NOT deleted: they move
    /// to the default כללי (settled Nov 11; the confirmation dialog is a UI concern, Catalog just performs the reassignment).</summary>
    public void DeleteCustomCategory(string name)
    {
        if (!IsCustomCategory(name)) throw new ArgumentException("only a custom category can be deleted");
        _s.CustomCategories.RemoveAll(c => SameCategory(c, name));
        foreach (var w in _s.Workspaces) if (w.Category is not null && SameCategory(w.Category, name)) w.Category = null;
    }
    /// <summary>Assigns a category from the managed list (ערוך קטגוריה / create form). The default is stored as null.</summary>
    public void SetCategory(string workspaceId, string? category)
    {
        var w = Ws(workspaceId);
        if (string.IsNullOrWhiteSpace(category)) { w.Category = null; return; }
        if (!AllCategories().Any(c => SameCategory(c, category))) throw new ArgumentException("unknown category");
        w.Category = SameCategory(category, Workspace.DefaultCategory) ? null : category;
    }

    // ---- card design (פופאפ עיצוב כרטיס, settled Nov 11): per-workspace color and background image. ----

    private static readonly System.Text.RegularExpressions.Regex ColorHex = new(@"^#[0-9A-Fa-f]{6}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Card color as #RRGGBB (the form the preview and the card render). Null = default look.</summary>
    public static string? ValidateColor(string? color)
    {
        if (color is null) return null;
        if (!ColorHex.IsMatch(color)) return "color must be #RRGGBB";
        return null;
    }
    /// <summary>Saves the card design of one workspace (color + background image path; null clears each). Other workspaces are
    /// untouched. The image is stored as a path reference; the file itself is not copied or verified (platform/storage decision).</summary>
    public void SetCardDesign(string workspaceId, string? color, string? imagePath)
    {
        var w = Ws(workspaceId);
        var ce = ValidateColor(color); if (ce != null) throw new ArgumentException(ce);
        if (imagePath is not null && (string.IsNullOrWhiteSpace(imagePath) || imagePath.Any(char.IsControl))) throw new ArgumentException("image path is empty");
        w.Color = color; w.ImagePath = imagePath;
    }

    private static void ValidateWorkspaceFields(string name, string? description, string? category)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("workspace name is required");
        var de = ValidateDescription(description); if (de != null) throw new ArgumentException(de);
    }

    private Workspace Ws(string id) => _s.Workspaces.FirstOrDefault(w => w.Id == id) ?? throw new KeyNotFoundException(id);

    /// <summary>Adds a new instance of the item. The same item may appear more than once in a workspace: each call creates an
    /// independent instance with its own id, name, description and launch config (the duplicate warning and confirmation are a UI concern).
    /// An existing instance is never modified by adding another one.</summary>
    public Membership AddToWorkspace(string workspaceId, string itemId, LaunchConfig? launch = null, string? name = null, string? description = null)
    {
        var w = Ws(workspaceId);
        if (!_s.Items.TryGetValue(itemId, out var item)) throw new KeyNotFoundException(itemId);
        var le = ValidateLaunch(item.Type, launch); if (le != null) throw new ArgumentException(le);
        var ne = ValidateInstanceName(name); if (ne != null) throw new ArgumentException(ne);
        var de = ValidateDescription(description); if (de != null) throw new ArgumentException(de);
        if (string.IsNullOrWhiteSpace(description)) description = null;
        var m = new Membership(itemId, launch, NewId("mi_", id => _s.Workspaces.SelectMany(x => x.Items).Any(x => x.Id == id)), name, description);
        w.Items.Add(m);
        return m;
    }

    /// <summary>True when the workspace already holds at least one instance whose catalog item has this exact type and target.</summary>
    public bool HasTarget(string workspaceId, ItemType type, string target)
    {
        var key = Key(type, target);
        return Ws(workspaceId).Items.Any(m => _s.Items.TryGetValue(m.ItemId, out var it) && Key(it.Type, it.Target) == key);
    }

    /// <summary>Removes one instance. Other instances of the same item, in this or other workspaces, are not affected.</summary>
    public void RemoveInstance(string workspaceId, string membershipId)
    {
        if (Ws(workspaceId).Items.RemoveAll(m => m.Id == membershipId) == 0) throw new KeyNotFoundException(membershipId);
        RemoveOrphans();
    }

    /// <summary>Edits one instance's per-workspace name and description (טופס עריכת אייטם). Only this instance changes:
    /// other instances of the same item, here or in other workspaces, and the shared catalog record keep their own values.
    /// Null name falls back to the catalog item's original name; null or blank description means no description.</summary>
    public void UpdateInstance(string workspaceId, string membershipId, string? name, string? description)
    {
        var w = Ws(workspaceId);
        var i = w.Items.FindIndex(m => m.Id == membershipId);
        if (i < 0) throw new KeyNotFoundException(membershipId);
        var ne = ValidateInstanceName(name); if (ne != null) throw new ArgumentException(ne);
        var de = ValidateDescription(description); if (de != null) throw new ArgumentException(de);
        if (string.IsNullOrWhiteSpace(description)) description = null;
        w.Items[i] = w.Items[i] with { Name = name, Description = description };
    }

    /// <summary>Removes every instance of the item in this workspace. UI removal targets one instance via <see cref="RemoveInstance"/>.</summary>
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

    /// <summary>Local edit: only this one instance changes; every other instance of the same target keeps its
    /// own name, description and launch config. Reuses a matching catalog item or creates one; the old item is removed if orphaned.
    /// This is how an address change (טופס עריכת אייטם or פופאפ עריכת כתובת) stays local to the current workspace for EVERY type, sites included.</summary>
    public Item ReplaceTargetLocal(string workspaceId, string membershipId, string newTarget, string? newName = null)
    {
        var w = Ws(workspaceId);
        var idx = w.Items.FindIndex(m => m.Id == membershipId);
        if (idx < 0) throw new KeyNotFoundException(membershipId);
        var old = _s.Items[w.Items[idx].ItemId];
        var item = GetOrAddItem(old.Type, newName ?? old.Name, newTarget, old.Description);
        w.Items[idx] = w.Items[idx] with { ItemId = item.Id };
        RemoveOrphans();
        return item;
    }

    /// <summary>Saves this instance's launch choice (פתח באמצעות → הגדר כברירת מחדל). Per instance: two instances of the same address,
    /// in this workspace or another, keep their own choices. Null clears back to the Windows default. The choice is only ever used inside
    /// Start-app; Windows file associations are never touched. Validation matches <see cref="ValidateLaunch"/>.</summary>
    public void SetLaunch(string workspaceId, string membershipId, LaunchConfig? launch)
    {
        var w = Ws(workspaceId);
        var i = w.Items.FindIndex(m => m.Id == membershipId);
        if (i < 0) throw new KeyNotFoundException(membershipId);
        var err = ValidateLaunch(_s.Items[w.Items[i].ItemId].Type, launch);
        if (err != null) throw new ArgumentException(err, nameof(launch));
        w.Items[i] = w.Items[i] with { Launch = launch };
    }

    /// <summary>The display grouping the user settled on (Nov 12, superseding the Nov 11 combined files+programs group): Sites, Folders, Files, Programs.</summary>
    public static int DisplayGroup(ItemType t) => t switch { ItemType.Link => 0, ItemType.Folder => 1, ItemType.File => 2, _ => 3 };

    /// <summary>הזז למעלה / הזז למטה: moves one instance within its display group, swapping with the nearest group neighbor.
    /// At the group's edge there is nothing to swap with and the call is a no-op. The new order is persisted in the workspace's item list.</summary>
    public void MoveInstance(string workspaceId, string membershipId, int delta)
    {
        if (delta == 0) return;
        var w = Ws(workspaceId);
        var i = w.Items.FindIndex(m => m.Id == membershipId);
        if (i < 0) throw new KeyNotFoundException(membershipId);
        var g = DisplayGroup(_s.Items[w.Items[i].ItemId].Type);
        var step = Math.Sign(delta); var j = i + step;
        while (j >= 0 && j < w.Items.Count && DisplayGroup(_s.Items[w.Items[j].ItemId].Type) != g) j += step;
        if (j < 0 || j >= w.Items.Count) return; // edge of the group
        (w.Items[i], w.Items[j]) = (w.Items[j], w.Items[i]);
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
        foreach (var it in _s.Items.Values) { var te = ValidateTarget(it.Type, it.Target); if (te != null) errs.Add($"{it.Id}: {te}"); var de = ValidateDescription(it.Description); if (de != null) errs.Add($"{it.Id}: {de}"); }
        foreach (var w in _s.Workspaces)
        {
            if (w.Category is not null && string.IsNullOrWhiteSpace(w.Category)) errs.Add($"{w.Id}: category is empty");
            if (ValidateColor(w.Color) is { } ce) errs.Add($"{w.Id}: {ce}");
            if (w.ImagePath is not null && (string.IsNullOrWhiteSpace(w.ImagePath) || w.ImagePath.Any(char.IsControl))) errs.Add($"{w.Id}: image path is empty");
            if (string.IsNullOrWhiteSpace(w.Name)) errs.Add($"{w.Id}: workspace name is required");
            if (ValidateDescription(w.Description) is { } wde) errs.Add($"{w.Id}: {wde}");
            if (w.Items.Any(m => m.Id is null)) errs.Add($"{w.Id}: instance without an id");
            if (w.Items.Where(m => m.Id is not null).GroupBy(m => m.Id).Any(g => g.Count() > 1)) errs.Add($"{w.Id}: duplicate instance id");
            foreach (var m in w.Items)
            {
                if (ValidateInstanceName(m.Name) is { } ne) errs.Add($"{w.Id}/{m.Id}: {ne}");
                if (ValidateDescription(m.Description) is { } de) errs.Add($"{w.Id}/{m.Id}: {de}");
                if (!_s.Items.TryGetValue(m.ItemId, out var it)) { errs.Add($"{w.Id}: dangling {m.ItemId}"); continue; }
                var le = ValidateLaunch(it.Type, m.Launch); if (le != null) errs.Add($"{w.Id}/{m.ItemId}: {le}");
            }
        }
        if (_s.Workspaces.GroupBy(w => w.Id).Any(g => g.Count() > 1)) errs.Add("duplicate workspace id");
        if (_s.CustomCategories.Any(c => ValidateCategoryName(c) != null)) errs.Add("invalid custom category name");
        if (_s.CustomCategories.Any(IsBuiltInCategory)) errs.Add("a custom category shadows a built-in");
        if (_s.CustomCategories.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) errs.Add("duplicate custom category");
        return errs;
    }
}
