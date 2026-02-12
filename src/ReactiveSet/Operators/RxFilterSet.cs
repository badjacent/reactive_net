using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

internal sealed class RxFilterSet<T> : IReactiveSet<T> where T : class
{
    private readonly IReactiveSet<T> _source;
    private readonly Func<T, bool> _predicate;

    public RxFilterSet(IReactiveSet<T> source, Func<T, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
    }

    public IObservable<IRxSetChange<T>[]> Changes =>
        Observable.Create<IRxSetChange<T>[]>(observer =>
        {
            var admitted = new HashSet<object>();

            return _source.Changes.Subscribe(
                onNext: batch =>
                {
                    var result = new List<IRxSetChange<T>>();

                    foreach (var change in batch)
                    {
                        switch (change)
                        {
                            case RxSetAdd<T> add:
                            {
                                if (_predicate(add.Item))
                                {
                                    admitted.Add(add.Lifetime);
                                    result.Add(add);
                                }
                                break;
                            }
                            case RxSetUpdate<T> update:
                            {
                                var wasAdmitted = admitted.Contains(update.Lifetime);
                                var nowPasses = _predicate(update.Item);

                                if (wasAdmitted && nowPasses)
                                {
                                    result.Add(update);
                                }
                                else if (wasAdmitted && !nowPasses)
                                {
                                    admitted.Remove(update.Lifetime);
                                    result.Add(new RxSetDelete<T>(update.Lifetime));
                                }
                                else if (!wasAdmitted && nowPasses)
                                {
                                    admitted.Add(update.Lifetime);
                                    result.Add(new RxSetAdd<T>(update.Lifetime, update.Item));
                                }
                                // !wasAdmitted && !nowPasses → nothing
                                break;
                            }
                            case RxSetDelete<T> delete:
                            {
                                if (admitted.Remove(delete.Lifetime))
                                {
                                    result.Add(delete);
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
