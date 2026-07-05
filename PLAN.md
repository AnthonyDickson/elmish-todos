# TODO

- Add favico and setup static file serving
- Centralise config and parse into record
- Bundle client into server binary (single-file publish)
  - Enable `StaticWebAssetsEnabled` in `.fsproj`
  - Add MSBuild target to copy client `dist/` into `wwwroot/` before build
  - Wire `UseStaticFiles` + `MapFallbackToFile("index.html")` in `Program.fs`
  - Publish: `dotnet publish -c Release --self-contained -r linux-x64 -p:PublishSingleFile=true`
- Create Docker image for release deployment
  - Multi-stage build: SDK → build client + server + publish above
  - Final stage: `FROM gcr.io/distroless/static-debian12` (or `alpine` with `linux-musl-x64`)
  - Copy single binary, set `ENTRYPOINT`, no shell or runtime needed
- Check whether the code is "production ready"

---

# SQLite Migration Plan (Fumble)

Replace the `MailboxProcessor`-based in-memory todo store with a SQLite-backed store using [Fumble](https://github.com/tforkmann/Fumble).

## Overview

- **Package**: `Fumble` v2.0.1 (single NuGet package; depends on `Microsoft.Data.Sqlite`)
- **Approach**: Each store function opens a fresh connection, runs its query, and disposes. No connection pooling needed — SQLite connections are lightweight.
- **Scope**: `Todos.fs` `Models`/`Store` modules change internally; handlers gain a light error branch (pattern-match on `Result`). A new `Db.fs` coordinates startup. Routes, auth, and OpenAPI metadata are untouched.

---

## Step 1 — Add Fumble Dependency

### `Directory.Packages.props`

Add the version:

```xml
<PackageVersion Include="Fumble" Version="2.0.1" />
```

### `src/ElmishTodos.Server/ElmishTodos.Server.fsproj`

Add the reference:

```xml
<PackageReference Include="Fumble" />
```

### Regenerate lockfile

```bash
dotnet restore --force-evaluate --project src/ElmishTodos.Server
```

---

## Step 2 — Add Connection String Configuration

### `src/ElmishTodos.Server/appsettings.Development.json`

```jsonc
{
  // … existing keys …
  "ConnectionStrings": {
    "Default": "Data Source=todos.db"
  }
}
```

For production, override via environment variable `ConnectionStrings__Default` or an `appsettings.json` on the deployment host. Use an absolute path (e.g. `/data/todos.db`) so the database survives container/publish redeploys.

---

## Step 3 — Rewrite `Todo.fs` Models + Store Modules

### What to remove

- `TodoMessage` discriminated union (the mailbox protocol)
- `type Store = MailboxProcessor<TodoMessage>`
- The recursive `loop` actor body inside `Store.start`
- The `Models` module can be deleted entirely — its only purpose was the DU and type alias

### What to add

#### New `Store` type

```fsharp
type Store = { ConnectionString: string }
```

#### Table schema

```sql
CREATE TABLE IF NOT EXISTS todos (
    Id        TEXT    NOT NULL PRIMARY KEY,
    Title     TEXT    NOT NULL,
    Completed INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT    NOT NULL              -- ISO 8601 (via Sql.dateTime / read.dateTime)
);
```

| Column      | SQLite Type        | F# Type    | Notes                                 |
| ----------- | ------------------ | ---------- | ------------------------------------- |
| `Id`        | `TEXT` (PK)        | `Guid`     | Stored as string                      |
| `Title`     | `TEXT NOT NULL`    | `string`   |                                       |
| `Completed` | `INTEGER NOT NULL` | `bool`     | `0` = false, `1` = true               |
| `CreatedAt` | `TEXT NOT NULL`    | `DateTime` | ISO 8601 (Fumble handles conversion)  |

#### Fumble row mapping helpers

```fsharp
open System

let private readTodo (read: Fumble.RowReader) : Todo = {
    Id        = read.string "Id" |> Guid.Parse
    Title     = read.string "Title"
    Completed = read.bool "Completed"
    CreatedAt = read.dateTime "CreatedAt"
}
```

#### Fumble parameter helpers

```fsharp
open Fumble

let private todoParams (todo: Todo) =
    Sql.parameters [
        "@id",        Sql.string (todo.Id.ToString())
        "@title",     Sql.string todo.Title
        "@completed", Sql.bool todo.Completed
        "@createdAt", Sql.dateTime todo.CreatedAt
    ]
```

#### Store functions

Store functions open a connection, run a Fumble pipeline, and return the `Result` directly so callers can handle errors explicitly:

| Function | SQL                                                                                                           | Return type                      |
| -------- | ------------------------------------------------------------------------------------------------------------- | -------------------------------- |
| `getAll` | `SELECT Id, Title, Completed, CreatedAt FROM todos`                                                           | `Task<Result<Todo list, exn>>`   |
| `get`    | `SELECT … FROM todos WHERE Id = @id`                                                                          | `Task<Result<Todo option, exn>>` |
| `upsert` | `INSERT OR REPLACE INTO todos (Id, Title, Completed, CreatedAt) VALUES (@id, @title, @completed, @createdAt)` | `Task<Result<unit, exn>>`        |
| `update` | `UPDATE … WHERE Id = @id RETURNING Id, Title, Completed, CreatedAt`                                            | `Task<Result<Todo option, exn>>` |
| `delete` | `DELETE FROM todos WHERE Id = @id`                                                                            | `Task<Result<bool, exn>>`        |

**`update`** uses SQLite's `RETURNING` clause to update and return the row in one round-trip.

```fsharp
// Example: getAll — uses Fumble's async API to avoid blocking the thread
let getAll (store: Store) =
    task {
        let! result =
            store.ConnectionString
            |> Sql.connect
            |> Sql.query "SELECT Id, Title, Completed, CreatedAt FROM todos"
            |> Sql.executeAsync readTodo
            |> Async.StartAsTask

        return result
    }
```

#### Handler integration

Handlers gain a lightweight error branch. The happy path is unchanged; on failure they return a 500 JSON `ApiError`:

```fsharp
// Before (in-memory — no error path):
let handler (store: Store) : EndpointHandler =
    fun ctx -> task {
        let! items = Store.getAll store
        return! Json.write ctx items
    }

// After (SQLite — Result match, caller formats the error):
let handler (store: Store) : EndpointHandler =
    fun ctx -> task {
        match! Store.getAll store with
        | Ok items -> return! Json.write ctx items
        | Error ex ->
            ctx.SetStatusCode 500
            return! Json.write ctx { Error = "Internal Server Error"; Details = ex.Message; StatusCode = Some 500 }
    }
```

**`migrate`** — each slice exposes a `migrate` function that only creates its own tables (no pragmas). Returns `Result` so `Db.initialize` can fail fast:

```fsharp
let migrate (connectionString: string) =
    task {
        let! result =
            connectionString
            |> Sql.connect
            |> Sql.query "CREATE TABLE IF NOT EXISTS todos (
                Id TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Completed INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            )"
            |> Sql.executeNonQueryAsync
            |> Async.StartAsTask

        return result |> Result.map ignore
    }
```

#### Public API

```fsharp
[<RequireQualifiedAccess>]
module Todos =
    type Store = { ConnectionString: string }

    let fromConnectionString (connStr: string) = { ConnectionString = connStr }
    let migrate = Store.migrate
    let endpoints = Api.endpoints
```

---

## Step 4 — Add `Db.fs` Coordinator

Create `src/ElmishTodos.Server/Db.fs`. It owns connection-level pragmas and calls each slice's `migrate` in the correct order (FK parents first):

```fsharp
namespace ElmishTodos.Server.Db

open Fumble

module Db =
    let initialize (connectionString: string) =
        task {
            let wal = connectionString |> Sql.connect |> Sql.enableWalMode
            match wal with Error ex -> return Error ex | Ok _ -> ()

            let fk = connectionString |> Sql.connect |> Sql.enableForeignKeys
            match fk with Error ex -> return Error ex | Ok _ -> ()

            return! Todos.Store.migrate connectionString
            // Future slices go here:
            // let! users = Users.Store.migrate connectionString
            // match users with Error ex -> return Error ex | Ok _ -> ()
            // return Ok ()
        }
```

Add `<Compile Include="Db.fs" />` to the `.fsproj` after `Todos.fs` (it references `Todos.Store.migrate`).

---

## Step 5 — Update `Program.fs`

Replace the store creation + endpoint wiring:

```fsharp
let todoEndpoints = Todos.startStore () |> Todos.endpoints
```

With:

```fsharp
let connectionString = app.Configuration.GetConnectionString "Default"

match (Db.Db.initialize connectionString).GetAwaiter().GetResult() with
| Error ex -> failwithf "Database initialization failed: %s" ex.Message
| Ok () -> ()

let store = Todos.fromConnectionString connectionString
let todoEndpoints = Todos.endpoints store
```

`failwithf` at startup is acceptable here — the app cannot function without its database. The full exception is available on `ex` for logging before the crash.

---

## Step 6 — Files Changed Summary

| File                                                  | Change                                                                                            |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Directory.Packages.props`                            | Add `<PackageVersion Include="Fumble" Version="2.0.1" />`                                         |
| `src/ElmishTodos.Server/ElmishTodos.Server.fsproj`    | Add `<PackageReference Include="Fumble" />` and `<Compile Include="Db.fs" />` after `Todos.fs`    |
| `src/ElmishTodos.Server/Db.fs`                        | **New** — pragmas + ordered `migrate` calls                                                       |
| `src/ElmishTodos.Server/packages.lock.json`           | Regenerated by `dotnet restore --force-evaluate`                                                  |
| `src/ElmishTodos.Server/appsettings.Development.json` | Add `ConnectionStrings.Default`                                                                   |
| `src/ElmishTodos.Server/Todos.fs`                     | Replace `Models` and `Store` modules; add `Result` branches to handlers (~65 removed, ~120 added) |
| `src/ElmishTodos.Server/Program.fs`                   | `startStore()` → `Db.initialize` + `fromConnectionString`                                         |

No changes to: `Auth.fs`, `OpenApi.fs`, `Todo.fs` (Shared), `Coders.fs`, `ApiError.fs`, client code, routes, or OpenAPI metadata.

---

## Risk / Considerations

- **Error handling**: Store functions return `Result<'T, exn>` directly — no mapping, no information loss. Handlers extract `ex.Message` for the JSON response but have access to the full exception for logging if needed. `Db.initialize` propagates the `exn` to `Program.fs`, which fails fast at startup.
- **Concurrency**: SQLite supports multiple readers but one writer at a time. WAL mode allows concurrent reads during a write. For a todo app this is more than sufficient.
- **SQLite file location**: `Data Source=todos.db` resolves relative to the server's working directory. Use an absolute path in production (e.g. `/data/todos.db`) so the DB survives container restarts and redeploys.
- **No data migration needed**: The current store is in-memory, so there is nothing to migrate.
- **`DateTime` precision**: Stored as ISO 8601 text via Fumble's `Sql.dateTime` / `read.dateTime`. Full `DateTime` precision is preserved.
- **Testing**: After migration, run `make server-build` and `dotnet test` (once tests exist) to confirm the store works correctly.
