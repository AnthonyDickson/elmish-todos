.PHONY: server-build server-run server-watch format lint client-watch client-build db-migration db-migrate db-generate db-update db-reset

server-build:
	dotnet build src/ElmishTodos.Server/ElmishTodos.Server.fsproj

server-run:
	ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/ElmishTodos.Server

server-watch:
	ASPNETCORE_ENVIRONMENT=Development dotnet watch run --project src/ElmishTodos.Server

format:
	dotnet fantomas .

lint:
	dotnet fsharplint lint ElmishTodos.slnx

client-watch:
	dotnet fable watch . --cwd src/ElmishTodos.Client --outDir build --run npx vite

client-build:
	dotnet fable . --cwd src/ElmishTodos.Client --outDir build --run npx vite build

# ── Database ──────────────────────────────────────────────────────────────

db-migration:
	@test -n "$(name)" || (echo "Usage: make db-migration name=<name>" >&2; exit 1)
	@echo "-- $(name)" > src/ElmishTodos.Server/migrations/$$(printf "%03d" $$(( $$(ls src/ElmishTodos.Server/migrations/*.sql 2>/dev/null | wc -l) + 1 )))_$(name).sql
	@echo "Created migration: migrations/$$(ls -t src/ElmishTodos.Server/migrations/*.sql | head -1 | xargs basename)"

db-migrate:
	dotnet fsi scripts/migrate.fsx

db-generate:
	dotnet sqlhydra sqlite --project src/ElmishTodos.Server/ElmishTodos.Server.fsproj

db-update: db-migrate db-generate

db-reset:
	rm -f src/ElmishTodos.Server/todos.db
	$(MAKE) db-update
