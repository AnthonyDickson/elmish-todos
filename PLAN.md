# TODO

- Swap out todo store with SQLite database (Fumble)
- Add favico and setup static file serving
- Create Docker image for release deployment

## OIDC auth via Authelia

### Architecture

Two auth flows coexist:

| Client          | Auth Type      | Flow                                                                         |
| --------------- | -------------- | ---------------------------------------------------------------------------- |
| SPA (browser)   | Session cookie | Server is confidential OIDC client → cookie after callback                   |
| Scalar API docs | JWT bearer     | Scalar is public OIDC client → PKCE → access token → `Authorization: Bearer` |

Server validates both: `AddCookie()` for the SPA, `AddJwtBearer()` for Scalar. Each scheme
can independently satisfy `[<Authorize>]`.

```
Browser ──→ .NET Server ──OIDC──→ Authelia
(Scalar)       │                       │
        cookie + bearer         OIDC provider
```

### Done

- [x] `docker-compose.yml` — off-the-shelf `authelia/authelia:4.39.20`
- [x] `authelia/configuration.yml` — TLS (self-signed), OIDC clients (`elmish-todos` + `scalar-docs`), file users, sessions
- [x] `authelia/users.yml` — dev test user (`dev` / `dev-password`)
- [x] `authelia/certs/` — self-signed cert for `127.0.0.1`
- [x] Scalar OAuth2 PKCE integration — `scalar-docs` public client, dev-only UI config
- [x] `Microsoft.AspNetCore.Authentication.OpenIdConnect` NuGet package added
- [x] README documented

### Remaining

#### Server

- Remove `src/ElmishTodos.Server/Auth.fs` — replaced by ASP.NET Core OIDC + JWT handlers
- Remove `src/ElmishTodos.Server/Middleware.fs` — auth gating via inline middleware
- Create `appsettings.Development.json` pointing at local Authelia
- Add `Microsoft.AspNetCore.Authentication.JwtBearer` NuGet package
- Rewrite `Program.fs`:
  - `AddCookie()` + `AddOpenIdConnect()` — SPA auth (cookie flow)
  - `AddJwtBearer()` — Scalar auth (bearer flow), validates against Authelia JWKS
  - `GET /login` — challenge → redirects to Authelia
  - `GET /logout` — sign out of cookie + OIDC schemes
  - `GET /signin-oidc` — callback, handled automatically by `OpenIdConnectHandler`
  - Policy-based authorization accepting both cookie + bearer schemes
- Replace `Middleware.requireAuthenticated` with inline auth guard in `Todos.fs`

#### Client

- `Api.fs` — 401 response → `window.location.assign("/login")`

#### Config

- `appsettings.Development.json` — committed, points at local Authelia
- `appsettings.json` — absent (prod supplies `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret` via env vars)
