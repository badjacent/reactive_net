using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

internal sealed class RxMapSet<T, U> : IReactiveSet<U>
    where T : class
    where U : class
{
    private readonly IReactiveSet<T> _source;
    private readonly Func<T, U> _selector;

    public RxMapSet(IReactiveSet<T> source, Func<T, U> selector)
    {
        _source = source;
        _selector = selector;
    }

    public IObservable<IRxSetChange<U>[]> Changes =>
        _source.Changes.Select(batch =>
        {
            var result = new IRxSetChange<U>[batch.Length];
            for (int i = 0; i < batch.Length; i++)
            {
                result[i] = batch[i] switch
                {
                    RxSetAdd<T> add => new RxSetAdd<U>(add.Lifetime, _selector(add.Item)),
                    RxSetUpdate<T> update => new RxSetUpdate<U>(update.Lifetime, _selector(update.Item)),
                    RxSetDelete<T> delete => new RxSetDelete<U>(delete.Lifetime),
                    _ => throw new InvalidOperationException("Unknown change type")
                };
            }
            return result;
        });
}
