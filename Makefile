.PHONY: server-build server-watch format lint client-watch client-build

server-build:
	dotnet build src/ElmishTodos.Server/ElmishTodos.Server.fsproj

server-watch:
	dotnet watch run --project src/ElmishTodos.Server

format:
	dotnet fantomas .

lint:
	dotnet fsharplint lint ElmishTodos.slnx

client-watch:
	dotnet fable watch . --cwd src/ElmishTodos.Client --outDir build --run npx vite

client-build:
	dotnet fable . --cwd src/ElmishTodos.Client --outDir build --run npx vite build
