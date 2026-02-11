# com.hollerson.reactivesets

Reactive sets, collections, and custom operators for [Rx.NET](https://github.com/dotnet/reactive) (System.Reactive).

> **Status: Design phase — not yet implemented.**

## Purpose

`com.hollerson.reactivesets` extends Rx.NET with reactive collections that propagate changes through set-algebraic operations. Instead of re-evaluating entire collections when a single element changes, this library models additions, removals, and updates as observable change streams — enabling efficient, composable, push-based collection pipelines.

## Planned Features

### Reactive Sets & Collections
- `IReactiveSet<T>` — an observable set that emits granular change notifications (add / remove / clear)
- Set operations: **union**, **intersect**, **except** (difference), **symmetric difference**
- Projection and filtering that preserve reactive change propagation

### Custom Rx Operators
- Extension methods on `IObservable<T>` for common patterns not covered by core Rx.NET
- Operators designed to compose naturally with reactive sets

## Target Framework & Dependencies

| Item | Value |
|------|-------|
| Target framework | .NET 8 |
| Primary dependency | `System.Reactive` (Rx.NET) |
| Namespace | `com.hollerson.reactivesets` |

## Documentation

- [Architecture & Design](docs/architecture.md)
- [API Surface Specification](docs/api-surface.md)

## License

TBD
