using System.Collections.ObjectModel;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    // ---- card design popup (פופאפ עיצוב כרטיס, settled Nov 11): per-workspace color and background image, with a live
    // preview in the view, a save button on any change, and the shared unsaved-exit dialog on X ----
    public bool DesignEditOpen { get => _designEditOpen; private set { Set(ref _designEditOpen, value); RaiseCommands(); } }
    private bool _designEditOpen;
    private string _designWsId = "";
    /// <summary>#RRGGBB, empty = the default look. The preview and the card show this value as the frame (with an image) or the background (without).</summary>
    public string DesignColor { get => _designColor; set { Set(ref _designColor, value ?? ""); Raise(nameof(DesignDirty)); RaiseCommands(); } }
    private string _designColor = "";
    /// <summary>Path of the uploaded background image; empty = none. Stored as a path reference (copying the file is an open storage decision).</summary>
    public string DesignImagePath { get => _designImagePath; set { Set(ref _designImagePath, value ?? ""); Raise(nameof(DesignDirty)); RaiseCommands(); } }
    private string _designImagePath = "";
    private string _origColor = "", _origImage = "";
    /// <summary>Any change in the design popup shows its Save button (same rule as the other edit screens).</summary>
    public bool DesignDirty => DesignEditOpen && (DesignColor != _origColor || DesignImagePath != _origImage);
    /// <summary>Provisional preset palette: the exact color set was never settled, so the popup offers these and a free #RRGGBB entry.</summary>
    public IReadOnlyList<string> DesignPalette { get; } = new[] { "#FFFFFF", "#FDE68A", "#BFDBFE", "#BBF7D0", "#FECACA", "#E9D5FF", "#E5E7EB" };
    private void BeginDesign(Workspace w)
    {
        var fresh = _current.Workspaces.FirstOrDefault(x => x.Id == w.Id) ?? w; // the caller may hold a pre-reload reference
        _designWsId = fresh.Id; _origColor = fresh.Color ?? ""; _origImage = fresh.ImagePath ?? "";
        DesignColor = _origColor; DesignImagePath = _origImage;
        DesignEditOpen = true;
    }
    private bool SaveDesign()
    {
        var color = DesignColor.Trim(); var image = DesignImagePath.Trim();
        if (color.Length > 0 && Catalog.ValidateColor(color) is { } ce) { FieldError("design", "הצבע אינו תקין. הזינו צבע בצורה #RRGGBB.", "Not saved: " + ce); return false; }
        var ok = Apply(c => c.SetCardDesign(_designWsId, color.Length == 0 ? null : color, image.Length == 0 ? null : image));
        if (ok) { Status = "Card design saved"; DesignEditOpen = false; }
        return ok;
    }
    private void CloseDesign()
    {
        if (DesignDirty) { MarkUnsavedHost(false, false, false, true); _exitAction = () => DesignEditOpen = false; UnsavedOpen = true; return; }
        DesignEditOpen = false;
    }

}
