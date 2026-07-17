# TODO

- Check whether gleam code should be pulled out to top level and add folders for namespacing this project's modules.
- Add test suite
- Check whether the code is "production ready"
- Make sure documentation is clear and concise for both devs and would-be end-users

---

# Gleam/Lustre Client Migration Plan

## Goal

Replace the F#/Fable/Elmish client with a Gleam/Lustre SPA, keeping the Vite-based build pipeline and the existing Oxpecker backend entirely unchanged.

## Architecture Comparison

| Layer        | Current                          | Target                            |
| ------------ | -------------------------------- | --------------------------------- |
| Language     | F# (Fable → JS)                  | Gleam (native JS target)          |
| Framework    | Elmish + Feliz (React)           | Lustre (native VDOM)              |
| Router       | Feliz.Router (vendored, 200 LOC) | Modem (~50 LOC)                   |
| HTTP         | Fable.Fetch                      | gleam_fetch                       |
| JSON         | Thoth.Json (auto-codecs)         | gleam_json + gleam/dynamic/decode |
| CSS          | Tailwind v4 (Vite plugin)        | Tailwind v4 (unchanged)           |
| Bundler      | Vite                             | Vite + vite-gleam plugin          |
| Package mgr  | NuGet + npm                      | Hex + npm (Tailwind only)         |
| localStorage | Browser.Dom interop              | Thin FFI wrapper                  |
| MVU pattern  | Nested (App → TodoPage)          | Nested (identical structure)      |

## What Stays Unchanged

- **Backend** — zero changes. Same Oxpecker server, OIDC auth, SQLite store, OpenAPI/Scalar docs.
- **JSON contracts** — the wire format is identical. Backend still sends the same JSON.
- **Tailwind v4** — same `app.css`, same `@import "tailwindcss"`, same Vite plugin.
- **Vite config** — proxy to `:5000`, watch ignores, dev server. Just add `vite-gleam` plugin.
- **`index.html`** — same `div#app` mount point, just swap `<script>` src.
- **Static assets** — `public/favicon.svg` stays.
- **Publish pipeline** — `copy-client-dist` still copies `dist/` → `wwwroot/`.
- **OIDC login flow** — auth is server-driven (redirect to `/login`, callback `/signin-oidc`). Client just follows redirects on 401.

## What Gets Removed

- `src/ElmishTodos.Client/` (entire directory)
- `src/ElmishTodos.Shared/` — replaces `ElmishTodos.Shared.fsproj` project reference from the server, but ApiError.fs is still used by the server
- Client NuGet packages: `Fable.Core`, `Fable.Elmish`, `Fable.Fetch`, `Feliz`, `Feliz.UseElmish`, `Thoth.Fetch`, `Thoth.Json`
- npm packages: `react`, `react-dom`, `@types/react`, `@types/react-dom`
- `.fsproj` compilation order management
- Fable compilation step from Makefile

## File-by-File Mapping

### Shared Types (ported to Gleam)

| F# Source            | Gleam Destination          | Notes                                                                               |
| -------------------- | -------------------------- | ----------------------------------------------------------------------------------- |
| `Shared/Todo.fs`     | `src/todo_item.gleam`     | `Todo` + `UpdateTodoRequest` custom types. `Guid` → `uuid.Uuid`, `DateTime` → `String`. Module named `todo_item` because `todo` is a Gleam keyword. |
| `Shared/ApiError.fs` | `src/api_error.gleam`      | `ApiError` custom type. `int option` → `Option(Int)`                                |
| `Shared/Coders.fs`   | Inline in each type module | Thoth auto-codecs → explicit `gleam/dynamic/decode` combinators (~15 LOC per type)  |

### Client Logic

| F# Source                 | Gleam Destination     | Notes                                                             |
| ------------------------- | --------------------- | ----------------------------------------------------------------- |
| `Client/src/Api.fs`       | `src/http_effect.gleam`   + `src/response.gleam` | HTTP transport (`send`/`send_with`) + decoding helpers |
| `Client/vendor/Router.fs` | Removed               | Replaced by `modem` library                                       |
| `Client/src/App.fs`       | `src/app.gleam`       | Thin shell: Modem routing + delegate to todo page                 |
| `Client/src/TodoPage.fs`  | `src/todo_page.gleam` | Full TodoMVC: model, update, view, localStorage                   |

