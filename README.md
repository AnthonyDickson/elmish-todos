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

```bash
# Build
dotnet build

# Run
dotnet run --project src/ElmishTodos

# Format code (fantomas)
dotnet fantomas .

# Lint
dotnet fsharplint lint ElmishTodos.slnx
```

### Development Environment

The project uses a Nix flake providing `.NET SDK 10`, `fsautocomplete` (LSP), and `dprint` (markdown formatting):

```bash
nix develop   # or direnv allow if direnv is configured
```

Local .NET tools (fantomas, fsharplint) are defined in `.config/dotnet-tools.json`. The flake's `shellHook` runs `dotnet tool restore` automatically.
