module ElmishTodos.Server.Program

open System.Collections.Generic
open System.Threading.Tasks

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi
open Oxpecker
open Oxpecker.OpenApi
open Scalar.AspNetCore

open ElmishTodos.Server.Auth
open ElmishTodos.Server.OpenApi
open ElmishTodos.Server.Todos


[<EntryPoint>]
let main (args : string array) : int =
    let builder = WebApplication.CreateBuilder args

    builder.Services
        .AddAuthentication(Auth.DemoScheme)
        .AddScheme<AuthenticationSchemeOptions, Auth.DemoBearerAuthHandler>(Auth.DemoScheme, ignore)
        .Services.AddAuthorization()
        .AddRouting()
        .AddOxpecker()
        .AddOpenApi (fun options ->
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
                        Description = $"Demo bearer token. Use `{Auth.DemoToken}`."
                    )

                if builder.Environment.IsDevelopment () then
                    doc.Components.SecuritySchemes["scalarOAuth2"] <-
                        OpenApiSecurityScheme (
                            Type = SecuritySchemeType.OAuth2,
                            Flows = OpenApiOAuthFlows (
                                AuthorizationCode = OpenApiOAuthFlow (
                                    AuthorizationUrl = System.Uri "https://127.0.0.1:9091/api/oidc/authorization",
                                    TokenUrl = System.Uri "https://127.0.0.1:9091/api/oidc/token"
                                )
                            )
                        )

                Task.CompletedTask)
            |> ignore)
    |> ignore

    let app = builder.Build ()

    app.MapOpenApi () |> ignore

    app.MapScalarApiReference (fun opts ->
        opts
            .WithTitle("ElmishTodos API")
            .WithTheme(ScalarTheme.DeepSpace)
            .WithDefaultHttpClient (ScalarTarget.Http, ScalarClient.Curl)
        |> ignore

        if app.Environment.IsDevelopment () then
            opts
                .AddPreferredSecuritySchemes([| "scalarOAuth2" |])
                .AddAuthorizationCodeFlow("scalarOAuth2", fun flow ->
                    flow.ClientId <- "scalar-docs"
                    flow.Pkce <- Pkce.Sha256
                    flow.SelectedScopes <- [| "openid"; "profile"; "email" |])
            |> ignore)
    |> ignore

    let todoEndpoints = Todos.startStore () |> Todos.endpoints

    app.UseRouting () |> ignore
    app.UseAuthentication () |> ignore
    app.UseAuthorization () |> ignore
    app.UseOxpecker todoEndpoints |> ignore
    app.Run ()
    0
