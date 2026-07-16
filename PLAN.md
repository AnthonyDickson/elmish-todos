# TODO

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
| `Shared/Todo.fs`     | `src/todo.gleam`           | `Todo` + `UpdateTodoRequest` custom types. `Guid` → `String`, `DateTime` → `String` |
| `Shared/ApiError.fs` | `src/api_error.gleam`      | `ApiError` custom type. `int option` → `Option(Int)`                                |
| `Shared/Coders.fs`   | Inline in each type module | Thoth auto-codecs → explicit `gleam/dynamic/decode` combinators (~15 LOC per type)  |

### Client Logic

| F# Source                 | Gleam Destination     | Notes                                                             |
| ------------------------- | --------------------- | ----------------------------------------------------------------- |
| `Client/src/Api.fs`       | `src/http.gleam`      | `get`/`post`/`put`/`patch`/`delete` wrappers around `gleam_fetch` |
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
| `Guid`                  | `String`                                    | Generate V7 UUIDs via `youid.v7()`        |
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
	cd src/ElmishTodos.Client && gleam build --target javascript && npx vite build
```

`copy-client-dist` and `publish` targets unchanged — they still copy `dist/` → `wwwroot/` and publish the server.

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

1. **Create Gleam project** inside `src/`:
   ```bash
   cd src && gleam new elmish_todos_client
   ```
2. **Rename to match existing directory** or restructure:
   - Target: `src/ElmishTodos.Client/` becomes a Gleam project
   - Clean out the old project first, or create alongside and swap
3. **Configure `gleam.toml`**:
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
   ```
4. **Install npm deps** (strip React, add vite-gleam):
   ```bash
   npm remove react react-dom @types/react @types/react-dom
   npm install vite-gleam
   ```
5. **Update `vite.config.ts`** — add `gleam()` plugin
6. **Move assets** — `app.css` + `public/` + `index.html` into Gleam project root
7. **Verify** `gleam build --target javascript` succeeds on empty project

### Phase 2: Shared Types + JSON Codecs (1 hr)

Create these modules (order matters — no dependencies between them):

1. **`src/api_error.gleam`** — `ApiError` type + JSON decoder
2. **`src/todo.gleam`** — `Todo` + `UpdateTodoRequest` types + JSON decoders/encoders
3. **Use `youid` package** — `youid.v7()` replaces `Guid.CreateVersion7()`, no wrapper module needed

Verify: write a quick test decoding a sample JSON response from the real API.

### Phase 3: HTTP Client (1 hr)

Create `src/http.gleam`:

1. `get(url)` — returns `Promise(Result(a, ApiError))`
2. `post(url, body)` — ditto
3. `put(url, body)` — ditto
4. `patch(url, body)` — ditto
5. `delete(url)` — ditto

Map the current response-handling logic: branch on `response.ok`, decode success vs error JSON.

Verify: call `GET /api/todos` against the running backend and inspect the decoded result.

### Phase 4: Effect System + Interpreter (1 hr)

Create `src/effect.gleam`:

```gleam
type HttpMethod {
  Get
  Post
  Put
  Patch
  Delete
}

type Effect(msg) {
  HttpRequest(HttpMethod, String, Option(String), fn(Result(String, String)) -> msg)
  LoadFromStore(String, fn(Result(String, String)) -> msg)
  SaveToStore(String, String)
  Redirect(String)
  After(Int, msg)
  Batch(List(Effect(msg)))
  None
}
```

Seven variants cover the entire app at any scale. Callers parameterize with keys, decoders, URLs, and message constructors — the `Effect` type never needs to grow.

**Interpreter** (`run` function, ~25 LOC) — the single place all I/O hits the real world:

- `HttpRequest` → `gleam_fetch` + JSON encode/decode
- `LoadFromStore` / `SaveToStore` → `window.localStorage` FFI
- `Redirect` → `window.location.assign` FFI
- `After` → `window.setTimeout` FFI
- `Batch` → recurse over list

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
4. **Remove Fable/Feliz/Elmish references** from docs
5. **Test publish pipeline** — `make publish` still works end-to-end

## Project Structure (Final)

```
src/ElmishTodos.Client/          # Gleam project
├── gleam.toml
├── manifest.toml
├── package.json
├── vite.config.ts
├── index.html
├── public/
│   └── favicon.svg
├── src/
│   ├── app.gleam                # Entry point + shell + routing
│   ├── todo_page.gleam          # Full TodoMVC (model, update, view)
│   ├── effect.gleam             # Effect type + interpreter (all I/O boundary)
│   ├── todo.gleam               # Todo + UpdateTodoRequest types + JSON codecs
│   ├── api_error.gleam          # ApiError type + JSON codec
│   ├── http.gleam               # HTTP client used by the interpreter
│   └── app.css                  # Tailwind v4 entry
├── build/                       # gleam build output (gitignored)
│   └── dev/javascript/
└── dist/                        # Vite bundle output (gitignored)
```

## Testing Strategy

The custom `Effect` type makes `update` a pure function returning `#(Model, Effect(Msg))`. Tests don't need a browser, a DOM, or mock HTTP — they pattern-match on the returned `Effect` value:

```gleam
fn create_todo_test() {
  let model = init_model() |> with_new_todo("Buy milk")
  let #(model, effect) = update(model, UserSubmittedNewTodo)

  model.new_todo |> should.equal("")

  case effect {
    HttpRequest(Post, "/api/todos", Some(body), _) ->
      body |> should.contain("Buy milk")
    _ -> panic as "expected HttpRequest"
  }
}

fn logout_test() {
  let #(model, effect) = update(init_model(), UserClickedLogout)
  effect |> should.equal(Redirect("/login"))
}
```

The only untestable code is the interpreter itself (~25 LOC of FFI calls), which is intentionally thin. `gleam test` runs on both Erlang and JS targets since `update` never touches browser APIs.

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

3. **Custom `Effect` type + interpreter** — `update` returns pure data (`Effect(msg)` values) describing I/O. A single `run` function interprets them into real browser calls. This makes `update` testable without a browser (assert on the `Effect` value), isolates all FFI in one place, and scales to any page count with a fixed set of 7 effect variants (`HttpRequest`, `LoadFromStore`, `SaveToStore`, `Redirect`, `After`, `Batch`, `None`). Callers parameterize with keys, decoders, and URLs — the type never grows.
