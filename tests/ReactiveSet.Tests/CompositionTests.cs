namespace com.hollerson.reactivesets.tests;

/// <summary>
/// Tests that verify operators compose correctly in pipelines.
/// </summary>
public class CompositionTests
{
    [Fact]
    public void MapThenFilter()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var pipeline = source
            .RxMap(u => new NamedItem(u.Id, u.Department))
            .RxFilter(x => x.Value == "Eng");

        using var collector = new ChangeCollector<NamedItem>(pipeline);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));

        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.Single(adds);
        Assert.Equal("Eng", adds[0].Item.Value);
    }

    [Fact]
    public void FilterThenMap()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var pipeline = source
            .RxFilter(u => u.Department == "Eng")
            .RxMap(u => new NamedItem(u.Id, u.Name));

        using var collector = new ChangeCollector<NamedItem>(pipeline);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));

        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.Single(adds);
        Assert.Equal("Alice", adds[0].Item.Value);
    }

    [Fact]
    public void JoinThenFilter()
    {
        var orders = new MutableReactiveSet<TestOrder, int>(o => o.Id);
        var customers = new MutableReactiveSet<TestCustomer, int>(c => c.Id);

        var pipeline = orders.RxJoin(
                customers,
                o => o.CustomerId,
                c => c.Id,
                (o, c) => new NamedItem(o.Id, $"{c.Name}:{o.Total}"))
            .RxFilter(x => x.Value.Contains("Alice"));

        using var collector = new ChangeCollector<NamedItem>(pipeline);

        customers.Add(new TestCustomer(10, "Alice"));
        customers.Add(new TestCustomer(20, "Bob"));
        orders.Add(new TestOrder(1, 10, 99));  // matches Alice
        orders.Add(new TestOrder(2, 20, 50));  // matches Bob

        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.Single(adds);
        Assert.Contains("Alice", adds[0].Item.Value);
    }

    [Fact]
    public void JoinThenMapThenCount()
    {
        var orders = new MutableReactiveSet<TestOrder, int>(o => o.Id);
        var customers = new MutableReactiveSet<TestCustomer, int>(c => c.Id);

        var counts = new List<int>();
        orders.RxJoin(
                customers,
                o => o.CustomerId,
                c => c.Id,
                (o, c) => new NamedItem(o.Id, c.Name))
            .RxCount()
            .Subscribe(c => counts.Add(c));

        customers.Add(new TestCustomer(10, "Alice"));
        orders.Add(new TestOrder(1, 10, 99));
        orders.Add(new TestOrder(2, 10, 50));

        Assert.Contains(1, counts);
        Assert.Contains(2, counts);
    }

    [Fact]
    public void PipelinePropagatesUpdates()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var pipeline = source
            .RxFilter(u => u.Department == "Eng")
            .RxMap(u => new NamedItem(u.Id, u.Name));

        using var view = new MaterializedSet<NamedItem, int>(pipeline, x => x.Id);

        source.Add(new TestUser(1, "Alice", "Eng"));
        Assert.Equal("Alice", view.TryGet(1)?.Value);

        source.Update(new TestUser(1, "Bob", "Eng"));
        Assert.Equal("Bob", view.TryGet(1)?.Value);

        source.Update(new TestUser(1, "Bob", "Sales")); // now filtered out
        Assert.Null(view.TryGet(1));
    }
}