### Assets

| F# Source                   | Gleam Destination    | Notes                                      |
| --------------------------- | -------------------- | ------------------------------------------ |
| `Client/src/app.css`        | `src/app.css`        | Move into Gleam project, identical content |
| `Client/public/favicon.svg` | `public/favicon.svg` | Identical                                  |
| `Client/index.html`         | `index.html`         | Update script src to `./src/app.mjs`       |

## Type Mapping Cheat Sheet

| F#                      | Gleam                                       | Notes                                     |
| ----------------------- | ------------------------------------------- | ----------------------------------------- |
| `Guid`                  | `uuid.Uuid`                                 | Use `youid` for V7 generation + `uuid.from_string`/`uuid.to_string` for JSON |
| `DateTime`              | `String`                                    | ISO 8601 strings. Parse/format as needed. |
| `bool`                  | `Bool`                                      | Identical                                 |
| `string`                | `String`                                    | Identical                                 |
| `int`                   | `Int`                                       | Identical                                 |
| `'T list`               | `List(a)`                                   | Identical semantics                       |
| `'T option`             | `Option(a)`                                 | Identical semantics                       |
| `Result<'T, 'E>`        | `Result(a, e)`                              | Identical semantics                       |
| `ApiResult<'T>` (DU)    | `Result(a, ApiError)`                       | Flatten — no need for a separate DU       |
| `Cmd<'Msg>`             | `Effect(Msg)`                               | Lustre's effect type                      |
| `Cmd.none`              | `effect.none()`                             |                                           |
| `Cmd.batch`             | `effect.batch([...])`                       |                                           |
| `Cmd.map`               | `effect.map(effect, fn)`                    |                                           |
| `Cmd.ofMsg`             | `effect.from(fn() { Ok(msg) })`             |                                           |
| `Cmd.ofEffect`          | `effect.from(fn(dispatch) { ... Ok(Nil) })` |                                           |
| `Cmd.OfPromise.perform` | `effect.from(fn(dispatch) { promise         | > promise.map(dispatch) })`               |

## MVU Translation Patterns

### Model

```fsharp
// F# — record type
type Model = {
    NewTodo : string
    Todos : Todo list
    Visibility : Visibility
    EditState : EditState option
    Toasts : Toast list
}
```

```gleam
// Gleam — custom type with single variant
type Model {
  Model(
    new_todo: String,
    todos: List(Todo),
    visibility: Visibility,
    edit_state: Option(EditState),
    toasts: List(Toast),
  )
}
```

### Messages

```fsharp
// F# — discriminated union
type Msg =
    | ClientLoadedTodos of Todo list
    | UserChangedNewTodo of string
    | UserSubmittedNewTodo
    | NoOp
```

```gleam
// Gleam — custom type
type Msg {
  ClientLoadedTodos(todos: List(Todo))
  UserChangedNewTodo(text: String)
  UserSubmittedNewTodo
  NoOp
}
```

### Update (leaf page)

```fsharp
// F# — returns Model * Cmd<Msg>
let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp -> model, Cmd.none
    | UserChangedNewTodo text -> { model with NewTodo = text }, Cmd.none
```

```gleam
// Gleam — returns #(Model, Effect(Msg))
fn update(model: Model, msg: Msg) -> #(Model, Effect(Msg)) {
  case msg {
    NoOp -> #(model, effect.none())
    UserChangedNewTodo(text) -> #(Model(..model, new_todo: text), effect.none())
  }
}
```

### Update (shell, delegating to child)

```fsharp
// F# — Elmish nested MVU
| TodoPageMsg innerMsg ->
    let innerModel, innerCmd = TodoPage.update innerMsg model.TodoPage
    { model with TodoPage = innerModel }, Cmd.map TodoPageMsg innerCmd
```

```gleam
// Gleam — identical pattern
TodoPageMsg(inner_msg) -> {
  let #(inner_model, inner_effect) = todo_page.update(model.todo_page, inner_msg)
  #(
    Model(..model, todo_page: inner_model),
    effect.map(inner_effect, fn(m: todo_page.Msg) { TodoPageMsg(m) }),
  )
}
```

### View

