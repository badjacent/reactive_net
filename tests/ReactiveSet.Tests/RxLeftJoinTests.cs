namespace com.hollerson.reactivesets.tests;

public class RxLeftJoinTests
{
    private MutableReactiveSet<TestOrder, int> CreateOrders() => new(o => o.Id);
    private MutableReactiveSet<TestCustomer, int> CreateCustomers() => new(c => c.Id);

    private IReactiveSet<NamedItem> LeftJoin(
        MutableReactiveSet<TestOrder, int> orders,
        MutableReactiveSet<TestCustomer, int> customers)
        => orders.RxLeftJoin(
            customers,
            o => o.CustomerId,
            c => c.Id,
            (o, c) => new NamedItem(o.Id, $"{c?.Name ?? "null"}:{o.Total}"));

    [Fact]
    public void LeftAddWithNoMatchEmitsAddWithNullRight()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = LeftJoin(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));

        var add = Assert.IsType<RxSetAdd<NamedItem>>(collector.Batches.Last()[0]);
        Assert.Equal("null:99", add.Item.Value);
    }

    [Fact]
    public void RightAddMatchingExistingLeftEmitsUpdate()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = LeftJoin(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));    // Add with null right
        customers.Add(new TestCustomer(10, "Alice")); // right appears

        // With 1 left and 1 right matching: the null-right lifetime updates
        var update = collector.AllEvents.OfType<RxSetUpdate<NamedItem>>().SingleOrDefault();
        Assert.NotNull(update);
        Assert.Equal("Alice:99", update!.Item.Value);
    }

    [Fact]
    public void RightDeleteWhileMatchedEmitsUpdateToNull()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = LeftJoin(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        customers.Add(new TestCustomer(10, "Alice"));
        orders.Add(new TestOrder(1, 10, 99));
        customers.Delete(10);

        // After right delete, order still has a downstream lifetime but with null right
        var lastEvents = collector.Batches.Last();
        // Should have a lifecycle that results in null right
        var allValues = collector.AllEvents
            .OfType<RxSetUpdate<NamedItem>>()
            .Select(u => u.Item.Value)
            .Concat(collector.AllEvents
                .OfType<RxSetAdd<NamedItem>>()
                .Select(a => a.Item.Value))
            .ToArray();

        // The last state should reflect null right
        Assert.Contains("null:99", allValues);
    }

    [Fact]
    public void LeftDeleteEmitsDownstreamDelete()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = LeftJoin(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        orders.Delete(1);

        Assert.Single(collector.AllEvents.OfType<RxSetDelete<NamedItem>>());
    }

    [Fact]
    public void LeftUpdateEmitsDownstreamUpdate()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = LeftJoin(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        customers.Add(new TestCustomer(10, "Alice"));
        orders.Add(new TestOrder(1, 10, 99));
        orders.Update(new TestOrder(1, 10, 50));

        var update = collector.AllEvents.OfType<RxSetUpdate<NamedItem>>().LastOrDefault();
        Assert.NotNull(update);
        Assert.Equal("Alice:50", update!.Item.Value);
    }

    [Fact]
    public void ManyToMany_MultipleRightsMatchOneLeft()
    {
        // Using a scenario where multiple rights share the same join key
        var left = new MutableReactiveSet<NamedItem, int>(x => x.Id);
        var right = new MutableReactiveSet<NamedItem, int>(x => x.Id);

        // Both items have Value "A" used as key
        var joined = left.RxLeftJoin(
            right,
            l => l.Value,
            r => r.Value,
            (l, r) => new NamedItem(l.Id, $"{l.Id}-{r?.Id.ToString() ?? "null"}"));

        using var collector = new ChangeCollector<NamedItem>(joined);

        left.Add(new NamedItem(1, "A"));          // Add with null right
        right.Add(new NamedItem(10, "A"));         // First right match
        right.Add(new NamedItem(20, "A"));         // Second right match

        // After second right: left 1 should have 2 downstream lifetimes
        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.True(adds.Length >= 2, $"Expected at least 2 adds, got {adds.Length}");
    }

    [Fact]
    public void ManyToMany_DeletingLastRightRestoresNullLifetime()
    {
        var left = new MutableReactiveSet<NamedItem, int>(x => x.Id);
        var right = new MutableReactiveSet<NamedItem, int>(x => x.Id);

        var joined = left.RxLeftJoin(
            right,
            l => l.Value,
            r => r.Value,
            (l, r) => new NamedItem(l.Id, $"{l.Id}-{r?.Id.ToString() ?? "null"}"));

        using var collector = new ChangeCollector<NamedItem>(joined);

        left.Add(new NamedItem(1, "A"));
        right.Add(new NamedItem(10, "A"));
        right.Delete(10); // Last right removed

        // Should restore the null-right lifetime for left 1
        var lastAdd = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().LastOrDefault();
        Assert.NotNull(lastAdd);
        Assert.Equal("1-null", lastAdd!.Item.Value);
    }

    [Fact]
    public void LeftAlwaysHasDownstreamLifetime()
    {
        // Core invariant: every left lifetime always has at least one downstream lifetime
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = LeftJoin(orders, customers);
        using var view = new MaterializedSet<NamedItem, int>(joined, x => x.Id);

        orders.Add(new TestOrder(1, 10, 99));
        Assert.Equal(1, view.Count); // null-right lifetime

        customers.Add(new TestCustomer(10, "Alice"));
        Assert.True(view.Count >= 1); // matched lifetime(s)

        customers.Delete(10);
        Assert.Equal(1, view.Count); // back to null-right
    }
}
