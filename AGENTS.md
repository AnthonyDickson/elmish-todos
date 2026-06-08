# AGENTS.md

## Project Overview

A full-stack F# .NET 10 todo app. The backend (`ElmishTodos.Server`) is a web API using Oxpecker with in-memory storage, bearer auth, and OpenAPI docs rendered via Scalar. The frontend (`ElmishTodos.Client`) is an Elmish (MVU) SPA compiled to JS via Fable, styled with Tailwind CSS, and bundled with Vite.

## Essential Commands

All commands run from the repo root. A `Makefile` wraps them as `make <target>`.

```bash
# Build the server
make server-build

# Run the server
make server-watch

# Format code (fantomas)
make format

# Lint
make lint

# Start the client dev server
make client-watch

# Build the client dist bundle
make client-build
```

### Development Environment

The project uses a Nix flake providing `.NET SDK 10`, `fsautocomplete` (LSP), `nodejs_24`, `gnumake`, and `dprint` (markdown formatting):

```bash
nix develop   # or direnv allow if direnv is configured
```

Local .NET tools (fantomas, fsharplint, fable) are defined in `.config/dotnet-tools.json`. The flake's `shellHook` runs `dotnet tool restore` automatically.

Before running client commands, install npm dependencies:

```bash
cd src/ElmishTodos.Client && npm install
```

## File Structure & Compilation Order

F# compiles files in order. The `.fsproj` defines this sequence — **new files must be inserted at the correct position** before files that depend on them.

The solution file is `ElmishTodos.slnx` (the newer XML-based format).

### Server (`src/ElmishTodos.Server/`)

The server follows **vertical slice architecture**: the `Todos/` directory is a self-contained feature slice. Cross-cutting concerns (`Auth`, `Middleware`, `OpenApi`) live at the project root. Module paths mirror file paths (e.g., `Todos/Models.fs` → `module ElmishTodos.Server.Todos.Models`).

### Client (`src/ElmishTodos.Client/`)

The client uses the Elmish (MVU) pattern compiled to JS via Fable, with Tailwind CSS v4 and React 19:

- `src/App.fs` — Application shell: top-level routing and page delegation
- `src/TodoPage.fs` — Todo MVU page: model, update, and view; persists to `localStorage`
- `vendor/Router.fs` — Vendored `Feliz.Router` (must compile before `TodoPage.fs`)
- `src/app.css` — Tailwind CSS v4 entry point
- `vite.config.ts` — Vite config with `@tailwindcss/vite` plugin (no PostCSS needed)
- `package.json` — React 19, Vite 8, Tailwind CSS 4
- `index.html` — Entry HTML referencing the built JS at `build/src/App.js`

## Solution Structure

```
ElmishTodos.slnx                     # Solution file (XML-based .slnx format)
src/
  ElmishTodos.Server/                # Web API project
  ElmishTodos.Client/                # Fable/Elmish + Vite frontend
    vendor/Router.fs                 # Vendored Feliz.Router
    src/
      App.fs                         # App shell (routing + page delegation)
      TodoPage.fs                    # Todo page (model, update, view)
```

## Architecture

### Client Architecture

The client is split into two MVU layers:

- **`App.fs`** — Thin shell handling routing and page delegation. Routes `/`, `/active`, `/completed` map to `Todo.Visibility` values.
- **`TodoPage.fs`** — The Todo MVC page. Persists state to `localStorage` via `initWithLocalStorage` / `updateWithLocalStorage`. Supports inline editing via double-click.

### Store (`src/ElmishTodos.Server/Todos/Store.fs`)

An actor-based in-memory store using `MailboxProcessor` to serialize state mutations. The message DU is `private` — external code must use the module-level functions:

- `Store.t` is a type alias: `MailboxProcessor<TodoMessage>`
- `Store.start ()` creates the agent with an empty `Map<Guid, TodoItem>`
- All mutations are single-threaded through the agent's mailbox
- Async replies use `PostAndAsyncReply`; fire-and-forget uses `Post`

### Handlers (`src/ElmishTodos.Server/Todos/Handlers.fs`)

Endpoint handlers follow a curried pattern:

```fsharp
// No route params: store → EndpointHandler
let getTodos (store : Store.t) : EndpointHandler = ...

// Route params: store → param → EndpointHandler  
let getTodo (store : Store.t) (id : Guid) : EndpointHandler = ...
```

All handlers return `EndpointHandler` (a function `HttpContext → Task`), using `task { }` computation expressions. Response writing uses `ctx.WriteJson`, status codes via `ctx.SetStatusCode`.

### Middleware (`src/ElmishTodos.Server/Middleware.fs`)

Shared middleware extracted from the handlers layer:

- `notFound msg` — writes a 404 JSON error response
- `requireAuthenticated` — gates requests behind bearer auth; returns 401 if unauthenticated

Both are imported by slice handlers that need them.

### Routes (`src/ElmishTodos.Server/Todos/Routes.fs`)

Each vertical slice owns its route definitions and OpenAPI metadata in a single file. The `endpoints` function returns an `Endpoint list` passed to `app.UseOxpecker`. Routes are organized by HTTP method using Oxpecker's `GET`, `POST`, `PUT`, `DELETE` list builders. Route patterns:

- Static: `route "/todos"`
- Parameterized: `routef "/todos/{%O:guid}" handler` — the parameter is passed as an additional argument to the handler
- Middleware composition: `(requireAuthenticated >=> handler)` uses kleisli composition

OpenAPI metadata is attached inline via `addOpenApi` with `OpenApiConfig`, specifying request/response body types and operation-level config (summary, description, security requirements).

### Auth (`src/ElmishTodos.Server/Auth.fs`)

A custom `AuthenticationHandler` that accepts a hardcoded bearer token. Constants:

- `DemoScheme = "DemoBearer"`
- `DemoToken = "demo-token"`

Valid request: `Authorization: Bearer demo-token`

### OpenAPI (`src/ElmishTodos.Server/OpenApi.fs`)

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

- **Lockfile is enforced**: `RestorePackagesWithLockFile` is true in both `.fsproj` files. After adding/updating NuGet packages, run `dotnet restore --lock-file-mode update` to regenerate `packages.lock.json`.
- **Compilation order matters in .fsproj**: new `.fs` files must be `<Compile Include>`'d in dependency order.
- **Client requires `npm install`** in `src/ElmishTodos.Client/` before `make client-watch` or `make client-build`.
- **Fable compiles F# to JS** — `make client-watch` uses `dotnet fable watch` with incremental compilation; `make client-build` produces a production bundle via `vite build`.
- **Fable outputs to `build/`** via `--outDir build`. When using `--outDir`, Fable 5 drops the `.fs` infix (e.g., `src/App.fs` → `build/src/App.js`, not `App.fs.js`). Without `--outDir`, the `.fs` infix is kept. Don't mix the two naming patterns.
- **No test project exists yet** — add one under `tests/ElmishTodos.Server.Tests/` or `tests/ElmishTodos.Client.Tests/` to keep the multi-project convention.
- **`FSharpOptionSchemaTransformer`** is defined in the `Oxpecker.OpenApi` package, not in the server's `OpenApi.fs`. The file only contains `FSharpRecordSchemaTransformer`.
- **`TodoMessage` DU is `private`** — you cannot construct these messages directly; use the module functions on `Store`.
- **Store is ephemeral** — all data is lost on restart (in-memory `Map`).
