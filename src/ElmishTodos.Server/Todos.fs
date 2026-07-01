namespace ElmishTodos.Server.Todos

module Models =
    open System

    open ElmishTodos.Shared.Todo

    // ── Todo Store ───────────────────────────────────────────────────────────────

    type TodoMessage =
        | GetAll of AsyncReplyChannel<Todo list>
        | Get of Guid * AsyncReplyChannel<Todo option>
        | Upsert of Todo
        | Update of id : Guid * title : string * completed : bool * reply : AsyncReplyChannel<Todo option>
        | Delete of Guid * AsyncReplyChannel<bool>

    type Store = MailboxProcessor<TodoMessage>

module Store =
    open System

    open ElmishTodos.Shared.Todo
    open Models

    let start () : Store =
        let rec loop (state : Map<Guid, Todo>) (inbox : Store) =
            async {
                let! msg = inbox.Receive ()

                match msg with
                | GetAll reply ->
                    reply.Reply (state.Values |> Seq.toList)
                    return! loop state inbox
                | Get (todoId, reply) ->
                    reply.Reply (state.TryFind todoId)
                    return! loop state inbox
                | Upsert todoItem -> return! loop (state.Add (todoItem.Id, todoItem)) inbox
                | Update (id = id; title = title; completed = completed; reply = reply) ->
                    match state.TryFind id with
                    | Some todo ->
                        let updated = {
                            todo with
                                Title = title
                                Completed = completed
                        }

                        let nextState = state.Add (id, updated)

                        reply.Reply <| Some updated
                        return! loop nextState inbox
                    | None ->
                        reply.Reply None
                        return! loop state inbox
                | Delete (id, reply) ->
                    let existed = state.ContainsKey id
                    let nextState = if existed then state.Remove id else state
                    reply.Reply existed
                    return! loop nextState inbox
            }

        MailboxProcessor.Start (loop Map.empty)

    let getAll (todoStore : Store) : Async<Todo list> = todoStore.PostAndAsyncReply GetAll

    let get (todoStore : Store) (todoId : Guid) : Async<Todo option> =
        todoStore.PostAndAsyncReply (fun reply -> Get (todoId, reply))

    let upsert (todoStore : Store) (todo : Todo) : unit = todoStore.Post (Upsert todo)

    let update (todoStore : Store) (id : Guid) (title : string) (completed : bool) : Async<Todo option> =
        todoStore.PostAndAsyncReply (fun reply -> Update (id, title, completed, reply))

    let delete (todoStore : Store) (todoId : Guid) : Async<bool> =
        todoStore.PostAndAsyncReply (fun reply -> Delete (todoId, reply))

