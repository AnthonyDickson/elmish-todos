FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

RUN apt-get update \
    && apt-get install -y ca-certificates curl gnupg make \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_24.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install Gleam
RUN curl -fsSL https://github.com/gleam-lang/gleam/releases/download/v1.17.0/gleam-v1.17.0-x86_64-unknown-linux-musl.tar.gz | tar -xz -C /usr/local/bin

WORKDIR /src

COPY ./dotnet-tools.json ./
RUN dotnet tool restore

COPY *.props *.targets ./
COPY LustreTodos.slnx ./
COPY src/LustreTodos.Shared/*.fsproj src/LustreTodos.Shared/
COPY src/LustreTodos.Server/*.fsproj src/LustreTodos.Server/
RUN dotnet restore LustreTodos.slnx

COPY src/lustre_todos_client/package*.json src/lustre_todos_client/
RUN npm ci --prefix src/lustre_todos_client

COPY ./Makefile ./
COPY ./src ./src
ARG RUNTIME=linux-x64
RUN make publish RUNTIME=${RUNTIME} PUBLISH_DIR=/publish

FROM debian:stable-slim AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl libicu76 \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 5000

COPY --from=build /publish/LustreTodos.Server /app/
COPY --from=build /publish/wwwroot/ /app/wwwroot/
COPY --from=build /publish/LustreTodos.Server.staticwebassets.endpoints.json /app/

WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENTRYPOINT ["./LustreTodos.Server"]
