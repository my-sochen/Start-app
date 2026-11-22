namespace StartApp.Presentation;

/// <summary>Sort keys of the תצוגה menu (approved 22.11 handoff). מיון מסדר, לא מסתיר. RecentEntries (כניסות אחרונות) is the default.</summary>
public enum ViewSort { RecentEntries, CreationDate, ItemCount, LaunchCount, Alphabetical }

/// <summary>גודל כרטיסים: קטן / בינוני (ברירת מחדל) / גדול.</summary>
public enum CardSizeOption { Small, Medium, Large }

/// <summary>תבנית תצוגה: כרטיסים (ברירת מחדל) / רשימה / אייקונים (שם וצבע בלבד).</summary>
public enum ViewTemplate { Cards, List, Icons }

/// <summary>One active filter shown as a chip on the filter bar (סינון משולב, approved 22.11 02:40:20).
/// Key identifies the chip for individual removal: "color:#RRGGBB", "cat:&lt;name&gt;", "item", "contains".
/// ColorHex carries the swatch for a color chip; null otherwise.</summary>
public sealed record FilterChip(string Key, string Label, string? ColorHex = null);
