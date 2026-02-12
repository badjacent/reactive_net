using System.Reactive.Linq;

namespace com.hollerson.reactivesets.tests;

public class ConstantReactiveSetTests
{
    [Fact]
    public async Task EmitsAddForEachItemOnSubscription()
    {
        var items = new[]
        {
            new NamedItem(1, "a"),
            new NamedItem(2, "b"),
            new NamedItem(3, "c"),
        };
        var set = new ConstantReactiveSet<NamedItem>(items);

        var batch = await set.Changes.FirstAsync();

        Assert.Equal(3, batch.Length);
        Assert.All(batch, e => Assert.IsType<RxSetAdd<NamedItem>>(e));
        var addedItems = batch.Cast<RxSetAdd<NamedItem>>().Select(a => a.Item).ToArray();
        Assert.Equal(items, addedItems);
    }

    [Fact]
    public async Task EachItemGetsUniqueLifetime()
    {
        var items = new[] { new NamedItem(1, "a"), new NamedItem(2, "b") };
        var set = new ConstantReactiveSet<NamedItem>(items);

        var batch = await set.Changes.FirstAsync();

        var lifetimes = batch.Cast<RxSetAdd<NamedItem>>().Select(a => a.Lifetime).ToArray();
        Assert.Equal(lifetimes.Distinct().Count(), lifetimes.Length);
    }

    [Fact]
    public void StreamDoesNotComplete()
    {
        var set = new ConstantReactiveSet<NamedItem>(new[] { new NamedItem(1, "a") });

        var completed = false;
        var received = new ManualResetEventSlim(false);

        set.Changes.Subscribe(
            onNext: _ => received.Set(),
            onCompleted: () => completed = true);

        received.Wait(TimeSpan.FromSeconds(2));
        Assert.False(completed);
    }

    [Fact]
    public async Task EmptyCollectionEmitsEmptyBatch()
    {
        var set = new ConstantReactiveSet<NamedItem>(Array.Empty<NamedItem>());

        var batch = await set.Changes.FirstAsync();

        Assert.Empty(batch);
    }

    [Fact]
    public async Task MultipleSubscribersEachGetAdds()
    {
        var items = new[] { new NamedItem(1, "a"), new NamedItem(2, "b") };
        var set = new ConstantReactiveSet<NamedItem>(items);

        var batch1 = await set.Changes.FirstAsync();
        var batch2 = await set.Changes.FirstAsync();

        Assert.Equal(2, batch1.Length);
        Assert.Equal(2, batch2.Length);
    }

    [Fact]
    public async Task LifetimesAreConsistentAcrossSubscribers()
    {
        var items = new[] { new NamedItem(1, "a"), new NamedItem(2, "b") };
        var set = new ConstantReactiveSet<NamedItem>(items);

        var batch1 = await set.Changes.FirstAsync();
        var batch2 = await set.Changes.FirstAsync();

        for (int i = 0; i < batch1.Length; i++)
        {
            var add1 = (RxSetAdd<NamedItem>)batch1[i];
            var add2 = (RxSetAdd<NamedItem>)batch2[i];
            Assert.Same(add1.Lifetime, add2.Lifetime);
        }
    }
}
