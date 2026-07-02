module ElmishTodos.Server.Program

open System.Collections.Generic
open System.Threading.Tasks

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http
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

    let oidcConfig = builder.Configuration.GetSection "Oidc"

    let loginReturnUrl =
        builder.Configuration["Login:ReturnUrl"]
        |> Option.ofObj
        |> Option.defaultValue "/"

    builder.Services
        .AddAuthentication(fun options ->
            options.DefaultScheme <- cookieScheme
            options.DefaultChallengeScheme <- oidcScheme)
        .AddCookie(
            cookieScheme,
            fun options ->
                options.Cookie.SecurePolicy <-
                    if builder.Environment.IsDevelopment () then
                        CookieSecurePolicy.SameAsRequest
                    else
                        CookieSecurePolicy.Always

                options.Cookie.SameSite <- SameSiteMode.Lax
                options.Cookie.HttpOnly <- true
                options.Cookie.IsEssential <- true
                options.ExpireTimeSpan <- System.TimeSpan.FromHours 1
                options.SlidingExpiration <- true
        )
        .AddOpenIdConnect(
            oidcScheme,
            fun options ->
                options.Authority <- oidcConfig["Authority"]
                options.ClientId <- oidcConfig["ClientId"]
                options.ClientSecret <- oidcConfig["ClientSecret"]
                options.ResponseType <- "code"
                options.CallbackPath <- oidcConfig["CallbackPath"]
                options.SaveTokens <- true

                options.PushedAuthorizationBehavior <-
                    Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationBehavior.Disable

                options.Scope.Add "openid" |> ignore
                options.Scope.Add "profile" |> ignore
                options.Scope.Add "email" |> ignore
                options.Scope.Add "offline_access" |> ignore

                if builder.Environment.IsDevelopment () then
                    options.RequireHttpsMetadata <- false

                    options.BackchannelHttpHandler <-
                        // Allow self-signed SSL certs
                        new System.Net.Http.HttpClientHandler (
                            ServerCertificateCustomValidationCallback = fun _ _ _ _ -> true
                        )
        )
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
        .AddOxpecker ()
    |> ignore

    if builder.Environment.IsDevelopment () then
        addOpenApiToBuilder builder

    let app = builder.Build ()

    if app.Environment.IsDevelopment () then
        addOpenApiToApp app

    app.MapGet (
        "/login",
        fun (ctx : Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                if ctx.User.Identity.IsAuthenticated then
                    ctx.Response.Redirect "/"
                else
                    let props = AuthenticationProperties (RedirectUri = loginReturnUrl)

                    return! ctx.ChallengeAsync (oidcScheme, props)
            }
            :> System.Threading.Tasks.Task
    )
    |> ignore

    app.MapGet (
        "/logout",
        // Currently Authelia does not support RP-Initiated Logout
        // (see https://github.com/authelia/authelia/pull/11660). Once released:
        //   1. Remove the EndSessionUrl from appsettings.Development.json
        //   2. Replace this handler with:
        //        fun ctx -> task {
        //            do! ctx.SignOutAsync cookieScheme
        //            return! ctx.SignOutAsync (oidcScheme, AuthenticationProperties (RedirectUri = loginReturnUrl)) }
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
