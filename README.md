# Oxpecker Todos API Example

This repo demonstrates a simple Todo API written in Oxpecker.
The todos are stored in a in-memory mapping that uses an agent (`MailboxProcessor`) for synchronising state updates.
OpenAPI docs are generated semi-automatically and then rendered with Scalar.

## Using This Template

To create a new project from this template, clone the repo and run the setup script:

```bash
git clone https://github.com/example/oxpecker my-project
cd my-project
./setup.sh MyProject
```

This renames all namespaces, modules, project files, directories, and solution file from `ElmishTodos` to `MyProject`.

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