```fsharp
// F# — Feliz (React)
Html.input [
    prop.type' "text"
    prop.value model.NewTodo
    prop.onChange (fun e -> dispatch (UserChangedNewTodo e))
    prop.classes ["text-gray-600"; "text-2xl"]
]
```

```gleam
// Gleam — Lustre native elements
html.input([
  attribute.type_("text"),
  attribute.value(model.new_todo),
  event.on_input(fn(text) { UserChangedNewTodo(text) }),
  attribute.class("text-gray-600 text-2xl"),
])
```

### HTTP Effects

```fsharp
// F# — Elmish Cmd
Cmd.OfPromise.perform (Api.get "/api/todos") () ClientFetchedTodos
```

```gleam
// Gleam — Lustre Effect
effect.from(fn(dispatch) {
  use resp <- promise.try_await(http.get("/api/todos"))
  let msg = case resp {
    Ok(todos) -> ClientFetchedTodos(todos)
    Error(err) -> ClientFetchFailed(err)
  }
  dispatch(msg)
  Ok(Nil)
})
```

## Dependencies

### Hex Packages

```toml
[dependencies]
gleam_stdlib = "~> 0.44"
gleam_json = "~> 3.1"
gleam_http = "~> 4.3"
gleam_fetch = "~> 1.4"
gleam_javascript = "~> 0.12"
lustre = "~> 5.7"
modem = "~> 2.1"
youid = "~> 2.1"
```

### npm Packages (keep only what Vite + Tailwind need)

```json
{
  "devDependencies": {
    "vite": "^8.0.16",
    "vite-gleam": "^1.0.0"
  },
  "dependencies": {
    "@tailwindcss/vite": "^4.3.0",
    "tailwindcss": "^4.3.0"
  }
}
```

## Build Pipeline Changes

### Makefile

```makefile
# Before
client-build:
	dotnet fable . --cwd src/ElmishTodos.Client --outDir build --run npx vite build

# After
client-build:
	cd client && gleam build --target javascript && npx vite build
```

`copy-client-dist` copies `client/dist/` → `server/ElmishTodos.Server/wwwroot/`. `publish` target unchanged — publishes the server to a self-contained binary.

### vite.config.ts

```ts
import { defineConfig } from 'vite'
import tailwindcss from '@tailwindcss/vite'
import gleam from 'vite-gleam'

const backendPaths = ['/api', '/login', '/logout', '/signin-oidc']

const proxy = Object.fromEntries(
  backendPaths.map(path => [path, {
    target: 'http://localhost:5000',
    changeOrigin: true,
  }])
)

export default defineConfig({
  clearScreen: false,
  server: {
    watch: {
      ignored: ["build/"]
    },
    proxy,
  },
  plugins: [
    gleam(),
    tailwindcss(),
  ],
})
```

### index.html

```html
<!DOCTYPE html>
<html>
  <head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link href="/src/app.css" rel="stylesheet">
    <link rel="icon" type="image/svg+xml" href="/favicon.svg">
  </head>
  <body>
    <div id="app"></div>
    <!-- vite-gleam resolves .gleam entry point -->
    <script type="module" src="./src/app.gleam"></script>
  </body>
</html>
```

## Step-by-Step Migration

### Phase 1: Scaffold (30 min)

1. **Create Gleam project** at the repo root:
   ```bash
   gleam new client
   ```
2. **Update `gleam.toml`** to use `name = "elmish_todos_client"`
   ```toml
   name = "elmish_todos_client"
   version = "1.0.0"
   target = "javascript"

   [dependencies]
   gleam_stdlib = "~> 0.44"
   gleam_json = "~> 3.1"
   gleam_http = "~> 4.3"
   gleam_fetch = "~> 1.4"
   gleam_javascript = "~> 0.12"
   lustre = "~> 5.7"
   modem = "~> 2.1"
   youid = "~> 1.6"
   ```
3. **Install npm deps** (strip React, add vite-gleam):
   ```bash
   npm remove react react-dom @types/react @types/react-dom
   npm install vite-gleam
   ```
4. **Update `vite.config.ts`** — add `gleam()` plugin
5. **Move assets** — `app.css` + `public/` + `index.html` into Gleam project root
6. **Verify** `gleam build --target javascript` succeeds

### Phase 2: Shared Types + JSON Codecs (1 hr)

Create these modules (order matters — no dependencies between them):

