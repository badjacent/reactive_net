namespace com.hollerson.reactivesets.tests;

public class RxSelectManyArrayTests
{
    [Fact]
    public void AddEmitsAddsForAllArrayElements()
    {
        var source = new MutableReactiveSet<TestOrder, int>(o => o.Id);
        var items = new Dictionary<int, LineItem[]>
        {
            [1] = new[] { new LineItem(10, "Widget", 2), new LineItem(11, "Gadget", 1) }
        };

        var flat = source.RxSelectMany(
            o => items[o.Id],
            li => li.Id);
        using var collector = new ChangeCollector<LineItem>(flat);

        source.Add(new TestOrder(1, 100, 99));

        var adds = collector.AllEvents.OfType<RxSetAdd<LineItem>>().ToArray();
        Assert.Equal(2, adds.Length);
    }

    [Fact]
    public void DeleteEmitsDeletesForAllChildren()
    {
        var source = new MutableReactiveSet<TestOrder, int>(o => o.Id);
        var items = new Dictionary<int, LineItem[]>
        {
            [1] = new[] { new LineItem(10, "Widget", 2), new LineItem(11, "Gadget", 1) }
        };

        var flat = source.RxSelectMany(
            o => items[o.Id],
            li => li.Id);
        using var collector = new ChangeCollector<LineItem>(flat);

        source.Add(new TestOrder(1, 100, 99));
        source.Delete(1);

        var deletes = collector.AllEvents.OfType<RxSetDelete<LineItem>>().ToArray();
        Assert.Equal(2, deletes.Length);
    }

    [Fact]
    public void UpdateDiffsArrayByKey()
    {
        var source = new MutableReactiveSet<TestOrder, int>(o => o.Id);
        var version = 0;
        var itemsV0 = new[] { new LineItem(10, "Widget", 2), new LineItem(11, "Gadget", 1) };
        var itemsV1 = new[] { new LineItem(10, "Widget", 5), new LineItem(12, "Doohickey", 3) };

        var flat = source.RxSelectMany(
            o => version == 0 ? itemsV0 : itemsV1,
            li => li.Id);
        using var collector = new ChangeCollector<LineItem>(flat);

        source.Add(new TestOrder(1, 100, 99));
        version = 1;
        source.Update(new TestOrder(1, 100, 50));

        // Key 10: present in both, value changed → Update
        // Key 11: removed → Delete
        // Key 12: new → Add
        var events = collector.AllEvents.ToArray();
        var updates = events.OfType<RxSetUpdate<LineItem>>().ToArray();
        var deletes = events.OfType<RxSetDelete<LineItem>>().ToArray();
        var addsAfterFirst = events.Skip(2).OfType<RxSetAdd<LineItem>>().ToArray(); // skip initial adds

        Assert.Single(updates);
        Assert.Equal(5, updates[0].Item.Quantity);
        Assert.Single(deletes);
        Assert.Single(addsAfterFirst);
        Assert.Equal("Doohickey", addsAfterFirst[0].Item.Product);
    }

    [Fact]
    public void UpdateWithUnchangedChildEmitsNoEvent()
    {
        var source = new MutableReactiveSet<TestOrder, int>(o => o.Id);
        var items = new[] { new LineItem(10, "Widget", 2) };

        var flat = source.RxSelectMany(
            _ => items, // always returns same items
            li => li.Id);
        using var collector = new ChangeCollector<LineItem>(flat);

        source.Add(new TestOrder(1, 100, 99));
        source.Update(new TestOrder(1, 100, 50)); // parent changes but children are identical

        // Initial Add for key 10, then no events on update (child unchanged)
        var adds = collector.AllEvents.OfType<RxSetAdd<LineItem>>().ToArray();
        var updates = collector.AllEvents.OfType<RxSetUpdate<LineItem>>().ToArray();
        Assert.Single(adds);
        Assert.Empty(updates);
    }
}

public class RxSelectManyReactiveSetTests
{
    [Fact]
    public void ChildSetEventsForwardedDownstream()
    {
        var source = new MutableReactiveSet<NamedItem, int>(x => x.Id);
        var childSets = new Dictionary<int, MutableReactiveSet<TestUser, int>>();

        var flat = source.RxSelectMany(item =>
        {
            var child = new MutableReactiveSet<TestUser, int>(u => u.Id);
            childSets[item.Id] = child;
            return (IReactiveSet<TestUser>)child;
        });
        using var collector = new ChangeCollector<TestUser>(flat);

        source.Add(new NamedItem(1, "group1"));
        childSets[1].Add(new TestUser(10, "Alice", "Eng"));

        var add = collector.AllEvents.OfType<RxSetAdd<TestUser>>().SingleOrDefault();
        Assert.NotNull(add);
        Assert.Equal("Alice", add!.Item.Name);
    }

    [Fact]
    public void ParentDeleteDeletesAllChildLifetimes()
    {
        var source = new MutableReactiveSet<NamedItem, int>(x => x.Id);
        var childSets = new Dictionary<int, MutableReactiveSet<TestUser, int>>();

        var flat = source.RxSelectMany(item =>
        {
            var child = new MutableReactiveSet<TestUser, int>(u => u.Id);
            childSets[item.Id] = child;
            return (IReactiveSet<TestUser>)child;
        });
        using var collector = new ChangeCollector<TestUser>(flat);

        source.Add(new NamedItem(1, "group1"));
        childSets[1].Add(new TestUser(10, "Alice", "Eng"));
        childSets[1].Add(new TestUser(11, "Bob", "Sales"));

        source.Delete(1);

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Equal(2, deletes.Length);
    }

    [Fact]
    public void MultipleParentsEachContributeChildren()
    {
        var source = new MutableReactiveSet<NamedItem, int>(x => x.Id);
        var childSets = new Dictionary<int, MutableReactiveSet<TestUser, int>>();

        var flat = source.RxSelectMany(item =>
        {
            var child = new MutableReactiveSet<TestUser, int>(u => u.Id);
            childSets[item.Id] = child;
            return (IReactiveSet<TestUser>)child;
        });
        using var collector = new ChangeCollector<TestUser>(flat);

        source.Add(new NamedItem(1, "group1"));
        source.Add(new NamedItem(2, "group2"));
        childSets[1].Add(new TestUser(10, "Alice", "Eng"));
        childSets[2].Add(new TestUser(20, "Bob", "Sales"));

        var adds = collector.AllEvents.OfType<RxSetAdd<TestUser>>().ToArray();
        Assert.Equal(2, adds.Length);
    }
}
