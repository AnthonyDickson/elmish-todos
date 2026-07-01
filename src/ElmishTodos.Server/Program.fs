module ElmishTodos.Server.Program

open System.Collections.Generic
open System.Threading.Tasks

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Authentication.OpenIdConnect
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi
open Oxpecker
open Oxpecker.OpenApi
open Scalar.AspNetCore

open ElmishTodos.Server.OpenApi
open ElmishTodos.Server.Todos


let private oidcScheme = OpenIdConnectDefaults.AuthenticationScheme
let private cookieScheme = CookieAuthenticationDefaults.AuthenticationScheme
let private bearerScheme = "bearer"


[<EntryPoint>]
let main (args : string array) : int =
    let builder = WebApplication.CreateBuilder args

    let oidcConfig = builder.Configuration.GetSection "Oidc"

    builder.Services
        .AddAuthentication(fun options ->
            options.DefaultScheme <- cookieScheme
            options.DefaultChallengeScheme <- oidcScheme)
        .AddCookie(cookieScheme)
        .AddOpenIdConnect(
            oidcScheme,
            fun options ->
                options.Authority <- oidcConfig["Authority"]
                options.ClientId <- oidcConfig["ClientId"]
                options.ClientSecret <- oidcConfig["ClientSecret"]
                options.ResponseType <- "code"
                options.CallbackPath <- oidcConfig["CallbackPath"]
                options.SaveTokens <- true
                options.PushedAuthorizationBehavior <- Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationBehavior.Disable
                options.Scope.Add "openid" |> ignore
                options.Scope.Add "profile" |> ignore
                options.Scope.Add "email" |> ignore
                options.Scope.Add "offline_access" |> ignore

                if builder.Environment.IsDevelopment () then
                    options.RequireHttpsMetadata <- false

                    options.BackchannelHttpHandler <-
                        new System.Net.Http.HttpClientHandler (
                            ServerCertificateCustomValidationCallback = fun _ _ _ _ -> true))
        .AddJwtBearer(
            bearerScheme,
            fun options ->
                options.Authority <- oidcConfig["Authority"]
                options.RequireHttpsMetadata <- builder.Environment.IsProduction ()
        )
        .Services.AddAuthorization(fun options ->
            options.AddPolicy (
                "authenticated",
                fun policy ->
                    policy.AddAuthenticationSchemes(cookieScheme, bearerScheme).RequireAuthenticatedUser ()
                    |> ignore
            )
            |> ignore)
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
                        Description = "JWT access token from Authelia. Use the Scalar OAuth2 flow to obtain one."
                    )

                doc.Components.SecuritySchemes["scalarOAuth2"] <-
                    OpenApiSecurityScheme (
                        Type = SecuritySchemeType.OAuth2,
                        Flows =
                            OpenApiOAuthFlows (
                                AuthorizationCode =
                                    OpenApiOAuthFlow (
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
                .AddAuthorizationCodeFlow (
                    "scalarOAuth2",
                    fun flow ->
                        flow.ClientId <- "scalar-docs"
                        flow.Pkce <- Pkce.Sha256
                        flow.SelectedScopes <- [| "openid"; "profile"; "email" |]
                )
            |> ignore)
    |> ignore

    app.MapGet (
        "/login",
        fun (ctx : Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                if ctx.User.Identity.IsAuthenticated then
                    ctx.Response.Redirect "/"
                else
                    let returnUrl =
                        let referer = ctx.Request.Headers.Referer.ToString ()

                        if System.String.IsNullOrEmpty referer then
                            "/"
                        else
                            referer

                    let props = AuthenticationProperties (RedirectUri = returnUrl)

                    return! ctx.ChallengeAsync (oidcScheme, props)
            }
            :> System.Threading.Tasks.Task
    )
    |> ignore

    app.MapGet (
        "/logout",
        fun (ctx : Microsoft.AspNetCore.Http.HttpContext) ->
            ctx.SignOutAsync (cookieScheme, AuthenticationProperties (RedirectUri = "/"))
    )
    |> ignore

    let todoEndpoints = Todos.startStore () |> Todos.endpoints

    app.UseRouting () |> ignore
    app.UseAuthentication () |> ignore
    app.UseAuthorization () |> ignore
    app.UseOxpecker todoEndpoints |> ignore
    app.Run ()
    0
