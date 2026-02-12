namespace com.hollerson.reactivesets;

public class MaterializedSet<T, TKey> : IDisposable
    where T : class
    where TKey : IEquatable<TKey>
{
    private readonly Func<T, TKey> _keySelector;
    private readonly Dictionary<object, (TKey Key, T Item)> _byLifetime = new();
    private readonly Dictionary<TKey, (object Lifetime, T Item)> _byKey;
    private readonly IDisposable _subscription;

    public MaterializedSet(
        IReactiveSet<T> source,
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
    {
        _keySelector = keySelector;
        _byKey = new Dictionary<TKey, (object, T)>(comparer ?? EqualityComparer<TKey>.Default);
        _subscription = source.Changes.Subscribe(ProcessBatch);
    }

    public int Count => _byKey.Count;

    public IReadOnlyCollection<T> Items => _byKey.Values.Select(v => v.Item).ToList().AsReadOnly();

    public T? TryGet(TKey key) => _byKey.TryGetValue(key, out var entry) ? entry.Item : null;

    public bool ContainsKey(TKey key) => _byKey.ContainsKey(key);

    public void Dispose() => _subscription.Dispose();

    private void ProcessBatch(IRxSetChange<T>[] batch)
    {
        foreach (var change in batch)
        {
            switch (change)
            {
                case RxSetAdd<T> add:
                {
                    var key = _keySelector(add.Item);
                    _byLifetime[add.Lifetime] = (key, add.Item);
                    _byKey[key] = (add.Lifetime, add.Item);
                    break;
                }
                case RxSetUpdate<T> update:
                {
                    var key = _keySelector(update.Item);
                    _byLifetime[update.Lifetime] = (key, update.Item);
                    _byKey[key] = (update.Lifetime, update.Item);
                    break;
                }
                case RxSetDelete<T> delete:
                {
                    if (_byLifetime.TryGetValue(delete.Lifetime, out var entry))
                    {
                        _byLifetime.Remove(delete.Lifetime);
                        _byKey.Remove(entry.Key);
                    }
                    break;
                }
            }
        }
    }
}
