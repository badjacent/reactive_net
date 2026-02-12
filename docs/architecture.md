# Architecture & Design

## Reactive Sets and Lifetimes: A Conceptual Overview

### Basic Reactive Sets

A **reactive set** is a single event stream. What makes it look like a set is the concept of a **lifetime**: each item in the set has a lifetime — it is added, optionally updated, and eventually removed. Multiple lifetimes are multiplexed through the one stream, interleaved in time. At any moment, the set's membership is the collection of currently active lifetimes.

A lifetime is a sequence of events for one logical item:

1. **Add(lifetime, item)** — the item enters the set. The lifetime begins.
2. **Update(lifetime, item)** (zero or more) — the item's state changes while it remains in the set.
3. **Delete(lifetime)** — the item leaves the set. The lifetime ends.

```
Stream over time:

  Add(1, alice)      → {(1, alice)}
  Add(2, bob)        → {(1, alice), (2, bob)}
  Update(1, beth)    → {(1, beth), (2, bob)}
  Delete(2)          → {(1, beth)}
  Add(3, carol)      → {(1, beth), (3, carol)}
```

This is one stream — an `IObservable<IRxSetChange<T>[]>` — with three lifetimes multiplexed through it.

### Operators - Some Examples

Operators manipulate reactive sets. Their inputs vary — an operator might transform one reactive set into another, bridge a plain `IObservable<T>` into the reactive set world, or combine two reactive sets. Here are some example operators - there are more in the detailed documentation.

#### RxSelectSingleLifetime

Bridges a plain `IObservable<T>` into a reactive set with a single lifetime. First value → Add, subsequent values → Update, completion → Delete. The result always contains zero or one items.

Example: a configuration service pushes `IObservable<AppConfig>` whenever the config changes. `RxSelectSingleLifetime` turns this into an `IReactiveSet<AppConfig>` with one lifetime that tracks the current config — so it can participate in joins and other set operations.

#### RxMap

Transforms each lifetime's value. 1:1 — every upstream lifetime produces exactly one downstream lifetime. Lifetime structure is unchanged; only the items are transformed.

Example: given a reactive set of `User` objects, `RxMap(user => user.DisplayName)` produces a reactive set of strings. When a user's display name changes (Update upstream), the corresponding string updates downstream.

#### RxJoin

Combines two reactive sets like a SQL inner join, but reactive. When a lifetime in the left set and a lifetime in the right set satisfy a join condition, a downstream lifetime exists carrying the combined value. The result updates incrementally as either side changes.

Example: a reactive set of `Order` objects and a reactive set of `Customer` objects. `RxJoin(order => order.CustomerId, customer => customer.Id)` produces a reactive set of `(Order, Customer)` pairs. When a customer updates their address, every joined order/customer pair for that customer updates downstream. This can then be combined with RxMap.

### Concrete Implementations

A reactive set is an interface — something has to produce the stream. Two basic implementations:

#### ConstantReactiveSet

A fixed collection. On subscription, it emits an Add for each item and never changes after that. Useful as a static input to operators — e.g., a fixed set of reference data that other sets join against.

#### MutableReactiveSet

A collection that can be changed imperatively. Calling its `Add`, `Update`, and `Delete` methods emits the corresponding events on the stream. This is the primary entry point for feeding external data into the reactive set world.

### Batched Events

The conceptual model presents events one at a time for clarity. In the actual API, the change stream is `IObservable<IRxSetChange<T>[]>` — each notification delivers an array of events. Experience shows that this batching is necessary for performance. The conceptual model still holds within a batch — events are ordered and obey the same preconditions.

See [requirements.md](requirements.md) for detailed specifications.
