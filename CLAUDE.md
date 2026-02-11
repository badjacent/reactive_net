# CLAUDE.md — Project Instructions

## Current Phase: Documentation & Design

This project is in the **documentation-first** phase. Do not write implementation code (C#, .csproj, etc.) unless the user explicitly asks for it.

### What to do
- Create, refine, and review documentation and design artifacts
- Discuss architecture, API surface, and design trade-offs
- Ensure docs are internally consistent (architecture aligns with API surface)

### What NOT to do
- Do not scaffold solution/project files
- Do not write C# implementation or test code
- Do not add NuGet package references

### Project details
- **Namespace:** `com.hollerson.reactivesets`
- **Target framework:** .NET 8
- **Primary dependency:** System.Reactive (Rx.NET)
- **Purpose:** Reactive sets/collections and custom Rx operators

### Key documentation files
- `README.md` — project overview and status
- `docs/architecture.md` — high-level design and data flow
- `docs/api-surface.md` — planned interfaces, classes, and method signatures
