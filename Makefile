.PHONY: server-build server-watch server-test \
		client-install-deps client-watch client-build client-test \
		copy-client-dist publish format lint e2e-test \
		db-migration db-migrate db-generate db-update db-reset

RUNTIME ?= linux-x64
PUBLISH_DIR ?= server/src/LustreTodos.Server/bin/Release/publish

server-build:
	dotnet build server/src/LustreTodos.Server/LustreTodos.Server.fsproj

server-watch:
	ASPNETCORE_ENVIRONMENT=Development dotnet watch run --project server/src/LustreTodos.Server --no-hot-reload

server-test:
	dotnet test server/tests/LustreTodos.Server.Tests

client-install-deps:
	cd client && npm install

client-test:
	cd client && gleam test

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

format:
	gleam format
	cd server && dotnet fantomas .

lint:
	cd server && dotnet fsharplint lint LustreTodos.slnx

e2e-test:
	docker compose -f docker-compose.e2e.yml up --abort-on-container-exit --exit-code-from e2e --remove-orphans

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
