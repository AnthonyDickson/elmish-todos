.PHONY: server-build server-run server-watch format lint client-install-deps client-watch client-build copy-client-dist publish db-migration db-migrate db-generate db-update db-reset

RUNTIME ?= linux-x64
PUBLISH_DIR ?= server/src/LustreTodos.Server/bin/Release/publish

server-build:
	dotnet build server/src/LustreTodos.Server/LustreTodos.Server.fsproj

server-run:
	ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/src/LustreTodos.Server

format:
	gleam format
	cd server && dotnet fantomas .

lint:
	cd server && dotnet fsharplint lint LustreTodos.slnx

client-install-deps:
	cd client && npm install

client-watch:
	cd client && npx vite

client-build:
	cd client && npx vite build

copy-client-dist: client-build
	mkdir -p server/src/LustreTodos.Server/wwwroot
	cp -r client/dist/* server/src/LustreTodos.Server/wwwroot/

publish: copy-client-dist
	dotnet publish server/src/LustreTodos.Server/LustreTodos.Server.fsproj \
		-c Release -r $(RUNTIME) -o $(PUBLISH_DIR) \
		-p:PublishTrimmed=true -p:TrimMode=partial

# ── Database ──────────────────────────────────────────────────────────────

db-migration:
	@test -n "$(name)" || (echo "Usage: make db-migration name=<name>" >&2; exit 1)
	@echo "-- $(name)" > server/src/LustreTodos.Server/migrations/$$(printf "%03d" $$(( $$(ls server/src/LustreTodos.Server/migrations/*.sql 2>/dev/null | wc -l) + 1 )))_$(name).sql
	@echo "Created migration: migrations/$$(ls -t server/src/LustreTodos.Server/migrations/*.sql | head -1 | xargs basename)"

db-migrate:
	cd server && dotnet fsi scripts/migrate.fsx

db-generate:
	cd server && dotnet sqlhydra sqlite --project src/LustreTodos.Server/LustreTodos.Server.fsproj

db-update: db-migrate db-generate

db-reset:
	rm -f server/src/LustreTodos.Server/todos.db
	$(MAKE) db-update
