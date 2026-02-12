namespace com.hollerson.reactivesets.tests;

public class RxFilterTests
{
    [Fact]
    public void AdmitsItemsMatchingPredicate()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Eng"));

        var add = Assert.IsType<RxSetAdd<TestUser>>(collector.Batches.Last()[0]);
        Assert.Equal("Alice", add.Item.Name);
    }

    [Fact]
    public void RejectsItemsNotMatchingPredicate()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Sales"));

        // No downstream events for non-matching adds
        Assert.Empty(collector.AllEvents.OfType<RxSetAdd<TestUser>>());
    }

    [Fact]
    public void UpdateThatNowPassesEmitsAdd()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Sales"));      // fails predicate
        source.Update(new TestUser(1, "Alice", "Eng"));      // now passes

        var add = collector.AllEvents.OfType<RxSetAdd<TestUser>>().SingleOrDefault();
        Assert.NotNull(add);
        Assert.Equal("Eng", add!.Item.Department);
    }

    [Fact]
    public void UpdateThatNowFailsEmitsDelete()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Eng"));         // passes
        source.Update(new TestUser(1, "Alice", "Sales"));    // now fails

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Single(deletes);
    }

    [Fact]
    public void UpdateThatStillPassesEmitsUpdate()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Update(new TestUser(1, "Bob", "Eng")); // still Eng

        var update = Assert.IsType<RxSetUpdate<TestUser>>(collector.Batches.Last()[0]);
        Assert.Equal("Bob", update.Item.Name);
    }

    [Fact]
    public void DeleteOfAdmittedItemEmitsDelete()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Delete(1);

        var deletes = collector.AllEvents.OfType<RxSetDelete<TestUser>>().ToArray();
        Assert.Single(deletes);
    }

    [Fact]
    public void DeleteOfNotAdmittedItemEmitsNothing()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Sales"));
        source.Delete(1);

        Assert.Empty(collector.AllEvents.OfType<RxSetDelete<TestUser>>());
    }

    [Fact]
    public void MultipleItemsFilteredIndependently()
    {
        var source = new MutableReactiveSet<TestUser, int>(u => u.Id);
        var filtered = source.RxFilter(u => u.Department == "Eng");
        using var collector = new ChangeCollector<TestUser>(filtered);

        source.Add(new TestUser(1, "Alice", "Eng"));
        source.Add(new TestUser(2, "Bob", "Sales"));
        source.Add(new TestUser(3, "Carol", "Eng"));

        var adds = collector.AllEvents.OfType<RxSetAdd<TestUser>>().ToArray();
        Assert.Equal(2, adds.Length);
        Assert.Equal("Alice", adds[0].Item.Name);
        Assert.Equal("Carol", adds[1].Item.Name);
    }
}
