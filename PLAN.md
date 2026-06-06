# Elmish TodoMVC — Two-Stage Implementation Plan

## Stage 1: Pure Elmish TodoMVC (Self-Contained Client)

### 1a. Tooling & Environment

Add to the Nix flake:
- `nodejs_22`
- Fable as a local .NET tool (`dotnet fable`)

New npm packages (`src/ElmishTodos.Client/package.json`):
- `vite`, `vite-plugin-fable` — dev server + bundler
- `tailwindcss`, `@tailwindcss/vite` — utility-first CSS framework

New NuGet packages (in the client `.fsproj`):
- `Fable.Core` 5.0.0
- `Fable.Elmish` 5.0.2
- `Feliz` 3.3.3
- `Feliz.UseElmish` 5.0.0

### 1b. Project Structure

```
src/ElmishTodos.Client/
├── ElmishTodos.Client.fsproj
├── package.json
├── vite.config.ts
├── public/
│   └── index.html
└── src/
    ├── app.css             # Tailwind directives (@import "tailwindcss")
    ├── App.fs              # React root + Elmish wiring
    ├── Types.fs            # Model, Msg, Todo, Visibility
    ├── State.fs            # init, update
    └── View.fs             # Feliz rendering (styled with Tailwind)
```

### 1c. Types.fs — Domain

```fsharp
type Visibility = All | Active | Completed

type Todo = {
    id : int
    title : string
    completed : bool
    editing : bool          // inline-edit mode flag
}

type Model = {
    todos : Todo list
    field : string            // new-todo input text
    visibility : Visibility
    nextId : int              // auto-incrementing ID
    uid : int                 // unique ID for focus bookkeeping
}
```

**Messages** (covering every Elm TodoMVC action):
- `NoOp | Focus of int` — runtime bookkeeping
- `UpdateField of string` — keystroke in new-todo input
- `AddTodo` — submit on Enter
- `ToggleAll of bool` — mark all complete/active
- `ToggleTodo of int` — toggle single
- `Delete of int` — remove
- `DeleteComplete` — clear completed
- `EditingEntry of int * bool` — enter/exit edit mode
- `UpdateEntry of int * string` — save edited title (Enter/blur)
- `EditEntry of int * string` — keystroke in edit field
- `ChangeVisibility of Visibility` — filter click

### 1d. State.fs — Pure MVU

- `init () : Model * Cmd<Msg>` — empty todo list, `All` filter, `nextId = 0`
- `update (msg: Msg) (model: Model) : Model * Cmd<Msg>` — pure function, no side-effects in Stage 1

Key update logic:
- **AddTodo**: trim field, if non-empty → prepend new `Todo` with incremented `nextId`, clear field
- **ToggleAll**: set all `completed` to the toggle value
- **ToggleTodo**: flip `completed` on the matching `id`
- **Delete**: filter out matching `id`
- **DeleteComplete**: filter out `completed = true`
- **EditingEntry**: set `editing` flag on/off, capture current title
- **UpdateEntry**: trim, if non-empty → update title + clear editing; if empty → delete
- **EditEntry**: update the editing todo's title in place
- **ChangeVisibility**: set filter
- No `Cmd` in Stage 1 except `Cmd.none` and `Focus`

### 1e. View.fs — Feliz rendering with Tailwind CSS

Recreate the classic TodoMVC look using Feliz components + Tailwind utility classes. Feliz uses the `Html.*` and `prop.*` module functions for a clean, type-safe API:

```fsharp
open Feliz

let view (model: Model) (dispatch: Msg -> unit) =
    Html.section [
        prop.className "max-w-lg mx-auto mt-20 font-sans antialiased"
        prop.children [
            // Header
            Html.header [
                prop.className "header"
                prop.children [
                    Html.h1 [ prop.className "text-7xl font-thin text-center text-red-400"; prop.text "todos" ]
                    Html.input [
                        prop.className "new-todo w-full ..."
                        prop.placeholder "What needs to be done?"
                        prop.value model.field
                        prop.onChange (fun value -> dispatch (UpdateField value))
                        prop.onKeyDown (fun ev ->
                            if ev.key = "Enter" then dispatch AddTodo)
                    ]
                ]
            ]
            // ... rest of the structure
        ]
    ]
```