1. **`src/api_error.gleam`** — `ApiError` type + JSON decoder
2. **`src/todo_item.gleam`** — `Todo` + `UpdateTodoRequest` types + JSON decoders/encoders. Named `todo_item` because `todo` is a Gleam keyword. `Todo.id` uses `uuid.Uuid` from the `youid` package (not bare `String`).
3. **Use `youid` package** — `youid.v7()` replaces `Guid.CreateVersion7()`. `uuid.from_string` and `uuid.to_string` handle JSON ↔ Uuid conversion.

Verify: write a quick test decoding a sample JSON response from the real API.

### Phase 3: Response Decoding + Error Formatting (30 min)

Create `src/response.gleam`:

1. `decode_success(body, decoder)` / `http_error_to_api_error(err)` — converts raw HTTP results
   into `Result(a, ApiError)` by JSON-decoding the success body and parsing
   `HttpError` bodies as `ApiError` JSON on a best-effort basis
2. `format_decode_error` / `format_decode_errors` — human-readable JSON parse
   error messages

All raw HTTP I/O (`fetch`, status checking, request building) lives inside
`http_effect.gleam`'s `send`/`send_with`, which depends only on stdlib.
`effect.gleam` imports `http_effect` for `HttpMethod`, `HttpError`, and
`send` to build thin per-method constructors (`get`, `post`, …).
Client code only needs to import `effect` for everyday effects;
`http_effect.send_with` is the power-user extension point for auth headers
and custom HTTP methods.

### Phase 4: Effect System + Interpreter (1 hr) ✅

Create `src/effect.gleam`, `src/http_effect.gleam`, and `src/effect_ffi.mjs`.

**`HttpMethod`** — a custom 5-variant type defined in `http_effect.gleam`.
Restricted to the methods the app supports. Uses `gleam/http.Method` internally but avoids the stdlib's 10 variants
(Head, Trace, Connect, Options, Other) which this app never uses. Compiler-enforced
exhaustiveness — no silent no-op branch for unsupported methods.

```gleam
pub type HttpMethod {
  Get
  Post
  Put
  Patch
  Delete
}
```

**`HttpError`** — a two-variant type defined in `http_effect.gleam` that classifies
HTTP failures without parsing JSON. Callers can branch on status codes before
decoding:

```gleam
pub type HttpError {
  NetworkError(String)          // fetch-level failure (timeout, connection drop)
  HttpError(status: Int, body: String)  // non-2xx response, raw body
}
```

**`Effect(msg)`** — seven variants with named fields. All variants carry raw
strings — the effect system describes I/O intent, not data semantics. HTTP
callbacks receive `Result(String, HttpError)`, storage callbacks receive
`Result(String, String)`:

```gleam
pub type Effect(msg) {
  HttpRequest(method: HttpMethod, url: String, body: String, runner: fn(fn(msg) -> Nil) -> Nil)
  LoadFromStore(key: String, callback: fn(Result(String, String)) -> msg)
  SaveToStore(key: String, value: String)
  Redirect(url: String)
  After(delay: Int, message: msg)
  Batch(effects: List(Effect(msg)))
  None
}
```

**Per-method constructors** — `effect.gleam` provides thin per-method helpers
(`get`, `post`, `put`, `patch`, `delete`) that delegate to `http_effect.send`
and wrap the result in an `HttpRequest` variant. Body params are raw `String`
with an explicit `content_type` so callers aren't locked into JSON:

```gleam
// effect.gleam — the only import most pages need
pub fn get(url, callback) -> Effect(msg)
pub fn post(url, body, content_type, callback) -> Effect(msg)
pub fn put(url, body, content_type, callback) -> Effect(msg)
pub fn patch(url, body, content_type, callback) -> Effect(msg)
pub fn delete(url, callback) -> Effect(msg)
```

`from_promise` bridges raw `http_effect` promises into `Effect` values:

```gleam
// effect.gleam — bridge for custom HTTP behaviour
pub fn from_promise(method, url, body, promise, callback) -> Effect(msg)
```

Template users add custom HTTP methods or override behaviour via
`http_effect.send` / `http_effect.send_with`:

```gleam
// http_effect.gleam — power-user extension point
pub fn send(method, url, body, content_type) -> Promise(Result(String, HttpError))
pub fn send_with(method, url, body, content_type, transform) -> Promise(...)
```

