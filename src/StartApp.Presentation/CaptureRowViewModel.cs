using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>One line of the capture list. Shows default details only: no actions menu, no editing (settled).</summary>
    public sealed class CaptureRow : ObservableBase
    {
        private readonly Action _changed; private bool _checked;
        public CaptureRow(CapturedItem item, bool isChecked, Action changed) { Item = item; _checked = isChecked; _changed = changed; }
        public CapturedItem Item { get; }
        public ItemType Type => Item.Type;
        public string Name => Item.Name;
        public string Target => Item.Target;
        public string Group => GroupOf(Item.Type);
        public string DetailsLabel => TypeName(Type) + (BrowserName is null ? "" : " · " + BrowserName);
        /// <summary>The captured browser's display name (Edge, Chrome, ...), null when no browser was identified. The same
        /// site open in two browsers stays one row per browser (owner decision Nov 14), and this name is how the user tells
        /// them apart before choosing.</summary>
        public string? BrowserName => CaptureRules.BrowserDisplayName(Item.Browser);
        /// <summary>Row suffix shown after the group: "  (Edge)" for a browser-captured site, empty otherwise.</summary>
        public string BrowserSuffix => BrowserName is null ? "" : $"  ({BrowserName})";
        public string Label => BrowserName is null ? "Select " + Item.Name : $"Select {Item.Name} ({BrowserName})";
        public bool IsChecked { get => _checked; set { if (_checked == value) return; _checked = value; Raise(nameof(IsChecked)); _changed(); } }
    }
}
