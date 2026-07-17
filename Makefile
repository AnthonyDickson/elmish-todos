.PHONY: server-build server-run server-watch format lint client-watch client-build copy-client-dist publish db-migration db-migrate db-generate db-update db-reset

RUNTIME ?= linux-x64
PUBLISH_DIR ?= server/ElmishTodos.Server/bin/Release/publish

server-build:
	dotnet build server/ElmishTodos.Server/ElmishTodos.Server.fsproj

server-run:
	ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/ElmishTodos.Server

format:
	gleam format
	dotnet fantomas .

lint:
	dotnet fsharplint lint ElmishTodos.slnx

client-watch:
	cd client && npx vite

client-build:
	cd client && gleam build --target javascript && npx vite build

copy-client-dist: client-build
	mkdir -p server/ElmishTodos.Server/wwwroot
	cp -r client/dist/* server/ElmishTodos.Server/wwwroot/

publish: copy-client-dist
	dotnet publish server/ElmishTodos.Server/ElmishTodos.Server.fsproj \
		-c Release -r $(RUNTIME) -o $(PUBLISH_DIR)

# ── Database ──────────────────────────────────────────────────────────────

db-migration:
	@test -n "$(name)" || (echo "Usage: make db-migration name=<name>" >&2; exit 1)
	@echo "-- $(name)" > server/ElmishTodos.Server/migrations/$$(printf "%03d" $$(( $$(ls server/ElmishTodos.Server/migrations/*.sql 2>/dev/null | wc -l) + 1 )))_$(name).sql
	@echo "Created migration: migrations/$$(ls -t server/ElmishTodos.Server/migrations/*.sql | head -1 | xargs basename)"

db-migrate:
	dotnet fsi scripts/migrate.fsx

db-generate:
	dotnet sqlhydra sqlite --project server/ElmishTodos.Server/ElmishTodos.Server.fsproj

db-update: db-migrate db-generate

db-reset:
	rm -f server/ElmishTodos.Server/todos.db
	$(MAKE) db-update