**Response decoding** — two functions in `response.gleam`:

- `decode_success(body, decoder)` — decodes a 2xx body into `Result(a, ApiError)`
- `http_error_to_api_error(err)` — converts `HttpError` → `ApiError` (best-effort JSON
  parse, falls back to generic on failure; `NetworkError` surfaced with no status code)

Callers already branch on `Result(String, HttpError)` from the effect callback,
so they call these directly on the variants they've already matched:

```gleam
import http_effect.{HttpError}

fn callback(result: Result(String, HttpError)) {
  case result {
    Ok(raw) ->
      case response.decode_success(raw, todo_item.todo_decoder()) {
        Ok(todo) -> TodoCreated(todo)
        Error(err) -> TodoCreateFailed(err)
      }
    Error(HttpError(401, _)) ->
      UserRedirectedToLogin
    Error(err) ->
      TodoCreateFailed(response.http_error_to_api_error(err))
  }
}
```

**`response.gleam`** — pure utility module. Exports `decode_success` (2xx body
→ `Result(a, ApiError)`) and `http_error_to_api_error` (`HttpError` → `ApiError`).
Depends on `http_effect` (for `HttpError`) and `api_error` (for `ApiError`).
Client code imports this alongside `http_effect`.

**Interpreter** (`run` function) — the single place all I/O hits the real world:

- `HttpRequest` → calls the captured runner closure, which builds a
  `gleam/http` request, sends it via `gleam_fetch`, reads the text body,
  classifies the outcome (2xx → `Ok(body)`, non-2xx → `Error(HttpError(status, body))`,
  network failure → `Error(NetworkError(description))`), and dispatches the
  callback's message
- `LoadFromStore` / `SaveToStore` → `window.localStorage` FFI (no try/catch —
  exceptions propagate; if localStorage is broken the app is broken)
- `Redirect` → `window.location.assign` FFI
- `After` → `gleam/javascript/promise.wait` + `promise.tap`
- `Batch` → recurse over list

**`effect_ffi.mjs`** — thin JS wrappers for browser APIs. Only handles
localStorage and redirect.

**Wiring into Lustre** — a thin wrapper in `app.gleam` converts the inspectable `Effect` into Lustre's opaque one:

```gleam
fn update_with_effect(model, msg) -> #(Model, lustre.Effect(Msg)) {
  let #(new_model, effect) = update(model, msg)
  #(new_model, effect.from(fn(dispatch) { run(effect, dispatch) }))
}
```

### Phase 5: App Shell + Modem Routing (30 min)

Create `src/app.gleam`:

1. `Model` — holds current `Visibility` + delegates to `TodoPage.Model`
2. `Msg` — `UrlChanged(List(String))` + `TodoPageMsg(TodoPage.Msg)`
3. `init` — wire up Modem, fetch initial todos
4. `update` — route matching: `[]` → All, `["active"]` → Active, `["completed"]` → Completed
5. `view` — render `TodoPage.view` inside a Modem-provided router wrapper

The current `App.fs` has nested MVU. Preserve this pattern exactly — `UrlChanged` routes directly into `TodoPage.UserChangedVisibility`, and all child effects are mapped through `effect.map`.

### Phase 6: TodoPage Model + Update (3–4 hrs)

Create `src/todo_page.gleam`:

1. **Types** — `Visibility`, `EditState`, `TodoAction`, `Toast`, `Model`, `Msg`
2. **`init`** — empty model + batch of `HttpRequest(GET, "/api/todos", ...)` + `LoadFromStore("todomvc-lustre", ...)`
3. **`update`** — all 15 message handlers returning `#(Model, Effect(Msg))`:
   - Optimistic create/update/delete with rollback on failure (`rollback` function)
   - Toast creation on error (`create_toast` function)
   - 401 → `Redirect("/login")`
   - Toast auto-dismiss → `After(5000, ToastDismissed(id))`
   - `UserDeletedCompletedTodos` → `Batch([...])`
   - Successful fetch/save → `SaveToStore("todomvc-lustre", json)`
4. **`update_with_storage`** — update + `SaveToStore` (simpler: just return the effect alongside normal effects)

