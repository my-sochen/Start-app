using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;
public class CardAddTests
{
    static JsonStore Repo()=>new(Path.Combine(Path.GetTempPath(),"card-"+Guid.NewGuid(),"store.json"));
    [Fact] public void Add_targets_clicked_card_and_preserves_owner_draft_and_navigation()
    {
        using var repo=Repo();repo.Mutate(c=>{c.CreateWorkspace("first");c.CreateWorkspace("second");});
        var owner=new MainViewModel(repo,new NotImplementedLauncher());owner.NewTarget="C:\\owner.txt";
        var target=owner.Workspaces.Last();var selected=owner.Selected!.Id;
        owner.AddItemToCommand.Execute(target);var child=owner.CardAddSession!;
        child.NewTarget="https://example.com/";child.NewItemName="local";child.NewDescription="desc";child.AddItemCommand.Execute(null);
        Assert.True(owner.InOverview);Assert.Equal(selected,owner.Selected!.Id);Assert.Equal("C:\\owner.txt",owner.NewTarget);
        Assert.Single(repo.Load().Workspaces.Single(w=>w.Id==target.Id).Items);
        Assert.Empty(repo.Load().Workspaces.Single(w=>w.Id!=target.Id).Items);
        owner.FinishCardAdd();Assert.Null(owner.CardAddSession);Assert.True(owner.InOverview);
        Assert.Equal("C:\\owner.txt",owner.NewTarget);
    }
    [Fact] public void Cancel_and_invalid_input_do_not_persist_and_duplicate_requires_confirmation()
    {
        using var repo=Repo();repo.Mutate(c=>c.CreateWorkspace("one"));var owner=new MainViewModel(repo,new NotImplementedLauncher());
        owner.AddItemToCommand.Execute(owner.Workspaces.Single());var child=owner.CardAddSession!;
        child.NewItemType=ItemType.Link;child.NewTarget="invalid";child.AddItemCommand.Execute(null);Assert.NotEmpty(child.AddItemError);Assert.Empty(repo.Load().Items);
        child.NewTarget="https://example.com/";child.AddItemCommand.Execute(null);Assert.Empty(child.NewTarget);
        child.NewTarget="https://example.com/";child.AddItemCommand.Execute(null);Assert.True(child.DuplicatePending);Assert.Single(repo.Load().Workspaces.Single().Items);
        child.CancelDuplicateCommand.Execute(null);Assert.Single(repo.Load().Workspaces.Single().Items);
        child.AddItemCommand.Execute(null);child.ConfirmDuplicateCommand.Execute(null);Assert.Equal(2,repo.Load().Workspaces.Single().Items.Count);
        child.NewTarget="C:\\cancel.txt";owner.FinishCardAdd();Assert.Equal(2,repo.Load().Workspaces.Single().Items.Count);
    }
    sealed class FailingRepo(IStoreRepository inner) : IStoreRepository
    {
        public bool Fail;
        public Store Load()=>inner.Load();
        public Store Mutate(Action<Catalog> change)=>Fail ? throw new IOException("fixture") : inner.Mutate(change);
        public void Dispose() { }
    }
    [Fact] public void Failed_save_retains_form_and_retries_without_entering_details()
    {
        using var store=Repo();store.Mutate(c=>c.CreateWorkspace("one"));var repo=new FailingRepo(store);
        var owner=new MainViewModel(repo,new NotImplementedLauncher());owner.AddItemToCommand.Execute(owner.Workspaces.Single());var child=owner.CardAddSession!;
        repo.Fail=true;child.NewTarget="https://example.com/";child.NewItemName="name";child.AddItemCommand.Execute(null);
        Assert.True(child.SaveBlocked);Assert.Equal("https://example.com/",child.NewTarget);Assert.Equal("name",child.NewItemName);Assert.Empty(store.Load().Items);Assert.True(owner.InOverview);
        repo.Fail=false;child.AddItemCommand.Execute(null);Assert.False(child.SaveBlocked);Assert.Empty(child.NewTarget);Assert.Single(store.Load().Items);
    }
}
