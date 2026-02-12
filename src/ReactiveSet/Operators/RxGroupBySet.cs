using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace com.hollerson.reactivesets;

internal sealed class RxGroupBySet<T, TKey> : IReactiveSet<IReactiveSet<T>>
    where T : class
    where TKey : IEquatable<TKey>
{
    private readonly IReactiveSet<T> _source;
    private readonly Func<T, TKey> _keySelector;

    public RxGroupBySet(IReactiveSet<T> source, Func<T, TKey> keySelector)
    {
        _source = source;
        _keySelector = keySelector;
    }

    public IObservable<IRxSetChange<IReactiveSet<T>>[]> Changes =>
        Observable.Create<IRxSetChange<IReactiveSet<T>>[]>(observer =>
        {
            // Group key → (group lifetime, MutableGroupSet, member count)
            var groups = new Dictionary<TKey, (object Lifetime, GroupReactiveSet<T> Set)>(
                EqualityComparer<TKey>.Default);
            // Upstream lifetime → group key
            var memberGroup = new Dictionary<object, TKey>();

            return _source.Changes.Subscribe(
                onNext: batch =>
                {
                    var result = new List<IRxSetChange<IReactiveSet<T>>>();

                    foreach (var change in batch)
                    {
                        switch (change)
                        {
                            case RxSetAdd<T> add:
                            {
                                var key = _keySelector(add.Item);
                                memberGroup[add.Lifetime] = key;

                                if (!groups.TryGetValue(key, out var group))
                                {
                                    group = (new object(), new GroupReactiveSet<T>());
                                    groups[key] = group;
                                    result.Add(new RxSetAdd<IReactiveSet<T>>(group.Lifetime, group.Set));
                                }

                                group.Set.EmitAdd(add.Lifetime, add.Item);
                                break;
                            }
                            case RxSetUpdate<T> update:
                            {
                                var oldKey = memberGroup[update.Lifetime];
                                var newKey = _keySelector(update.Item);

                                if (EqualityComparer<TKey>.Default.Equals(oldKey, newKey))
                                {
                                    // Same group — emit update into group
                                    groups[oldKey].Set.EmitUpdate(update.Lifetime, update.Item);
                                }
                                else
                                {
                                    // Key changed — move between groups
                                    memberGroup[update.Lifetime] = newKey;

                                    // Remove from old group
                                    var oldGroup = groups[oldKey];
                                    oldGroup.Set.EmitDelete(update.Lifetime);
                                    if (oldGroup.Set.Count == 0)
                                    {
                                        groups.Remove(oldKey);
                                        result.Add(new RxSetDelete<IReactiveSet<T>>(oldGroup.Lifetime));
                                    }

                                    // Add to new group
                                    if (!groups.TryGetValue(newKey, out var newGroup))
                                    {
                                        newGroup = (new object(), new GroupReactiveSet<T>());
                                        groups[newKey] = newGroup;
                                        result.Add(new RxSetAdd<IReactiveSet<T>>(newGroup.Lifetime, newGroup.Set));
                                    }
                                    newGroup.Set.EmitAdd(update.Lifetime, update.Item);
                                }
                                break;
                            }
                            case RxSetDelete<T> delete:
                            {
                                if (memberGroup.TryGetValue(delete.Lifetime, out var key))
                                {
                                    memberGroup.Remove(delete.Lifetime);
                                    var group = groups[key];
                                    group.Set.EmitDelete(delete.Lifetime);
                                    if (group.Set.Count == 0)
                                    {
                                        groups.Remove(key);
                                        result.Add(new RxSetDelete<IReactiveSet<T>>(group.Lifetime));
                                    }
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

/// <summary>
/// A reactive set used internally by RxGroupBy that supports imperative emission
/// and replays current state to new subscribers.
/// </summary>
internal sealed class GroupReactiveSet<T> : IReactiveSet<T> where T : class
{
    private readonly Subject<IRxSetChange<T>[]> _subject = new();
    private readonly Dictionary<object, T> _state = new();

    public int Count => _state.Count;

    public IObservable<IRxSetChange<T>[]> Changes =>
        Observable.Create<IRxSetChange<T>[]>(observer =>
        {
            // Replay current state
            if (_state.Count > 0)
            {
                var replay = _state
                    .Select(kv => (IRxSetChange<T>)new RxSetAdd<T>(kv.Key, kv.Value))
                    .ToArray();
                observer.OnNext(replay);
            }
            return _subject.Subscribe(observer);
        });

    public void EmitAdd(object lifetime, T item)
    {
        _state[lifetime] = item;
        _subject.OnNext(new IRxSetChange<T>[] { new RxSetAdd<T>(lifetime, item) });
    }

    public void EmitUpdate(object lifetime, T item)
    {
        _state[lifetime] = item;
        _subject.OnNext(new IRxSetChange<T>[] { new RxSetUpdate<T>(lifetime, item) });
    }

    public void EmitDelete(object lifetime)
    {
        _state.Remove(lifetime);
        _subject.OnNext(new IRxSetChange<T>[] { new RxSetDelete<T>(lifetime) });
    }
}
