namespace com.hollerson.reactivesets;

public interface IReactiveSet<T> where T : class
{
    IObservable<IRxSetChange<T>[]> Changes { get; }
}
