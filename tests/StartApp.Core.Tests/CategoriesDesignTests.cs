using StartApp.Core;
using Xunit;

/// <summary>Managed categories (base + custom, settled Nov 11) and the per-workspace card design (color + background image).
/// Base categories can never be edited or deleted; deleting a custom category moves its workspaces to כללי without deleting them.</summary>
public class CategoriesDesignTests
{
    private static Catalog Cat() => new(new Store());

    [Fact] public void Default_list_contains_the_settled_builtins_in_order()
    {
        var c = Cat();
        Assert.Equal(Catalog.BuiltInCategories, c.AllCategories());
        Assert.True(Catalog.IsBuiltInCategory(Workspace.DefaultCategory));
        Assert.Equal(21, c.AllCategories().Count);
        Assert.Equal(21, c.AllCategories().Distinct().Count());
        Assert.All(Catalog.BuiltInCategories, name => {
            Assert.True(Catalog.IsBuiltInCategory(name));
            Assert.Throws<ArgumentException>(() => c.DeleteCustomCategory(name));
            Assert.Throws<ArgumentException>(() => c.RenameCustomCategory(name, "replacement"));
        });
        Assert.Empty(c.Store.CustomCategories);
    }

    [Fact] public void Add_custom_category_and_reject_duplicates_and_builtin_shadowing()
    {
        var c = Cat();
        c.AddCustomCategory("מוזיקה אישית");
        Assert.Equal(Catalog.BuiltInCategories.Concat(new[] { "מוזיקה אישית" }), c.AllCategories());
        Assert.Throws<ArgumentException>(() => c.AddCustomCategory("מוזיקה אישית"));
        Assert.Throws<ArgumentException>(() => c.AddCustomCategory("כללי"));
        Assert.Throws<ArgumentException>(() => c.AddCustomCategory("  "));
        c.AddCustomCategory("Graphics");
        Assert.Throws<ArgumentException>(() => c.AddCustomCategory("graphics")); // case-insensitive duplicate
        Assert.Empty(c.Validate());
    }

    [Fact] public void Only_custom_categories_can_be_renamed_or_deleted()
    {
        var c = Cat();
        Assert.Throws<ArgumentException>(() => c.RenameCustomCategory("כללי", "x"));
        Assert.Throws<ArgumentException>(() => c.DeleteCustomCategory("כללי"));
        Assert.Throws<ArgumentException>(() => c.RenameCustomCategory("nope", "x"));
        Assert.Throws<ArgumentException>(() => c.DeleteCustomCategory("nope"));
    }

    [Fact] public void Rename_updates_every_assigned_workspace()
    {
        var c = Cat();
        c.AddCustomCategory("מוזיקה אישית");
        var a = c.CreateWorkspace("a"); var b = c.CreateWorkspace("b");
        c.SetCategory(a.Id, "מוזיקה אישית"); c.SetCategory(b.Id, "מוזיקה אישית");
        c.RenameCustomCategory("מוזיקה אישית", "פרויקט מוזיקה");
        Assert.Equal("פרויקט מוזיקה", a.Category); Assert.Equal("פרויקט מוזיקה", b.Category);
        Assert.Equal(Catalog.BuiltInCategories.Concat(new[] { "פרויקט מוזיקה" }), c.AllCategories());
        Assert.Empty(c.Validate());
    }

    [Fact] public void Delete_moves_assigned_workspaces_to_general_without_deleting_them()
    {
        var c = Cat();
        c.AddCustomCategory("מוזיקה אישית"); c.AddCustomCategory("גרפיקה אישית");
        var a = c.CreateWorkspace("a"); var b = c.CreateWorkspace("b"); var other = c.CreateWorkspace("c");
        c.SetCategory(a.Id, "מוזיקה אישית"); c.SetCategory(b.Id, "מוזיקה אישית"); c.SetCategory(other.Id, "גרפיקה אישית");
        Assert.Equal(2, c.WorkspacesInCategory("מוזיקה אישית").Count);
        c.DeleteCustomCategory("מוזיקה אישית");
        Assert.Equal(3, c.Store.Workspaces.Count); // no workspace was deleted
        Assert.Equal(Workspace.DefaultCategory, a.EffectiveCategory); Assert.Null(a.Category);
        Assert.Equal(Workspace.DefaultCategory, b.EffectiveCategory);
        Assert.Equal("גרפיקה אישית", other.EffectiveCategory); // other categories untouched
        Assert.Equal(Catalog.BuiltInCategories.Concat(new[] { "גרפיקה אישית" }), c.AllCategories());
        Assert.Empty(c.Validate());
    }

    [Fact] public void SetCategory_accepts_list_values_and_stores_the_default_as_none()
    {
        var c = Cat();
        c.AddCustomCategory("מוזיקה אישית");
        var w = c.CreateWorkspace("a");
        c.SetCategory(w.Id, "מוזיקה אישית"); Assert.Equal("מוזיקה אישית", w.Category);
        c.SetCategory(w.Id, Workspace.DefaultCategory); Assert.Null(w.Category);
        c.SetCategory(w.Id, null); Assert.Null(w.Category);
        Assert.Throws<ArgumentException>(() => c.SetCategory(w.Id, "לא קיים"));
    }

    [Fact] public void Card_design_is_per_workspace_and_validated()
    {
        var c = Cat();
        var a = c.CreateWorkspace("a"); var b = c.CreateWorkspace("b");
        c.SetCardDesign(a.Id, "#BFDBFE", "C:\\img\\bg.png");
        Assert.Equal("#BFDBFE", a.Color); Assert.Equal("C:\\img\\bg.png", a.ImagePath);
        Assert.Null(b.Color); Assert.Null(b.ImagePath); // two cards do not affect each other
        Assert.Throws<ArgumentException>(() => c.SetCardDesign(a.Id, "blue", null));
        Assert.Throws<ArgumentException>(() => c.SetCardDesign(a.Id, "#FFF", null));
        Assert.Throws<ArgumentException>(() => c.SetCardDesign(a.Id, null, "  "));
        c.SetCardDesign(a.Id, null, null); // clearing works
        Assert.Null(a.Color); Assert.Null(a.ImagePath);
        Assert.Empty(c.Validate());
    }

    [Fact] public void Validate_flags_bad_design_and_bad_custom_categories()
    {
        var s = new Store();
        var c = new Catalog(s);
        var w = c.CreateWorkspace("a");
        w.Color = "blue";
        Assert.Contains(c.Validate(), e => e.Contains("color must be #RRGGBB"));
        w.Color = null; w.ImagePath = " ";
        Assert.Contains(c.Validate(), e => e.Contains("image path is empty"));
        w.ImagePath = null;
        s.CustomCategories.Add("כללי");
        Assert.Contains(c.Validate(), e => e.Contains("shadows a built-in"));
        s.CustomCategories.Clear(); s.CustomCategories.Add("Graphics"); s.CustomCategories.Add("graphics");
        Assert.Contains(c.Validate(), e => e.Contains("duplicate custom category"));
    }
}
