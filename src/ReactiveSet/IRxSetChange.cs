namespace com.hollerson.reactivesets;

public interface IRxSetChange<out T> where T : class { }

public sealed record RxSetAdd<T>(object Lifetime, T Item) : IRxSetChange<T>
    where T : class;

public sealed record RxSetUpdate<T>(object Lifetime, T Item) : IRxSetChange<T>
    where T : class;

public sealed record RxSetDelete<T>(object Lifetime) : IRxSetChange<T>
    where T : class;