**Structural outline** (all styled with Tailwind):
- **Root container**: `max-w-lg mx-auto mt-20 font-sans antialiased`
- **Header**: `<h1>` _"todos"_ in large thin red text + new-todo `<input>` (full-width, large placeholder, subtle shadow)
- **Main section** (visible when todos exist):
  - **Toggle-all checkbox**: positioned at the top-left of the list
  - **Todo list item** (`<li>`):
    - **View mode**: checkbox + label + destroy button in a horizontal row; hover reveals destroy button
    - **Edit mode**: input replaces the label (double-click to enter)
    - Completed items: strikethrough + muted color
  - Conditional classes via `prop.className` with computed strings (e.g., `completed` + `editing` state)
- **Footer** (visible when todos exist):
  - Items-left counter → `float-left`
  - Filter links (All / Active / Completed) → centered, bordered on selected
  - Clear-completed button → `float-right`, visible when any completed
- **Info footer**: _"Double-click to edit a todo"_ + attribution text

Key Tailwind patterns used:
- Layout: `flex`, `grid`, `justify-between`, `items-center`, `space-y-*`
- Typography: `text-*`, `font-*`, `line-through`, `italic`
- Borders & shadows: `shadow-*`, `border`, `rounded`
- Visibility: `hidden`, `hover:*`, `group-hover:*` (destroy button)
- States: `peer-checked:*` (strikethrough), `opacity-*`, `text-*` for active filters

### 1f. App.fs — Wiring

```fsharp
module App

open Feliz
open Feliz.UseElmish

[<ReactComponent>]
let TodosApp () =
    let model, dispatch = React.useElmish (State.init, State.update, [||])
    View.view model dispatch

ReactDOM.createRoot (document.getElementById "app").render (TodosApp ())
```

### 1g. index.html + vite config

- `index.html`: empty `<div id="app">` + `<link>` to Tailwind-built CSS
- `vite.config.ts`: `vite-plugin-fable` pointing at the `.fsproj` + `@tailwindcss/vite` plugin

### Stage 1 Verification

`npm run dev` → opens browser → full TodoMVC works entirely client-side (add, edit, toggle, filter, clear completed, items-left counter, toggle-all). **No backend involved.**

Verify against <https://github.com/tastejs/todomvc/blob/master/app-spec.md>

---

## Stage 2: API Integration

### 2a. Backend Changes (`src/ElmishTodos/`)

**Store.fs** — add two new messages:
- `ToggleAll of bool * AsyncReplyChannel<unit>`
- `DeleteCompleted of AsyncReplyChannel<unit>`

**Handlers.fs** — add two endpoints:
- `PATCH /todos/toggle-all` — body `{ completed: bool }`, toggles all
- `DELETE /todos/completed` — removes completed todos

**Routes.fs** — register the new routes

**Program.fs** — add:
- `app.UseDefaultFiles()` + `app.UseStaticFiles()` to serve the client's `dist/` output
- SPA fallback: `app.MapFallbackToFile("index.html")` if using SPA routing

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

| Stage | Action | File |
|-------|--------|------|
| 1 | Modify | `flake.nix` (add nodejs) |
| 1 | Create | `src/ElmishTodos.Client/ElmishTodos.Client.fsproj` |
| 1 | Create | `src/ElmishTodos.Client/package.json` |
| 1 | Create | `src/ElmishTodos.Client/vite.config.ts` |
| 1 | Create | `src/ElmishTodos.Client/public/index.html` |
| 1 | Create | `src/ElmishTodos.Client/src/app.css` |
| 1 | Create | `src/ElmishTodos.Client/src/Types.fs` |
| 1 | Create | `src/ElmishTodos.Client/src/State.fs` |
| 1 | Create | `src/ElmishTodos.Client/src/View.fs` |
| 1 | Create | `src/ElmishTodos.Client/src/App.fs` |
| 2 | Modify | `src/ElmishTodos/Todos/Store.fs` |
| 2 | Modify | `src/ElmishTodos/Todos/Handlers.fs` |
| 2 | Modify | `src/ElmishTodos/Todos/Routes.fs` |
| 2 | Modify | `src/ElmishTodos/Program.fs` |
| 2 | Create | `src/ElmishTodos.Client/src/Api.fs` |
| 2 | Modify | `src/ElmishTodos.Client/src/Types.fs` |
| 2 | Modify | `src/ElmishTodos.Client/src/State.fs` |
| 2 | Modify | `src/ElmishTodos.Client/src/View.fs` |
