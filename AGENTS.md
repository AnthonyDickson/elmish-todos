# AGENTS.md

## Project Overview

A full-stack todo app with an Oxpecker F# .NET 10 backend (SQLite + OIDC auth + OpenAPI) and a Gleam/Lustre SPA frontend styled with Tailwind CSS v4 and bundled with Vite.

## Essential Commands

```bash
make server-build    # Build the server
make server-watch    # Run the server (auto-applies DB migrations)
make server-test     # Server xUnit tests
make client-install-deps  # npm install
make client-watch    # Start the client dev server (Vite + Gleam watch)
make client-build    # Production client bundle
make client-test     # Client gleeunit tests
make e2e-test        # Playwright E2E tests in Docker
make copy-client-dist # Copy client dist into server wwwroot/
make publish         # Single-file publish (builds client, copies assets, publishes server)
make format          # Format F# with fantomas + gleam format
make lint            # Lint F# with fsharplint
```

### Publishing

```bash
make publish                    # linux-x64 (default)
make publish RUNTIME=osx-arm64  # macOS Apple Silicon
```

This builds the client, copies it into the server's `wwwroot/`, then publishes the server as a self-contained single-file binary with trimming. The output is at `server/src/LustreTodos.Server/bin/Release/publish/`.

### Database Commands

```bash
make db-migration name=add_foo  # Scaffold a new migration file
make db-migrate                 # Apply pending migrations (standalone script)
make db-generate                # Regenerate Db.fs types from live DB (SqlHydra)
make db-update                  # db-migrate + db-generate (full schema update)
make db-reset                   # Delete DB, re-apply all migrations, regenerate
```

Dev environment via Nix: `nix develop` (or `direnv allow`). Before client commands, run `make client-install-deps` to install npm packages.

## File Structure & Compilation Order

F# compiles server files in the order listed in `.fsproj`. **New files must be inserted before files that depend on them.** The Gleam client has no ordering constraints.

```
server/
  LustreTodos.slnx                    # XML-based solution format
  Directory.Build.props               # Enables Central Package Management
  Directory.Packages.props            # All NuGet package versions
  global.json                         # .NET SDK version
  dotnet-tools.json                   # Local tool manifest
  fsharplint.json                     # FSharpLint configuration
  sqlhydra-sqlite.toml                # SqlHydra CLI configuration
  .editorconfig                       # Code style (fantomas)
  .fantomasignore                     # Files excluded from formatting
  scripts/
    migrate.fsx                       # Standalone DbUp migration runner
  src/
    LustreTodos.Server/
      LustreTodos.Server.fsproj       # Project file
      packages.lock.json              # Locked dependency graph
      appsettings.Development.json    # Dev environment config
      ApiError.fs                     # ApiError record type
      Coders.fs                       # Generic JSON encode/decode
      Config.fs                       # OIDC/OAuth2 configuration
      Auth.fs                         # OIDC auth setup (cookie + JWT bearer)
      OpenApi.fs                      # FSharpRecordSchemaTransformer
      Json.fs                         # JSON read/write helpers
      Db.fs                           # SqlHydra-generated types + QueryContextFactory
      Todos.fs                        # Store, handlers, routes (vertical slice)
      Program.fs                      # Entry point, DbUp, OpenAPI/Scalar config
      migrations/                     # Numbered .sql files applied by DbUp at startup
client/
  src/
    app.gleam                         # Entry point + app shell + routing
    app.css                           # Tailwind CSS v4 entry
    main.js                           # Vite entry point
    todos_mvc/
      todo_page.gleam                 # Full TodoMVC (model, update, view)
      effect.gleam                    # Effect type + interpreter + HTTP constructors
      http_effect.gleam               # HTTP transport: HttpMethod, HttpError, send/send_with
      effect_ffi.mjs                  # Browser FFI (localStorage, redirect, navigation)
      response.gleam                  # Decode helpers + JSON error formatting
      todo_item.gleam                 # Todo + UpdateTodoRequest types + JSON codecs
      api_error.gleam                 # ApiError type + JSON codec
      guard.gleam                     # use-compatible early-return for Option/Result
  test/
    todos_mvc/
      todo_page_test.gleam            # Pure unit tests for update logic
tests/
  e2e/
    global-setup.ts                   # OIDC login before test suite
    todos.spec.ts                     # Playwright test specs
    playwright.config.ts              # Playwright configuration
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

- `Store` — SQLite-backed query functions (`getAll`, `get`, `upsert`, `update`, `delete`) using SqlHydra.Query
- `Api` — Endpoint handler sub-modules (`GetAll`, `Get`, `Create`, `Update`, `Delete`), each exposing a `handler` and an `endpoint` function
- `Todos` (top-level, `[<RequireQualifiedAccess>]`) — public API: `Todos.Store`, `Todos.endpoints`

The `Store` holds a `QueryContextFactory` (from `Db.fs`) wired at startup in `Program.fs`. All queries use SqlHydra computation expressions (`selectTask`, `insertTask`, `updateTask`, `deleteTask`). A thin mapping layer copies between `main.Todos` DB rows and the `Todo` API type — the types are structurally identical thanks to SqlHydra-compatible type hints (`GUID`, `BOOLEAN`, `DATETIME`) in the migration SQL.

### Database (`Db.fs` + `migrations/`)

- **`Db.fs`** — Auto-generated by `dotnet sqlhydra sqlite`. Contains record types, table declarations, and `QueryContextFactory`. Committed to source control — compiles immediately after clone, no code-gen needed. Do not modify directly, use the make targets.
- **`migrations/`** — Numbered `.sql` files (e.g. `001_create_todos.sql`) embedded as resources. DbUp runs them in order at startup, tracking applied scripts in a `SchemaVersions` table. Use SqlHydra-compatible type hints (`GUID`, `BOOLEAN`, `DATETIME`) in column definitions — these aren't real SQLite types but influence codegen. See [SqlHydra's SqliteDataTypes.fs](https://github.com/JordanMarr/SqlHydra/blob/main/src/SqlHydra.Cli/Sqlite/SqliteDataTypes.fs) for the full list.
- **`scripts/migrate.fsx`** — Standalone DbUp migration runner that reads SQL files directly from disk. Used by `make db-migrate` to apply migrations without building the server — avoids the chicken-and-egg problem where a schema change breaks the build before `Db.fs` is regenerated.
- **Error handling**: SqlHydra throws on infrastructure failures (dead connection, disk full). A global middleware in `Program.fs` catches all unhandled exceptions and returns `500`. Handlers focus on domain logic (`Option` → `404`, validation → `400`).

#### Schema Workflows

**After cloning:**
```bash
cd server && dotnet restore
make server-build    # Db.fs is committed — compiles immediately
make server-watch   # DbUp creates todos.db + applies migrations at startup
```

**Changing the schema:**
```bash
make db-migration name=add_priority   # scaffolds migrations/002_add_priority.sql
# … write the SQL in the new file …
make db-update                        # apply migrations + regenerate types
# … fix compile errors in Todos.fs mapping functions …
make server-build
```

**Starting fresh:**
```bash
make db-reset                         # delete DB, re-apply all migrations, regenerate
```

**Key constraints:**
- Migration files are applied once, in order — never modify an already-run migration. Add a new file for changes.
- `Db.fs` is auto-generated — do not hand-edit. The mapping layer in `Todos.fs` is the control point for DB ↔ API type conversions.
- Connection string is `Data Source=todos.db` (relative, resolves to project root). Use an absolute path (e.g. `/data/todos.db`) in production via `ConnectionStrings__Default` env var.

Handler pattern:

```fsharp
// No route param: store → EndpointHandler
let handler (store : Store) : EndpointHandler = ...

