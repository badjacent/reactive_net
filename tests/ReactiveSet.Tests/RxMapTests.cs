namespace com.hollerson.reactivesets.tests;

public class RxMapTests
{
    [Fact]
    public void MapsAddItems()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var mapped = source.RxMap(u => new NamedItem(u.Id, u.Name));
        using var collector = new ChangeCollector<NamedItem>(mapped);

        source.Add(new TestUser(1, "Alice", "Eng"));

        var add = Assert.IsType<RxSetAdd<NamedItem>>(collector.Batches.Last()[0]);
        Assert.Equal("Alice", add.Item.Value);
    }

    [Fact]
    public void MapsUpdateItems()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var mapped = source.RxMap(u => new NamedItem(u.Id, u.Department));
        using var collector = new ChangeCollector<NamedItem>(mapped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Update(new TestUser(1, "Alice", "Sales"));

        var update = Assert.IsType<RxSetUpdate<NamedItem>>(collector.Batches.Last()[0]);
        Assert.Equal("Sales", update.Item.Value);
    }

    [Fact]
    public void ForwardsDeletes()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var mapped = source.RxMap(u => new NamedItem(u.Id, u.Name));
        using var collector = new ChangeCollector<NamedItem>(mapped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Delete(1);

        Assert.IsType<RxSetDelete<NamedItem>>(collector.Batches.Last()[0]);
    }

    [Fact]
    public void PreservesLifetimeIdentity()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var mapped = source.RxMap(u => new NamedItem(u.Id, u.Name));
        using var collector = new ChangeCollector<NamedItem>(mapped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Update(new TestUser(1, "Bob", "Eng"));
        source.Delete(1);

        var lifetimes = collector.AllEvents.Select(e => e switch
        {
            RxSetAdd<NamedItem> a => a.Lifetime,
            RxSetUpdate<NamedItem> u => u.Lifetime,
            RxSetDelete<NamedItem> d => d.Lifetime,
            _ => null
        }).ToArray();

        Assert.All(lifetimes, l => Assert.Same(lifetimes[0], l));
    }

    [Fact]
    public void OneToOneLifetimes()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var mapped = source.RxMap(u => new NamedItem(u.Id, u.Name));
        using var collector = new ChangeCollector<NamedItem>(mapped);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));

        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.Equal(2, adds.Length);
        Assert.NotSame(adds[0].Lifetime, adds[1].Lifetime);
    }
}
