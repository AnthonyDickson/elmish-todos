# Server Tests

## Architecture

Tests live in `server/tests/LustreTodos.Server.Tests/` and use xUnit with a
custom `TestApp` fixture that builds a minimal ASP.NET Core host.

### Test host (`TestApp.fs`)

Uses `HostBuilder` + `UseTestServer()` (the .NET 10 replacement for the
deprecated `WebHostBuilder`). The pattern comes from
[Oxpecker's own test suite](https://github.com/Lanayx/Oxpecker/blob/develop/tests/Oxpecker.Tests/Routing.Tests.fs).

The host contains only what the endpoints need:

- SQLite (temp file, auto-cleaned)
- Routing + Oxpecker middleware
- `Api.endpoints` — directly, **not** `Todos.endpoints`

No auth, no OIDC, no static files, no OpenAPI.

### Why `Api.endpoints` not `Todos.endpoints`

`Todos.endpoints` wraps every endpoint with `addFilter Auth.requireAuth`. The
auth filter is an Oxpecker middleware that calls `IAuthorizationService`, which
requires the full OIDC/JWT setup from `Program.fs`. Since the test host has
none of that, calling `Todos.endpoints` would crash on the first request.

The fix: `requireAuth` lives in the `Todos` facade module (composition layer),
not in `Api.endpoints` (handler layer). Tests call `Api.endpoints` directly and
bypass auth entirely. The production `Program.fs` calls `Todos.endpoints` and
gets the auth filter.

### Fixture lifecycle

`TestApp` implements `IDisposable` — creates a temp SQLite file, applies
migrations once, and deletes the file on dispose. xUnit's `IClassFixture<TestApp>`
shares one instance across all tests in the class.

Each test calls `fixture.CleanDatabase()` to reset state.

## Test cases

One per endpoint, happy path only:

| Test                                      | Method | Endpoint               | Asserts                     |
| ----------------------------------------- | ------ | ---------------------- | --------------------------- |
| `GET /api/todos returns empty list`       | GET    | `/api/todos`           | 200, `[]`                   |
| `GET /api/todos returns seeded todos`     | GET    | `/api/todos`           | 200, seeded item in array   |
| `GET /api/todos/{id} returns the todo`    | GET    | `/api/todos/{id}`      | 200, matches seeded item    |
| `GET /api/todos/{id} returns 404`         | GET    | `/api/todos/{id}`      | 404                         |
| `POST /api/todos creates a todo`          | POST   | `/api/todos`           | 201, matches input          |
| `PATCH /api/todos/{id} updates a todo`    | PATCH  | `/api/todos/{id}`      | 200, reflects changes       |
| `DELETE /api/todos/{id} removes the todo` | DELETE | `/api/todos/{id}`      | 204, 404 on re-GET          |
| `DELETE /api/todos/completed`             | DELETE | `/api/todos/completed` | 204, incomplete-only remain |
