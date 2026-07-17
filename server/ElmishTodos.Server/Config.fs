namespace ElmishTodos.Server.Config

open System.ComponentModel.DataAnnotations

[<CLIMutable>]
type OidcOptions = {
    [<Required>]
    Authority : string

    [<Required>]
    ClientId : string

    [<Required>]
    ClientSecret : string

    [<Required>]
    CallbackPath : string
}

[<CLIMutable>]
type OAuth2Options = {
    [<Required>]
    AuthorizationUrl : string

    [<Required>]
    TokenUrl : string
}

[<CLIMutable>]
type LoginOptions = { ReturnUrl : string }
