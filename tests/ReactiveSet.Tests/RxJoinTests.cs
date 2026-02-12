namespace com.hollerson.reactivesets.tests;

public class RxJoinTests
{
    private MutableReactiveSet<TestOrder, int> CreateOrders() => new(o => o.Id);
    private MutableReactiveSet<TestCustomer, int> CreateCustomers() => new(c => c.Id);

    private IReactiveSet<NamedItem> Join(
        MutableReactiveSet<TestOrder, int> orders,
        MutableReactiveSet<TestCustomer, int> customers)
        => orders.RxJoin(
            customers,
            o => o.CustomerId,
            c => c.Id,
            (o, c) => new NamedItem(o.Id, $"{c.Name}:{o.Total}"));

    [Fact]
    public void MatchProducesDownstreamAdd()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        customers.Add(new TestCustomer(10, "Alice"));

        var add = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().SingleOrDefault();
        Assert.NotNull(add);
        Assert.Equal("Alice:99", add!.Item.Value);
    }

    [Fact]
    public void NoMatchProducesNoDownstreamEvent()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        customers.Add(new TestCustomer(20, "Bob")); // different key

        Assert.Empty(collector.AllEvents.OfType<RxSetAdd<NamedItem>>());
    }

    [Fact]
    public void LeftUpdateEmitsDownstreamUpdate()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        customers.Add(new TestCustomer(10, "Alice"));
        orders.Update(new TestOrder(1, 10, 50)); // same customerId, different total

        var update = collector.AllEvents.OfType<RxSetUpdate<NamedItem>>().SingleOrDefault();
        Assert.NotNull(update);
        Assert.Equal("Alice:50", update!.Item.Value);
    }

    [Fact]
    public void RightUpdateEmitsDownstreamUpdate()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        customers.Add(new TestCustomer(10, "Alice"));
        customers.Update(new TestCustomer(10, "Beth"));

        var update = collector.AllEvents.OfType<RxSetUpdate<NamedItem>>().SingleOrDefault();
        Assert.NotNull(update);
        Assert.Equal("Beth:99", update!.Item.Value);
    }

    [Fact]
    public void RightDeleteEmitsDownstreamDelete()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        customers.Add(new TestCustomer(10, "Alice"));
        customers.Delete(10);

        Assert.Single(collector.AllEvents.OfType<RxSetDelete<NamedItem>>());
    }

    [Fact]
    public void LeftDeleteEmitsDownstreamDelete()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99));
        customers.Add(new TestCustomer(10, "Alice"));
        orders.Delete(1);

        Assert.Single(collector.AllEvents.OfType<RxSetDelete<NamedItem>>());
    }

    [Fact]
    public void ManyToMany_MultipleLeftsMatchOneRight()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        customers.Add(new TestCustomer(10, "Alice"));
        orders.Add(new TestOrder(1, 10, 99));
        orders.Add(new TestOrder(2, 10, 50));
        orders.Add(new TestOrder(3, 10, 25));

        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.Equal(3, adds.Length);
    }

    [Fact]
    public void ManyToMany_RightUpdateAffectsAllMatchingLefts()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        customers.Add(new TestCustomer(10, "Alice"));
        orders.Add(new TestOrder(1, 10, 99));
        orders.Add(new TestOrder(2, 10, 50));

        customers.Update(new TestCustomer(10, "Beth"));

        var updates = collector.AllEvents.OfType<RxSetUpdate<NamedItem>>().ToArray();
        Assert.Equal(2, updates.Length);
        Assert.All(updates, u => Assert.Contains("Beth", u.Item.Value));
    }

    [Fact]
    public void LeftUpdateChangingKeyDeletesOldMatchAddsNewMatch()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        customers.Add(new TestCustomer(10, "Alice"));
        customers.Add(new TestCustomer(20, "Bob"));
        orders.Add(new TestOrder(1, 10, 99));

        // Order 1 changes customer from 10 to 20
        orders.Update(new TestOrder(1, 20, 99));

        // Should see Delete (old match with Alice) then Add (new match with Bob)
        var events = collector.AllEvents.ToArray();
        var deletesAfterFirstAdd = events.Skip(1).OfType<RxSetDelete<NamedItem>>().Count();
        var addsAfterFirstAdd = events.Skip(1).OfType<RxSetAdd<NamedItem>>().Count();
        Assert.True(deletesAfterFirstAdd >= 1);
        Assert.True(addsAfterFirstAdd >= 1);
    }

    [Fact]
    public void LeftAddedBeforeRightThenRightAdded()
    {
        var orders = CreateOrders();
        var customers = CreateCustomers();
        var joined = Join(orders, customers);
        using var collector = new ChangeCollector<NamedItem>(joined);

        orders.Add(new TestOrder(1, 10, 99)); // no match yet
        Assert.Empty(collector.AllEvents.OfType<RxSetAdd<NamedItem>>());

        customers.Add(new TestCustomer(10, "Alice")); // now matches
        var add = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().SingleOrDefault();
        Assert.NotNull(add);
    }
}