Each `Cmd.OfPromise.perform` becomes a `HttpRequest` variant with a callback. The `NoOp` pattern for fire-and-forget success responses stays. The overall structure is mechanical translation — no redesign. All FFI is isolated in the interpreter, all update logic is pure.

### Phase 7: TodoPage View (3–4 hrs)

Mechanical translation of HTML construction:

1. **`view_toast`** — toast notification UI
2. **`todo_list_item`** — individual todo row (edit mode vs display mode)
3. **`view`** — full page layout:
   - Toast container (fixed, top-right)
   - Header with title + new-todo input
   - Todo list (filtered by visibility)
   - Footer: item count, filter links (All/Active/Completed), clear completed, logout

Key translations:

- `Html.div` → `html.div`
- `Html.input` → `html.input`
- `prop.classes [...]` → `attribute.class("... ...")` (space-separated)
- `prop.className "..."` → same but note Tailwind classes are strings, not lists
- `prop.onChange` → `event.on_input`
- `prop.onClick` → `event.on_click`
- `prop.onKeyDown` → `event.on_keydown`
- `prop.onDoubleClick` → custom event handler
- `prop.onBlur` → `event.on_blur`
- `prop.isChecked` → `attribute.checked`
- `prop.onCheckedChange` → `event.on_check`
- `prop.href` → `attribute.href`
- `prop.text` → `element.text`
- `prop.children [...]` → children list as last argument
- Conditional classes: `if ... then "..." else "..."` → `case ... { ... }`
- `Html.none` → `element.none()`

### Phase 8: Wire It Together (30 min)

1. **`src/app.gleam`** — `pub fn main()` entry point with effect interpreter wiring:
   ```gleam
   pub fn main() {
     let app = lustre.application(init, update_with_effect, view)
     let assert Ok(_) = lustre.start(app, "#app", Nil)
     Nil
   }

   fn update_with_effect(model, msg) {
     let #(new_model, effect) = update(model, msg)
     #(new_model, effect.from(fn(dispatch) { run(effect, dispatch) }))
   }
   ```
2. **Update `index.html`** — script src to `./src/app.gleam`
3. **Test full flow** — dev server, create/edit/delete todos, filter, logout

### Phase 9: Build Pipeline + Cleanup (30 min)

1. **Update Makefile** — replace `client-build` and `client-watch`
2. **Remove F# client project** from solution
3. **Remove client F# packages** from `Directory.Packages.props`
4. **Update documentation:**
   - Rewrite `docs/client-architecture.md` — replace Elmish/Feliz/React component patterns with Lustre's nested MVU + component system. Cover: Lustre component boundaries, `lustre.element` vs `lustre.simple`, effect mapping between parent/child, and Modem routing patterns.
   - Remove Fable/Feliz/Elmish references from `AGENTS.md` and any other docs.
5. **Test publish pipeline** — `make publish` still works end-to-end

### Phase 10: Absorb Shared Project into Server (15 min)

The Shared project only existed to share types between the Fable client and the server. With the client gone, absorb those types directly into the server project and delete the Shared project entirely:

1. **Move** `ApiError.fs`, `Coders.fs`, `Todo.fs` from `server/ElmishTodos.Server/` (where they were previously absorbed) — already done as part of Phase 10 in a prior iteration.
2. **Rename namespaces** from `ElmishTodos.Shared.*` to `ElmishTodos.Server.*` in the moved files
3. **Update imports** in `Auth.fs`, `Todos.fs`, `Program.fs`, `Json.fs` — change `open ElmishTodos.Shared.*` to `open ElmishTodos.Server.*`
4. **Simplify** `Coders.fs` — remove `#if FABLE_COMPILER` conditional, keep only `Thoth.Json.Net`
5. **Update** `ElmishTodos.Server.fsproj`:
   - Add `Compile` items for the three moved files (before the existing files — `ApiError.fs` before `Coders.fs` before `Todo.fs`)
   - Remove `ProjectReference` to `ElmishTodos.Shared`
6. **Remove** Shared project from solution and delete `server/ElmishTodos.Shared/` directory (if it exists)
7. **Verify** server builds cleanly

## Project Structure (Final)

