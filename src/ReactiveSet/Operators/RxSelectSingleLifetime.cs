using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

public static class RxSelectSingleLifetime
{
    public static IReactiveSet<T> Create<T>(IObservable<T> source) where T : class
        => new SelectSingleLifetimeSet<T>(source);

    private sealed class SelectSingleLifetimeSet<T> : IReactiveSet<T> where T : class
    {
        private readonly IObservable<T> _source;

        public SelectSingleLifetimeSet(IObservable<T> source) => _source = source;

        public IObservable<IRxSetChange<T>[]> Changes =>
            Observable.Create<IRxSetChange<T>[]>(observer =>
            {
                var lifetime = new object();
                var started = false;

                return _source.Subscribe(
                    onNext: value =>
                    {
                        if (!started)
                        {
                            started = true;
                            observer.OnNext(new IRxSetChange<T>[] { new RxSetAdd<T>(lifetime, value) });
                        }
                        else
                        {
                            observer.OnNext(new IRxSetChange<T>[] { new RxSetUpdate<T>(lifetime, value) });
                        }
                    },
                    onError: ex =>
                    {
                        if (started)
                        {
                            observer.OnNext(new IRxSetChange<T>[] { new RxSetDelete<T>(lifetime) });
                        }
                        observer.OnError(ex);
                    },
                    onCompleted: () =>
                    {
                        if (started)
                        {
                            observer.OnNext(new IRxSetChange<T>[] { new RxSetDelete<T>(lifetime) });
                        }
                        // Stream stays open — do NOT call observer.OnCompleted()
                    });
            });
    }
}