// With route param: store → param → EndpointHandler
let handler (store : Store) (id : Guid) : EndpointHandler = ...
```

Each `endpoint` function wraps the handler with OpenAPI metadata via `addOpenApi`. Routes use `/api/todos` prefix for GET/POST and `/api/todos/{id}` for GET/PUT/DELETE.

### Client (Gleam/Lustre SPA)

Two-layer MVU:

- `app.gleam` — Entry point + thin shell: routing via custom navigation effects + delegate to todo page. Effect interpreter wiring.
- `todo_page.gleam` — Full TodoMVC: model, update (pure, returns `Effect` values), view.
- `effect.gleam` — Custom `Effect` type with 9 variants (`HttpRequest`, `LoadFromStore`, `SaveToStore`, `Redirect`, `After`, `Navigate`, `PushUrl`, `ReplaceUrl`, `Batch`, `None`) + interpreter (`run`) + `map`/`batch`/`none` helpers + thin per-method HTTP constructors (`get`, `post`, `put`, `patch`, `delete`) + navigation constructors (`navigate`, `push_url`, `replace_url`). Devs only need to import `effect` for everyday effects.
- `http_effect.gleam` — HTTP transport layer. Defines `HttpMethod` and `HttpError` types, exposes `send` / `send_with` (configurable request building). Depends only on stdlib — the extension point for auth headers, retry logic, or custom HTTP methods.
- `effect_ffi.mjs` — Thin JS wrappers for `window.localStorage`, `window.location.assign`, and client-side navigation (click interception on internal links, popstate listener, `history.pushState`/`replaceState`).
- `response.gleam` — `decode_success` (2xx body → typed `Result`) and `http_error_to_api_error` (`HttpError` → `ApiError`) helpers. Pages that branch on specific HTTP status codes (e.g. 401) also need `import http_effect.{HttpError}`.
- `todo_item.gleam` / `api_error.gleam` — Shared types with explicit `gleam_json` decoders.
- `guard.gleam` — `use`-compatible helpers (`guard.some`, `guard.ok`) for early return from `Option`/`Result`.

### Tests

Three layers: server xUnit tests (`make server-test`, ephemeral SQLite, no auth), client gleeunit tests (`make client-test`, pure `update` unit tests, no browser), and E2E Playwright tests (`make e2e-test`, full stack in Docker Compose with Authelia). E2E tests use `data-testid` attributes for selectors — add them to `todo_page.gleam` when new interactive elements are introduced.

### Static Assets

**Client assets** (images, fonts, favicons, PDFs — anything the SPA references) live in `client/public/`. Vite serves them at root in dev and copies them into `dist/` on build. They reach the server via `make copy-client-dist`.

**Server-only assets** (e.g. `robots.txt` that should exist regardless of the client bundle) live in `server/src/LustreTodos.Server/wwwroot/`. Note that `wwwroot/` is gitignored and recreated by `copy-client-dist`, so the source of truth for any persisted file must live elsewhere (or use a build step).

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

- **Lockfile enforced**: After adding/updating NuGet packages, run `dotnet restore --force-evaluate <project>`.
- **Compilation order**: Insert new `.fs` files at the correct position in `.fsproj`.
- **SqlHydra query parameters**: Function parameters can't be captured directly in query expressions. Bind them to local `let` values first (e.g. `let idStr = id.ToString()` before using in a `where` clause).
- **Central Package Management**: Versions in `Directory.Packages.props`; project files use bare `<PackageReference Include="..." />`.
- **Client needs `npm install`** before first `make client-watch` or `make client-build`.
- **Tests** — xUnit in `server/tests/`, gleeunit in `client/test/`, Playwright E2E in `tests/e2e/`.