```
client/                           # Gleam project
├── gleam.toml
├── manifest.toml
├── package.json
├── vite.config.ts
├── index.html
├── public/
│   └── favicon.svg
├── src/
   │   ├── http_effect.gleam          # HTTP transport: HttpMethod, HttpError, send/send_with
│   ├── response.gleam            # Decode helpers + JSON error formatting
│   ├── effect.gleam             # Effect type + interpreter + per-method HTTP constructors
│   ├── effect_ffi.mjs           # Browser FFI (localStorage, redirect)
│   ├── todo_item.gleam          # Todo + UpdateTodoRequest types + JSON codecs
│   ├── api_error.gleam          # ApiError type + JSON codec
│   ├── app.css                  # Tailwind v4 entry
│   ├── app.gleam                # Entry point + shell + routing
│   ├── todo_page.gleam          # Full TodoMVC (model, update, view)
├── test/                        # gleeunit tests
├── build/                       # gleam build output (gitignored)
│   └── dev/javascript/
└── dist/                        # Vite bundle output (gitignored)
server/
  ElmishTodos.Server/            # Oxpecker backend (unchanged)
tests/                           # future .NET test project
```

## Testing Strategy

The custom `Effect` type makes `update` a pure function returning `#(Model, Effect(Msg))`. Tests don't need a browser, a DOM, or mock HTTP — they pattern-match on the returned `Effect` value:

```gleam
fn create_todo_test() {
  let model = init_model() |> with_new_todo("Buy milk")
  let #(model, effect) = update(model, UserSubmittedNewTodo)

  model.new_todo |> should.equal("")

  case effect {
    HttpRequest(method: Post, url: "/api/todos", body: body_str, ..)
      -> body_str |> should.contain("Buy milk")
    _ -> panic as "expected HttpRequest"
  }
}

fn logout_test() {
  let #(model, effect) = update(init_model(), UserClickedLogout)
  effect |> should.equal(Redirect("/login"))
}
```

The only untestable code is the interpreter itself (~20 LOC of FFI calls), which is intentionally thin. `gleam test` runs on both Erlang and JS targets since `update` never touches browser APIs.

## Full Lifecycle Example

Creating a todo from `UserSubmittedNewTodo` through the Lustre loop and back:

```gleam
import effect
import http_effect.{HttpError}
import response

// 1. update returns model + effect
UserSubmittedNewTodo -> {
  let body = todo_item.todo_to_json(new_todo) |> json.to_string
  let effect = effect.post("/api/todos", body, "application/json", fn(result) {
    case result {
      Ok(raw) ->
        case response.decode_success(raw, todo_item.todo_decoder()) {
          Ok(todo) -> TodoCreated(todo)
          Error(err) -> TodoCreateFailed(err)
        }
      Error(HttpError(401, _)) ->
        UserRedirectedToLogin
      Error(err) ->
        TodoCreateFailed(response.http_error_to_api_error(err))
    }
  })
  #(model, effect)
}

// 2. Lustre wires the effect into its runtime
fn update_with_effect(model, msg) {
  let #(new_model, effect) = update(model, msg)
  #(new_model, lustre_effect.from(fn(dispatch) { effect.run(effect, dispatch) }))
}

// 3. effect.run sends via gleam_fetch, reads body, checks status
// 4. Promise resolves → callback fires → TodoCreated(todo) dispatched
// 5. Lustre calls update again with TodoCreated(todo)
```

The `LoadFromStore` callback follows the same pattern — `result.fold` on the
raw `Result(String, String)`, with `json.parse` and decoding done at the call
site.

## Risks & Mitigations

| Risk                                            | Likelihood | Impact | Mitigation                                                                                                                                                      |
| ----------------------------------------------- | ---------- | ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `vite-gleam` plugin incompatibility with Vite 8 | Low        | High   | Test early. Fallback: use lustre_dev_tools (zero-config alternative)                                                                                            |
| Modem routing edge cases (hash vs path mode)    | Low        | Medium | Modem supports both. Current app uses path mode — Modem default                                                                                                 |
| `gleam_fetch` differences from Fable.Fetch      | Low        | Low    | Both wrap the Fetch API. Only difference is Gleam's `Promise` vs Fable's `promise` CE                                                                           |
| `gleam_javascript` promise API differences      | Low        | Medium | Gleam's `use` sugar for promises is equivalent to F# task CE. Slightly different syntax but same semantics.                                                     |
| Tailwind class merging differences              | Medium     | Low    | Feliz auto-merges class lists. Lustre merges duplicate attributes on the same element with string concatenation. May need to audit whitespace in class strings. |
| No test suite for the client currently          | —          | —      | Same situation as before. A test suite can be added later in Gleam (`gleam test`).                                                                              |

