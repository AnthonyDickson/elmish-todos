# Elmish Todos

A port of [TodoMVC in Elm](https://github.com/evancz/elm-todomvc/) (which is based on
[TodoMVC](https://github.com/tastejs/todomvc)) in F# using Elmish and Feliz.

## Getting Started

All commands run from the repo root.

```bash
# Build the server
make server-build

# Run the server
make server-run
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

### Authelia (Dev-only OIDC Provider)

> [!WARNING]
> **Development only.** Authelia, its config, certs, secrets, and users are a local dev fixture.
> In production, configure a real OIDC provider via the `Oidc__*` environment variables (see below).
> None of the files under `authelia/`, `docker-compose.yml`, or `appsettings.Development.json` are used in production.

The server uses OpenID Connect for authentication. In development, [Authelia](https://www.authelia.com/) acts as a local OIDC provider via Docker Compose with file-based users.

Two OIDC clients are registered — one for the SPA (cookie flow) and one for Scalar API docs (PKCE bearer flow):

|              | SPA                                   | Scalar                                              |
| ------------ | ------------------------------------- | --------------------------------------------------- |
| Client ID    | `elmish-todos`                        | `scalar-docs`                                       |
| Client type  | Confidential                          | Public (PKCE)                                       |
| Auth flow    | Authorization code → session cookie   | Authorization code + PKCE → `Authorization: Bearer` |
| Redirect URI | `http://localhost:5000/signin-oidc`   | `http://localhost:5000/scalar/v1`                   |
| Scopes       | `openid profile email offline_access` | `openid profile email`                              |

The server validates both: `AddCookie()` for the SPA's session and `AddJwtBearer()` for Scalar's tokens.
Either scheme satisfies the auth requirement on protected endpoints.

**Start Authelia:**

```bash
docker compose up -d
```

Authelia will be available at `https://127.0.0.1:9091` (self-signed certificate).
The OIDC discovery document is at
`https://127.0.0.1:9091/.well-known/openid-configuration`.

**Test user:**

|          |                |
| -------- | -------------- |
| Username | `dev`          |
| Password | `dev-password` |

**Configuration:**

| Setting             | Value                     | Source                         |
| ------------------- | ------------------------- | ------------------------------ |
| `Oidc:Authority`    | `https://127.0.0.1:9091`  | `appsettings.Development.json` |
| `Oidc:ClientId`     | `elmish-todos`            | `appsettings.Development.json` |
| `Oidc:ClientSecret` | `elmish-todos-dev-secret` | `appsettings.Development.json` |
| `Oidc:CallbackPath` | `/signin-oidc`            | `appsettings.Development.json` |

In production, these are supplied via environment variables (`Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret`).

**Auth endpoints** (on the .NET server, port 5000):

| Endpoint           | Purpose                                                |
| ------------------ | ------------------------------------------------------ |
| `GET /login`       | Initiates OIDC challenge → redirects to Authelia login |
| `GET /logout`      | Signs out of the cookie session                        |
| `GET /signin-oidc` | OIDC callback (handled by `OpenIdConnectHandler`)      |

**Protected endpoints** return `401` with `{ "error": "Unauthorized", "statusCode": 401 }`.
The SPA redirects to `/login` on receipt.

**Files (development only):**

```
docker-compose.yml            # Authelia service (dev only)
authelia/
  configuration.yml           # TLS, session, OIDC clients, file user backend (dev only)
  users.yml                   # Dev user credentials, argon2 hashed (dev only)
  certs/                      # Self-signed TLS cert + key for 127.0.0.1 (dev only)
src/ElmishTodos.Server/
  appsettings.Development.json  # OIDC config for dev (dev only)
```

**Stopping Authelia:**

```bash
docker compose down
```

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
