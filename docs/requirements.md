# Detailed Requirements

See [architecture.md](architecture.md) for the conceptual overview, [api-surface.md](api-surface.md) for the human-readable API guide.

## IRxSetChange&lt;T&gt;

Each event in the stream is an `IRxSetChange<out T>` (covariant in `T`). The three concrete variants — `RxSetAdd<T>`, `RxSetUpdate<T>`, `RxSetDelete<T>` — are sealed records. This section covers their preconditions and guarantees.

### Preconditions

- **Add** — there must not already be an active lifetime for this identity. Emitting Add for a lifetime that is already active is an error.
- **Update** — there must be an active lifetime for this identity. The value does not need to differ from the current value; redundant updates are permitted.
- **Delete** — there must be an active lifetime for this identity.

Violating a precondition is a programming error in the source or operator that produced the event. The library throws an `InvalidOperationException` on precondition violations. It does not silently ignore invalid events or error the stream.

### Ordering

Within a single reactive set, events are **sequentially consistent** — subscribers see events in the order they were applied. There is no interleaving of a single set's events across threads (the stream is serialized).

### No Clear Event

There is no bulk "Clear" event. Clearing a set emits individual Delete events for each active lifetime. This keeps downstream operators simple — they only need to handle three event types.

### Batching

The change stream is `IObservable<IRxSetChange<T>[]>` — each notification delivers an array (batch) of events. Within a batch, events are ordered and obey the same preconditions as individual events.

Operators receive an upstream batch, process all events in order, and emit a single downstream batch containing all resulting events. An operator never emits partial batches or multiple batches for a single upstream batch. If processing an upstream batch produces no downstream events, no downstream notification is emitted.

## IReactiveSet&lt;T&gt;

```csharp
public interface IReactiveSet<T> where T : class
{
    IObservable<IRxSetChange<T>[]> Changes { get; }
}
```

- `T` is constrained to reference types (`where T : class`).
- `Changes` exposes the batched change event stream.
- `IReactiveSet<T>` does not expose mutation methods — those belong to concrete implementations.
- `IReactiveSet<T>` does not implement `IDisposable`. Subscription lifetime is managed by the subscriber (via the `IDisposable` returned by `Subscribe`).
- **Subscription replay:** all reactive sets replay their current state on subscription. A new subscriber receives an initial batch of `Add` events representing all currently active lifetimes, then receives subsequent changes as they occur. All subscribers see the same logical set state.

## Concrete Implementations

### ConstantReactiveSet&lt;T&gt;

```csharp
public class ConstantReactiveSet<T> : IReactiveSet<T> where T : class
{
    public ConstantReactiveSet(IEnumerable<T> items);
    public IObservable<IRxSetChange<T>[]> Changes { get; }
}
```

**Behavior:**
- On subscription, emits a single batch containing an `Add` event for each item.
- Never emits Update or Delete events.
- The stream remains open (does not complete) after the initial batch.
- Each item is assigned a unique lifetime identifier at construction.

### MutableReactiveSet&lt;T, TKey&gt;

```csharp
public class MutableReactiveSet<T, TKey> : IReactiveSet<T>
    where T : class
    where TKey : IEquatable<TKey>
{
    public MutableReactiveSet(
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null);

    public IObservable<IRxSetChange<T>[]> Changes { get; }

    public void Add(T item);
    public void Update(T item);
    public void Delete(TKey key);
}
```

**Behavior:**
- The key selector extracts a `TKey` from each item. Keys are compared using the provided `IEqualityComparer<TKey>`, defaulting to `EqualityComparer<TKey>.Default`.
- `Add(item)` — extracts the key, verifies no active lifetime for that key, emits `Add`. Throws `InvalidOperationException` if the key is already active.
- `Update(item)` — extracts the key, verifies an active lifetime exists, emits `Update`. Throws `InvalidOperationException` if the key is not active.
- `Delete(key)` — verifies an active lifetime exists for the key, emits `Delete`. Throws `InvalidOperationException` if the key is not active.
- Each mutation emits a batch containing a single event.
- On subscription, emits a single batch containing an `Add` event for each currently active lifetime, replaying the current state. Subsequent mutations emit as normal.
- The key type does not appear in the `IReactiveSet<T>` interface — it is an implementation detail of MutableReactiveSet.

