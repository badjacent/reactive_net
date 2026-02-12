using System.Reactive.Linq;

namespace com.hollerson.reactivesets;

public static class ReactiveSetExtensions
{
    public static IReactiveSet<U> RxMap<T, U>(
        this IReactiveSet<T> source,
        Func<T, U> selector)
        where T : class
        where U : class
        => new RxMapSet<T, U>(source, selector);

    public static IReactiveSet<T> RxFilter<T>(
        this IReactiveSet<T> source,
        Func<T, bool> predicate)
        where T : class
        => new RxFilterSet<T>(source, predicate);

    public static IReactiveSet<U> RxSelectMany<T, U>(
        this IReactiveSet<T> source,
        Func<T, IReactiveSet<U>> selector)
        where T : class
        where U : class
        => new RxSelectManyReactiveSetOp<T, U>(source, selector);

    public static IReactiveSet<U> RxSelectMany<T, U, UKey>(
        this IReactiveSet<T> source,
        Func<T, U[]> selector,
        Func<U, UKey> keySelector)
        where T : class
        where U : class
        where UKey : IEquatable<UKey>
        => new RxSelectManyArrayOp<T, U, UKey>(source, selector, keySelector);

    public static IReactiveSet<TResult> RxJoin<TLeft, TRight, TKey, TResult>(
        this IReactiveSet<TLeft> left,
        IReactiveSet<TRight> right,
        Func<TLeft, TKey> leftKeySelector,
        Func<TRight, TKey> rightKeySelector,
        Func<TLeft, TRight, TResult> projection)
        where TLeft : class
        where TRight : class
        where TKey : IEquatable<TKey>
        where TResult : class
        => new RxJoinSet<TLeft, TRight, TKey, TResult>(left, right, leftKeySelector, rightKeySelector, projection);

    public static IReactiveSet<TResult> RxLeftJoin<TLeft, TRight, TKey, TResult>(
        this IReactiveSet<TLeft> left,
        IReactiveSet<TRight> right,
        Func<TLeft, TKey> leftKeySelector,
        Func<TRight, TKey> rightKeySelector,
        Func<TLeft, TRight?, TResult> projection)
        where TLeft : class
        where TRight : class
        where TKey : IEquatable<TKey>
        where TResult : class
        => new RxLeftJoinSet<TLeft, TRight, TKey, TResult>(left, right, leftKeySelector, rightKeySelector, projection);

    public static IReactiveSet<IReactiveSet<T>> RxGroupBy<T, TKey>(
        this IReactiveSet<T> source,
        Func<T, TKey> keySelector)
        where T : class
        where TKey : IEquatable<TKey>
        => new RxGroupBySet<T, TKey>(source, keySelector);

    public static IObservable<T[]> RxSnapshot<T>(this IReactiveSet<T> source)
        where T : class
    {
        return source.Changes.Scan(
            new Dictionary<object, T>(),
            (state, batch) =>
            {
                foreach (var change in batch)
                {
                    switch (change)
                    {
                        case RxSetAdd<T> add:
                            state[add.Lifetime] = add.Item;
                            break;
                        case RxSetUpdate<T> update:
                            state[update.Lifetime] = update.Item;
                            break;
                        case RxSetDelete<T> delete:
                            state.Remove(delete.Lifetime);
                            break;
                    }
                }
                return state;
            })
            .Select(state => state.Values.ToArray());
    }

    public static IObservable<int> RxCount<T>(this IReactiveSet<T> source)
        where T : class
    {
        return source.Changes.Scan(0, (count, batch) =>
        {
            foreach (var change in batch)
            {
                switch (change)
                {
                    case RxSetAdd<T>:
                        count++;
                        break;
                    case RxSetDelete<T>:
                        count--;
                        break;
                }
            }
            return count;
        });
    }
}
