# TODO

- Swap out todo store with SQLite database (Fumble)
- Add favico and setup static file serving
- Create Docker image for release deployment

## OIDC auth via Authelia

### Architecture

Browser talks directly to the .NET server. The server is the OIDC Relying Party — Authelia is
the OIDC Provider. After a one-time login redirect through Authelia, the server issues its own
session cookie. Authelia is out of the request path for all subsequent requests.

```
Browser ──→ .NET Server ──OIDC──→ Authelia
              │                       │
        session cookie         OIDC provider
```

### What's removed

- `src/ElmishTodos.Server/Auth.fs` — deleted, OIDC handler from ASP.NET Core
- `src/ElmishTodos.Server/Middleware.fs` — deleted, auth gating via `[<Authorize>]`
- Demo bearer token — gone entirely

### Server changes (`Program.fs`)

- `AddCookie()` + `AddOpenIdConnect()` from ASP.NET Core
- Auth endpoints:
  - `GET /login` — challenge → redirects to Authelia
  - `GET /logout` — sign out of cookie + OIDC schemes
  - `GET /signin-oidc` — callback, handled automatically by `OpenIdConnectHandler`
- Route groups protected with `[<Authorize>]`
- OIDC config bound from `IConfiguration`

### NuGet additions

- `Microsoft.AspNetCore.Authentication.OpenIdConnect`

### Config

- `appsettings.Development.json` — committed, points at local Authelia
- `appsettings.json` — absent (prod supplies `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret` via env vars)

### Client change (`Api.fs`)

- 401 response → `window.location.assign("/login")`

### Dev infra

- `docker-compose.yml` — off-the-shelf `authelia/authelia` image only
- `authelia/configuration.yml` — OIDC client registration, file-based users, sessions
- `authelia/users.yml` — dev test users
- Server runs bare-metal via `dotnet watch`
