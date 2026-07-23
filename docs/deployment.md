# Deployment

## Publishing

```bash
make publish                    # linux-x64 (default)
make publish RUNTIME=osx-arm64  # macOS Apple Silicon
```

Builds the client Vite bundle, copies it into `server/src/LustreTodos.Server/wwwroot/`,
then publishes the server as a self-contained single-file binary with trimming.
Output: `server/src/LustreTodos.Server/bin/Release/publish/`

## Docker

### Building

```bash
docker build -t ghcr.io/your-org/lustre-todos:latest .
docker push ghcr.io/your-org/lustre-todos:latest
```

The multi-stage `Dockerfile` installs Node.js and Gleam, builds the client, then
publishes the server into a `debian:stable-slim` runtime image with only `ca-certificates`,
`curl`, and `libicu` installed.

### Deploying

```bash
docker pull ghcr.io/your-org/lustre-todos:latest
docker compose -f docker-compose.prod.yml up -d
```

The compose file ships with dev OIDC defaults. Override for production:

```bash
Oidc__Authority=https://auth.example.com \
Oidc__ClientId=lustre-todos \
Oidc__ClientSecret="$(pass show oidc/lustre-todos/client-secret)" \
Oidc__CallbackPath=/signin-oidc \
Login__ReturnUrl=https://todos.example.com/ \
  docker compose -f docker-compose.prod.yml up -d
```

See [Production OIDC Setup](prod-oidc-setup.md) for the full OIDC configuration guide.

### Database in Production

The server uses SQLite. Override the connection string to use a persistent path:

```bash
ConnectionStrings__Default="Data Source=/data/todos.db" \
  docker compose -f docker-compose.prod.yml up -d
```

Use an absolute path — relative paths resolve to the container's working directory,
which is ephemeral.

## Static Assets

**Client assets** (images, fonts, favicons — anything the SPA references) live in
`client/public/`. Vite serves them at root in dev and copies them into `dist/`
on build. They reach the server via `make copy-client-dist`.

**Server-only assets** (e.g. `robots.txt`) live in `server/src/LustreTodos.Server/wwwroot/`.
The `wwwroot/` directory is gitignored and populated by `copy-client-dist` during
publish. Persistent server assets should have their source of truth elsewhere
(e.g. a build step that copies them in).
