using System.Reactive.Linq;

namespace com.hollerson.reactivesets.tests;

public class RxSnapshotTests
{
    [Fact]
    public void EmitsFullMembershipAfterEachBatch()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var snapshots = new List<TestUser[]>();
        source.RxSnapshot().Subscribe(s => snapshots.Add(s));

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));

        Assert.Equal(2, snapshots.Count);
        Assert.Single(snapshots[0]);
        Assert.Equal(2, snapshots[1].Length);
    }

    [Fact]
    public void SnapshotReflectsUpdates()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var snapshots = new List<TestUser[]>();
        source.RxSnapshot().Subscribe(s => snapshots.Add(s));

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Update(new TestUser(1, "Alice", "Sales"));

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("Sales", snapshots[1][0].Department);
    }

    [Fact]
    public void SnapshotReflectsDeletes()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var snapshots = new List<TestUser[]>();
        source.RxSnapshot().Subscribe(s => snapshots.Add(s));

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Delete(1);

        Assert.Equal(2, snapshots.Count);
        Assert.Empty(snapshots[1]);
    }
}

public class RxCountTests
{
    [Fact]
    public void EmitsCountAfterEachBatch()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var counts = new List<int>();
        source.RxCount().Subscribe(c => counts.Add(c));

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));

        Assert.Equal(2, counts.Count);
        Assert.Equal(1, counts[0]);
        Assert.Equal(2, counts[1]);
    }

    [Fact]
    public void CountDecrementsOnDelete()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var counts = new List<int>();
        source.RxCount().Subscribe(c => counts.Add(c));

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Delete(1);

        Assert.Equal(new[] { 1, 0 }, counts);
    }

    [Fact]
    public void CountUnchangedByUpdate()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var counts = new List<int>();
        source.RxCount().Subscribe(c => counts.Add(c));

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Update(new TestUser(1, "Alice", "Sales"));

        // Count should be 1 both times (or only emit once if unchanged)
        Assert.All(counts, c => Assert.Equal(1, c));
    }
}
