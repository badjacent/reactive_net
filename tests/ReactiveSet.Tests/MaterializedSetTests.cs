namespace com.hollerson.reactivesets.tests;

public class MaterializedSetTests
{
    [Fact]
    public void ReflectsAdds()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        using var view = new MaterializedSet<TestUser, int>(source, u => u.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));

        Assert.Equal(1, view.Count);
        Assert.Equal("Alice", view.TryGet(1)?.Name);
        Assert.True(view.ContainsKey(1));
    }

    [Fact]
    public void ReflectsUpdates()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        using var view = new MaterializedSet<TestUser, int>(source, u => u.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Update(new TestUser(1, "Alice", "Sales"));

        Assert.Equal(1, view.Count);
        Assert.Equal("Sales", view.TryGet(1)?.Department);
    }

    [Fact]
    public void ReflectsDeletes()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        using var view = new MaterializedSet<TestUser, int>(source, u => u.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Delete(1);

        Assert.Equal(0, view.Count);
        Assert.Null(view.TryGet(1));
        Assert.False(view.ContainsKey(1));
    }

    [Fact]
    public void ItemsReturnsAllCurrentValues()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        using var view = new MaterializedSet<TestUser, int>(source, u => u.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));

        Assert.Equal(2, view.Items.Count);
    }

    [Fact]
    public void DisposeTearsDownSubscription()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var view = new MaterializedSet<TestUser, int>(source, u => u.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));
        view.Dispose();

        // After dispose, mutations to source should not throw
        // (the view just won't see them)
        source.Add(new TestUser(2, "Bob", "Sales"));

        Assert.Equal(1, view.Count); // still sees pre-dispose state
    }

    [Fact]
    public void WorksWithConstantReactiveSet()
    {
        var items = new[] { new NamedItem(1, "a"), new NamedItem(2, "b") };
        var source = new ConstantReactiveSet<NamedItem>(items);
        using var view = new MaterializedSet<NamedItem, int>(source, i => i.Id);

        Assert.Equal(2, view.Count);
        Assert.Equal("a", view.TryGet(1)?.Value);
        Assert.Equal("b", view.TryGet(2)?.Value);
    }

    [Fact]
    public void SynchronousConsistencyAfterMutation()
    {
        // Spec: "After a mutation method returns, the caller can synchronously
        // query any MaterializedSet subscribed to the same pipeline."
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        using var view = new MaterializedSet<TestUser, int>(source, u => u.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));
        // Immediately after Add returns, view must reflect the change
        Assert.Equal(1, view.Count);
        Assert.Equal("Alice", view.TryGet(1)!.Name);

        source.Update(new TestUser(1, "Alice", "Sales"));
        Assert.Equal("Sales", view.TryGet(1)!.Department);

        source.Delete(1);
        Assert.Equal(0, view.Count);
    }
}
