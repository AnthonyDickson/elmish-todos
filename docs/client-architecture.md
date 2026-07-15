# Client Architecture

## Component-level vs. top-level Elmish

`App.fs` uses a single `React.useElmish` component that owns all state:

```fsharp
type Model = { TodoPage : TodoPage.Model }
type Msg = TodoPageMsg of TodoPage.Msg
```

This requires wrapping child models and messages in the parent's types, and
forwarding via `Cmd.map` in `update`. Each new feature adds a variant to `Msg`,
a field to `Model`, and a branch in `update`. This scales linearly.

More importantly, a single top-level Elmish component re-executes the entire
`view` function on every `dispatch`. The VDOM prevents unnecessary DOM
mutations, but the `ReactElement` tree is rebuilt in full each time. There are
no component boundaries for React to short-circuit at. This is negligible at
small scale but material when unrelated features (e.g. a chart and a search
bar) share a parent — every keystroke in the search bar triggers a full tree
rebuild.

Splitting features into separate `React.useElmish` components solves both
problems: no message/model wrapping, and React can skip both the F# view
computation and the VDOM diff for subtrees whose props haven't changed.

### Composition patterns

**Parent → child (props + callbacks)**

```fsharp
[<ReactComponent>]
let SearchBar (onSearch: string -> unit) =
    let state, dispatch = React.useElmish(SearchBar.init, SearchBar.update, [||])
    Html.div [
        Html.input [
            prop.onChange (fun (v: string) -> dispatch (SearchBar.SetQuery v))
        ]
        Html.button [
            prop.onClick (fun _ ->
                dispatch SearchBar.Search
                onSearch state.Query
            )
        ]
    ]
```

The parent passes a callback. No knowledge of `SearchBar.Msg` or
`SearchBar.Model`.

**Siblings (lifted state)**

```fsharp
[<ReactComponent>]
let App () =
    let filter, setFilter = React.useState ""
    Html.div [
        SearchBar(onSearch = setFilter)
        TodoList(filter = filter)
    ]
```

Use React's `useState` for simple shared values between sibling Elmish
components.

**Global state (React Context)**

```fsharp
let authContext = React.createContext<AuthState>("auth")

[<ReactComponent>]
let App () =
    let auth, dispatch = React.useElmish(Auth.init, Auth.update, [||])
    React.contextProvider(authContext, auth, [
        Router()
        Header()
        MainContent()
    ])

[<ReactComponent>]
let MainContent () =
    let auth = React.useContext(authContext)
    if auth.IsLoggedIn then TodoApp() else LoginPrompt()
```

Only the consuming component re-renders on context change.

## When to split

| Condition                                            | Pattern                         |
| ---------------------------------------------------- | ------------------------------- |
| Single page, one concern                             | Top-level `useElmish` (current) |
| Multiple features with independent state             | Multiple `useElmish` components |
| Siblings sharing a filter/sort value                 | Lifted state with `useState`    |
| Auth, theme, user prefs (global, infrequent updates) | React Context                   |

Rule of thumb: add a new `useElmish` component when a feature's state has no
reason to live in the parent's model.

## References

- [Fable blog: Elmish 4 + React.useElmish](https://fable.io/blog/2022/2022-10-13-use-elmish.html)
- [Feliz UseElmish docs](https://fable-hub.github.io/Feliz/ecosystem/Hooks/Feliz.UseElmish)
- [Optimising F# and React integration with Elmish Store](https://dev.to/lkrzywizna/optimizing-f-and-react-integration-with-elmish-store-a-guide-to-efficient-state-management-316m)
