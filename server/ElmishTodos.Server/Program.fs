module ElmishTodos.Server.Program

open System
open System.Collections.Generic
open System.ComponentModel.DataAnnotations
open System.Reflection
open System.Threading.Tasks

open DbUp
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.OpenApi
open Oxpecker
open Oxpecker.OpenApi
open Scalar.AspNetCore

open ElmishTodos.Server.ApiError
open ElmishTodos.Server.Auth
open ElmishTodos.Server.Config
open ElmishTodos.Server.Json
open ElmishTodos.Server.OpenApi
open ElmishTodos.Server.Todos


let private addOpenApiToBuilder (builder : WebApplicationBuilder) (oauth2 : OAuth2Options) =
    let oauth2AuthUrl = oauth2.AuthorizationUrl
    let oauth2TokenUrl = oauth2.TokenUrl

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

            doc.Security <- ResizeArray [ Auth.oauthRequirement doc ]

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

let private handleException (loggerFactory : ILoggerFactory) (next : RequestDelegate) : RequestDelegate =
    let logger = loggerFactory.CreateLogger "ElmishTodos.Server.Program"

    RequestDelegate (fun (ctx : HttpContext) ->
        task {
            try
                return! next.Invoke ctx
            with ex ->
                logger.LogError (ex, "An unhandled exception occurred")

                ctx.Response.StatusCode <- 500

                return!
                    Json.write ctx {
                        Error = "Internal Server Error"
                        Details = "An unexpected error occurred"
                        StatusCode = Some 500
                    }
        }
        :> Task)

let private readSection<'T when 'T : not struct and 'T : (new : unit -> 'T)>
    (services : IServiceCollection)
    (config : IConfiguration)
    (sectionName : string)
    =
    let section = config.GetSection sectionName
    let value = new 'T ()
    section.Bind value

    let results = ResizeArray<ValidationResult> ()

    if not (Validator.TryValidateObject (value, ValidationContext value, results, validateAllProperties = true)) then
        let messages =
            results
            |> Seq.map (fun r ->
                let names =
                    if r.MemberNames |> Seq.isEmpty then
                        sectionName
                    else
                        r.MemberNames |> String.concat ", "

                $"  - {names}: {r.ErrorMessage}")
            |> String.concat "\n"

        failwithf
            "Configuration validation failed for section '%s':\n%s\n\nEnsure the required settings are present in appsettings or environment variables."
            sectionName
            messages

    services.AddOptions<'T>().Bind(section).ValidateDataAnnotations().ValidateOnStart ()
    |> ignore

    value


module private Health =
    let handler : EndpointHandler =
        fun ctx -> task { return! ctx.Response.WriteAsync "OK" }

    let endpoints () = [
        GET [
            route "/health" handler
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [| ResponseBody (typeof<string>, statusCode = 200) |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Check whether the API is healthy"
                            op.Security <- ResizeArray ()
                            Task.CompletedTask
                )
            )
        ]
    ]

[<EntryPoint>]
let main (args : string array) : int =
    let builder = WebApplication.CreateBuilder args

    let oidc = readSection<OidcOptions> builder.Services builder.Configuration "Oidc"

    let oauth2 =
        readSection<OAuth2Options> builder.Services builder.Configuration "OAuth2"

    let login = readSection<LoginOptions> builder.Services builder.Configuration "Login"

    Auth.configureServices builder.Services (builder.Environment.IsDevelopment ()) oidc

    builder.Services.AddRouting().AddOxpecker () |> ignore

    if builder.Environment.IsDevelopment () then
        addOpenApiToBuilder builder oauth2

    let app = builder.Build ()

    let connectionString = app.Configuration.GetConnectionString "Default"
    applyMigrations connectionString

    if app.Environment.IsDevelopment () then
        addOpenApiToApp app

    let loginReturnUrl = login.ReturnUrl |> Option.ofObj |> Option.defaultValue "/"

    let authEndpoints = Auth.endpoints loginReturnUrl
    let todoStore = Todos.Store.create connectionString
    let todoEndpoints = Todos.endpoints todoStore
    let allEndpoints = Seq.concat [ authEndpoints; todoEndpoints; Health.endpoints () ]

    app.Use (handleException (app.Services.GetRequiredService<ILoggerFactory> ()))
    |> ignore

    app.UseStaticFiles () |> ignore
    app.UseRouting () |> ignore
    app.UseAuthentication () |> ignore
    app.UseAuthorization () |> ignore
    app.UseOxpecker allEndpoints |> ignore

    // Run the vite dev server for accessing the SPA bundle.
    if not (app.Environment.IsDevelopment ()) then
        app.MapFallbackToFile "index.html" |> ignore

    app.Run ()
    0
