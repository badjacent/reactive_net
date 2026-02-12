using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

internal sealed class RxLeftJoinSet<TLeft, TRight, TKey, TResult> : IReactiveSet<TResult>
    where TLeft : class
    where TRight : class
    where TKey : IEquatable<TKey>
    where TResult : class
{
    private readonly IReactiveSet<TLeft> _left;
    private readonly IReactiveSet<TRight> _right;
    private readonly Func<TLeft, TKey> _leftKeySelector;
    private readonly Func<TRight, TKey> _rightKeySelector;
    private readonly Func<TLeft, TRight?, TResult> _projection;

    public RxLeftJoinSet(
        IReactiveSet<TLeft> left,
        IReactiveSet<TRight> right,
        Func<TLeft, TKey> leftKeySelector,
        Func<TRight, TKey> rightKeySelector,
        Func<TLeft, TRight?, TResult> projection)
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
            var leftByLifetime = new Dictionary<object, (TKey Key, TLeft Item)>();
            var rightByLifetime = new Dictionary<object, (TKey Key, TRight Item)>();
            var leftByKey = new Dictionary<TKey, List<object>>(EqualityComparer<TKey>.Default);
            var rightByKey = new Dictionary<TKey, List<object>>(EqualityComparer<TKey>.Default);

            // Downstream lifetimes: (leftLifetime, rightLifetime?) → downstreamLifetime
            // rightLifetime is null for null-right entries
            var downstreamLifetimes = new Dictionary<(object Left, object? Right), object>();

            object GetOrCreateDownstream(object leftLt, object? rightLt)
            {
                var pair = (leftLt, rightLt);
                if (!downstreamLifetimes.TryGetValue(pair, out var lt))
                {
                    lt = new object();
                    downstreamLifetimes[pair] = lt;
                }
                return lt;
            }

            List<object> GetRightsForKey(TKey key)
                => rightByKey.TryGetValue(key, out var list) ? list : new List<object>();

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
                            if (!leftByKey.TryGetValue(key, out var leftList))
                            {
                                leftList = new List<object>();
                                leftByKey[key] = leftList;
                            }
                            leftList.Add(add.Lifetime);

                            var rights = GetRightsForKey(key);
                            if (rights.Count == 0)
                            {
                                // No match — emit with null right
                                var downLt = GetOrCreateDownstream(add.Lifetime, null);
                                result.Add(new RxSetAdd<TResult>(downLt, _projection(add.Item, null)));
                            }
                            else
                            {
                                // One downstream per matching right
                                foreach (var rightLt in rights)
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
                                var rights = GetRightsForKey(newKey);
                                if (rights.Count == 0)
                                {
                                    var downLt = downstreamLifetimes[(update.Lifetime, null)];
                                    result.Add(new RxSetUpdate<TResult>(downLt, _projection(update.Item, null)));
                                }
                                else
                                {
                                    foreach (var rightLt in rights)
                                    {
                                        var rightItem = rightByLifetime[rightLt].Item;
                                        var downLt = downstreamLifetimes[(update.Lifetime, rightLt)];
                                        result.Add(new RxSetUpdate<TResult>(downLt, _projection(update.Item, rightItem)));
                                    }
                                }
                            }
                            else
                            {
                                // Key changed — delete old downstream, add new
                                leftByKey[oldEntry.Key].Remove(update.Lifetime);
                                if (leftByKey[oldEntry.Key].Count == 0) leftByKey.Remove(oldEntry.Key);

                                if (!leftByKey.TryGetValue(newKey, out var newLeftList))
                                {
                                    newLeftList = new List<object>();
                                    leftByKey[newKey] = newLeftList;
                                }
                                newLeftList.Add(update.Lifetime);

                                // Delete all old downstream lifetimes for this left
                                var keysToRemove = downstreamLifetimes.Keys
                                    .Where(k => ReferenceEquals(k.Left, update.Lifetime)).ToList();
                                foreach (var pair in keysToRemove)
                                {
                                    result.Add(new RxSetDelete<TResult>(downstreamLifetimes[pair]));
                                    downstreamLifetimes.Remove(pair);
                                }

                                // Add new downstream
                                var newRights = GetRightsForKey(newKey);
                                if (newRights.Count == 0)
                                {
                                    var downLt = GetOrCreateDownstream(update.Lifetime, null);
                                    result.Add(new RxSetAdd<TResult>(downLt, _projection(update.Item, null)));
                                }
                                else
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

                            var keysToRemove = downstreamLifetimes.Keys
                                .Where(k => ReferenceEquals(k.Left, delete.Lifetime)).ToList();
                            foreach (var pair in keysToRemove)
                            {
                                result.Add(new RxSetDelete<TResult>(downstreamLifetimes[pair]));
                                downstreamLifetimes.Remove(pair);
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
                            if (!rightByKey.TryGetValue(key, out var rightList))
                            {
                                rightList = new List<object>();
                                rightByKey[key] = rightList;
                            }
                            rightList.Add(add.Lifetime);

                            if (leftByKey.TryGetValue(key, out var leftList))
                            {
                                foreach (var leftLt in leftList)
                                {
                                    var leftItem = leftByLifetime[leftLt].Item;

                                    // Is this the first right for this left?
                                    // If so, reuse the null-right downstream and emit Update
                                    var nullPair = (leftLt, (object?)null);
                                    if (downstreamLifetimes.TryGetValue(nullPair, out var nullDownLt))
                                    {
                                        // Reassign the null-right downstream to this right
                                        downstreamLifetimes.Remove(nullPair);
                                        downstreamLifetimes[(leftLt, (object?)add.Lifetime)] = nullDownLt;
                                        result.Add(new RxSetUpdate<TResult>(nullDownLt, _projection(leftItem, add.Item)));
                                    }
                                    else
                                    {
                                        // Additional right — new downstream
                                        var downLt = GetOrCreateDownstream(leftLt, add.Lifetime);
                                        result.Add(new RxSetAdd<TResult>(downLt, _projection(leftItem, add.Item)));
                                    }
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
                                // Key unchanged — update all downstream pairs with this right
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
                                // Key changed
                                rightByKey[oldEntry.Key].Remove(update.Lifetime);
                                var oldRightCount = rightByKey.TryGetValue(oldEntry.Key, out var oldList) ? oldList.Count : 0;
                                if (oldRightCount == 0) rightByKey.Remove(oldEntry.Key);

                                if (!rightByKey.TryGetValue(newKey, out var newRightList))
                                {
                                    newRightList = new List<object>();
                                    rightByKey[newKey] = newRightList;
                                }
                                newRightList.Add(update.Lifetime);

                                // Delete old matches
                                if (leftByKey.TryGetValue(oldEntry.Key, out var oldLefts))
                                {
                                    foreach (var leftLt in oldLefts)
                                    {
                                        var pair = (leftLt, (object?)update.Lifetime);
                                        if (downstreamLifetimes.TryGetValue(pair, out var downLt))
                                        {
                                            result.Add(new RxSetDelete<TResult>(downLt));
                                            downstreamLifetimes.Remove(pair);
                                        }

                                        // If this left now has no rights, restore null-right
                                        if (oldRightCount == 0 && !downstreamLifetimes.Keys.Any(k => ReferenceEquals(k.Left, leftLt) && k.Right != null))
                                        {
                                            var leftItem = leftByLifetime[leftLt].Item;
                                            var nullDownLt = GetOrCreateDownstream(leftLt, null);
                                            result.Add(new RxSetAdd<TResult>(nullDownLt, _projection(leftItem, null)));
                                        }
                                    }
                                }

                                // Add new matches
                                if (leftByKey.TryGetValue(newKey, out var newLefts))
                                {
                                    foreach (var leftLt in newLefts)
                                    {
                                        var leftItem = leftByLifetime[leftLt].Item;

                                        // Remove null-right if this is the first right for this left at new key
                                        var nullPair = (leftLt, (object?)null);
                                        if (downstreamLifetimes.TryGetValue(nullPair, out var nullDownLt))
                                        {
                                            result.Add(new RxSetDelete<TResult>(nullDownLt));
                                            downstreamLifetimes.Remove(nullPair);
                                        }

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
                            var remainingRights = rightByKey.TryGetValue(entry.Key, out var rl) ? rl.Count : 0;
                            if (remainingRights == 0) rightByKey.Remove(entry.Key);

                            if (leftByKey.TryGetValue(entry.Key, out var leftList))
                            {
                                foreach (var leftLt in leftList)
                                {
                                    var pair = (leftLt, (object?)delete.Lifetime);
                                    if (downstreamLifetimes.TryGetValue(pair, out var downLt))
                                    {
                                        result.Add(new RxSetDelete<TResult>(downLt));
                                        downstreamLifetimes.Remove(pair);
                                    }

                                    // If this left now has no rights, restore null-right
                                    if (remainingRights == 0 && !downstreamLifetimes.Keys.Any(k => ReferenceEquals(k.Left, leftLt) && k.Right != null))
                                    {
                                        var leftItem = leftByLifetime[leftLt].Item;
                                        var nullDownLt = GetOrCreateDownstream(leftLt, null);
                                        result.Add(new RxSetAdd<TResult>(nullDownLt, _projection(leftItem, null)));
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
