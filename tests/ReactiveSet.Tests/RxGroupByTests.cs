namespace com.hollerson.reactivesets.tests;

public class RxGroupByTests
{
    [Fact]
    public void AddCreatesGroupIfNotExists()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));

        var groupAdd = collector.AllEvents.OfType<RxSetAdd<IReactiveSet<TestUser>>>().SingleOrDefault();
        Assert.NotNull(groupAdd);
    }

    [Fact]
    public void AddToExistingGroupDoesNotCreateNewGroup()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Eng"));

        var groupAdds = collector.AllEvents.OfType<RxSetAdd<IReactiveSet<TestUser>>>().ToArray();
        Assert.Single(groupAdds); // only one group created
    }

    [Fact]
    public void ItemAppearsInCorrectGroup()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));

        var group = collector.AllEvents.OfType<RxSetAdd<IReactiveSet<TestUser>>>().Single().Item;
        using var groupCollector = new ChangeCollector<TestUser>(group);

        // Group should already contain Alice (subscription replay)
        var adds = groupCollector.AllEvents.OfType<RxSetAdd<TestUser>>().ToArray();
        Assert.Single(adds);
        Assert.Equal("Alice", adds[0].Item.Name);
    }

    [Fact]
    public void UpdateWithUnchangedKeyEmitsUpdateInGroup()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));

        var group = collector.AllEvents.OfType<RxSetAdd<IReactiveSet<TestUser>>>().Single().Item;
        using var groupCollector = new ChangeCollector<TestUser>(group);

        source.Update(new TestUser(1, "Alicia", "Eng")); // same department

        var updates = groupCollector.AllEvents.OfType<RxSetUpdate<TestUser>>().ToArray();
        Assert.Single(updates);
        Assert.Equal("Alicia", updates[0].Item.Name);
    }

    [Fact]
    public void UpdateChangingKeyMovesItemBetweenGroups()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var outerCollector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Eng"));

        // Alice moves to Sales
        source.Update(new TestUser(1, "Alice", "Sales"));

        // Should now have 2 groups
        var groupAdds = outerCollector.AllEvents.OfType<RxSetAdd<IReactiveSet<TestUser>>>().ToArray();
        Assert.Equal(2, groupAdds.Length);
    }

    [Fact]
    public void EmptyGroupIsDeleted()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Delete(1);

        var groupDeletes = collector.AllEvents.OfType<RxSetDelete<IReactiveSet<TestUser>>>().ToArray();
        Assert.Single(groupDeletes);
    }

    [Fact]
    public void KeyChangeFromSingleItemGroupDeletesOldGroup()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));   // creates Eng group
        source.Update(new TestUser(1, "Alice", "Sales")); // empties Eng, creates Sales

        var groupDeletes = collector.AllEvents.OfType<RxSetDelete<IReactiveSet<TestUser>>>().ToArray();
        Assert.Single(groupDeletes); // Eng group deleted
    }

    [Fact]
    public void MultipleGroupsIndependent()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var grouped = source.RxGroupBy(u => u.Department);
        using var collector = new ChangeCollector<IReactiveSet<TestUser>>(grouped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));
        source.Add(new TestUser(3, "Carol", "Eng"));

        var groups = collector.AllEvents.OfType<RxSetAdd<IReactiveSet<TestUser>>>().ToArray();
        Assert.Equal(2, groups.Length); // Eng and Sales
    }
}
