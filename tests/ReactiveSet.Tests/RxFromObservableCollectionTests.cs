using System.Reactive.Subjects;

namespace com.hollerson.reactivesets.tests;

public class RxFromObservableCollectionTests
{
    [Fact]
    public void FirstSnapshotEmitsAdds()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[]
        {
            new TestUser(1, "Alice", "Eng"),
            new TestUser(2, "Bob", "Sales"),
        });

        var adds = collector.AllEvents.OfType<RxSetAdd<TestUser>>().ToArray();
        Assert.Equal(2, adds.Length);
    }

    [Fact]
    public void NewKeyInSubsequentSnapshotEmitsAdd()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[] { new TestUser(1, "Alice", "Eng") });
        subject.OnNext(new[]
        {
            new TestUser(1, "Alice", "Eng"),
            new TestUser(2, "Bob", "Sales"),
        });

        var adds = collector.AllEvents.OfType<RxSetAdd<TestUser>>().ToArray();
        Assert.Equal(2, adds.Length); // initial + new
    }

    [Fact]
    public void MissingKeyInSubsequentSnapshotEmitsDelete()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[]
        {
            new TestUser(1, "Alice", "Eng"),
            new TestUser(2, "Bob", "Sales"),
        });
        subject.OnNext(new[] { new TestUser(1, "Alice", "Eng") }); // Bob removed

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Single(deletes);
    }

    [Fact]
    public void ChangedValueEmitsUpdate()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[] { new TestUser(1, "Alice", "Eng") });
        subject.OnNext(new[] { new TestUser(1, "Alice", "Sales") }); // dept changed

        var updates = collector.AllEvents.OfType<RxSetUpdate<TestUser>>().ToArray();
        Assert.Single(updates);
        Assert.Equal("Sales", updates[0].Item.Department);
    }

    [Fact]
    public void UnchangedValueEmitsNoEvent()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        var alice = new TestUser(1, "Alice", "Eng");
        subject.OnNext(new[] { alice });
        subject.OnNext(new[] { alice }); // identical

        Assert.Single(collector.Batches); // only initial batch
    }

    [Fact]
    public void SourceCompletionDeletesAllThenStaysOpen()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[]
        {
            new TestUser(1, "Alice", "Eng"),
            new TestUser(2, "Bob", "Sales"),
        });
        subject.OnCompleted();

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Equal(2, deletes.Length);
        Assert.False(collector.Completed);
    }

    [Fact]
    public void SourceErrorDeletesAllThenErrors()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[] { new TestUser(1, "Alice", "Eng") });
        subject.OnError(new Exception("test"));

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Single(deletes);
        Assert.NotNull(collector.Error);
    }

    [Fact]
    public void EmptySnapshotDeletesAll()
    {
        var subject = new Subject<IEnumerable<TestUser>>();
        var set = RxFromObservableCollection.Create(subject, (TestUser u) => u.Id);
        using var collector = new ChangeCollector<TestUser>(set);

        subject.OnNext(new[]
        {
            new TestUser(1, "Alice", "Eng"),
            new TestUser(2, "Bob", "Sales"),
        });
        subject.OnNext(Array.Empty<TestUser>()); // empty snapshot

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Equal(2, deletes.Length);
    }
}
