namespace ElmishTodos.Middleware

module Middleware =
    open Oxpecker
    open ElmishTodos.Auth
    open ElmishTodos.Todos.Models

    let notFound (msg : string) : EndpointHandler =
        fun ctx ->
            ctx.SetStatusCode 404
            ctx.WriteJson { Error = "Not Found"; Details = msg }

    let requireAuthenticated : EndpointMiddleware =
        fun next ctx ->
            task {
                if
                    not (isNull ctx.User)
                    && not (isNull ctx.User.Identity)
                    && ctx.User.Identity.IsAuthenticated
                then
                    return! next ctx
                else
                    ctx.SetStatusCode 401

                    return!
                        ctx.WriteJson {
                            Error = "Unauthorized"
                            Details = $"Provide Authorization: Bearer {Auth.DemoToken}"
                        }
            }
