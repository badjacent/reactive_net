namespace com.hollerson.reactivesets.tests;

public class BatchingTests
{
    [Fact]
    public void ConstantSetEmitsSingleBatchForAllItems()
    {
        var items = Enumerable.Range(1, 100).Select(i => new NamedItem(i, $"item{i}")).ToArray();
        var set = new ConstantReactiveSet<NamedItem>(items);
        using var collector = new ChangeCollector<NamedItem>(set);

        Assert.Single(collector.Batches);
        Assert.Equal(100, collector.Batches[0].Length);
    }

    [Fact]
    public void MutableSetEmitsOneBatchPerMutation()
    {
        var set = new MutableReactiveSet<NamedItem, int>(x => x.Id);
        using var collector = new ChangeCollector<NamedItem>(set);

        set.Add(new NamedItem(1, "a"));
        set.Add(new NamedItem(2, "b"));
        set.Add(new NamedItem(3, "c"));

        Assert.Equal(3, collector.Batches.Count);
        Assert.All(collector.Batches, b => Assert.Single(b));
    }

    [Fact]
    public void OperatorEmitsAtMostOneBatchPerUpstreamBatch()
    {
        var items = new[] { new NamedItem(1, "a"), new NamedItem(2, "b"), new NamedItem(3, "c") };
        var source = new ConstantReactiveSet<NamedItem>(items);
        var mapped = source.RxMap(x => new NamedItem(x.Id, x.Value.ToUpper()));
        using var collector = new ChangeCollector<NamedItem>(mapped);

        // ConstantReactiveSet emits one batch → RxMap should emit one batch
        Assert.Single(collector.Batches);
        Assert.Equal(3, collector.Batches[0].Length);
    }

    [Fact]
    public void FilterMayProduceSmallerBatch()
    {
        var items = new[]
        {
            new TestUser(1, "Alice", "Eng"),
            new TestUser(2, "Bob", "Sales"),
            new TestUser(3, "Carol", "Eng"),
        };
        var source = new ConstantReactiveSet<TestUser>(items);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        // Source emits 3 items in one batch, but only 2 pass filter
        Assert.Single(collector.Batches);
        Assert.Equal(2, collector.Batches[0].Length);
    }

    [Fact]
    public void FilterEmitsNoBatchWhenNothingPasses()
    {
        var items = new[] { new TestUser(1, "Alice", "Sales") };
        var source = new ConstantReactiveSet<TestUser>(items);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        // Nothing passes → no downstream batch
        Assert.Empty(collector.Batches);
    }
}
