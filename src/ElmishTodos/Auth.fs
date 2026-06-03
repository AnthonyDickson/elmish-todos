namespace ElmishTodos.Auth

module Auth =
    open Microsoft.AspNetCore.Authentication
    open Microsoft.Extensions.Logging
    open Microsoft.Extensions.Options
    open System
    open System.Security.Claims
    open System.Text.Encodings.Web

    [<Literal>]
    let DemoScheme = "DemoBearer"

    [<Literal>]
    let DemoToken = "demo-token"

    type DemoBearerAuthHandler
        (options : IOptionsMonitor<AuthenticationSchemeOptions>, logger : ILoggerFactory, encoder : UrlEncoder) =
        inherit AuthenticationHandler<AuthenticationSchemeOptions> (options, logger, encoder)

        override _.HandleAuthenticateAsync () =
            let authHeader = base.Request.Headers.Authorization.ToString ()
            let expectedHeader = $"Bearer {DemoToken}"

            task {
                if String.IsNullOrWhiteSpace authHeader then
                    return AuthenticateResult.NoResult ()
                elif not (String.Equals (authHeader, expectedHeader, StringComparison.Ordinal)) then
                    return AuthenticateResult.Fail "Invalid bearer token."
                else
                    let claims = [
                        Claim (ClaimTypes.NameIdentifier, "demo-user")
                        Claim (ClaimTypes.Name, "Demo User")
                    ]

                    let identity = ClaimsIdentity (claims, DemoScheme)
                    let principal = ClaimsPrincipal identity
                    let ticket = AuthenticationTicket (principal, DemoScheme)

                    return AuthenticateResult.Success ticket
            }
