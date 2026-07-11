# TODO

- Add favico and setup static file serving
- Centralise config and parse into record
- Add test suite
- Bundle client into server binary (single-file publish)
  - Enable `StaticWebAssetsEnabled` in `.fsproj`
  - Add MSBuild target to copy client `dist/` into `wwwroot/` before build
  - Wire `UseStaticFiles` + `MapFallbackToFile("index.html")` in `Program.fs`
  - Publish: `dotnet publish -c Release --self-contained -r linux-x64 -p:PublishSingleFile=true`
- Create Docker image for release deployment
  - Multi-stage build: SDK → build client + server + publish above
  - Final stage: `FROM gcr.io/distroless/static-debian12` (or `alpine` with `linux-musl-x64`)
  - Copy single binary, set `ENTRYPOINT`, no shell or runtime needed
- Check whether the code is "production ready"