## Relationship to System.Reactive

The library is a **consumer** of System.Reactive, not a fork.

```
System.Reactive primitives          This library
─────────────────────────────       ──────────────────────────────
IObservable<T>                  →   change event stream per set
IObserver<T>                    →   subscribers to change events
IScheduler                      →   dedicated processing thread
Observable extension methods    →   reactive set operators
```

Change event streams are plain `IObservable<IRxSetChange<T>[]>` values — they compose with existing Rx operators (`ObserveOn`, `Merge`, etc.) without any special adapters. However, applying Rx operators that reorder or filter individual events (like `Where` or `Select`) directly on the change stream may violate lifetime preconditions. Use the library's own operators (RxFilter, RxMap, etc.) for safe transformations.

## Data Flow

```
 ┌───────────────────┐   ┌──────────────────┐
 │ MutableReactiveSet │   │ IObservable<T>   │
 │ ConstantReactiveSet│   │ (plain Rx stream)│
 └────────┬──────────┘   └────────┬─────────┘
          │                       │
          │ IReactiveSet<T>       │ RxSelectSingleLifetime
          │                       │ RxSelectMultipleLifetimes
          ▼                       ▼
 ┌────────────────────────────────────────┐
 │            Operators                   │
 │  RxMap, RxJoin, RxFilter, ...          │
 │  (each produces a new IReactiveSet)    │
 └────────────────┬───────────────────────┘
                  │ IObservable<IRxSetChange<U>[]>
                  ▼
 ┌────────────────────────────────────────┐
 │  MaterializedSet, RxSnapshot, RxCount  │
 │  (terminal consumers)                  │
 └────────────────────────────────────────┘
```

Concrete implementations and bridging operators produce reactive sets. Operators compose them — transforming, combining, filtering. At every stage the stream carries batches of `IRxSetChange<T>` events with lifetime structure. Terminal consumers (MaterializedSet, RxSnapshot, RxCount) materialize the stream into queryable or observable state.

## Operators

Operators produce reactive sets from various inputs — a single reactive set, two reactive sets, a plain `IObservable<T>`, etc. They translate upstream lifetime events into downstream lifetime events, tracking lifetimes structurally without needing key information from the items.

All operators process upstream batches atomically: receive one batch, produce one batch (or none if no downstream events result).

### RxSelectSingleLifetime

**Input:** `IObservable<T>`
**Output:** `IReactiveSet<T>`

Bridges a plain Rx stream into a reactive set with a single lifetime.

**Rules:**
- First value from source → `Add(1, value)`
- Subsequent values → `Update(1, value)`
- Source completes → `Delete(1)`. The Changes stream stays open.
- Source errors → `Delete(1)` if the lifetime was active, then error the Changes stream.

**Example:**

```
IObservable<string>:   --hello---world---!---|

Downstream:            Add(1, hello)      → {(1, hello)}
                       Update(1, world)   → {(1, world)}
                       Update(1, !)       → {(1, !)}
                       Delete(1)          → {}
```

**State:** tracks whether the lifetime has started (to distinguish first value from subsequent). No other state.

RxSelectSingleLifetime is a special case of RxSelectMultipleLifetimes where the outer stream emits exactly once.

### RxSelectMultipleLifetimes

**Input:** `IObservable<IObservable<T>>`
**Output:** `IReactiveSet<T>`

Each inner `IObservable<T>` emitted by the outer stream becomes one lifetime in the set.

**Rules (per inner observable):**
- First value → `Add(lifetime, value)`
- Subsequent values → `Update(lifetime, value)`
- Inner completion → `Delete(lifetime)`
- Inner error → `Delete(lifetime)` for that inner observable only

**Outer observable:**
- Outer completion → no new lifetimes can start. Existing inner subscriptions continue normally. The resulting reactive set's Changes stream stays open.
- Outer error → `Delete` for all active lifetimes from inner subscriptions, then error the Changes stream.

**Example:**

