# AGENTS.md

> **Template setup:** run `./setup.sh <ProjectName>` to rename the project after cloning.

## Project Overview

An F# .NET 10 web API example using the Oxpecker web framework. Implements a todo CRUD API with in-memory storage, bearer auth, and OpenAPI documentation rendered via Scalar.

## Essential Commands

```bash
# Build
dotnet build

# Run
dotnet run --project src/OxpeckerApi

# Format code (fantomas)
dotnet fantomas .

# Lint
dotnet fsharplint lint OxpeckerApi.slnx
```

### Development Environment

The project uses a Nix flake providing `.NET SDK 10`, `fsautocomplete` (LSP), and `dprint` (markdown formatting):

```bash
nix develop   # or direnv allow if direnv is configured
```

Local .NET tools (fantomas, fsharplint) are defined in `.config/dotnet-tools.json`. The flake's `shellHook` runs `dotnet tool restore` automatically.

## File Structure & Compilation Order

F# compiles files in order. The `.fsproj` defines this sequence — **new files must be inserted at the correct position**:

Source code lives under `src/OxpeckerApi/`. The solution file is `OxpeckerApi.slnx` (the newer XML-based format).

| File                               | Purpose                                                  |
| ---------------------------------- | -------------------------------------------------------- |
| `src/OxpeckerApi/Auth.fs`          | Demo bearer authentication handler                       |
| `src/OxpeckerApi/Middleware.fs`    | Shared middleware (`notFound`, `requireAuthenticated`)   |
| `src/OxpeckerApi/OpenApi.fs`       | F#-aware OpenAPI schema transformers                     |
| `src/OxpeckerApi/Program.fs`       | App composition: DI, middleware pipeline                 |
| `src/OxpeckerApi/Todos/Handlers.fs`| CRUD endpoint handlers                                   |
| `src/OxpeckerApi/Todos/Models.fs`  | Domain types (`TodoItem`) + request/error DTOs           |
| `src/OxpeckerApi/Todos/Routes.fs`  | Route definitions + OpenAPI metadata for the Todos slice |
| `src/OxpeckerApi/Todos/Store.fs`   | In-memory store via `MailboxProcessor`                   |

The codebase follows **vertical slice architecture**. The `Todos/` directory is a self-contained feature slice owning its domain types, store, handlers, and route registration.

Modules correspond to file paths relative to the project root (e.g., `Todos/Models.fs` → `module OxpeckerApi.Todos.Models`). Cross-cutting concerns (`Auth`, `Middleware`, `OpenApi`) live at the project root.

## Solution Structure

```
OxpeckerApi.slnx          # Solution file (XML-based .slnx format)
src/
  OxpeckerApi/            # Web API project
```

## Architecture

### Store (`src/OxpeckerApi/Todos/Store.fs`)

An actor-based in-memory store using `MailboxProcessor` to serialize state mutations. The message DU is `private` — external code must use the module-level functions:

- `Store.t` is a type alias: `MailboxProcessor<TodoMessage>`
- `Store.start ()` creates the agent with an empty `Map<Guid, TodoItem>`
- All mutations are single-threaded through the agent's mailbox
- Async replies use `PostAndAsyncReply`; fire-and-forget uses `Post`

### Handlers (`src/OxpeckerApi/Todos/Handlers.fs`)

Endpoint handlers follow a curried pattern:

```fsharp
// No route params: store → EndpointHandler
let getTodos (store : Store.t) : EndpointHandler = ...

// Route params: store → param → EndpointHandler  
let getTodo (store : Store.t) (id : Guid) : EndpointHandler = ...
```

All handlers return `EndpointHandler` (a function `HttpContext → Task`), using `task { }` computation expressions. Response writing uses `ctx.WriteJson`, status codes via `ctx.SetStatusCode`.

### Middleware (`src/OxpeckerApi/Middleware.fs`)

Shared middleware extracted from the handlers layer:

- `notFound msg` — writes a 404 JSON error response
- `requireAuthenticated` — gates requests behind bearer auth; returns 401 if unauthenticated

Both are imported by slice handlers that need them.

### Routes (`src/OxpeckerApi/Todos/Routes.fs`)

Each vertical slice owns its route definitions and OpenAPI metadata in a single file. The `endpoints` function returns an `Endpoint list` passed to `app.UseOxpecker`. Routes are organized by HTTP method using Oxpecker's `GET`, `POST`, `PUT`, `DELETE` list builders. Route patterns:

- Static: `route "/todos"`
- Parameterized: `routef "/todos/{%O:guid}" handler` — the parameter is passed as an additional argument to the handler
- Middleware composition: `(requireAuthenticated >=> handler)` uses kleisli composition

OpenAPI metadata is attached inline via `addOpenApi` with `OpenApiConfig`, specifying request/response body types and operation-level config (summary, description, security requirements).

### Auth (`src/OxpeckerApi/Auth.fs`)

A custom `AuthenticationHandler` that accepts a hardcoded bearer token. Constants:

- `DemoScheme = "DemoBearer"`
- `DemoToken = "demo-token"`

Valid request: `Authorization: Bearer demo-token`

### OpenAPI (`src/OxpeckerApi/OpenApi.fs`)

Contains `FSharpRecordSchemaTransformer` and references `FSharpOptionSchemaTransformer` (from the Oxpecker.OpenApi NuGet package). The record transformer marks non-option fields as required in the generated schema.

Route-level OpenAPI metadata is attached via `addOpenApi` with `OpenApiConfig`, specifying request/response body types and operation-level config (summary, description, security requirements).

## Code Style & Conventions

### Formatting (via `.editorconfig` / fantomas)

- Spaces before parameters, members, colons, and invocations
- Commas: space after, not before
- Semicolons: space after, not before
- Multiline bracket style: **stroustrup** (opening brace at end of line, closing brace dedented)

### Naming

- Modules use PascalCase matching the filename
- Functions use camelCase
- Types (records, DUs) use PascalCase
- `[<Literal>]` constants use PascalCase
- `[<RequireQualifiedAccess>]` on modules that expose a type alias (e.g., `Store`) to avoid naming collisions

### Error Handling

Errors use a record type `{ Error: string; Details: string }` serialized as JSON with the appropriate HTTP status code. There is no exception-based error handling.

## Gotchas

- **Lockfile is enforced**: `RestorePackagesWithLockFile` is true in the `.fsproj`. After adding/updating NuGet packages, run `dotnet restore --lock-file-mode update` to regenerate `packages.lock.json`.
- **Compilation order matters in .fsproj**: adding a new `.fs` file requires inserting `<Compile Include="NewFile.fs" />` at the correct position before any file that depends on it.
- **No test project exists yet** — add one under `src/OxpeckerApi.Tests/` to keep the multi-project convention.
- **`FSharpOptionSchemaTransformer`** is defined in the `Oxpecker.OpenApi` package, not in the project's `OpenApi.fs`. The file only contains `FSharpRecordSchemaTransformer`.
- **`TodoMessage` DU is `private`** — you cannot construct these messages directly; use the module functions on `Store`.
- **Store is ephemeral** — all data is lost on restart (in-memory `Map`).