## Resolved Decisions

1. **UUID v7** — Use [`youid`](https://github.com/lpil/youid) (`lpil/youid` on Hex). Provides `youid.v7()` which maps directly to `Guid.CreateVersion7()`.

2. **Nested MVU** — Keep the two-layer `App` → `TodoPage` pattern. This repo is a template for non-trivial projects (20K+ LOC) where nested MVU is essential for scaling: each page gets its own `Msg` type (avoids merge conflicts on a giant flat union), its own `Model` (prevents pages from accidentally depending on each other's state), and its own `update` function (keeps diffs reviewable). The cost is small — `effect.map` on each delegation and one wrapper variant per child — and Lustre supports this cleanly. The current `App.fs` architecture (route shell mapping URLs to child messages) is preserved exactly.

3. **Custom `Effect` type + interpreter** — `update` returns pure data (`Effect(msg)` values) describing I/O. A single `run` function interprets them into real browser calls. This makes `update` testable without a browser (assert on the `Effect` value), isolates all FFI in one place, and scales to any page count with a fixed set of 7 effect variants (`HttpRequest`, `LoadFromStore`, `SaveToStore`, `Redirect`, `After`, `Batch`, `None`).

4. **Raw-string effect payloads** — Both HTTP and storage effects carry raw
   `String` values. The `Effect(msg)` type describes I/O intent (what URL, what
   method, what bytes), not data semantics. Callers own serialisation and
   deserialisation — `response.decode_success` / `response.http_error_to_api_error`
   convert a raw HTTP result into a typed value inside the callback. This keeps `Effect` naturally
   single-parameter, avoids coupling the interpreter to `json.Json`, and makes
   the effect API symmetric across HTTP and storage. For a template project
   that may store non-JSON data in `localStorage`, this leaves the data format
   as an explicit caller decision rather than an interpreter assumption.

5. **Closure-based `HttpRequest` runner** — The `HttpRequest` variant stores
   method, URL, and body as inspectable fields (for test assertions) plus an
   opaque runner closure that the interpreter calls. The closure captures
   `http_effect.gleam`'s `send` promise and the caller's callback, keeping the
   executor logic out of `Effect`.

6. **Per-method constructors** — Instead of a single `request(method, ...)` with
   an `Option` body and a runtime check for POST-without-body,
   each HTTP method has its own thin constructor in `effect.gleam` (`get`, `post`,
   `put`, `patch`, `delete`) that delegates to `http_effect.send`.
   Custom HTTP behaviour (auth headers, non-standard methods) goes through
   `http_effect.send` / `http_effect.send_with` directly. Body params are
   `String` — the caller pre-serialises. No `Option` ambiguity, no panics,
   no silent no-ops.

7. **Custom `HttpMethod` over `gleam/http.Method`** — The stdlib `Method` type has 10 variants (Head, Trace, Connect, Options, Other). The app only supports 5. Using the stdlib type requires a catch-all pattern in the interpreter (silent no-op) or a panic (runtime crash). A custom 5-variant type gives compiler-enforced exhaustiveness — passing an unsupported method is impossible.

8. **`http.gleam` → `http_effect.gleam`** — Originally named `http_client.gleam`,
   then `response.gleam` when raw HTTP I/O was moved into `effect.gleam`.
   Final split: `http_effect.gleam` defines `HttpMethod`, `HttpError`, and
   `send`/`send_with` (stdlib-only transport layer). `effect.gleam` imports
   `http_effect` and adds thin per-method constructors (`get`, `post`, …)
   plus the `Effect` type and interpreter. `response.gleam` imports
   `HttpError` from `http_effect` and provides pure decoding utilities
   (`decode_success`, `http_error_to_api_error`). Template users customise
   HTTP behaviour in `http_effect.gleam` without touching the interpreter.

9. **No exception swallowing in FFI** — `effect_ffi.mjs` does not wrap localStorage calls in try/catch. If `localStorage` is broken (security error, quota exceeded), the exception propagates — silently discarding writes or masking permission errors is worse than crashing.
