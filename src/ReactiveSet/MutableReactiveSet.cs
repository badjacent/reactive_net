using System.Reactive.Subjects;

namespace com.hollerson.reactivesets;

public class MutableReactiveSet<T, TKey> : IReactiveSet<T>
    where T : class
    where TKey : IEquatable<TKey>
{
    private readonly Func<T, TKey> _keySelector;
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly Subject<IRxSetChange<T>[]> _subject = new();
    private readonly Dictionary<TKey, (object Lifetime, T Item)> _state;

    public MutableReactiveSet(
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
    {
        _keySelector = keySelector;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _state = new Dictionary<TKey, (object, T)>(_comparer);
    }

    public IObservable<IRxSetChange<T>[]> Changes =>
        System.Reactive.Linq.Observable.Create<IRxSetChange<T>[]>(observer =>
        {
            // Replay current state
            if (_state.Count > 0)
            {
                var replay = _state.Values
                    .Select(entry => (IRxSetChange<T>)new RxSetAdd<T>(entry.Lifetime, entry.Item))
                    .ToArray();
                observer.OnNext(replay);
            }

            return _subject.Subscribe(observer);
        });

    public void Add(T item)
    {
        var key = _keySelector(item);
        if (_state.ContainsKey(key))
            throw new InvalidOperationException($"An active lifetime already exists for key '{key}'.");

        var lifetime = new object();
        _state[key] = (lifetime, item);
        _subject.OnNext(new IRxSetChange<T>[] { new RxSetAdd<T>(lifetime, item) });
    }

    public void Update(T item)
    {
        var key = _keySelector(item);
        if (!_state.TryGetValue(key, out var entry))
            throw new InvalidOperationException($"No active lifetime exists for key '{key}'.");

        _state[key] = (entry.Lifetime, item);
        _subject.OnNext(new IRxSetChange<T>[] { new RxSetUpdate<T>(entry.Lifetime, item) });
    }

    public void Delete(TKey key)
    {
        if (!_state.TryGetValue(key, out var entry))
            throw new InvalidOperationException($"No active lifetime exists for key '{key}'.");

        _state.Remove(key);
        _subject.OnNext(new IRxSetChange<T>[] { new RxSetDelete<T>(entry.Lifetime) });
    }
}
