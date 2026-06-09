# Elmish TodoMVC — Two-Stage Implementation Plan

## Stage 2: API Integration

### 2a. Backend Changes (`src/ElmishTodos.Server/`)

**Store.fs** — add two new messages:

- `ToggleAll of bool * AsyncReplyChannel<unit>`
- `DeleteCompleted of AsyncReplyChannel<unit>`

**Handlers.fs** — add two endpoints:

- `PATCH /todos/toggle-all` — body `{ completed: bool }`, toggles all
- `DELETE /todos/completed` — removes completed todos

**Routes.fs** — register the new routes

**ElmishTodos.Server.fsproj** — remove `<StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>` (added to suppress a `dotnet watch` error for API-only projects; needed once we serve static files)

**Program.fs** — add:

- `app.UseDefaultFiles()` + `app.UseStaticFiles()` to serve the client's `dist/` output (copied into a `wwwroot/` directory)
- SPA fallback: `app.MapFallbackToFile("index.html")` for client-side routing

### 2b. Client Changes

**Add `Api.fs`** — HTTP client wrappers using `fetch`:

```fsharp
module Api
val loadTodos : unit -> JS.Promise<Todo[]>
val createTodo : title:string -> JS.Promise<Todo>
val updateTodo : id:int * title:string * completed:bool -> JS.Promise<Todo>
val deleteTodo : id:int -> JS.Promise<unit>
val toggleAll : completed:bool -> JS.Promise<unit>
val deleteCompleted : unit -> JS.Promise<unit>
```

**Modify Types.fs**:

- Change `id: int` → `id: System.Guid` (or `string`) to match API
- Add `FetchTodos of Todo list` and `FetchError of string` messages

**Modify State.fs**:

- `AddTodo` → returns `Cmd.OfPromise.perform Api.createTodo ...`
- `ToggleTodo` → `Cmd.OfPromise.perform Api.updateTodo ...`
- `Delete` → `Cmd.OfPromise.perform Api.deleteTodo ...`
- `UpdateEntry` → `Cmd.OfPromise.perform Api.updateTodo ...`
- `ToggleAll` → `Cmd.OfPromise.perform Api.toggleAll ...`
- `DeleteComplete` → `Cmd.OfPromise.perform Api.deleteCompleted ...`
- `init` → `Cmd.OfPromise.perform Api.loadTodos ... FetchTodos FetchError`

**Modify View.fs**:

- Add loading/error indicators (minimal — match Elm TodoMVC spirit)

### 2c. Build & Serve

- `npm run build` → outputs `dist/` with compiled JS + CSS + HTML
- `dotnet run --project src/ElmishTodos` serves both API and static client
- API at `/todos/*`, SPA at `/`

---

## Files to Create/Modify Summary

| Stage | Action | File                                               |
| ----- | ------ | -------------------------------------------------- |
| 2     | Modify | `src/ElmishTodos/Todos/Store.fs`                   |
| 2     | Modify | `src/ElmishTodos/Todos/Handlers.fs`                |
| 2     | Modify | `src/ElmishTodos/Todos/Routes.fs`                  |
| 2     | Modify | `src/ElmishTodos/Program.fs`                       |
| 2     | Create | `src/ElmishTodos.Client/src/Api.fs`                |
| 2     | Modify | `src/ElmishTodos.Client/src/Types.fs`              |
| 2     | Modify | `src/ElmishTodos.Client/src/State.fs`              |
| 2     | Modify | `src/ElmishTodos.Client/src/View.fs`               |
