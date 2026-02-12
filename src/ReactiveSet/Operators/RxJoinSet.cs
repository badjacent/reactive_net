using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

internal sealed class RxJoinSet<TLeft, TRight, TKey, TResult> : IReactiveSet<TResult>
    where TLeft : class
    where TRight : class
    where TKey : IEquatable<TKey>
    where TResult : class
{
    private readonly IReactiveSet<TLeft> _left;
    private readonly IReactiveSet<TRight> _right;
    private readonly Func<TLeft, TKey> _leftKeySelector;
    private readonly Func<TRight, TKey> _rightKeySelector;
    private readonly Func<TLeft, TRight, TResult> _projection;

    public RxJoinSet(
        IReactiveSet<TLeft> left,
        IReactiveSet<TRight> right,
        Func<TLeft, TKey> leftKeySelector,
        Func<TRight, TKey> rightKeySelector,
        Func<TLeft, TRight, TResult> projection)
    {
        _left = left;
        _right = right;
        _leftKeySelector = leftKeySelector;
        _rightKeySelector = rightKeySelector;
        _projection = projection;
    }

    public IObservable<IRxSetChange<TResult>[]> Changes =>
        Observable.Create<IRxSetChange<TResult>[]>(observer =>
        {
            // Left state: lifetime → (key, item)
            var leftByLifetime = new Dictionary<object, (TKey Key, TLeft Item)>();
            // Right state: lifetime → (key, item)
            var rightByLifetime = new Dictionary<object, (TKey Key, TRight Item)>();
            // Left lifetimes indexed by join key
            var leftByKey = new Dictionary<TKey, List<object>>(EqualityComparer<TKey>.Default);
            // Right lifetimes indexed by join key
            var rightByKey = new Dictionary<TKey, List<object>>(EqualityComparer<TKey>.Default);
            // Downstream lifetimes: (leftLifetime, rightLifetime) → downstreamLifetime
            var downstreamLifetimes = new Dictionary<(object, object), object>();

            object GetOrCreateDownstream(object leftLt, object rightLt)
            {
                var pair = (leftLt, rightLt);
                if (!downstreamLifetimes.TryGetValue(pair, out var lt))
                {
                    lt = new object();
                    downstreamLifetimes[pair] = lt;
                }
                return lt;
            }

            void ProcessLeftBatch(IRxSetChange<TLeft>[] batch)
            {
                var result = new List<IRxSetChange<TResult>>();

                foreach (var change in batch)
                {
                    switch (change)
                    {
                        case RxSetAdd<TLeft> add:
                        {
                            var key = _leftKeySelector(add.Item);
                            leftByLifetime[add.Lifetime] = (key, add.Item);
                            if (!leftByKey.TryGetValue(key, out var list))
                            {
                                list = new List<object>();
                                leftByKey[key] = list;
                            }
                            list.Add(add.Lifetime);

                            // Match against existing rights
                            if (rightByKey.TryGetValue(key, out var rightList))
                            {
                                foreach (var rightLt in rightList)
                                {
                                    var rightItem = rightByLifetime[rightLt].Item;
                                    var downLt = GetOrCreateDownstream(add.Lifetime, rightLt);
                                    result.Add(new RxSetAdd<TResult>(downLt, _projection(add.Item, rightItem)));
                                }
                            }
                            break;
                        }
                        case RxSetUpdate<TLeft> update:
                        {
                            var oldEntry = leftByLifetime[update.Lifetime];
                            var newKey = _leftKeySelector(update.Item);
                            leftByLifetime[update.Lifetime] = (newKey, update.Item);

                            if (EqualityComparer<TKey>.Default.Equals(oldEntry.Key, newKey))
                            {
                                // Key unchanged — update all downstream pairs
                                if (rightByKey.TryGetValue(newKey, out var rightList))
                                {
                                    foreach (var rightLt in rightList)
                                    {
                                        var rightItem = rightByLifetime[rightLt].Item;
                                        var downLt = downstreamLifetimes[(update.Lifetime, rightLt)];
                                        result.Add(new RxSetUpdate<TResult>(downLt, _projection(update.Item, rightItem)));
                                    }
                                }
                            }
                            else
                            {
                                // Key changed — delete old matches, add new matches
                                leftByKey[oldEntry.Key].Remove(update.Lifetime);
                                if (leftByKey[oldEntry.Key].Count == 0) leftByKey.Remove(oldEntry.Key);

                                if (!leftByKey.TryGetValue(newKey, out var newList))
                                {
                                    newList = new List<object>();
                                    leftByKey[newKey] = newList;
                                }
                                newList.Add(update.Lifetime);

                                // Delete old matches
                                if (rightByKey.TryGetValue(oldEntry.Key, out var oldRights))
                                {
                                    foreach (var rightLt in oldRights)
                                    {
                                        var pair = (update.Lifetime, rightLt);
                                        if (downstreamLifetimes.TryGetValue(pair, out var downLt))
                                        {
                                            result.Add(new RxSetDelete<TResult>(downLt));
                                            downstreamLifetimes.Remove(pair);
                                        }
                                    }
                                }

                                // Add new matches
                                if (rightByKey.TryGetValue(newKey, out var newRights))
                                {
                                    foreach (var rightLt in newRights)
                                    {
                                        var rightItem = rightByLifetime[rightLt].Item;
                                        var downLt = GetOrCreateDownstream(update.Lifetime, rightLt);
                                        result.Add(new RxSetAdd<TResult>(downLt, _projection(update.Item, rightItem)));
                                    }
                                }
                            }
                            break;
                        }
                        case RxSetDelete<TLeft> delete:
                        {
                            var entry = leftByLifetime[delete.Lifetime];
                            leftByLifetime.Remove(delete.Lifetime);
                            leftByKey[entry.Key].Remove(delete.Lifetime);
                            if (leftByKey[entry.Key].Count == 0) leftByKey.Remove(entry.Key);

                            // Delete all downstream pairs involving this left
                            if (rightByKey.TryGetValue(entry.Key, out var rightList))
                            {
                                foreach (var rightLt in rightList)
                                {
                                    var pair = (delete.Lifetime, rightLt);
                                    if (downstreamLifetimes.TryGetValue(pair, out var downLt))
                                    {
                                        result.Add(new RxSetDelete<TResult>(downLt));
                                        downstreamLifetimes.Remove(pair);
                                    }
                                }
                            }
                            break;
                        }
                    }
                }

                if (result.Count > 0)
                    observer.OnNext(result.ToArray());
            }

            void ProcessRightBatch(IRxSetChange<TRight>[] batch)
            {
                var result = new List<IRxSetChange<TResult>>();

                foreach (var change in batch)
                {
                    switch (change)
                    {
                        case RxSetAdd<TRight> add:
                        {
                            var key = _rightKeySelector(add.Item);
                            rightByLifetime[add.Lifetime] = (key, add.Item);
                            if (!rightByKey.TryGetValue(key, out var list))
                            {
                                list = new List<object>();
                                rightByKey[key] = list;
                            }
                            list.Add(add.Lifetime);

                            if (leftByKey.TryGetValue(key, out var leftList))
                            {
                                foreach (var leftLt in leftList)
                                {
                                    var leftItem = leftByLifetime[leftLt].Item;
                                    var downLt = GetOrCreateDownstream(leftLt, add.Lifetime);
                                    result.Add(new RxSetAdd<TResult>(downLt, _projection(leftItem, add.Item)));
                                }
                            }
                            break;
                        }
                        case RxSetUpdate<TRight> update:
                        {
                            var oldEntry = rightByLifetime[update.Lifetime];
                            var newKey = _rightKeySelector(update.Item);
                            rightByLifetime[update.Lifetime] = (newKey, update.Item);

                            if (EqualityComparer<TKey>.Default.Equals(oldEntry.Key, newKey))
                            {
                                if (leftByKey.TryGetValue(newKey, out var leftList))
                                {
                                    foreach (var leftLt in leftList)
                                    {
                                        var leftItem = leftByLifetime[leftLt].Item;
                                        var downLt = downstreamLifetimes[(leftLt, update.Lifetime)];
                                        result.Add(new RxSetUpdate<TResult>(downLt, _projection(leftItem, update.Item)));
                                    }
                                }
                            }
                            else
                            {
                                rightByKey[oldEntry.Key].Remove(update.Lifetime);
                                if (rightByKey[oldEntry.Key].Count == 0) rightByKey.Remove(oldEntry.Key);

                                if (!rightByKey.TryGetValue(newKey, out var newList))
                                {
                                    newList = new List<object>();
                                    rightByKey[newKey] = newList;
                                }
                                newList.Add(update.Lifetime);

                                if (leftByKey.TryGetValue(oldEntry.Key, out var oldLefts))
                                {
                                    foreach (var leftLt in oldLefts)
                                    {
                                        var pair = (leftLt, update.Lifetime);
                                        if (downstreamLifetimes.TryGetValue(pair, out var downLt))
                                        {
                                            result.Add(new RxSetDelete<TResult>(downLt));
                                            downstreamLifetimes.Remove(pair);
                                        }
                                    }
                                }

                                if (leftByKey.TryGetValue(newKey, out var newLefts))
                                {
                                    foreach (var leftLt in newLefts)
                                    {
                                        var leftItem = leftByLifetime[leftLt].Item;
                                        var downLt = GetOrCreateDownstream(leftLt, update.Lifetime);
                                        result.Add(new RxSetAdd<TResult>(downLt, _projection(leftItem, update.Item)));
                                    }
                                }
                            }
                            break;
                        }
                        case RxSetDelete<TRight> delete:
                        {
                            var entry = rightByLifetime[delete.Lifetime];
                            rightByLifetime.Remove(delete.Lifetime);
                            rightByKey[entry.Key].Remove(delete.Lifetime);
                            if (rightByKey[entry.Key].Count == 0) rightByKey.Remove(entry.Key);

                            if (leftByKey.TryGetValue(entry.Key, out var leftList))
                            {
                                foreach (var leftLt in leftList)
                                {
                                    var pair = (leftLt, delete.Lifetime);
                                    if (downstreamLifetimes.TryGetValue(pair, out var downLt))
                                    {
                                        result.Add(new RxSetDelete<TResult>(downLt));
                                        downstreamLifetimes.Remove(pair);
                                    }
                                }
                            }
                            break;
                        }
                    }
                }

                if (result.Count > 0)
                    observer.OnNext(result.ToArray());
            }

            var leftSub = _left.Changes.Subscribe(ProcessLeftBatch);
            var rightSub = _right.Changes.Subscribe(ProcessRightBatch);

            return new CompositeDisposable(leftSub, rightSub);
        });
}
