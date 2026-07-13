module ElmishTodos.Server.Program

open System.Collections.Generic
open System.Reflection
open System.Threading.Tasks

open DbUp
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi
open Oxpecker
open Oxpecker.OpenApi
open Scalar.AspNetCore

open ElmishTodos.Shared.ApiError
open ElmishTodos.Server.Auth
open ElmishTodos.Server.Json
open ElmishTodos.Server.OpenApi
open ElmishTodos.Server.Todos


let private addOpenApiToBuilder (builder : WebApplicationBuilder) =
    let oauth2AuthUrl = builder.Configuration["OAuth2:AuthorizationUrl"]
    let oauth2TokenUrl = builder.Configuration["OAuth2:TokenUrl"]

    builder.Services.AddOpenApi (fun options ->
        options.AddSchemaTransformer<FSharpOptionSchemaTransformer> () |> ignore
        options.AddSchemaTransformer<OpenApi.FSharpRecordSchemaTransformer> () |> ignore
        options.AddSchemaTransformer<OpenApi.XmlDocSchemaTransformer> () |> ignore

        options.AddDocumentTransformer (fun doc _ _ ->
            if isNull doc.Components then
                doc.Components <- OpenApiComponents ()

            if isNull doc.Components.SecuritySchemes then
                doc.Components.SecuritySchemes <- Dictionary<string, IOpenApiSecurityScheme> ()

            doc.Components.SecuritySchemes["bearerAuth"] <-
                OpenApiSecurityScheme (
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT access token from Authelia. Use the Scalar OAuth2 flow to obtain one."
                )

            doc.Components.SecuritySchemes["scalarOAuth2"] <-
                OpenApiSecurityScheme (
                    Type = SecuritySchemeType.OAuth2,
                    Flows =
                        OpenApiOAuthFlows (
                            AuthorizationCode =
                                OpenApiOAuthFlow (
                                    AuthorizationUrl = System.Uri oauth2AuthUrl,
                                    TokenUrl = System.Uri oauth2TokenUrl
                                )
                        )
                )

            Task.CompletedTask)
        |> ignore)
    |> ignore

let private addOpenApiToApp (app : WebApplication) =
    app.MapOpenApi () |> ignore

    app.MapScalarApiReference (fun opts ->
        opts
            .WithTitle("ElmishTodos API")
            .WithTheme(ScalarTheme.DeepSpace)
            .WithDefaultHttpClient (ScalarTarget.Http, ScalarClient.Curl)
        |> ignore

        opts
            .AddPreferredSecuritySchemes([| "scalarOAuth2" |])
            .AddAuthorizationCodeFlow (
                "scalarOAuth2",
                fun flow ->
                    flow.ClientId <- "scalar-docs"
                    flow.Pkce <- Pkce.Sha256
                    flow.SelectedScopes <- [| "openid"; "profile"; "email" |]
            )
        |> ignore)
    |> ignore

let private applyMigrations (connectionString : string) =
    let result =
        DeployChanges.To
            .SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly ())
            .LogToConsole()
            .Build()
            .PerformUpgrade ()

    if not result.Successful then
        failwithf "Database migration failed: %O" result.Error

    use conn = new SqliteConnection (connectionString)
    conn.Open ()

    use walCmd = conn.CreateCommand ()
    walCmd.CommandText <- "PRAGMA journal_mode = WAL"
    walCmd.ExecuteNonQuery () |> ignore

    use fkCmd = conn.CreateCommand ()
    fkCmd.CommandText <- "PRAGMA foreign_keys = ON"
    fkCmd.ExecuteNonQuery () |> ignore

let private handleException (next : RequestDelegate) : RequestDelegate =
    RequestDelegate (fun (ctx : HttpContext) ->
        task {
            try
                return! next.Invoke ctx
            with ex ->
                ctx.Response.StatusCode <- 500

                return!
                    Json.write ctx {
                        Error = "Internal Server Error"
                        Details = "An unexpected error occurred"
                        StatusCode = Some 500
                    }
        }
        :> Task)

[<EntryPoint>]
let main (args : string array) : int =
    let builder = WebApplication.CreateBuilder args

    Auth.configureServices builder

    builder.Services.AddRouting().AddOxpecker () |> ignore

    if builder.Environment.IsDevelopment () then
        addOpenApiToBuilder builder

    let app = builder.Build ()

    let connectionString = app.Configuration.GetConnectionString "Default"
    applyMigrations connectionString

    if app.Environment.IsDevelopment () then
        addOpenApiToApp app

    let loginReturnUrl =
        app.Configuration["Login:ReturnUrl"] |> Option.ofObj |> Option.defaultValue "/"

    let authEndpoints = Auth.endpoints loginReturnUrl
    let todoStore = Todos.Store.create connectionString
    let todoEndpoints = Todos.endpoints todoStore
    let allEndpoints = Seq.concat [ authEndpoints; todoEndpoints ]

    app.Use handleException |> ignore
    app.UseStaticFiles(StaticFileOptions(RequestPath = "/static")) |> ignore
    app.UseRouting () |> ignore
    app.UseAuthentication () |> ignore
    app.UseAuthorization () |> ignore
    app.UseOxpecker allEndpoints |> ignore
    app.Run ()
    0
