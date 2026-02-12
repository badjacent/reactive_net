using System.Reactive.Subjects;

namespace com.hollerson.reactivesets.tests;

public class RxSelectMultipleLifetimesTests
{
    [Fact]
    public void EachInnerObservableBecomesOneLifetime()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner1 = new Subject<NamedItem>();
        var inner2 = new Subject<NamedItem>();

        outer.OnNext(inner1);
        outer.OnNext(inner2);

        inner1.OnNext(new NamedItem(1, "a"));
        inner2.OnNext(new NamedItem(2, "x"));

        var adds = collector.AllEvents.OfType<RxSetAdd<NamedItem>>().ToArray();
        Assert.Equal(2, adds.Length);
        Assert.NotSame(adds[0].Lifetime, adds[1].Lifetime);
    }

    [Fact]
    public void FirstInnerValueEmitsAdd()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner = new Subject<NamedItem>();
        outer.OnNext(inner);
        inner.OnNext(new NamedItem(1, "a"));

        var add = Assert.IsType<RxSetAdd<NamedItem>>(collector.Batches[0][0]);
        Assert.Equal("a", add.Item.Value);
    }

    [Fact]
    public void SubsequentInnerValuesEmitUpdate()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner = new Subject<NamedItem>();
        outer.OnNext(inner);
        inner.OnNext(new NamedItem(1, "a"));
        inner.OnNext(new NamedItem(1, "b"));

        Assert.IsType<RxSetAdd<NamedItem>>(collector.Batches[0][0]);
        var update = Assert.IsType<RxSetUpdate<NamedItem>>(collector.Batches[1][0]);
        Assert.Equal("b", update.Item.Value);
    }

    [Fact]
    public void InnerCompletionEmitsDelete()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner = new Subject<NamedItem>();
        outer.OnNext(inner);
        inner.OnNext(new NamedItem(1, "a"));
        inner.OnCompleted();

        Assert.Equal(2, collector.Batches.Count);
        Assert.IsType<RxSetDelete<NamedItem>>(collector.Batches[1][0]);
    }

    [Fact]
    public void InnerErrorEmitsDeleteForThatInnerOnly()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner1 = new Subject<NamedItem>();
        var inner2 = new Subject<NamedItem>();

        outer.OnNext(inner1);
        outer.OnNext(inner2);
        inner1.OnNext(new NamedItem(1, "a"));
        inner2.OnNext(new NamedItem(2, "x"));

        inner1.OnError(new Exception("inner1 error"));

        // inner1 should get a Delete
        var deletes = collector.AllEvents.OfType<RxSetDelete<NamedItem>>().ToArray();
        Assert.Single(deletes);

        // The overall stream should NOT error
        Assert.Null(collector.Error);

        // inner2 should still be live
        inner2.OnNext(new NamedItem(2, "y"));
        var updates = collector.AllEvents.OfType<RxSetUpdate<NamedItem>>().ToArray();
        Assert.Single(updates);
    }

    [Fact]
    public void OuterCompletionKeepsExistingInnersAlive()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner = new Subject<NamedItem>();
        outer.OnNext(inner);
        inner.OnNext(new NamedItem(1, "a"));

        outer.OnCompleted();

        // Stream stays open
        Assert.False(collector.Completed);
        Assert.Null(collector.Error);

        // Inner still works
        inner.OnNext(new NamedItem(1, "b"));
        var update = Assert.IsType<RxSetUpdate<NamedItem>>(collector.Batches.Last()[0]);
        Assert.Equal("b", update.Item.Value);
    }

    [Fact]
    public void OuterErrorDeletesAllInnersAndErrorsStream()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner1 = new Subject<NamedItem>();
        var inner2 = new Subject<NamedItem>();
        outer.OnNext(inner1);
        outer.OnNext(inner2);
        inner1.OnNext(new NamedItem(1, "a"));
        inner2.OnNext(new NamedItem(2, "x"));

        outer.OnError(new Exception("outer error"));

        var deletes = collector.AllEvents.OfType<RxSetDelete<NamedItem>>().ToArray();
        Assert.Equal(2, deletes.Length);
        Assert.NotNull(collector.Error);
    }

    [Fact]
    public void MultipleInnersInterleaved()
    {
        var outer = new Subject<IObservable<NamedItem>>();
        var set = RxSelectMultipleLifetimes.Create(outer);
        using var collector = new ChangeCollector<NamedItem>(set);

        var inner1 = new Subject<NamedItem>();
        var inner2 = new Subject<NamedItem>();

        outer.OnNext(inner1);
        inner1.OnNext(new NamedItem(1, "a"));

        outer.OnNext(inner2);
        inner1.OnNext(new NamedItem(1, "b"));
        inner2.OnNext(new NamedItem(2, "x"));

        inner1.OnCompleted(); // Delete inner1

        inner2.OnNext(new NamedItem(2, "y"));
        inner2.OnCompleted(); // Delete inner2

        var events = collector.AllEvents.ToArray();
        Assert.IsType<RxSetAdd<NamedItem>>(events[0]);       // inner1 add
        Assert.IsType<RxSetUpdate<NamedItem>>(events[1]);    // inner1 update
        Assert.IsType<RxSetAdd<NamedItem>>(events[2]);       // inner2 add
        Assert.IsType<RxSetDelete<NamedItem>>(events[3]);    // inner1 delete
        Assert.IsType<RxSetUpdate<NamedItem>>(events[4]);    // inner2 update
        Assert.IsType<RxSetDelete<NamedItem>>(events[5]);    // inner2 delete
    }
}
