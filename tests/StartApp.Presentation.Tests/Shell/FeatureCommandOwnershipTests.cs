using System.Reflection;
using System.Windows.Input;
using StartApp.Core;
using StartApp.Presentation;

public class FeatureCommandOwnershipTests
{
    private sealed class EmptyRepository : IStoreRepository
    {
        public Store Load() => new();
        public Store Mutate(Action<Catalog> change) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public void Feature_initializers_preserve_every_registered_command_and_the_dev_notification_order()
    {
        var vm = new MainViewModel(new EmptyRepository(), new NotImplementedLauncher());
        var expected = new[]
        {
            "CaptureNewCommand",
            "CaptureExistingCommand",
            "OpenSettingsCommand",
            "CloseSettingsCommand",
            "AddFromOpenCommand",
            "CapturePickWorkspaceCommand",
            "CaptureAddCommand",
            "CaptureDropCommand",
            "CaptureSaveCommand",
            "CaptureBackCommand",
            "CaptureCloseCommand",
            "CaptureCreateCommand",
            "EditStagedCommand",
            "CloseStagedEditCommand",
            "RemoveStagedCommand",
            "StagedCopyAddressCommand",
            "StagedMoveUpCommand",
            "StagedMoveDownCommand",
            "StagedEditAddressCommand",
            "StagedOpenWithCommand",
            "ConfirmCaptureDupSingleCommand",
            "CancelCaptureDupSingleCommand",
            "ConfirmCaptureDupGroupCommand",
            "CancelCaptureDupGroupCommand",
            "CloseGlobalConflictCommand",
            "MoveGlobalConflictCommand",
            "OpenGlobalEnvCommand",
            "CloseGlobalEnvCommand",
            "StartGlobalAddCommand",
            "ConfirmGlobalAddCommand",
            "CancelGlobalAddCommand",
            "RemoveGlobalEntryCommand",
            "OpenAllItemsCommand",
            "CloseAllItemsCommand",
            "OpenAllItemCommand",
            "ConfirmSelectionCommand",
            "BackToOpeningSelectionCommand",
            "ShowLastOpeningResultsCommand",
            "CloseLastOpeningResultsCommand",
            "DismissToastCommand",
            "ToastDetailsCommand",
            "CopyTechnicalDetailsCommand",
            "CreateWorkspaceCommand",
            "OpenDetailsCommand",
            "OpenWorkspaceForCommand",
            "AddItemToCommand",
            "BackCommand",
            "SaveDetailsCommand",
            "SaveAndExitCommand",
            "ExitWithoutSavingCommand",
            "CloseUnsavedCommand",
            "FocusAddCommand",
            "OpenItemCommand",
            "CopyAddressCommand",
            "EditItemCommand",
            "SaveItemEditCommand",
            "CancelItemEditCommand",
            "EditAddressCommand",
            "SaveAddressCommand",
            "CopyFullAddressCommand",
            "CloseAddressEditCommand",
            "OpenWithCommand",
            "OpenWithGoCommand",
            "CloseOpenWithCommand",
            "MoveItemUpCommand",
            "MoveItemDownCommand",
            "DesignCardCommand",
            "PickImageCommand",
            "PickCreateImageCommand",
            "RemoveCreateImageCommand",
            "ApplyCreatePaletteColorCommand",
            "RemoveImageCommand",
            "ApplyPaletteColorCommand",
            "SaveDesignCommand",
            "CloseDesignCommand",
            "EditCategoryPopupCommand",
            "OpenPickerCommand",
            "PickTypeCommand",
            "PickResourceCommand",
            "ClosePickerCommand",
            "PickCategoryCommand",
            "AddCategoryCommand",
            "BeginRenameCategoryCommand",
            "SaveRenameCategoryCommand",
            "CancelRenameCategoryCommand",
            "DeleteCategoryCommand",
            "CloseCategoryPopupCommand",
            "ShowCategoryDeleteListCommand",
            "ConfirmCategoryDeleteCommand",
            "CancelCategoryDeleteCommand",
            "RemoveItemCommand",
            "AddItemCommand",
            "ConfirmDuplicateCommand",
            "CancelDuplicateCommand",
            "CloseEmptyWorkspaceCommand",
            "GoToDetailsCommand",
            "OpenWorkspaceCommand",
            "ConfirmOpenAnywayCommand",
            "CancelOpenCommand"
        };
        var lazyNames = new[] { "CancelDefaultReplaceCommand", "ClearFiltersCommand", "ConfirmDefaultReplaceCommand", "RemoveFilterChipCommand", "ToggleDefaultForCommand" };
        var properties = typeof(MainViewModel).GetProperties()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .ToDictionary(p => p.Name, p => (ICommand)p.GetValue(vm)!);
        Assert.All(properties.Values, command => Assert.NotNull(command));
        Assert.Equal(expected.Concat(lazyNames).OrderBy(x => x), properties.Keys.OrderBy(x => x));
        var calls = new List<string>();
        foreach (var (name, command) in properties)
            command.CanExecuteChanged += (_, _) => calls.Add(name);
        var registry = (CommandRegistry)typeof(MainViewModel)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        registry.RaiseAll();
        Assert.Equal(expected.Concat(lazyNames), calls);
        calls.Clear();
        registry.RaiseAll();
        Assert.Equal(expected.Concat(lazyNames), calls);
    }
}
