using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace com.hollerson.reactivesets.tests;

public class RxSelectSingleLifetimeTests
{
    [Fact]
    public void FirstValueEmitsAdd()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        var item = new NamedItem(1, "hello");
        subject.OnNext(item);

        Assert.Single(collector.Batches);
        var add = Assert.IsType<RxSetAdd<NamedItem>>(collector.Batches[0][0]);
        Assert.Equal(item, add.Item);
    }

    [Fact]
    public void SubsequentValuesEmitUpdate()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        subject.OnNext(new NamedItem(1, "hello"));
        subject.OnNext(new NamedItem(1, "world"));

        Assert.Equal(2, collector.Batches.Count);
        Assert.IsType<RxSetAdd<NamedItem>>(collector.Batches[0][0]);
        var update = Assert.IsType<RxSetUpdate<NamedItem>>(collector.Batches[1][0]);
        Assert.Equal("world", update.Item.Value);
    }

    [Fact]
    public void AddAndUpdatesShareSameLifetime()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        subject.OnNext(new NamedItem(1, "a"));
        subject.OnNext(new NamedItem(1, "b"));
        subject.OnNext(new NamedItem(1, "c"));

        var lifetimes = collector.AllEvents.Select(e => e switch
        {
            RxSetAdd<NamedItem> a => a.Lifetime,
            RxSetUpdate<NamedItem> u => u.Lifetime,
            _ => null
        }).ToArray();

        Assert.All(lifetimes, l => Assert.Same(lifetimes[0], l));
    }

    [Fact]
    public void SourceCompletionEmitsDelete()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        subject.OnNext(new NamedItem(1, "hello"));
        subject.OnCompleted();

        Assert.Equal(2, collector.Batches.Count);
        Assert.IsType<RxSetDelete<NamedItem>>(collector.Batches[1][0]);
    }

    [Fact]
    public void ChangesStreamStaysOpenAfterSourceCompletion()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        subject.OnNext(new NamedItem(1, "hello"));
        subject.OnCompleted();

        Assert.False(collector.Completed);
    }

    [Fact]
    public void SourceErrorEmitsDeleteThenErrors()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        subject.OnNext(new NamedItem(1, "hello"));
        subject.OnError(new Exception("test error"));

        // Should have Delete before error
        var delete = collector.AllEvents.OfType<RxSetDelete<NamedItem>>().SingleOrDefault();
        Assert.NotNull(delete);
        Assert.NotNull(collector.Error);
        Assert.Equal("test error", collector.Error!.Message);
    }

    [Fact]
    public void SourceErrorBeforeAnyValueJustErrors()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        subject.OnError(new Exception("test error"));

        // No lifetime was active, so no Delete needed
        Assert.Empty(collector.AllEvents.OfType<RxSetDelete<NamedItem>>());
        Assert.NotNull(collector.Error);
    }

    [Fact]
    public void NoEventsBeforeFirstValue()
    {
        var subject = new Subject<NamedItem>();
        var set = RxSelectSingleLifetime.Create(subject);
        using var collector = new ChangeCollector<NamedItem>(set);

        Assert.Empty(collector.Batches);
    }
}
