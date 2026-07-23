# Lustre Todos

A TodoMVC port with an F#/Oxpecker backend (SQLite + OIDC auth + OpenAPI)
and a Gleam/Lustre SPA frontend (Tailwind CSS v4, Vite).
This repo is intended to serve as a template for larger projects.

## Getting Started

Using Docker:

```bash
docker compose up
```

Or natively:

```bash
make server-build    # Build the server
make server-watch    # Run at :5000 (auto-creates SQLite DB + applies migrations)

# In a second terminal:
make client-install-deps # First time only
make client-watch        # Vite dev server at :5173
```

See the [Makefile](Makefile) for all targets.

## How It Works

- **Backend** — Oxpecker on .NET 10 with OIDC auth (cookie + JWT bearer).
  Endpoints live in a single vertical slice (`Todos.fs`). SQLite with DbUp
  migrations and SqlHydra type-safe queries. See [Database](docs/database.md).
- **Frontend** — Gleam/Lustre SPA with nested MVU. A custom `Effect` type
  keeps `update` pure — all I/O (HTTP, localStorage, navigation) runs through
  one interpreter. See [Client Architecture](docs/client-architecture.md).
- **Auth** — Dev OIDC via Authelia (`docker compose up -d`, test user
  `dev`/`dev-password`). See [Production OIDC Setup](docs/prod-oidc-setup.md).
- **Testing** — xUnit server tests, gleeunit client unit tests, Playwright E2E
  tests via Docker Compose. See `docs/server-tests.md`, `docs/client-architecture.md#client-tests`, and `docs/e2e-tests.md`.
- **Deployment** — Single-file publish (`make publish`) or Docker
  (`docker build` + `docker compose -f docker-compose.prod.yml up -d`).
  See [Deployment](docs/deployment.md).

## Dev Environment

Nix flake (.NET SDK 10, fsautocomplete, dprint). NuGet Central Package
Management — versions in `Directory.Packages.props`.

## Docs

- [Client Architecture](docs/client-architecture.md)
- [Database](docs/database.md)
- [Deployment](docs/deployment.md)
- [JSON Serialization](docs/json-serialization.md)
- [Production OIDC Setup](docs/prod-oidc-setup.md)
- [Server Tests](docs/server-tests.md)
- [E2E Tests](docs/e2e-tests.md)
