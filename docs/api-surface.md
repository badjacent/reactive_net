# API Surface Specification

## Namespace

All types live under `com.hollerson.reactivesets`.

---

## Change Notification Types

### `SetChange<T>`

Sealed record hierarchy representing a single change to a set.

```csharp
public abstract record SetChange<T>;
public sealed record Add<T>(T Item) : SetChange<T>;
public sealed record Remove<T>(T Item) : SetChange<T>;
public sealed record Clear<T> : SetChange<T>;
```

---

## Core Interfaces

### `IReactiveSet<T>`

Read-only observable view of a set.

```csharp
public interface IReactiveSet<T> : IDisposable
{
    /// Stream of granular change notifications.
    IObservable<SetChange<T>> Changes { get; }

    /// Point-in-time snapshot of current membership.
    IReadOnlySet<T> CurrentValue { get; }

    /// Current element count.
    int Count { get; }
}
```

---

## Concrete Classes

### `ReactiveSet<T>`

Mutable source set. Implements `IReactiveSet<T>` and provides mutation methods.

```csharp
public class ReactiveSet<T> : IReactiveSet<T>
{
    public ReactiveSet(IEqualityComparer<T>? comparer = null);

    // --- IReactiveSet<T> ---
    public IObservable<SetChange<T>> Changes { get; }
    public IReadOnlySet<T> CurrentValue { get; }
    public int Count { get; }

    // --- Mutation ---
    /// Returns true if the item was added (not already present).
    public bool Add(T item);

    /// Returns true if the item was removed (was present).
    public bool Remove(T item);

    /// Removes all elements, emits Clear.
    public void Clear();

    public void Dispose();
}
```

---

## Set Operations (Extension Methods)

All operations are extension methods on `IReactiveSet<T>`, returning a new `IReactiveSet<T>`.

```csharp
public static class ReactiveSetExtensions
{
    /// Elements present in either set.
    public static IReactiveSet<T> Union<T>(
        this IReactiveSet<T> left,
        IReactiveSet<T> right,
        IEqualityComparer<T>? comparer = null);

    /// Elements present in both sets.
    public static IReactiveSet<T> Intersect<T>(
        this IReactiveSet<T> left,
        IReactiveSet<T> right,
        IEqualityComparer<T>? comparer = null);

    /// Elements in left but not in right.
    public static IReactiveSet<T> Except<T>(
        this IReactiveSet<T> left,
        IReactiveSet<T> right,
        IEqualityComparer<T>? comparer = null);

    /// Elements in either set but not in both.
    public static IReactiveSet<T> SymmetricDifference<T>(
        this IReactiveSet<T> left,
        IReactiveSet<T> right,
        IEqualityComparer<T>? comparer = null);

    /// Elements matching the predicate.
    public static IReactiveSet<T> Where<T>(
        this IReactiveSet<T> source,
        Func<T, bool> predicate);

    /// Projected elements (must remain unique under the result comparer).
    public static IReactiveSet<TResult> Select<T, TResult>(
        this IReactiveSet<T> source,
        Func<T, TResult> selector,
        IEqualityComparer<TResult>? comparer = null);
}
```

---

## Custom Rx Operators

Extension methods on `IObservable<SetChange<T>>` for working directly with change streams.

```csharp
public static class SetChangeObservableExtensions
{
    /// Accumulate changes into a snapshot, emitting the full set after each change.
    public static IObservable<IReadOnlySet<T>> Aggregate<T>(
        this IObservable<SetChange<T>> changes,
        IEqualityComparer<T>? comparer = null);

    /// Buffer changes over a time window, emitting batched change lists.
    public static IObservable<IReadOnlyList<SetChange<T>>> BufferChanges<T>(
        this IObservable<SetChange<T>> changes,
        TimeSpan window,
        IScheduler? scheduler = null);

    /// Convert a plain observable sequence into Add notifications
    /// (useful for bridging non-set streams into the reactive-set world).
    public static IObservable<SetChange<T>> ToSetChanges<T>(
        this IObservable<T> source);
}
```

---

## Usage Examples

### Creating and observing a reactive set

```csharp
using com.hollerson.reactivesets;

var set = new ReactiveSet<int>();

set.Changes.Subscribe(change => Console.WriteLine(change));

set.Add(1);   // prints: Add { Item = 1 }
set.Add(2);   // prints: Add { Item = 2 }
set.Remove(1); // prints: Remove { Item = 1 }

Console.WriteLine(set.Count); // 1
```

### Composing set operations

```csharp
var admins = new ReactiveSet<string>();
var editors = new ReactiveSet<string>();

// People who are admins OR editors
IReactiveSet<string> staff = admins.Union(editors);

// People who are admins AND editors
IReactiveSet<string> superUsers = admins.Intersect(editors);

staff.Changes.Subscribe(c => Console.WriteLine($"Staff change: {c}"));

admins.Add("Alice");   // Staff change: Add { Item = Alice }
editors.Add("Alice");  // (no staff change — Alice already in union)
editors.Add("Bob");    // Staff change: Add { Item = Bob }
admins.Remove("Alice"); // (no staff change — Alice still in editors)
editors.Remove("Alice"); // Staff change: Remove { Item = Alice }
```

### Filtering and projection

```csharp
var numbers = new ReactiveSet<int>();

IReactiveSet<int> evens = numbers.Where(n => n % 2 == 0);
IReactiveSet<string> labels = evens.Select(n => $"Item-{n}");

labels.Changes.Subscribe(c => Console.WriteLine(c));

numbers.Add(1); // (no output — filtered out)
numbers.Add(2); // prints: Add { Item = Item-2 }
numbers.Add(4); // prints: Add { Item = Item-4 }
numbers.Remove(2); // prints: Remove { Item = Item-2 }
```

### Bridging with standard Rx

```csharp
IObservable<int> incoming = Observable.Interval(TimeSpan.FromSeconds(1))
    .Select(i => (int)i);

// Convert plain observable to set-change stream
IObservable<SetChange<int>> changes = incoming.ToSetChanges();

// Accumulate into snapshots
changes.Aggregate().Subscribe(snapshot =>
    Console.WriteLine($"Set now contains {snapshot.Count} items"));

// Buffer changes over 5-second windows
changes.BufferChanges(TimeSpan.FromSeconds(5)).Subscribe(batch =>
    Console.WriteLine($"Batch of {batch.Count} changes"));
```