```
Outer emits inner1:    --a---b---|
Outer emits inner2:         --x---y---------|

Downstream:            Add(1, a)       → {(1, a)}
                       Update(1, b)    → {(1, b)}
                       Add(2, x)       → {(1, b), (2, x)}
                       Delete(1)       → {(2, x)}
                       Update(2, y)    → {(2, y)}
                       Delete(2)       → {}
```

**State:** tracks active inner subscriptions and whether each has started (to distinguish first value from subsequent). Each inner observable is assigned a unique lifetime identifier.

### RxMap

**Input:** `IReactiveSet<T>`, `Func<T, U>`
**Output:** `IReactiveSet<U>`

Transforms the value carried by each lifetime. 1:1 — every upstream lifetime produces exactly one downstream lifetime.

**Rules:**
- Upstream `Add(lifetime, item)` → `Add(lifetime, f(item))`
- Upstream `Update(lifetime, item)` → `Update(lifetime, f(item))`
- Upstream `Delete(lifetime)` → `Delete(lifetime)`

**Example:**

Given `f = user => user.Department`:

```
Upstream                          Downstream
────────                          ──────────
Add(1, alice{dept=Eng})       →   Add(1, Eng)         → {(1, Eng)}
Add(2, bob{dept=Sales})       →   Add(2, Sales)       → {(1, Eng), (2, Sales)}
Update(1, alice{dept=Sales})  →   Update(1, Sales)    → {(1, Sales), (2, Sales)}
Delete(2)                     →   Delete(2)           → {(1, Sales)}
```

**State:** none. Applies `f` to whatever item arrives and forwards the event with the same lifetime.

### RxJoin

**Input:** `IReactiveSet<TLeft>`, `IReactiveSet<TRight>`, `Func<TLeft, TKey>`, `Func<TRight, TKey>`, `Func<TLeft, TRight, TResult>`
**Output:** `IReactiveSet<TResult>`

Combines two reactive sets by matching lifetimes whose key-selector functions produce equal keys, then applies a projection function to produce the downstream value. This is a many-to-many join: each (left lifetime, right lifetime) pair with matching keys produces a separate downstream lifetime, mirroring SQL inner join semantics. For example, if 3 orders share `customerId=10` and customer 10 is active, there are 3 downstream lifetimes.

**Rules:**
- Left `Add` + matching right(s) already active → downstream `Add` for each matching right, with `projection(left, right)`
- Right `Add` + matching left(s) already active → downstream `Add` for each matching left, with `projection(left, right)`
- Either side `Update` while match active → downstream `Update` with `projection(newLeft, newRight)`
- Either side `Delete` while match active → downstream `Delete`
- Left `Update` that changes the join key → downstream `Delete` for old match (if any), downstream `Add` for new match (if any)
- Right `Update` that changes the join key → downstream `Delete` for old match (if any), downstream `Add` for new match (if any)

**Example:**

Given `leftKey = order => order.CustomerId`, `rightKey = customer => customer.Id`, `projection = (order, customer) => new OrderDetail(order.Id, customer.Name, order.Total)`:

```
Left (Orders)                   Right (Customers)           Downstream
─────────────                   ─────────────────           ──────────
Add(1, orderA{custId=10})                                   (no match)
                                Add(10, alice)              Add(OrderDetail(1, alice, 99))
Update(1, orderB{custId=10})                                Update(OrderDetail(1, alice, 50))
                                Update(10, beth)            Update(OrderDetail(1, beth, 50))
                                Delete(10)                  Delete()
```

**State:** must track all active lifetimes from both sides, indexed by join key, to detect matches when either side changes. Must also store current values from both sides to re-apply the projection on updates.

### RxLeftJoin

**Input:** `IReactiveSet<TLeft>`, `IReactiveSet<TRight>`, `Func<TLeft, TKey>`, `Func<TRight, TKey>`, `Func<TLeft, TRight?, TResult>`
**Output:** `IReactiveSet<TResult>`

Like RxJoin, but the left side is always present. This is a many-to-many join mirroring SQL left join semantics: each (left lifetime, right lifetime) pair with matching keys produces a downstream lifetime, plus each unmatched left lifetime produces a downstream lifetime with a null right. The projection function receives a nullable right value.

