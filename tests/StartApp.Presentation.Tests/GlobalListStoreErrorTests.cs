using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class GlobalListStoreErrorTests
{
    public static IEnumerable<object[]> Cases() => from newer in new[]{false,true} from kind in Enum.GetValues<GlobalListKind>() from action in new[]{"add","remove","move"} where action!="move"||kind!=GlobalListKind.GlobalItems select new object[]{newer,kind,action};
    [Theory,MemberData(nameof(Cases))]
    public void Every_global_list_save_refuses_changed_store_preserves_ui_and_copyable_diagnostics(bool newer,GlobalListKind kind,string action)
    {
        var path=Path.Combine(Path.GetTempPath(),"list-store-"+Guid.NewGuid(),"store.json");
        using var repo=new JsonStore(path);var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.OpenGlobalEnvCommand.Execute(null);
        var sourceKind=action=="move"?(kind==GlobalListKind.Whitelist?GlobalListKind.Blacklist:GlobalListKind.Whitelist):kind;
        vm.StartGlobalAddCommand.Execute(sourceKind);vm.NewEntryTypeIndex=0;vm.NewEntryName="test";vm.NewEntryTarget="https://example.com/";vm.ConfirmGlobalAddCommand.Execute(null);
        var rows=kind==GlobalListKind.GlobalItems?vm.GlobalItemRows:kind==GlobalListKind.Whitelist?vm.WhitelistRows:vm.BlacklistRows;
        MainViewModel.GlobalEntryRow? row=null;
        if(action=="remove")row=rows.Single(r=>r.Target=="https://example.com/");
        else {vm.StartGlobalAddCommand.Execute(kind);vm.NewEntryTypeIndex=0;vm.NewEntryName="draft";vm.NewEntryTarget=action=="move"?"https://example.com/":"https://other.example/";if(action=="move"){vm.ConfirmGlobalAddCommand.Execute(null);Assert.True(vm.GlobalConflictOpen);}}
        var count=rows.Count;var bytes=newer?"{\"schemaVersion\":999}":"invalid json";File.WriteAllText(path,bytes);
        var command=action=="add"?vm.ConfirmGlobalAddCommand:action=="remove"?vm.RemoveGlobalEntryCommand:vm.MoveGlobalConflictCommand;
        Assert.True(command.CanExecute(row));command.Execute(row);
        Assert.True(vm.ReadOnly);Assert.True(vm.ToastVisible);Assert.True(vm.ToastPersistent);Assert.True(vm.GlobalEnvErrorVisible);
        Assert.Equal(count,rows.Count);Assert.Equal(bytes,File.ReadAllText(path));Assert.False(command.CanExecute(row));
        if(action!="remove"){Assert.Equal(kind,vm.AddingKind);Assert.Equal("draft",vm.NewEntryName);}
        if(action=="move")Assert.True(vm.GlobalConflictOpen);
        string? copied=null;vm.CopyAddressRequested+=s=>copied=s;vm.CopyTechnicalDetailsCommand.Execute(vm.ToastTechnicalDetails);Assert.Equal(vm.ToastTechnicalDetails,copied);
        if(!newer){var diagnostic=Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!,"store.json.diag-*"));Assert.Contains(diagnostic,copied);Assert.Equal(bytes,File.ReadAllText(diagnostic));}
        command.Execute(row);Assert.Equal(bytes,File.ReadAllText(path));
    }
}
