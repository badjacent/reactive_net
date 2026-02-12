namespace com.hollerson.reactivesets.tests;

/// <summary>
/// Subscribes to a reactive set and collects all batches of changes for assertions.
/// </summary>
public class ChangeCollector<T> : IDisposable where T : class
{
    private readonly IDisposable _subscription;
    private readonly List<IRxSetChange<T>[]> _batches = new();
    private Exception? _error;
    private bool _completed;

    public ChangeCollector(IReactiveSet<T> source)
    {
        _subscription = source.Changes.Subscribe(
            batch => _batches.Add(batch),
            ex => _error = ex,
            () => _completed = true);
    }

    public IReadOnlyList<IRxSetChange<T>[]> Batches => _batches;
    public Exception? Error => _error;
    public bool Completed => _completed;

    /// <summary>
    /// All events flattened across all batches.
    /// </summary>
    public IEnumerable<IRxSetChange<T>> AllEvents => _batches.SelectMany(b => b);

    public void Dispose() => _subscription.Dispose();
}
