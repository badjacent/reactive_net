using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

internal sealed class RxSelectManyArrayOp<T, U, UKey> : IReactiveSet<U>
    where T : class
    where U : class
    where UKey : IEquatable<UKey>
{
    private readonly IReactiveSet<T> _source;
    private readonly Func<T, U[]> _selector;
    private readonly Func<U, UKey> _keySelector;

    public RxSelectManyArrayOp(IReactiveSet<T> source, Func<T, U[]> selector, Func<U, UKey> keySelector)
    {
        _source = source;
        _selector = selector;
        _keySelector = keySelector;
    }

    public IObservable<IRxSetChange<U>[]> Changes =>
        Observable.Create<IRxSetChange<U>[]>(observer =>
        {
            // Per upstream lifetime: dictionary of child key → (lifetime, item)
            var childState = new Dictionary<object, Dictionary<UKey, (object Lifetime, U Item)>>();

            return _source.Changes.Subscribe(
                onNext: batch =>
                {
                    var result = new List<IRxSetChange<U>>();

                    foreach (var change in batch)
                    {
                        switch (change)
                        {
                            case RxSetAdd<T> add:
                            {
                                var children = _selector(add.Item);
                                var dict = new Dictionary<UKey, (object, U)>(EqualityComparer<UKey>.Default);
                                foreach (var child in children)
                                {
                                    var key = _keySelector(child);
                                    var lt = new object();
                                    dict[key] = (lt, child);
                                    result.Add(new RxSetAdd<U>(lt, child));
                                }
                                childState[add.Lifetime] = dict;
                                break;
                            }
                            case RxSetUpdate<T> update:
                            {
                                var oldDict = childState[update.Lifetime];
                                var newChildren = _selector(update.Item);
                                var newDict = new Dictionary<UKey, (object, U)>(EqualityComparer<UKey>.Default);

                                var newByKey = new Dictionary<UKey, U>(EqualityComparer<UKey>.Default);
                                foreach (var child in newChildren)
                                    newByKey[_keySelector(child)] = child;

                                // Deletes: keys in old but not in new
                                foreach (var (key, (lt, _)) in oldDict)
                                {
                                    if (!newByKey.ContainsKey(key))
                                        result.Add(new RxSetDelete<U>(lt));
                                }

                                // Adds and Updates
                                foreach (var (key, newItem) in newByKey)
                                {
                                    if (oldDict.TryGetValue(key, out var oldEntry))
                                    {
                                        // Key exists in both
                                        if (!EqualityComparer<U>.Default.Equals(oldEntry.Item, newItem))
                                        {
                                            result.Add(new RxSetUpdate<U>(oldEntry.Lifetime, newItem));
                                        }
                                        newDict[key] = (oldEntry.Lifetime, newItem);
                                    }
                                    else
                                    {
                                        // New key
                                        var lt = new object();
                                        newDict[key] = (lt, newItem);
                                        result.Add(new RxSetAdd<U>(lt, newItem));
                                    }
                                }

                                childState[update.Lifetime] = newDict;
                                break;
                            }
                            case RxSetDelete<T> delete:
                            {
                                if (childState.TryGetValue(delete.Lifetime, out var dict))
                                {
                                    foreach (var (_, (lt, _)) in dict)
                                        result.Add(new RxSetDelete<U>(lt));
                                    childState.Remove(delete.Lifetime);
                                }
                                break;
                            }
                        }
                    }

                    if (result.Count > 0)
                        observer.OnNext(result.ToArray());
                },
                onError: observer.OnError,
                onCompleted: observer.OnCompleted);
        });
}
