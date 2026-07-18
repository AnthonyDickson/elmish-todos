# Client Architecture

The client is a Gleam/Lustre SPA built with nested MVU (Model-View-Update).
All I/O is managed through a custom `Effect` type with a single interpreter,
keeping update functions pure and testable.

## Nested MVU

The app uses a two-layer MVU structure: `app.gleam` (shell) delegates to
`todo_page.gleam` (feature page).

```gleam
// app.gleam — thin shell
pub type Model {
  Model(todo_page: todo_page.Model)
}

pub type Msg {
  UrlChanged(path: String)
  TodoPageMsg(todo_page.Msg)
}

pub fn update(model: Model, msg: Msg) -> #(Model, effect.Effect(Msg)) {
  case msg {
    TodoPageMsg(inner_msg) -> {
      let #(inner_model, inner_effect) =
        todo_page.update(model.todo_page, inner_msg)
      #(
        Model(..model, todo_page: inner_model),
        effect.map(inner_effect, TodoPageMsg),
      )
    }
    // ...
  }
}
```

Each page gets its own `Msg` type, `Model`, and `update` function. The parent
maps child effects through `effect.map` to wrap child messages in the parent's
variant. This scales to any number of pages without a growing flat union.

## Effect System

`update` returns pure data (`#(Model, Effect(Msg))`) — never performs I/O.
A single `effect.run` function interprets effects into real browser calls:

| Effect               | Side effect                              |
| -------------------- | ---------------------------------------- |
| `HttpRequest`        | `gleam_fetch` → typed callback           |
| `LoadFromStore`      | `window.localStorage.getItem`            |
| `SaveToStore`        | `window.localStorage.setItem`            |
| `Redirect`           | `window.location.assign`                 |
| `After(delay, msg)`  | Dispatch `msg` after `delay` ms          |
| `Navigate`           | Click/popstate interception + pushState  |
| `PushUrl/ReplaceUrl` | `history.pushState` / `replaceState`     |
| `Batch([...])`       | Run multiple effects                     |
| `None`               | No-op                                    |

### Bridging into Lustre

The shell converts the inspectable `Effect` into Lustre's opaque effect type:

```gleam
fn update_with_effect(model, msg) -> #(Model, lustre_effect.Effect(Msg)) {
  let #(new_model, effect) = update(model, msg)
  #(new_model, lustre_effect.from(fn(dispatch) {
    effect.run(effect, dispatch)
  }))
}
```

### Per-method HTTP constructors

`effect.gleam` provides thin per-method helpers that delegate to
`http_effect.send`. Callers pre-serialise bodies as `String`:

```gleam
effect.get("/api/todos", callback)
effect.post(url, body, "application/json", callback)
effect.patch(url, body, "application/json", callback)
effect.delete(url, callback)
```

## Client-Side Routing

Routing uses custom navigation effects via browser APIs — no router library:

- `effect.navigate(UrlChanged)` — intercepts clicks on internal links,
  listens for `popstate`, and dispatches the initial URL on page load
- `effect.push_url("/active")` / `effect.replace_url("/completed")` — programmatic navigation
- URL path segments determine visibility: `[]` → All, `["active"]` → Active,
  `["completed"]` → Completed

## When to add a page

| Condition                                     | Pattern                    |
| --------------------------------------------- | -------------------------- |
| Single feature, one concern                   | Add to existing page       |
| New feature with independent state            | New `page.gleam` module    |
| Feature shares state with existing page       | Extend existing page       |
| Global state (auth, theme, user prefs)        | Extend app shell model     |

Rule of thumb: add a new page module when a feature's state has no reason to
live in another page's model, and its messages would bloat an existing `Msg`
type.

## References

- [Lustre docs](https://hexdocs.pm/lustre/)
- [Gleam JavaScript target](https://gleam.run/running/compiling-to-javascript/)
- [vite-gleam plugin](https://github.com/nicdum/vite-gleam)