For example, if order A has `customerId=10` and there are 2 customers with key 10, there are 2 downstream lifetimes for order A. If there are 0 customers with key 10, there is 1 downstream lifetime with `right=null`.

**Rules:**
- Left `Add` with no matching right(s) → one downstream `Add` with `projection(left, null)`
- Left `Add` with matching right(s) already active → downstream `Add` for each matching right, with `projection(left, right)`
- Right `Add` that matches existing left(s) → for each matched left: if the left had a null-right downstream, `Update` it with `projection(left, right)`; if the left already had other right matches, emit an additional downstream `Add` with `projection(left, right)`
- Right `Delete` while matched → for each matched left: downstream `Delete` for the pair. If this was the left's last right match, emit downstream `Add` with `projection(left, null)` to restore the null-right lifetime.
- Left `Delete` → downstream `Delete` for all downstream lifetimes associated with this left (whether matched or null-right)
- Either side `Update` while matched → downstream `Update` with `projection(newLeft, newRight)` on affected downstream lifetimes
- Left `Update` that changes the join key → downstream `Delete` for old matches, then apply Left `Add` rules for the new key
- Right `Update` that changes the join key → downstream `Delete` for old matches (restoring null-right lifetimes for affected lefts if needed), then apply Right `Add` rules for the new key

**State:** must track all active lifetimes from both sides, indexed by join key. Must track downstream lifetimes per (left, right) pair and per unmatched left. Must store current values from both sides to re-apply the projection.

### RxFilter

**Input:** `IReactiveSet<T>`, `Func<T, bool>`
**Output:** `IReactiveSet<T>`

Filters lifetimes by predicate on the current item value.

**Rules:**
- Upstream `Add(lifetime, item)` where predicate passes → downstream `Add(lifetime, item)`
- Upstream `Add(lifetime, item)` where predicate fails → no downstream event
- Upstream `Update(lifetime, item)` where predicate now passes and previously failed → downstream `Add(lifetime, item)`
- Upstream `Update(lifetime, item)` where predicate still passes → downstream `Update(lifetime, item)`
- Upstream `Update(lifetime, item)` where predicate now fails and previously passed → downstream `Delete(lifetime)`
- Upstream `Delete(lifetime)` where lifetime was admitted → downstream `Delete(lifetime)`
- Upstream `Delete(lifetime)` where lifetime was not admitted → no downstream event

**State:** tracks which upstream lifetimes are currently admitted (passed the predicate).

### RxSelectMany (reactive set projection)

**Input:** `IReactiveSet<T>`, `Func<T, IReactiveSet<U>>`
**Output:** `IReactiveSet<U>`

Each upstream lifetime produces a child reactive set via the projection function. The output is the flattened union of all active child sets.

**Rules:**
- Upstream `Add(lifetime, item)` → call `projection(item)`, subscribe to the child set, forward all child events downstream with new lifetime identifiers.
- Upstream `Update(lifetime, item)` → dispose the old child subscription, call `projection(item)` to get a new child set, subscribe to it. The new child set emits an initial batch of `Add` events representing its current membership. Diff this initial batch (by lifetime identity) against the tracked state of the old child: emit `Add` for lifetimes in the new child but not the old, `Delete` for lifetimes in the old child but not the new, `Update` for lifetimes present in both where the value changed. Continue forwarding the new child's events going forward.
- Upstream `Delete(lifetime)` → dispose the child subscription, emit `Delete` for each active child lifetime.

**State:** tracks the active child set subscription and current child lifetimes for each upstream lifetime.

### RxSelectMany (array projection)

**Input:** `IReactiveSet<T>`, `Func<T, U[]>`, `Func<U, UKey>`
**Output:** `IReactiveSet<U>`

Each upstream lifetime produces a fixed array of child values. A key selector is required to identify child items across updates.

