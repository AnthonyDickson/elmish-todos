namespace ElmishTodos.Server.Middleware

module Middleware =
    open Oxpecker
    open Microsoft.AspNetCore.Http
    open ElmishTodos.Shared.Coders
    open ElmishTodos.Server.Auth
    open ElmishTodos.Server.ApiError

    let private writeJson (ctx : HttpContext) (error : ApiError) =
        task {
            let json = Encode.toString error
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            return! ctx.Response.WriteAsync json
        }

    let notFound (msg : string) : EndpointHandler =
        fun ctx ->
            ctx.SetStatusCode 404
            writeJson ctx { Error = "Not Found"; Details = msg }

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
                        writeJson ctx {
                            Error = "Unauthorized"
                            Details = $"Provide Authorization: Bearer {Auth.DemoToken}"
                        }
            }
