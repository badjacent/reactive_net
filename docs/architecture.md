# Architecture & Design

## Overview

`com.hollerson.reactivesets` layers reactive collection semantics on top of Rx.NET. The core idea: model a **set** not as a static snapshot but as an `IObservable` stream of **change notifications**. Downstream operations (union, intersect, filter, map) subscribe to these change streams and incrementally maintain their own state — no full recomputation needed when a single element is added or removed.

## Core Abstractions

### Change Notification Model

Every reactive set emits a stream of `SetChange<T>` values:

```
SetChange<T>
├── Add(T item)
├── Remove(T item)
└── Clear
```

`SetChange<T>` is a discriminated-union-style type (implemented as a sealed record hierarchy). Each variant carries exactly the information a downstream operator needs to update its state.

A reactive set's observable surface is:

```
IObservable<SetChange<T>>
```

This is the fundamental building block — all set operations consume and produce `IObservable<SetChange<T>>` streams.

### `IReactiveSet<T>`

The primary abstraction combining:

| Member | Purpose |
|--------|---------|
| `IObservable<SetChange<T>> Changes` | Stream of granular add/remove/clear notifications |
| `IReadOnlySet<T> CurrentValue` | Snapshot of the current membership (for late subscribers or imperative queries) |
| `int Count` | Current element count |

`IReactiveSet<T>` does **not** extend `ISet<T>` — it is a read-only, observable projection. Mutation happens through a concrete `ReactiveSet<T>` class that implements `IReactiveSet<T>` and exposes `Add`, `Remove`, and `Clear` methods.

### Relationship to System.Reactive

```
System.Reactive primitives          This library
─────────────────────────────       ────────────────────────
IObservable<T>                  →   IObservable<SetChange<T>>
IObserver<T>                    →   (subscribers to change streams)
IScheduler                      →   (used for concurrency control)
Observable extension methods    →   SetObservable extension methods
```

The library is a **consumer** of System.Reactive, not a fork. All scheduling, subscription lifecycle, and error/completion semantics follow standard Rx conventions.

## Data Flow

```
 ┌──────────────┐
 │ ReactiveSet  │  (mutable source)
 │  Add / Remove│
 └──────┬───────┘
        │ IObservable<SetChange<T>>
        ▼
 ┌──────────────┐     ┌──────────────┐
 │   Filter     │     │     Map      │
 │ (predicate)  │     │ (selector)   │
 └──────┬───────┘     └──────┬───────┘
        │                     │
        ▼                     ▼
 ┌──────────────────────────────────┐
 │           Union / Intersect      │
 │  (combines two change streams)   │
 └──────────────┬───────────────────┘
                │ IObservable<SetChange<T>>
                ▼
         [ subscriber ]
```

1. A `ReactiveSet<T>` is the entry point — mutable, emits `SetChange<T>` on each mutation.
2. Intermediate operators (`Filter`, `Map`, `Union`, `Intersect`, `Except`, `SymmetricDifference`) each maintain internal state and translate upstream changes into downstream changes.
3. Terminal subscribers observe the final composed stream.

## Operator State Management

Each set operator maintains just enough internal state to produce correct incremental output:

| Operator | Internal State | Behavior on Add(x) upstream |
|----------|---------------|----------------------------|
| **Filter** | None (stateless pass-through) | Emit `Add(x)` if predicate matches, else skip |
| **Map** | Mapping cache `T → U` | Emit `Add(selector(x))`, cache mapping |
| **Union** | Reference counts per element | Increment refcount; emit `Add(x)` only if refcount goes from 0 → 1 |
| **Intersect** | Presence flags per source | Emit `Add(x)` only when present in all sources |
| **Except** | Elements from both sources | Emit `Add(x)` if in left and not in right |

## Threading & Schedulers

- **Default:** operators do not introduce concurrency. If the source emits on a particular scheduler, downstream operators process on the same scheduler.
- **ObserveOn / SubscribeOn:** standard Rx operators can be applied to any `IObservable<SetChange<T>>` stream to control scheduling.
- **Thread safety of `ReactiveSet<T>`:** mutations (`Add`, `Remove`, `Clear`) will be serialized. Subscribers receive notifications on the calling thread unless explicitly scheduled elsewhere.
- **`CurrentValue` consistency:** accessing `CurrentValue` while changes are in flight is a point-in-time snapshot — it may not yet reflect changes that are currently propagating through the pipeline.

## Error & Completion Semantics

- **Error:** if a source set's change stream errors, the error propagates downstream per standard Rx semantics. Operators do not swallow errors.
- **Completion:** a source set completes its change stream when disposed. Composite operators (union, intersect) complete when all upstream sources have completed.
- **Disposal:** subscribing to a reactive set pipeline returns an `IDisposable`. Disposing it tears down the subscription chain, same as any Rx subscription.

## Design Principles

1. **Incremental over recomputation** — every operator should do O(1) work per change, not O(n) over the whole set.
2. **Rx-native** — change streams are plain `IObservable<SetChange<T>>`, composable with all existing Rx operators.
3. **No hidden threads** — concurrency is explicit via schedulers, never implicit.
4. **Snapshot availability** — `CurrentValue` provides an escape hatch for imperative code that needs the current state.
