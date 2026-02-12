# CLAUDE.md — Project Instructions

## Current Phase: Implementation

### Project details
- **Namespace:** `com.hollerson.reactivesets`
- **Target framework:** .NET 8
- **Primary dependency:** System.Reactive (Rx.NET)
- **Test framework:** xUnit + System.Reactive.Testing
- **Purpose:** Reactive sets/collections and custom Rx operators

### Solution structure
- `src/ReactiveSet/` — class library
- `tests/ReactiveSet.Tests/` — xUnit tests

### Key documentation files
- `docs/architecture.md` — conceptual overview
- `docs/api-surface.md` — API guide for humans
- `docs/requirements.md` — detailed specs for implementation

### Build & test
```
dotnet build
dotnet test
```
