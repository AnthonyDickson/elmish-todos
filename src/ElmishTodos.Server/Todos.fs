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

    // ── Request DTOs ─────────────────────────────────────────────────────────────

    type CreateTodoRequest = { Title : string }

    type UpdateTodoRequest = { Title : string; Completed : bool }

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

module Handlers =
    open System

    open Oxpecker

    open ElmishTodos.Shared.Todo
    open ElmishTodos.Server.ApiError
    open ElmishTodos.Server.Middleware
    open Models

    /// GET /todos — list all items
    let getTodos (store : Store) : EndpointHandler =
        fun ctx ->
            task {
                let! items = Store.getAll store
                return! ctx.WriteJson items
            }

    /// GET /todos/{id} — get one item
    let getTodo (store : Store) (id : Guid) : EndpointHandler =
        fun ctx ->
            task {
                let! todo = Store.get store id

                match todo with
                | Some item -> return! ctx.WriteJson item
                | None -> return! Middleware.notFound $"Todo {id} not found" ctx
            }

    /// GET /private-todos — protected demo route
    let getPrivateTodos (store : Store) : EndpointHandler =
        fun ctx ->
            task {
                let! items = Store.getAll store
                return! ctx.WriteJson items
            }

    /// POST /todos — create an item
    let createTodo (store : Store) : EndpointHandler =
        fun ctx ->
            task {
                let! req = ctx.BindJson<CreateTodoRequest> ()

                if String.IsNullOrWhiteSpace req.Title then
                    ctx.SetStatusCode 400

                    return!
                        ctx.WriteJson {
                            Error = "Validation Error"
                            Details = "Title is required"
                        }
                else
                    let item = {
                        Id = Guid.NewGuid ()
                        Title = req.Title.Trim ()
                        Completed = false
                        CreatedAt = DateTime.UtcNow
                    }

                    Store.upsert store item
                    ctx.SetStatusCode 201
                    return! ctx.WriteJson item
            }

    /// PUT /todos/{id} — replace an item
    let updateTodo (store : Store) (id : Guid) : EndpointHandler =
        fun ctx ->
            task {
                let! req = ctx.BindJson<UpdateTodoRequest> ()

                if String.IsNullOrWhiteSpace req.Title then
                    ctx.SetStatusCode 400

                    return!
                        ctx.WriteJson {
                            Error = "Validation Error"
                            Details = "Title is required"
                        }
                else
                    let! updated = Store.update store id (req.Title.Trim ()) req.Completed

                    match updated with
                    | Some updated -> return! ctx.WriteJson updated
                    | None -> return! Middleware.notFound $"Todo {id} not found" ctx
            }

    /// DELETE /todos/{id} — remove an item
    let deleteTodo (store : Store) (id : Guid) : EndpointHandler =
        fun ctx ->
            task {
                let! deleted = Store.delete store id

                if deleted then
                    ctx.SetStatusCode 204
                    return ()
                else
                    return! Middleware.notFound $"Todo {id} not found" ctx
            }

module Routes =
    open Microsoft.OpenApi
    open System.Threading.Tasks

    open Oxpecker
    open Oxpecker.OpenApi

    open ElmishTodos.Shared.Todo
    open ElmishTodos.Server.Auth
    open ElmishTodos.Server.ApiError
    open ElmishTodos.Server.Middleware
    open Models

    let private bearerRequirement () : OpenApiSecurityRequirement =
        let schemeRef =
            OpenApiSecuritySchemeReference ("bearerAuth", null, "SecuritySchemes")

        let requirement = OpenApiSecurityRequirement ()
        requirement[schemeRef] <- ResizeArray<string> ()
        requirement

    let endpoints (store : Store) : Endpoint list = [
        GET [
            route "/todos" (Handlers.getTodos store)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [| ResponseBody typeof<Todo array> |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "List all todos"
                            op.Description <- "Returns every todo item in the store."
                            Task.CompletedTask
                )
            )

            routef "/todos/{%O:guid}" (Handlers.getTodo store)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody typeof<Todo>
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Get a todo by ID"
                            op.Description <- "Returns a single todo item, or 404 if not found."
                            Task.CompletedTask
                )
            )

            route "/private-todos" (Middleware.requireAuthenticated >=> Handlers.getPrivateTodos store)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody typeof<Todo array>
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "List private todos"
                            op.Description <- $"Protected demo route. Use Authorization: Bearer {Auth.DemoToken}"
                            op.Security <- ResizeArray [ bearerRequirement () ]
                            Task.CompletedTask
                )
            )
        ]

        POST [
            route "/todos" (Handlers.createTodo store)
            |> addOpenApi (
                OpenApiConfig (
                    requestBody = RequestBody typeof<CreateTodoRequest>,
                    responseBodies = [|
                        ResponseBody (typeof<Todo>, statusCode = 201)
                        ResponseBody (typeof<ApiError>, statusCode = 400)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Create a todo"
                            op.Description <- "Creates a new todo item and returns it with status 201."
                            Task.CompletedTask
                )
            )
        ]

        PUT [
            routef "/todos/{%O:guid}" (Handlers.updateTodo store)
            |> addOpenApi (
                OpenApiConfig (
                    requestBody = RequestBody typeof<UpdateTodoRequest>,
                    responseBodies = [|
                        ResponseBody typeof<Todo>
                        ResponseBody (typeof<ApiError>, statusCode = 400)
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Update a todo"
                            op.Description <- "Replaces the title and completed flag of an existing todo."
                            Task.CompletedTask
                )
            )
        ]

        DELETE [
            routef "/todos/{%O:guid}" (Handlers.deleteTodo store)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody (typeof<unit>, statusCode = 204)
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Delete a todo"
                            op.Description <- "Permanently removes a todo. Returns 204 on success."
                            Task.CompletedTask
                )
            )
        ]
    ]

/// This module defines the public API of the Todos feature slice
[<RequireQualifiedAccess>]
module Todos =
    type Store = Models.Store

    let startStore = Store.start
    let endpoints = Routes.endpoints
