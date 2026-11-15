using System.Text.Json;
using System.Text.Json.Serialization;

using StartApp.Core;

namespace StartApp.Storage.Json;



/// <summary>Whole-state JSON file. Serialized writes within one process; temp file + replace. NOT a durability guarantee against power loss, and no cross-process coordination yet (open question: multiple instances).</summary>
public sealed class JsonStore : IStoreRepository
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly string _path;
    private readonly object _gate = new();
    /// <summary>Newest schema this build reads and writes. Schema 2 = schema 1 plus the optional item description; schema 3 = schema 2 plus
    /// per-instance membership fields (id, local name, local description) and the optional workspace category; schema 4 = schema 3 plus
    /// custom categories and the per-workspace card design (color, image path). Older builds refuse a newer
    /// file as "too new" and leave it untouched; this build still reads schemas 1-3 and upgrades them on the next save, after keeping a one-time copy.</summary>
    public const int SupportedSchema = 4;
    private const int OldestReadableSchema = 1;
    /// <summary>Moves the invalid file aside, then the next load starts empty. Code only: nothing in the app calls it, and a real reset needs the user's own explicit approval. Needs the writer lock like any other write.</summary>
    public string? DiscardInvalidFile()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            EnsureWriter(); // take the single-writer lock first: another running instance holding it gets StoreLockedException and the file is not moved
            if (!File.Exists(_path)) return null;
            var q = _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..6];
            File.Move(_path, q);
            return q;
        }
    }
    private FileStream? _lock;
    public JsonStore(string path) { _path = path; }

    /// <summary>Single writer across processes: exclusive lock file held until Dispose. Second holder gets StoreLockedException. Reads stay allowed.</summary>
    private void EnsureWriter()
    {
        if (_lock != null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        try { _lock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) when (e is not (DirectoryNotFoundException or PathTooLongException or FileNotFoundException)) { throw new StoreLockedException(e); }
    }
    public void Dispose() { lock (_gate) { _lock?.Dispose(); _lock = null; } }

    public Store Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new Store();
            try
            {
                var text = File.ReadAllText(_path);
                // Version gate first, on the raw document, so a newer or oddly-cased file is never read as schema 1.
                using (var doc = JsonDocument.Parse(text))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("schemaVersion", out var v) || !v.TryGetInt32(out var ver))
                        throw new JsonException("missing or invalid schemaVersion");
                    if (ver > SupportedSchema) throw new StoreTooNewException(ver, SupportedSchema);
                    if (ver < OldestReadableSchema) throw new JsonException($"unsupported schemaVersion {ver}");
                }
                RejectDuplicateKeys(text);
                var s = JsonSerializer.Deserialize<Store>(text, Opts) ?? throw new JsonException("null");
                CheckShape(s);
                var errs = new Catalog(s).Validate();
                if (errs.Count > 0) throw new JsonException(string.Join("; ", errs));
                return s;
            }
            catch (JsonException ex)
            {
                string? copy = null;
                try
                {
                    // One diagnostic copy per distinct corrupt content: the name is a content hash, so repeated failed loads of the same bytes reuse it.
                    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_path)))[..16];
                    copy = _path + ".diag-" + hash;
                    if (!File.Exists(copy)) File.Copy(_path, copy);
                }
                catch { copy = null; }
                throw new StoreCorruptException(ex.Message, copy);
            }
        }
    }

    // Private on purpose: the only write path is Mutate, which always goes through the fail-closed Load first.
    private void Save(Store s)
    {
        lock (_gate)
        {
            var errs = new Catalog(s).Validate();
            if (errs.Count > 0) throw new InvalidOperationException("Refusing to save inconsistent store: " + string.Join("; ", errs));
            EnsureWriter();
            s.SchemaVersion = SupportedSchema;
            KeepPreUpgradeCopy();
            var tmp = _path + ".tmp"; // same directory as target
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs)) { w.Write(JsonSerializer.Serialize(s, Opts)); w.Flush(); fs.Flush(true); }
            File.Move(tmp, _path, overwrite: true);
        }
    }

    /// <summary>
    /// Before a file of an older schema is replaced by a newer-schema file, make sure a byte-exact copy of it exists next to it
    /// (store.json.schema1.bak / store.json.schema2.bak / ...). A current-schema file needs no copy.
    /// The copy is written to a temp name, flushed, then published with a no-overwrite move, so a final backup name only ever holds a complete copy
    /// (an interrupted copy leaves just a *.tmp that the next attempt rewrites). An existing backup is trusted only if its bytes equal the current file;
    /// otherwise it is left alone and the copy goes to store.json.schemaN.&lt;hash&gt;.bak. Nothing is overwritten or deleted. Any failure throws, so the upgrade does not happen.
    /// Not claimed: power-loss durability beyond the flush of the temp file.
    /// </summary>
    private void KeepPreUpgradeCopy()
    {
        if (!File.Exists(_path)) return;
        var bytes = File.ReadAllBytes(_path);
        var body = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsMemory(3) : bytes.AsMemory(); // JsonDocument rejects a BOM; the backup still copies the original bytes
        int ver;
        using (var doc = JsonDocument.Parse(body))
        {
            if (!(doc.RootElement.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out ver))) return;
            if (ver >= SupportedSchema) return;
        }
        var bak = _path + ".schema" + ver + ".bak";
        if (File.Exists(bak))
        {
            if (File.ReadAllBytes(bak).AsSpan().SequenceEqual(bytes)) return;
            bak = _path + ".schema" + ver + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16] + ".bak";
            if (File.Exists(bak))
            {
                if (File.ReadAllBytes(bak).AsSpan().SequenceEqual(bytes)) return;
                throw new IOException("A different file already uses the backup name " + bak + "; not upgrading.");
            }
        }
        var tmp = bak + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
            File.Move(tmp, bak); // no overwrite: throws if the name appeared meanwhile
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    /// <summary>Isolated mutation: change a deep copy, validate, persist; only a successful save is reported. Nothing partial leaks to the caller.</summary>
    public Store Mutate(Action<Catalog> change)
    {
        lock (_gate)
        {
            EnsureWriter();
            var loaded = Load();
            var copy = JsonSerializer.Deserialize<Store>(JsonSerializer.Serialize(loaded, Opts), Opts)!;
            change(new Catalog(copy));
            Save(copy);
            return copy;
        }
    }

    /// <summary>Syntactically valid JSON can still carry nulls where the model requires values; treat that as corrupt data.</summary>
    private static void CheckShape(Store s)
    {
        if (s.Items is null || s.Workspaces is null || s.CustomCategories is null) throw new JsonException("items, workspaces and custom categories are required");
        foreach (var c in s.CustomCategories) if (c is null) throw new JsonException("custom category is null");
        foreach (var (key, it) in s.Items)
        {
            if (it is null || it.Id is null || it.Name is null || it.Target is null) throw new JsonException($"item '{key}' has missing fields");
            if (it.Id != key) throw new JsonException($"item id '{it.Id}' does not match its key '{key}'");
        }
        foreach (var w in s.Workspaces)
        {
            if (w is null || w.Id is null || w.Name is null || w.Items is null) throw new JsonException("workspace has missing fields");
            foreach (var m in w.Items) if (m is null || m.ItemId is null) throw new JsonException("workspace member has missing fields");
        }
    }

    /// <summary>JSON allows repeated keys (last wins in the serializer); we refuse them so no data is silently dropped.</summary>
    private static void RejectDuplicateKeys(string text)
    {
        var r = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(text));
        var stack = new Stack<HashSet<string>?>();
        while (r.Read())
        {
            switch (r.TokenType)
            {
                case JsonTokenType.StartObject: stack.Push(new HashSet<string>(StringComparer.Ordinal)); break;
                case JsonTokenType.StartArray: stack.Push(null); break;
                case JsonTokenType.EndObject: case JsonTokenType.EndArray: stack.Pop(); break;
                case JsonTokenType.PropertyName:
                    if (!stack.Peek()!.Add(r.GetString()!)) throw new JsonException($"duplicate key '{r.GetString()}'");
                    break;
            }
        }
    }
}