**Rules:**
- Upstream `Add(lifetime, item)` → evaluate `projection(item)`, emit `Add` for each element in the resulting array, keyed by the child key selector.
- Upstream `Update(lifetime, item)` → evaluate `projection(item)`, diff the new array against the previous array by child key: emit `Add` for new keys, `Delete` for removed keys, `Update` for keys present in both where the value changed.
- Upstream `Delete(lifetime)` → emit `Delete` for each active child lifetime.

**State:** tracks the current array of child values (indexed by child key) for each upstream lifetime.

### RxUnion, RxIntersect, RxExcept — not included

Traditional set-algebra operators (union, intersect, except) require cross-set identity — deciding when an item in one set is "the same" as an item in another. Since lifetimes are per-set and not visible in the item type, these operators would require key selectors and (for union/intersect) a conflict resolution strategy to pick which source's value wins. At that point they are redundant with RxJoin and RxLeftJoin, which already handle cross-set matching with explicit key selectors and a projection function. Use RxJoin/RxLeftJoin instead.

### RxGroupBy

**Input:** `IReactiveSet<T>`, `Func<T, TKey>`
**Output:** `IReactiveSet<IReactiveSet<T>>`

Partitions the source into groups by key. Each distinct key value produces a downstream lifetime carrying a child reactive set containing all items with that key.

**Rules:**
- Upstream `Add(lifetime, item)` → extract group key. If no group exists for this key, create a new child reactive set and emit a downstream `Add` for the group. Emit an `Add` into the child reactive set for the item.
- Upstream `Update(lifetime, item)` where group key is unchanged → emit an `Update` into the existing child reactive set for the item.
- Upstream `Update(lifetime, item)` where group key has changed → emit a `Delete` from the old group's child reactive set. If the old group is now empty, emit a downstream `Delete` for that group. If no group exists for the new key, create one and emit a downstream `Add`. Emit an `Add` into the new group's child reactive set.
- Upstream `Delete(lifetime)` → emit a `Delete` from the item's group's child reactive set. If the group is now empty, emit a downstream `Delete` for that group.

**State:** tracks group membership per key, maintains child reactive sets, tracks which group each upstream lifetime belongs to.

### RxFromObservableCollection

**Input:** `IObservable<IEnumerable<T>>`, `Func<T, TKey>`
**Output:** `IReactiveSet<T>`

Bridges a stream of full collection snapshots into a reactive set by diffing each snapshot against the previous.

**Rules:**
- First snapshot → emit `Add` for each item, keyed by the key selector.
- Subsequent snapshots → diff against previous snapshot by key:
  - Keys present in new but not old → `Add`
  - Keys present in old but not new → `Delete`
  - Keys present in both where the value has changed → `Update`
  - Keys present in both where the value is unchanged → no event

A `IEqualityComparer<TKey>` may optionally be supplied for key comparison. Item equality (for detecting updates) uses `EqualityComparer<T>.Default`.

**Source completion:** emit `Delete` for all active lifetimes (equivalent to diffing against an empty snapshot). The Changes stream stays open.

**Source error:** emit `Delete` for all active lifetimes, then error the Changes stream.

**State:** the previous snapshot as a `Dictionary<TKey, T>`, for diffing.

### MaterializedSet

**Input:** `IReactiveSet<T>`, `Func<T, TKey>`
**Output:** synchronous queryable collection

Subscribes to the source reactive set and maintains an internal dictionary keyed by `TKey`. Provides synchronous read access to current membership.

**Members:**
- `Count` — number of active lifetimes
- `Items` — `IReadOnlyCollection<T>` of all current values
- `TryGet(TKey key)` — returns the current value for a key, or null if not active
- `ContainsKey(TKey key)` — whether a key has an active lifetime
- `Dispose()` — tears down the subscription to the source

**Behavior:**
- On `Add` → inserts into internal dictionary
- On `Update` → replaces value in internal dictionary
- On `Delete` → removes from internal dictionary

**State:** a `Dictionary<TKey, T>` of active lifetimes. The key selector and optional `IEqualityComparer<TKey>` are provided at construction.

### RxSnapshot

**Input:** `IReactiveSet<T>`
**Output:** `IObservable<T[]>`

Materializes a reactive set back into a plain observable. Maintains an internal collection, applies each batch of changes, and emits the full current membership as an array after each batch.

