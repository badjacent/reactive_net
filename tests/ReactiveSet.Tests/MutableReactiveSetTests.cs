using System.Reactive.Linq;

namespace com.hollerson.reactivesets.tests;

public class MutableReactiveSetTests
{
    private MutableReactiveSet<TestUser, int> CreateUserSet()
        => new(u => u.Id);

    [Fact]
    public void AddEmitsAddEvent()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        var alice = new TestUser(1, "Alice", "Eng");
        set.Add(alice);

        Assert.Single(collector.Batches);
        var add = Assert.IsType<RxSetAdd<TestUser>>(collector.Batches[0][0]);
        Assert.Equal(alice, add.Item);
    }

    [Fact]
    public void UpdateEmitsUpdateEvent()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Update(new TestUser(1, "Alice", "Sales"));

        Assert.Equal(2, collector.Batches.Count);
        var update = Assert.IsType<RxSetUpdate<TestUser>>(collector.Batches[1][0]);
        Assert.Equal("Sales", update.Item.Department);
    }

    [Fact]
    public void DeleteEmitsDeleteEvent()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Delete(1);

        Assert.Equal(2, collector.Batches.Count);
        Assert.IsType<RxSetDelete<TestUser>>(collector.Batches[1][0]);
    }

    [Fact]
    public void AddUpdateDeleteShareSameLifetime()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Update(new TestUser(1, "Alice", "Sales"));
        set.Delete(1);

        var addLifetime = ((RxSetAdd<TestUser>)collector.Batches[0][0]).Lifetime;
        var updateLifetime = ((RxSetUpdate<TestUser>)collector.Batches[1][0]).Lifetime;
        var deleteLifetime = ((RxSetDelete<TestUser>)collector.Batches[2][0]).Lifetime;

        Assert.Same(addLifetime, updateLifetime);
        Assert.Same(addLifetime, deleteLifetime);
    }

    [Fact]
    public void EachMutationEmitsSingleEventBatch()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Update(new TestUser(1, "Alice", "Sales"));
        set.Delete(1);

        Assert.Equal(3, collector.Batches.Count);
        Assert.All(collector.Batches, b => Assert.Single(b));
    }

    [Fact]
    public void AddDuplicateKeyThrows()
    {
        var set = CreateUserSet();
        set.Add(new TestUser(1, "Alice", "Eng"));

        Assert.Throws<InvalidOperationException>(() =>
            set.Add(new TestUser(1, "Bob", "Sales")));
    }

    [Fact]
    public void UpdateNonExistentKeyThrows()
    {
        var set = CreateUserSet();

        Assert.Throws<InvalidOperationException>(() =>
            set.Update(new TestUser(1, "Alice", "Sales")));
    }

    [Fact]
    public void DeleteNonExistentKeyThrows()
    {
        var set = CreateUserSet();

        Assert.Throws<InvalidOperationException>(() =>
            set.Delete(1));
    }

    [Fact]
    public void DeleteThenAddSameKeySuceeds()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Delete(1);
        set.Add(new TestUser(1, "Bob", "Sales"));

        Assert.Equal(3, collector.Batches.Count);
        var lastAdd = Assert.IsType<RxSetAdd<TestUser>>(collector.Batches[2][0]);
        Assert.Equal("Bob", lastAdd.Item.Name);
    }

    [Fact]
    public void RedundantUpdateIsPermitted()
    {
        var set = CreateUserSet();
        using var collector = new ChangeCollector<TestUser>(set);

        var alice = new TestUser(1, "Alice", "Eng");
        set.Add(alice);
        set.Update(alice); // same value — allowed per spec

        Assert.Equal(2, collector.Batches.Count);
        Assert.IsType<RxSetUpdate<TestUser>>(collector.Batches[1][0]);
    }

    [Fact]
    public void StreamDoesNotComplete()
    {
        var set = CreateUserSet();
        var completed = false;

        set.Changes.Subscribe(
            onNext: _ => { },
            onCompleted: () => completed = true);

        set.Add(new TestUser(1, "Alice", "Eng"));

        Assert.False(completed);
    }

    [Fact]
    public void LateSubscriberGetsReplayOfCurrentState()
    {
        var set = CreateUserSet();

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Add(new TestUser(2, "Bob", "Sales"));

        // Subscribe after mutations
        using var collector = new ChangeCollector<TestUser>(set);

        // Should get an initial batch with Adds for both users
        Assert.True(collector.Batches.Count >= 1);
        var initialAdds = collector.Batches[0].Cast<RxSetAdd<TestUser>>().ToArray();
        Assert.Equal(2, initialAdds.Length);

        var names = initialAdds.Select(a => a.Item.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Alice", "Bob" }, names);
    }

    [Fact]
    public void LateSubscriberSeesUpdatedValues()
    {
        var set = CreateUserSet();

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Update(new TestUser(1, "Alice", "Sales"));

        using var collector = new ChangeCollector<TestUser>(set);

        var add = Assert.IsType<RxSetAdd<TestUser>>(collector.Batches[0][0]);
        Assert.Equal("Sales", add.Item.Department);
    }

    [Fact]
    public void LateSubscriberDoesNotSeeDeletedItems()
    {
        var set = CreateUserSet();

        set.Add(new TestUser(1, "Alice", "Eng"));
        set.Delete(1);

        using var collector = new ChangeCollector<TestUser>(set);

        // Should get empty initial state
        if (collector.Batches.Count > 0)
        {
            Assert.Empty(collector.Batches[0]);
        }
    }

    [Fact]
    public void CustomComparerIsUsed()
    {
        var set = new MutableReactiveSet<TestUser, string>(
            u => u.Name,
            StringComparer.OrdinalIgnoreCase);

        set.Add(new TestUser(1, "Alice", "Eng"));

        // "alice" should collide with "Alice" using case-insensitive comparison
        Assert.Throws<InvalidOperationException>(() =>
            set.Add(new TestUser(2, "alice", "Sales")));
    }
}
