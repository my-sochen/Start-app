using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class NoColorEdgeTests
{
    static JsonStore Repo() => new(Path.Combine(Path.GetTempPath(), "none-edge-"+Guid.NewGuid().ToString("N"), "store.json"));
    [Fact] public void No_color_design_open_close_and_image_save_do_not_invent_color()
    {
        using var repo=Repo(); repo.Mutate(c=>c.CreateWorkspace("none"));
        var vm=new MainViewModel(repo,new NotImplementedLauncher());
        vm.DesignCardCommand.Execute(vm.Workspaces.Single());
        Assert.Equal("",vm.DesignColor); Assert.False(vm.DesignDirty); Assert.False(vm.SaveDesignCommand.CanExecute(null));
        vm.CloseDesignCommand.Execute(null); Assert.False(vm.UnsavedOpen); Assert.Null(repo.Load().Workspaces.Single().Color);
        vm.DesignCardCommand.Execute(vm.Workspaces.Single()); vm.DesignImagePath=@"C:\image.png"; vm.SaveDesignCommand.Execute(null);
        Assert.Null(repo.Load().Workspaces.Single().Color); Assert.Equal(@"C:\image.png",repo.Load().Workspaces.Single().ImagePath); Assert.Empty(repo.Load().AdvancedColorHistory);
        vm.DesignCardCommand.Execute(vm.Workspaces.Single()); vm.ApplyPaletteColorCommand.Execute("#8BAA9D"); vm.CloseDesignCommand.Execute(null); vm.ExitWithoutSavingCommand.Execute(null);
        Assert.Null(repo.Load().Workspaces.Single().Color);
    }
    [Theory] [InlineData(ViewSort.Alphabetical)] [InlineData(ViewSort.CreationDate)] [InlineData(ViewSort.ItemCount)] [InlineData(ViewSort.LaunchCount)] [InlineData(ViewSort.RecentEntries)]
    public void No_color_rows_survive_every_sort_and_text_category_filters(ViewSort sort)
    {
        var rows=new[]{new Workspace{Id="n",Name="needle",Category="Work",Color=null},new Workspace{Id="c",Name="colored",Category="Work",Color="#8BAA9D"}};
        var view=new WorkspaceViewSession{Sort=sort}; Assert.Equal(2,view.Query(rows,new Store()).Count());
        view.ContainsText="needle";view.Categories.Add("Work");Assert.Equal("n",view.Query(rows,new Store()).Single().Id);
        view.ContainsText="";view.Colors.Add("#8BAA9D");Assert.Equal("c",view.Query(rows,new Store()).Single().Id);
        view.Colors.Clear();Assert.Equal(2,view.Query(rows,new Store()).Count());Assert.Null(rows[0].Color);
    }
    [Fact] public void Store_roundtrip_preserves_none_and_existing_color_after_unrelated_mutation()
    {
        using var repo=Repo();repo.Mutate(c=>{c.CreateWorkspace("none");var old=c.CreateWorkspace("old");c.SetCardDesign(old.Id,"#7189BB",null);});
        var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.Reload();
        Assert.DoesNotContain("",vm.FilterColorOptions);Assert.DoesNotContain(vm.FilterColorOptions,x=>x==null);
        repo.Mutate(c=>c.CreateWorkspace("unrelated"));var loaded=repo.Load();
        Assert.Null(loaded.Workspaces.Single(w=>w.Name=="none").Color);Assert.Equal("#7189BB",loaded.Workspaces.Single(w=>w.Name=="old").Color);
        Assert.Null(new MainViewModel(repo,new NotImplementedLauncher()).Workspaces.Single(w=>w.Name=="none").Color);
    }
}