**State:** the current membership collection.

### RxCount

**Input:** `IReactiveSet<T>`
**Output:** `IObservable<int>`

Emits the current count of active lifetimes after each batch of changes.

**State:** an integer counter.

## Threading Model

### Dedicated processing thread

A reactive set pipeline runs on a dedicated thread. All event processing — from the source through operators to subscribers — happens on this thread, synchronously.

When a caller mutates a MutableReactiveSet (Add/Update/Delete):
- **If already on the processing thread:** the event is processed synchronously inline. The method returns after the entire pipeline has processed the event.
- **If on a different thread:** the event is dispatched to the processing thread and the caller blocks until processing completes. When the method returns, all downstream state (including MaterializedSet views) reflects the change.

This means mutations are always synchronous from the caller's perspective — the method does not return until the event has been fully processed. Callers from multiple threads are serialized; at most one event is being processed at any time.

### Operators do not introduce concurrency

Operators process events on whatever thread delivers them — they never spawn threads or schedule work. The threading model is entirely determined by the source.

### ObserveOn for subscriber-side threading

A subscriber that needs to process events on a different thread (e.g., a UI thread) can use Rx's standard `ObserveOn` operator. In this case, `OnNext` on the processing thread returns after scheduling the work — it does not wait for the subscriber to finish. This means slow subscribers on other threads do not block the pipeline, but their view of the data may lag behind.

### Consistency guarantee

After a mutation method returns, the caller can synchronously query any MaterializedSet subscribed to the same pipeline and see the effect of the mutation.

## Error & Completion Semantics

### Completion

Reactive sets are not presumed to complete. A completed Changes stream means the set is empty forever — no further events will arrive. Operators should not complete their output stream if there is another option.

Specific rules:
- **ConstantReactiveSet** — never completes. The initial Adds are emitted; the stream stays open indefinitely.
- **MutableReactiveSet** — never completes. The stream stays open for future mutations.
- **Bridge operators** (RxSelectSingleLifetime, RxSelectMultipleLifetimes, RxFromObservableCollection) — source completion triggers Delete for active lifetimes but does not complete the Changes stream.
- **Single-input operators** (RxMap, RxFilter, RxSelectMany, RxGroupBy) — do not complete their output stream unless the upstream completes. If upstream completes, downstream completes.
- **Multi-input operators** (RxJoin, RxLeftJoin) — complete only when all upstream sources have completed.
- **Terminal consumers** (RxSnapshot, RxCount) — follow the same rule as single-input operators.

### Error

When a source's change stream errors:
1. The error propagates downstream per standard Rx semantics.
2. Operators do not swallow errors.
3. Before propagating the error, operators emit `Delete` events for any active downstream lifetimes, so that subscribers see clean lifetime endings before the stream terminates.

For multi-input operators (RxJoin, RxLeftJoin): if one upstream errors, the operator deletes all active downstream lifetimes and errors the output stream. The other upstream subscription is disposed.

For bridge operators, error behavior is specified per operator (see RxSelectSingleLifetime, RxSelectMultipleLifetimes).

### Disposal

Subscribing to a reactive set returns an `IDisposable`. Disposing it tears down the subscription chain. No Delete events are emitted on disposal — the subscriber has already opted out.

## Design Principles

1. **Lifetimes are stream structure** — a lifetime connects an Add, its Updates, and its Delete. It is a property of the stream, not of the items. Transformations change items; lifetimes carry through.
2. **Keys are a local concern** — key selectors appear where needed: at mutable sources, in joins, in grouping, in diffing. They are not part of the core `IReactiveSet<T>` interface.
3. **Incremental** — every operator does O(1) work per event, not O(n) over the whole set.
4. **Rx-native** — change event streams are plain `IObservable`, composable with existing Rx infrastructure.
5. **Dedicated thread, synchronous processing** — all pipeline processing happens on a single dedicated thread. Mutations block until fully processed. No hidden concurrency.
6. **Updates propagate** — a change to a lifetime's value flows through the entire pipeline, with each operator deciding what that change means for its downstream lifetimes.
