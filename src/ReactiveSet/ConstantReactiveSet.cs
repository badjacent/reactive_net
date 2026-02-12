using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

public class ConstantReactiveSet<T> : IReactiveSet<T> where T : class
{
    private readonly IRxSetChange<T>[] _initialBatch;

    public ConstantReactiveSet(IEnumerable<T> items)
    {
        _initialBatch = items
            .Select(item => (IRxSetChange<T>)new RxSetAdd<T>(new object(), item))
            .ToArray();
    }

    public IObservable<IRxSetChange<T>[]> Changes =>
        Observable.Create<IRxSetChange<T>[]>(observer =>
        {
            observer.OnNext(_initialBatch);
            // Stream stays open — never completes
            return System.Reactive.Disposables.Disposable.Empty;
        });
}
