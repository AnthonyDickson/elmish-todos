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

### Adding Dependencies

This project uses NuGet's [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) (CPM).
All package versions live in `Directory.Packages.props` — project files reference them by name only, with no version attribute.

```xml
<!-- Directory.Packages.props — single source of truth -->
<Project>
  <ItemGroup>
    <PackageVersion Include="Oxpecker" Version="2.0.0" />
    <PackageVersion Include="Fable.Core" Version="5.0.0" />
    <!-- … -->
  </ItemGroup>
</Project>
```

```xml
<!-- In .fsproj files — name only, version comes from Directory.Packages.props -->
<PackageReference Include="Oxpecker" />
```

**To add a new dependency:**

```bash
# dotnet add package handles CPM — it adds the PackageReference
# to the .fsproj and the PackageVersion to Directory.Packages.props
dotnet add src/ElmishTodos.Server/ElmishTodos.Server.fsproj package SomePackage
```

The CLI writes the version into `Directory.Packages.props` and a bare `<PackageReference>` into the `.fsproj`.
To upgrade a version, edit `Directory.Packages.props` directly and run `dotnet restore --force-evaluate --project <project>`.

The shared project (`src/ElmishTodos.Shared/`) contains types used by both client and server.
It uses conditional `PackageReference` with `FABLE_COMPILER` to select `Thoth.Json` (client) or `Thoth.Json.Net` (server) — both versions are defined in `Directory.Packages.props`.
