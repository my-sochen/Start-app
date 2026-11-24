using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>עצב כרטיס from the card actions menu: opens the design popup (פופאפ עיצוב כרטיס) for that workspace.</summary>
    public ICommand DesignCardCommand { get; private set; } = null!;
    /// <summary>העלאת תמונה in the design popup: the view opens a file picker and sets DesignImagePath.</summary>
    public ICommand PickImageCommand { get; private set; } = null!;
    /// <summary>הסרת תמונה in the design popup: back to the color-as-background look (takes effect on save).</summary>
    public ICommand RemoveImageCommand { get; private set; } = null!;
    /// <summary>A preset swatch in the design popup: applies that color to the preview (saved only via Save).</summary>
    public ICommand ApplyPaletteColorCommand { get; private set; } = null!;
    public ICommand SaveDesignCommand { get; private set; } = null!;
    /// <summary>The design popup's X: closes silently when untouched, otherwise asks via the unsaved-exit dialog.</summary>
    public ICommand CloseDesignCommand { get; private set; } = null!;

    private void InitCardDesignEntryCommands()
    {
        DesignCardCommand = Cmd(p => BeginDesign((Workspace)p!), p => p is Workspace && !ReadOnly);
        PickImageCommand = Cmd(_ => PickImageRequested?.Invoke(), _ => DesignEditOpen && !ReadOnly);
    }

    private void InitCardDesignEditCommands()
    {
        RemoveImageCommand = Cmd(_ => DesignImagePath = "", _ => DesignEditOpen && DesignImagePath.Length > 0);
        ApplyPaletteColorCommand = Cmd(p => DesignColor = (string)p!, p => p is string && DesignEditOpen && !ReadOnly);
        SaveDesignCommand = Cmd(_ => SaveDesign(), _ => DesignEditOpen && DesignDirty && !ReadOnly);
        CloseDesignCommand = Cmd(_ => CloseDesign(), _ => DesignEditOpen);
    }

    // ---- card design popup (פופאפ עיצוב כרטיס, settled Nov 11): per-workspace color and background image, with a live
    // preview in the view, a save button on any change, and the shared unsaved-exit dialog on X ----
    private readonly CardDesignSession _cardDesign = new();
    public bool DesignEditOpen { get => _cardDesign.IsOpen; private set { if (_cardDesign.IsOpen != value) { _cardDesign.IsOpen = value; Raise(nameof(DesignEditOpen)); } RaiseCommands(); } }
    /// <summary>#RRGGBB, empty = the default look. The preview and the card show this value as the frame (with an image) or the background (without).</summary>
    public string DesignColor { get => _cardDesign.Color; set { var color = value ?? ""; if (_cardDesign.Color != color) { _cardDesign.Color = color; Raise(nameof(DesignColor)); } Raise(nameof(DesignDirty)); RaiseCommands(); } }
    /// <summary>Path of the uploaded background image; empty = none. Stored as a path reference (copying the file is an open storage decision).</summary>
    public string DesignImagePath { get => _cardDesign.ImagePath; set { var image = value ?? ""; if (_cardDesign.ImagePath != image) { _cardDesign.ImagePath = image; Raise(nameof(DesignImagePath)); } Raise(nameof(DesignDirty)); RaiseCommands(); } }
    /// <summary>Any change in the design popup shows its Save button (same rule as the other edit screens).</summary>
    public bool DesignDirty => _cardDesign.IsDirty;
    /// <summary>Provisional preset palette: the exact color set was never settled, so the popup offers these and a free #RRGGBB entry.</summary>
    public IReadOnlyList<string> DesignPalette { get; } = new[] { "#FFFFFF", "#FDE68A", "#BFDBFE", "#BBF7D0", "#FECACA", "#E9D5FF", "#E5E7EB" };
    private void BeginDesign(Workspace w)
    {
        var fresh = _current.Workspaces.FirstOrDefault(x => x.Id == w.Id) ?? w; // the caller may hold a pre-reload reference
        _cardDesign.CaptureOriginal(fresh);
        DesignColor = _cardDesign.OriginalColor; DesignImagePath = _cardDesign.OriginalImagePath;
        DesignEditOpen = true;
    }
    private bool SaveDesign()
    {
        var color = DesignColor.Trim(); var image = DesignImagePath.Trim();
        if (color.Length > 0 && Catalog.ValidateColor(color) is { } ce) { FieldError("design", "הצבע אינו תקין. הזינו צבע בצורה #RRGGBB.", "Not saved: " + ce); return false; }
        var ok = Apply(c => c.SetCardDesign(_cardDesign.WorkspaceId, color.Length == 0 ? null : color, image.Length == 0 ? null : image));
        if (ok) { Status = "Card design saved"; DesignEditOpen = false; }
        return ok;
    }
    private void CloseDesign()
    {
        if (DesignDirty) { MarkUnsavedHost(false, false, false, true); _unsavedExit.Continuation = () => DesignEditOpen = false; UnsavedOpen = true; return; }
        DesignEditOpen = false;
    }

}
