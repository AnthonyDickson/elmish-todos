# TODO

- Create Docker image for release deployment
  - Multi-stage build: SDK → build client + server + publish above
  - Final stage: `FROM gcr.io/distroless/static-debian12` (or `alpine` with `linux-musl-x64`)
  - Copy `ElmishTodos.Server`, `wwwroot` and `ElmishTodos.Server.staticwebassets.endpoints.json`, set `ENTRYPOINT`
- Centralise publish flags in fsproj
- Add test suite
- Check whether the code is "production ready"
