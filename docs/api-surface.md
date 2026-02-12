# API Surface

All types live under `com.hollerson.reactivesets`.

## Core Types

### `IRxSetChange<T>`

A single change event. Three variants: Add and Update carry a lifetime identifier and an item; Delete carries only the lifetime.

```csharp
public interface IRxSetChange<out T> where T : class { }

public sealed record RxSetAdd<T>(object Lifetime, T Item) : IRxSetChange<T>
    where T : class;

public sealed record RxSetUpdate<T>(object Lifetime, T Item) : IRxSetChange<T>
    where T : class;

public sealed record RxSetDelete<T>(object Lifetime) : IRxSetChange<T>
    where T : class;
```

### `IReactiveSet<T>`

The core interface. A reactive set exposes a single change stream delivering batches of events.

```csharp
public interface IReactiveSet<T> where T : class
{
    IObservable<IRxSetChange<T>[]> Changes { get; }
}
```

## Creating Reactive Sets

### From a fixed collection

```csharp
var departments = new ConstantReactiveSet<Department>(new[]
{
    new Department(1, "Engineering"),
    new Department(2, "Sales"),
});
// On subscription, emits Add for each item. Never changes.
```

### From imperative mutations

```csharp
var users = new MutableReactiveSet<User, int>(user => user.Id);

users.Add(new User(1, "Alice", "Engineering"));
users.Update(new User(1, "Alice", "Sales"));
users.Delete(1);
```

The key selector (`user => user.Id`) maps items to lifetimes. The key type (`int` here) requires `IEquatable<TKey>`. An optional `IEqualityComparer<TKey>` can be provided.

### From a plain observable

```csharp
IObservable<AppConfig> configStream = configService.GetConfigUpdates();

IReactiveSet<AppConfig> config = RxSelectSingleLifetime.Create(configStream);
// First value → Add, subsequent values → Update, completion → Delete.
// Always contains zero or one items.
```

### From a stream of observables

```csharp
IObservable<IObservable<StockPrice>> priceFeeds = exchange.GetPriceFeeds();

IReactiveSet<StockPrice> prices = RxSelectMultipleLifetimes.Create(priceFeeds);
// Each inner IObservable<StockPrice> becomes one lifetime.
// First value → Add, subsequent values → Update, inner completion → Delete.
// RxSelectSingleLifetime is the special case where the outer stream emits once.
```

### From snapshots of a collection

```csharp
IObservable<IEnumerable<User>> snapshots = pollingService.GetUsers();

IReactiveSet<User> users = RxFromObservableCollection.Create(snapshots, user => user.Id);
// Diffs each snapshot against the previous to produce Add/Update/Delete events.
```

## Transforming

### RxMap — project each item

```csharp
IReactiveSet<string> names = users.RxMap(user => user.DisplayName);
// 1:1 lifetimes. When a user's name changes (Update), the string updates downstream.
```

### RxFilter — keep items matching a predicate

```csharp
IReactiveSet<User> engineers = users.RxFilter(user => user.Department == "Engineering");
// A user who changes department from Engineering to Sales:
//   downstream sees a Delete (no longer matches).
// A user who changes department from Sales to Engineering:
//   downstream sees an Add (now matches).
```

### RxSelectMany — flatten nested sets

Each upstream item produces a child set; the result is the flattened union.

```csharp
// Each department has a reactive set of its members
IReactiveSet<User> allUsers = departments.RxSelectMany(dept => dept.Members);
// When a department is deleted, all its members disappear downstream.
// When a department is added, all its members appear.
```

Array overload for static projections:

```csharp
// Each order has a fixed list of line items
IReactiveSet<LineItem> allLineItems = orders.RxSelectMany(order => order.LineItems);
// On order Update, the new line items are diffed against the old.
```

## Combining

### RxJoin — inner join

```csharp
IReactiveSet<OrderDetail> orderDetails = orders.RxJoin(
    customers,
    order => order.CustomerId,
    customer => customer.Id,
    (order, customer) => new OrderDetail(order.Id, customer.Name, order.Total));
// A downstream lifetime exists only while both sides have a matching lifetime.
// When a customer updates their name, all their order details update downstream.
```

### RxLeftJoin — left join

```csharp
IReactiveSet<OrderView> orderViews = orders.RxLeftJoin(
    customers,
    order => order.CustomerId,
    customer => customer.Id,
    (order, customer) => new OrderView(order.Id, customer?.Name, order.Total));
// Every order always has a downstream lifetime.
// customer is null when no match exists; updates to non-null when a match appears.
```

## Grouping

### RxGroupBy — partition by key

```csharp
IReactiveSet<IReactiveSet<User>> byDepartment = users.RxGroupBy(user => user.Department);
// Each distinct department value produces a child reactive set.
// Users moving between departments leave one group and join another.
```

## Materializing

### MaterializedSet — synchronous queryable view

```csharp
var view = new MaterializedSet<User, int>(users, user => user.Id);

// Synchronous queries at any time:
int count = view.Count;
IReadOnlyCollection<User> all = view.Items;
User? alice = view.TryGet(1);
bool exists = view.ContainsKey(1);
```

Subscribes to the source reactive set, maintains an internal collection, and exposes it for synchronous reads. Implements `IDisposable` to tear down the subscription.

### RxSnapshot — full membership as an observable

```csharp
IObservable<User[]> allUsers = users.RxSnapshot();
// Emits the full current membership as an array after each batch of changes.
```

### RxCount — current count as an observable

```csharp
IObservable<int> userCount = users.RxCount();
// Emits the number of active lifetimes after each batch.
```

## Composing Operators

Operators return `IReactiveSet<T>`, so they chain naturally:

```csharp
var activeEngineerOrders = orders
    .RxJoin(customers, o => o.CustomerId, c => c.Id,
        (order, customer) => new { order, customer })
    .RxFilter(x => x.customer.Department == "Engineering")
    .RxFilter(x => x.order.Status == OrderStatus.Active)
    .RxMap(x => new OrderSummary(x.order.Id, x.customer.Name, x.order.Total));

activeEngineerOrders.RxCount().Subscribe(count =>
    Console.WriteLine($"Active engineering orders: {count}"));
```
