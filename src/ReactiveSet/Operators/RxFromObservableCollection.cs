using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

public static class RxFromObservableCollection
{
    public static IReactiveSet<T> Create<T, TKey>(
        IObservable<IEnumerable<T>> source,
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where T : class
        where TKey : IEquatable<TKey>
        => new FromObservableCollectionSet<T, TKey>(source, keySelector, comparer);

    private sealed class FromObservableCollectionSet<T, TKey> : IReactiveSet<T>
        where T : class
        where TKey : IEquatable<TKey>
    {
        private readonly IObservable<IEnumerable<T>> _source;
        private readonly Func<T, TKey> _keySelector;
        private readonly IEqualityComparer<TKey> _comparer;

        public FromObservableCollectionSet(
            IObservable<IEnumerable<T>> source,
            Func<T, TKey> keySelector,
            IEqualityComparer<TKey>? comparer)
        {
            _source = source;
            _keySelector = keySelector;
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        public IObservable<IRxSetChange<T>[]> Changes =>
            Observable.Create<IRxSetChange<T>[]>(observer =>
            {
                var prev = new Dictionary<TKey, (object Lifetime, T Item)>(_comparer);

                return _source.Subscribe(
                    onNext: snapshot =>
                    {
                        var result = new List<IRxSetChange<T>>();
                        var newByKey = new Dictionary<TKey, T>(_comparer);

                        foreach (var item in snapshot)
                            newByKey[_keySelector(item)] = item;

                        // Deletes: in old but not new
                        foreach (var (key, (lt, _)) in prev)
                        {
                            if (!newByKey.ContainsKey(key))
                                result.Add(new RxSetDelete<T>(lt));
                        }

                        var next = new Dictionary<TKey, (object Lifetime, T Item)>(_comparer);

                        foreach (var (key, newItem) in newByKey)
                        {
                            if (prev.TryGetValue(key, out var oldEntry))
                            {
                                // Exists in both
                                if (!EqualityComparer<T>.Default.Equals(oldEntry.Item, newItem))
                                {
                                    result.Add(new RxSetUpdate<T>(oldEntry.Lifetime, newItem));
                                }
                                next[key] = (oldEntry.Lifetime, newItem);
                            }
                            else
                            {
                                // New key
                                var lt = new object();
                                next[key] = (lt, newItem);
                                result.Add(new RxSetAdd<T>(lt, newItem));
                            }
                        }

                        prev = next;

                        if (result.Count > 0)
                            observer.OnNext(result.ToArray());
                    },
                    onError: ex =>
                    {
                        // Delete all active lifetimes, then error
                        if (prev.Count > 0)
                        {
                            var deletes = prev.Values
                                .Select(e => (IRxSetChange<T>)new RxSetDelete<T>(e.Lifetime))
                                .ToArray();
                            observer.OnNext(deletes);
                        }
                        prev.Clear();
                        observer.OnError(ex);
                    },
                    onCompleted: () =>
                    {
                        // Delete all active lifetimes, stream stays open
                        if (prev.Count > 0)
                        {
                            var deletes = prev.Values
                                .Select(e => (IRxSetChange<T>)new RxSetDelete<T>(e.Lifetime))
                                .ToArray();
                            observer.OnNext(deletes);
                        }
                        prev.Clear();
                        // Do NOT call observer.OnCompleted()
                    });
            });
    }
}
