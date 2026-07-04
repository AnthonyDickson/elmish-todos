module ElmishTodos.Server.Program

open System.Collections.Generic
open System.Threading.Tasks

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

[<EntryPoint>]
let main (args : string array) : int =
    let builder = WebApplication.CreateBuilder args

    Auth.configureServices builder

    if builder.Environment.IsDevelopment () then
        addOpenApiToBuilder builder

    let app = builder.Build ()

    if app.Environment.IsDevelopment () then
        addOpenApiToApp app

    let loginReturnUrl =
        app.Configuration["Login:ReturnUrl"] |> Option.ofObj |> Option.defaultValue "/"

    let authEndpoints = Auth.endpoints loginReturnUrl
    let todoEndpoints = Todos.startStore () |> Todos.endpoints
    let allEndpoints = Seq.concat [ authEndpoints; todoEndpoints ]

    app.UseRouting () |> ignore
    app.UseAuthentication () |> ignore
    app.UseAuthorization () |> ignore
    app.UseOxpecker allEndpoints |> ignore
    app.Run ()
    0
