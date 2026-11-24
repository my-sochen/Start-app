using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>An item moved to the workspace side but not saved yet. Editable here (name, description, address, open-with); the capture side is never changed by these edits.</summary>
    public sealed class StagedRow : ObservableBase
    {
        private string _name, _description = "", _target, _openWith = "";
        private bool _isEditing;
        public bool IsEditing { get => _isEditing; internal set => Set(ref _isEditing, value); }
        /// <summary>A staged site starts with the browser it was captured in already filled as its default (spec Nov 11);
        /// the staged פתח באמצעות popup can still change or clear it before שמור writes anything.</summary>
        public StagedRow(CapturedItem i) { Type = i.Type; OriginalName = i.Name; _name = i.Name; _target = i.Target; _openWith = i.Type == ItemType.Link && !string.IsNullOrEmpty(i.Browser) ? i.Browser! : ""; }
        public ItemType Type { get; }
        public string OriginalName { get; }
        public string TypeLabel => TypeName(Type);
        public string Group => GroupOf(Type);
        public string Label => "Added: " + OriginalName;
        public string Name { get => _name; set => Set(ref _name, value ?? ""); }
        public string Description { get => _description; set => Set(ref _description, value ?? ""); }
        public string Target { get => _target; set { Set(ref _target, value ?? ""); Raise(nameof(TargetError)); } }
        public string TargetError => Catalog.ValidateInputTarget(Type, Target) ?? "";
        public string OpenWith { get => _openWith; set => Set(ref _openWith, value ?? ""); }
        /// <summary>פתח באמצעות does not apply to a program item (it is the program) - the menu entry hides.</summary>
        public bool IsProgram => Type == ItemType.Program;
        public string OpenWithDefaultLabel => Type == ItemType.Folder ? "ללא" : "ברירת המחדל של Windows";
        public bool OpenWithApplicable => !IsProgram;
        public string TargetLabel => Type == ItemType.Link ? "כתובת האתר" : "נתיב";
    }
}
