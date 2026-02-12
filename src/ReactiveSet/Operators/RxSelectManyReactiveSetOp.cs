using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

internal sealed class RxSelectManyReactiveSetOp<T, U> : IReactiveSet<U>
    where T : class
    where U : class
{
    private readonly IReactiveSet<T> _source;
    private readonly Func<T, IReactiveSet<U>> _selector;

    public RxSelectManyReactiveSetOp(IReactiveSet<T> source, Func<T, IReactiveSet<U>> selector)
    {
        _source = source;
        _selector = selector;
    }

    public IObservable<IRxSetChange<U>[]> Changes =>
        Observable.Create<IRxSetChange<U>[]>(observer =>
        {
            // Per upstream lifetime: (subscription, dictionary of child upstream lifetime → downstream lifetime)
            var children = new Dictionary<object, (IDisposable Subscription, Dictionary<object, object> LifetimeMap)>();

            void SubscribeToChild(object parentLifetime, IReactiveSet<U> childSet)
            {
                var lifetimeMap = new Dictionary<object, object>();
                var sub = childSet.Changes.Subscribe(
                    onNext: childBatch =>
                    {
                        var result = new List<IRxSetChange<U>>();
                        foreach (var change in childBatch)
                        {
                            switch (change)
                            {
                                case RxSetAdd<U> add:
                                {
                                    var downLt = new object();
                                    lifetimeMap[add.Lifetime] = downLt;
                                    result.Add(new RxSetAdd<U>(downLt, add.Item));
                                    break;
                                }
                                case RxSetUpdate<U> update:
                                {
                                    if (lifetimeMap.TryGetValue(update.Lifetime, out var downLt))
                                        result.Add(new RxSetUpdate<U>(downLt, update.Item));
                                    break;
                                }
                                case RxSetDelete<U> delete:
                                {
                                    if (lifetimeMap.TryGetValue(delete.Lifetime, out var downLt))
                                    {
                                        lifetimeMap.Remove(delete.Lifetime);
                                        result.Add(new RxSetDelete<U>(downLt));
                                    }
                                    break;
                                }
                            }
                        }
                        if (result.Count > 0)
                            observer.OnNext(result.ToArray());
                    });

                children[parentLifetime] = (sub, lifetimeMap);
            }

            void DeleteAllChildLifetimes(object parentLifetime, List<IRxSetChange<U>> result)
            {
                if (children.TryGetValue(parentLifetime, out var entry))
                {
                    entry.Subscription.Dispose();
                    foreach (var (_, downLt) in entry.LifetimeMap)
                        result.Add(new RxSetDelete<U>(downLt));
                    children.Remove(parentLifetime);
                }
            }

            var sourceSub = _source.Changes.Subscribe(
                onNext: batch =>
                {
                    var result = new List<IRxSetChange<U>>();

                    foreach (var change in batch)
                    {
                        switch (change)
                        {
                            case RxSetAdd<T> add:
                            {
                                var childSet = _selector(add.Item);
                                SubscribeToChild(add.Lifetime, childSet);
                                break;
                            }
                            case RxSetUpdate<T> update:
                            {
                                // Delete old child lifetimes
                                DeleteAllChildLifetimes(update.Lifetime, result);
                                // Subscribe to new child set
                                var newChildSet = _selector(update.Item);
                                SubscribeToChild(update.Lifetime, newChildSet);
                                break;
                            }
                            case RxSetDelete<T> delete:
                            {
                                DeleteAllChildLifetimes(delete.Lifetime, result);
                                break;
                            }
                        }
                    }

                    if (result.Count > 0)
                        observer.OnNext(result.ToArray());
                },
                onError: observer.OnError,
                onCompleted: observer.OnCompleted);

            return new CompositeDisposable(
                sourceSub,
                Disposable.Create(() =>
                {
                    foreach (var (_, (sub, _)) in children)
                        sub.Dispose();
                    children.Clear();
                }));
        });
}
