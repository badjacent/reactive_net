using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

public static class RxSelectMultipleLifetimes
{
    public static IReactiveSet<T> Create<T>(IObservable<IObservable<T>> source) where T : class
        => new SelectMultipleLifetimesSet<T>(source);

    private sealed class SelectMultipleLifetimesSet<T> : IReactiveSet<T> where T : class
    {
        private readonly IObservable<IObservable<T>> _source;

        public SelectMultipleLifetimesSet(IObservable<IObservable<T>> source) => _source = source;

        public IObservable<IRxSetChange<T>[]> Changes =>
            Observable.Create<IRxSetChange<T>[]>(observer =>
            {
                var innerSubscriptions = new CompositeDisposable();
                var activeLifetimes = new HashSet<object>();
                var gate = new object();

                var outerSub = _source.Subscribe(
                    onNext: innerObservable =>
                    {
                        var lifetime = new object();
                        var started = false;

                        var innerSub = new SingleAssignmentDisposable();
                        innerSub.Disposable = innerObservable.Subscribe(
                            onNext: value =>
                            {
                                lock (gate)
                                {
                                    if (!started)
                                    {
                                        started = true;
                                        activeLifetimes.Add(lifetime);
                                        observer.OnNext(new IRxSetChange<T>[] { new RxSetAdd<T>(lifetime, value) });
                                    }
                                    else
                                    {
                                        observer.OnNext(new IRxSetChange<T>[] { new RxSetUpdate<T>(lifetime, value) });
                                    }
                                }
                            },
                            onError: _ =>
                            {
                                lock (gate)
                                {
                                    if (started && activeLifetimes.Remove(lifetime))
                                    {
                                        observer.OnNext(new IRxSetChange<T>[] { new RxSetDelete<T>(lifetime) });
                                    }
                                }
                            },
                            onCompleted: () =>
                            {
                                lock (gate)
                                {
                                    if (started && activeLifetimes.Remove(lifetime))
                                    {
                                        observer.OnNext(new IRxSetChange<T>[] { new RxSetDelete<T>(lifetime) });
                                    }
                                }
                            });

                        innerSubscriptions.Add(innerSub);
                    },
                    onError: ex =>
                    {
                        lock (gate)
                        {
                            // Delete all active lifetimes
                            var deletes = activeLifetimes
                                .Select(lt => (IRxSetChange<T>)new RxSetDelete<T>(lt))
                                .ToArray();
                            if (deletes.Length > 0)
                                observer.OnNext(deletes);
                            activeLifetimes.Clear();
                            innerSubscriptions.Dispose();
                            observer.OnError(ex);
                        }
                    },
                    onCompleted: () =>
                    {
                        // Outer completed — existing inners continue, stream stays open
                    });

                return new CompositeDisposable(outerSub, innerSubscriptions);
            });
    }
}