module Api =
    open System
    open System.Threading.Tasks

    open Microsoft.AspNetCore.Http
    open Oxpecker
    open Oxpecker.OpenApi

    open ElmishTodos.Shared.ApiError
    open ElmishTodos.Shared.Todo
    open Models

    module private Json =
        open System.IO
        open System.Text

        open Microsoft.AspNetCore.Http

        open ElmishTodos.Shared.Coders

        let write (ctx : HttpContext) (object : 'T) =
            task {
                ctx.Response.ContentType <- "application/json; charset=utf-8"
                return! ctx.Response.WriteAsync (Encode.toString object)
            }

        let read (ctx : HttpContext) =
            task {
                use reader = new StreamReader (ctx.Request.Body, Encoding.UTF8)
                let! body = reader.ReadToEndAsync ()
                return Decode.fromString body
            }

    module private Helpers =
        open Microsoft.OpenApi

        let notFound (msg : string) : EndpointHandler =
            fun ctx ->
                ctx.SetStatusCode 404
                Json.write ctx { Error = "Not Found"; Details = msg; StatusCode = Some 404 }

        let requireAuth : EndpointMiddleware =
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
                        return! Json.write ctx { Error = "Unauthorized"; Details = "Authentication required"; StatusCode = Some 401 }
                }

        let oauthRequirement () : OpenApiSecurityRequirement =
            let schemeRef =
                OpenApiSecuritySchemeReference ("scalarOAuth2", null, "SecuritySchemes")

            let requirement = OpenApiSecurityRequirement ()
            requirement[schemeRef] <- ResizeArray [ "openid"; "profile"; "email" ]
            requirement

    module GetAll =
        /// GET /todos — list all items
        let handler (store : Store) : EndpointHandler =
            fun ctx ->
                task {
                    let! items = Store.getAll store
                    return! Json.write ctx items
                }

        let endpoint (store: Store) =
            route "/api/todos" (Helpers.requireAuth >=> handler store)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody typeof<Todo array>
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "List all todos"
                            op.Description <- "Returns every todo item in the store."
                            op.Security <- ResizeArray [ Helpers.oauthRequirement () ]
                            Task.CompletedTask
                )
            )

    module Get =
        /// GET /todos/{id} — get one item
        let handler (store : Store) (id : Guid) : EndpointHandler =
            fun ctx ->
                task {
                    let! todo = Store.get store id

                    match todo with
                    | Some item -> return! Json.write ctx item
                    | None -> return! Helpers.notFound $"Todo {id} not found" ctx
                }

        let endpoint (store: Store) =
            routef "/todos/{%O:guid}" (fun id -> Helpers.requireAuth >=> handler store id)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody typeof<Todo>
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Get a todo by ID"
                            op.Description <- "Returns a single todo item, or 404 if not found."
                            op.Security <- ResizeArray [ Helpers.oauthRequirement () ]
                            Task.CompletedTask
                )
            )

    module Create =
        /// POST /todos — create an item
        let handler (store : Store) : EndpointHandler =
            fun ctx ->
                task {
                    let! result: Result<Todo, string> = Json.read ctx

                    match result with
                    | Ok todo ->
                        if String.IsNullOrWhiteSpace todo.Title then
                            ctx.SetStatusCode 400

                            return!
                                Json.write ctx {
                                    Error = "Validation Error"
                                    Details = "Title is required"
                                    StatusCode = Some 400
                                }
                        else
                            Store.upsert store todo
                            ctx.SetStatusCode 201
                            return! Json.write ctx todo
                    | Error err ->
                        ctx.SetStatusCode 400

                        return!
                            Json.write ctx {
                                Error = "Validation Error"
                                Details = err
                                StatusCode = Some 400
                            }
                }

        let endpoint (store: Store) =
            route "/api/todos" (Helpers.requireAuth >=> handler store)
            |> addOpenApi (
                OpenApiConfig (
                    requestBody = RequestBody typeof<Todo>,
                    responseBodies = [|
                        ResponseBody (typeof<Todo>, statusCode = 201)
                        ResponseBody (typeof<ApiError>, statusCode = 400)
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Create a todo"
                            op.Description <- "Creates a new todo item and returns it with status 201."
                            op.Security <- ResizeArray [ Helpers.oauthRequirement () ]
                            Task.CompletedTask
                )
            )

    module Update =
        /// PUT /todos/{id} — replace an item
        let handler (store : Store) (id : Guid) : EndpointHandler =
            fun ctx ->
                task {
                    let! result: Result<UpdateTodoRequest, string> = Json.read ctx

                    match result with
                    | Ok req ->
                        if String.IsNullOrWhiteSpace req.Title then
                            ctx.SetStatusCode 400

                            return!
                                Json.write ctx {
                                        Error = "Validation Error"
                                        Details = "Title is required"
                                        StatusCode = Some 400
                                    }
                        else
                            let! updated = Store.update store id (req.Title.Trim ()) req.Completed

                            match updated with
                            | Some updated -> return! Json.write ctx updated
                            | None -> return! Helpers.notFound $"Todo {id} not found" ctx
                    | Error err ->
                        ctx.SetStatusCode 400

                        return!
                            Json.write ctx {
                                    Error = "Validation Error"
                                    Details = err
                                    StatusCode = Some 400
                                }
                }

        let endpoint (store: Store) =
            routef "/api/todos/{%O:guid}" (fun id -> Helpers.requireAuth >=> handler store id)
                |> addOpenApi (
                    OpenApiConfig (
                        requestBody = RequestBody typeof<UpdateTodoRequest>,
                        responseBodies = [|
                            ResponseBody typeof<Todo>
                            ResponseBody (typeof<ApiError>, statusCode = 400)
                            ResponseBody (typeof<ApiError>, statusCode = 401)
                            ResponseBody (typeof<ApiError>, statusCode = 404)
                        |],
                        configureOperation =
                            fun op _ _ ->
                                op.Summary <- "Update a todo"
                                op.Description <- "Replaces the title and completed flag of an existing todo."
                                op.Security <- ResizeArray [ Helpers.oauthRequirement () ]
                                Task.CompletedTask
                    )
                )

    module Delete =
        /// DELETE /todos/{id} — remove an item
        let handler (store : Store) (id : Guid) : EndpointHandler =
            fun ctx ->
                task {
                    let! deleted = Store.delete store id

                    if deleted then
                        ctx.SetStatusCode 204
                        return ()
                    else
                        return! Helpers.notFound $"Todo {id} not found" ctx
                }

        let endpoint (store: Store) =
            routef "/api/todos/{%O:guid}" (fun id -> Helpers.requireAuth >=> handler store id)
                |> addOpenApi (
                    OpenApiConfig (
                        responseBodies = [|
                            ResponseBody (typeof<unit>, statusCode = 204)
                            ResponseBody (typeof<ApiError>, statusCode = 401)
                            ResponseBody (typeof<ApiError>, statusCode = 404)
                        |],
                        configureOperation =
                            fun op _ _ ->
                                op.Summary <- "Delete a todo"
                                op.Description <- "Permanently removes a todo. Returns 204 on success."
                                op.Security <- ResizeArray [ Helpers.oauthRequirement () ]
                                Task.CompletedTask
                    )
                )

    let endpoints (store: Store): Oxpecker.RoutingTypes.Endpoint seq =
        [
            GET [
                GetAll.endpoint store
                Get.endpoint store
            ]

            POST [
                Create.endpoint store
            ]

            PATCH [
                Update.endpoint store
            ]

            DELETE [
                Delete.endpoint store
            ]
        ]

/// This module defines the public API of the Todos feature slice
[<RequireQualifiedAccess>]
module Todos =
    type Store = Models.Store

    let startStore = Store.start
    let endpoints = Api.endpoints
