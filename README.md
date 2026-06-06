# Elmish Todos

A port of [TodoMVC in Elm](https://github.com/evancz/elm-todomvc/) (which is based on
[TodoMVC](https://github.com/tastejs/todomvc)) in F# using Elmish and Feliz.

## Getting Started

All commands run from the repo root.

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

A `make` (or `make build`) runs the default `build` target.

### Development Environment

The project uses a Nix flake providing `.NET SDK 10`, `fsautocomplete` (LSP), and `dprint` (markdown formatting):

```bash
nix develop   # or direnv allow if direnv is configured
```

Local .NET tools (fantomas, fsharplint, fable) are defined in `.config/dotnet-tools.json`. The flake's `shellHook` runs `dotnet tool restore` automatically.
