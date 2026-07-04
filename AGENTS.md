# AGENTS.md

## Project Overview

A full-stack F# .NET 10 todo app. The backend is a web API using Oxpecker with an in-memory `MailboxProcessor` store, OIDC auth (cookie + JWT bearer), and Scalar-rendered OpenAPI docs. The frontend is an Elmish (MVU) SPA compiled to JS via Fable, styled with Tailwind CSS v4, and bundled with Vite.

## Essential Commands

```bash
make server-build    # Build the server
make server-watch    # Run the server with hot reload
make client-watch    # Start the client dev server (Vite + Fable watch)
make client-build    # Production client bundle
make format          # Format F# with fantomas
make lint            # Lint F# with fsharplint
```

Dev environment via Nix: `nix develop` (or `direnv allow`). Before client commands, run `npm install` in `src/ElmishTodos.Client/`.

## File Structure & Compilation Order

F# compiles files in the order listed in `.fsproj`. **New files must be inserted before files that depend on them.**

```
ElmishTodos.slnx                      # XML-based solution format
Directory.Build.props                 # Enables Central Package Management
Directory.Packages.props              # All NuGet package versions
src/
  ElmishTodos.Shared/
    ApiError.fs                       # ApiError record type
    Coders.fs                         # Generic JSON encode/decode (conditional Thoth)
    Todo.fs                           # Todo + UpdateTodoRequest types
  ElmishTodos.Server/
    Auth.fs                           # OIDC auth setup (cookie + JWT bearer)
    OpenApi.fs                        # FSharpRecordSchemaTransformer
    Todos.fs                          # Store, models, handlers, routes (vertical slice)
    Program.fs                        # Entry point, OpenAPI/Scalar config
  ElmishTodos.Client/
    vendor/Router.fs                  # Vendored Feliz.Router
    src/
      Api.fs                          # HTTP client (get/post/put/patch/delete)
      TodoPage.fs                     # Todo MVU page (model, update, view)
      App.fs                          # App shell (routing + page delegation)
      app.css                         # Tailwind CSS v4 entry
```

## Architecture

### Auth (`Auth.fs`)

Uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` with two parallel schemes:

- **Cookie** (`CookieAuthenticationDefaults`) — SPA session, authorization code flow.
- **JWT Bearer** (`"bearer"`) — for Scalar API docs, PKCE flow.

Both satisfy the `"authenticated"` authorization policy. The `requireAuth` middleware gates protected endpoints.

Cookie defaults in production: `SecurePolicy=Always`, `SameSite=Lax`, `HttpOnly=true`. In dev, `RequireHttpsMetadata=false` and self-signed certs are accepted.

Environment config via `Oidc:Authority`, `Oidc:ClientId`, `Oidc:ClientSecret`, `Oidc:CallbackPath`. The `Login:ReturnUrl` and `OAuth2:*` settings are read in `Program.fs`.

### Store & Handlers (`Todos.fs`)

Single-file vertical slice with nested modules:

- `Models` — `TodoMessage` DU (`private`), `Store` type alias (`MailboxProcessor<TodoMessage>`)
- `Store` — Agent start + accessor functions (`getAll`, `get`, `upsert`, `update`, `delete`)
- `Api` — Endpoint handler sub-modules (`GetAll`, `Get`, `Create`, `Update`, `Delete`), each exposing a `handler` and an `endpoint` function
- `Todos` (top-level, `[<RequireQualifiedAccess>]`) — public API: `Todos.Store`, `Todos.startStore`, `Todos.endpoints`

Handler pattern:

```fsharp
// No route param: store → EndpointHandler
let handler (store : Store) : EndpointHandler = ...

// With route param: store → param → EndpointHandler
let handler (store : Store) (id : Guid) : EndpointHandler = ...
```

Each `endpoint` function wraps the handler with OpenAPI metadata via `addOpenApi`. Routes use `/api/todos` prefix for GET/POST and `/api/todos/{id}` for GET/PUT/DELETE.

### Client (`App.fs` + `TodoPage.fs`)

Two-layer MVU:

- `App.fs` — Thin shell: routes `/`, `/active`, `/completed` → `TodoPage.Visibility`.
- `TodoPage.fs` — Full TodoMVC: persist to `localStorage` via `initWithLocalStorage` / `updateWithLocalStorage`.
- `Api.fs` — Typed HTTP client with `ApiResult<'T>` (Success/Failure discriminated union). Uses `fetchUnsafe` (no credentials), `Thoth.Json` for codecs.

## Code Style & Conventions

### Formatting (fantomas via `.editorconfig`)

- Stroustrup bracket style, spaces before parameters/colons/invocations
- Space after commas and semicolons, not before

### Naming

- Modules: PascalCase, matching filename
- Functions: camelCase
- Types (records, DUs): PascalCase
- `[<RequireQualifiedAccess>]` on modules that expose a type alias (e.g. `Store`, `Todos`)
- `[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]` when module and type share a name

### Error Handling

Json-serialized `ApiError` records (`{ Error: string; Details: string; StatusCode: int option }`) with appropriate HTTP status codes. No exceptions.

## Gotchas

- **Lockfile enforced**: After adding/updating NuGet packages, run `dotnet restore --force-evaluate --project <project>`.
- **Compilation order**: Insert new `.fs` files at the correct position in `.fsproj`.
- **Central Package Management**: Versions in `Directory.Packages.props`; project files use bare `<PackageReference Include="..." />`.
- **Fable output**: `--outDir build` drops the `.fs` infix (`src/App.fs` → `build/src/App.js`). Don't mix with non-`--outDir` builds.
- **Client needs `npm install`** before first `make client-watch` or `make client-build`.
- **`TodoMessage` DU is `private`** — use `Store` module functions only.
- **Store is ephemeral** — in-memory `Map`, lost on restart.
- **No test project yet** — add under `tests/`.
