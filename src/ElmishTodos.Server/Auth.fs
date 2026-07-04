namespace ElmishTodos.Server.Auth

open System

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Authentication.OpenIdConnect
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi
open Oxpecker

open ElmishTodos.Shared.ApiError
open ElmishTodos.Shared.Coders

[<RequireQualifiedAccess>]
module Auth =
    [<Literal>]
    let private policyName = "authenticated"

    let private cookieScheme = CookieAuthenticationDefaults.AuthenticationScheme
    let private oidcScheme = OpenIdConnectDefaults.AuthenticationScheme
    let private bearerScheme = "bearer"

    let oauthRequirement () : OpenApiSecurityRequirement =
        let schemeRef =
            OpenApiSecuritySchemeReference ("scalarOAuth2", null, "SecuritySchemes")

        let requirement = OpenApiSecurityRequirement ()
        requirement[schemeRef] <- ResizeArray [ "openid"; "profile"; "email" ]
        requirement

    let private requirePolicy (policyName : string) : EndpointMiddleware =
        fun next ctx ->
            task {
                let authz = ctx.RequestServices.GetRequiredService<IAuthorizationService> ()

                let! result = authz.AuthorizeAsync (ctx.User, null, policyName)

                if result.Succeeded then
                    return! next ctx
                else
                    ctx.SetStatusCode 401
                    ctx.Response.ContentType <- "application/json; charset=utf-8"

                    return!
                        ctx.Response.WriteAsync (
                            Encode.toString {
                                Error = "Unauthorized"
                                Details = "Authentication required"
                                StatusCode = Some 401
                            }
                        )
            }

    let requireAuth : EndpointMiddleware = requirePolicy policyName

    let configureServices (builder : WebApplicationBuilder) =
        let oidcConfig = builder.Configuration.GetSection "Oidc"

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
                    options.ExpireTimeSpan <- TimeSpan.FromHours 1
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

                    options.PushedAuthorizationBehavior <- PushedAuthorizationBehavior.Disable

                    options.Scope.Add "openid" |> ignore
                    options.Scope.Add "profile" |> ignore
                    options.Scope.Add "email" |> ignore
                    options.Scope.Add "offline_access" |> ignore

                    if builder.Environment.IsDevelopment () then
                        options.RequireHttpsMetadata <- false

                        options.BackchannelHttpHandler <-
                            new Net.Http.HttpClientHandler (
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
                    policyName,
                    fun policy ->
                        policy.AddAuthenticationSchemes(cookieScheme, bearerScheme).RequireAuthenticatedUser ()
                        |> ignore
                )
                |> ignore)
            .AddRouting()
            .AddOxpecker ()
        |> ignore

    let private loginHandler (returnUrl : string) : EndpointHandler =
        fun ctx ->
            task {
                if ctx.User.Identity.IsAuthenticated then
                    ctx.Response.Redirect "/"
                else
                    let props = AuthenticationProperties (RedirectUri = returnUrl)
                    return! ctx.ChallengeAsync (oidcScheme, props)
            }

    let private logoutHandler : EndpointHandler =
        fun ctx ->
            task {
                // Currently Authelia does not support RP-Initiated Logout
                // (see https://github.com/authelia/authelia/pull/11660). Once released,
                // replace with: ctx.SignOutAsync cookieScheme then
                // ctx.SignOutAsync (oidcScheme, AuthenticationProperties (RedirectUri = returnUrl))
                return! ctx.SignOutAsync (cookieScheme, AuthenticationProperties (RedirectUri = "/"))
            }

    let endpoints (loginReturnUrl : string) : Oxpecker.RoutingTypes.Endpoint seq = [
        GET [ route "/login" (loginHandler loginReturnUrl); route "/logout" logoutHandler ]
    ]
