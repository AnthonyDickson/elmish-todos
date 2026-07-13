namespace ElmishTodos.Server.Todos

module Store =
    open System

    open ElmishTodos.Shared.Todo
    open ElmishTodos.Server.Db
    open SqlHydra.Query
    open SqlHydra.Query.SqliteExtensions

    type Store = { Db : QueryContextFactory }

    let create (connectionString : string) = {
        Db = QueryContextFactory.Create connectionString
    }

    // ── DB row ↔ API type mapping ──────────────────────────────────────────

    let private toTodo (row : main.Todos) : Todo = {
        Id = row.Id
        Title = row.Title
        Completed = row.Completed
        CreatedAt = row.CreatedAt
    }

    let private toRow (todo : Todo) : main.Todos = {
        Id = todo.Id
        Title = todo.Title
        Completed = todo.Completed
        CreatedAt = todo.CreatedAt
    }

    // ── Queries ────────────────────────────────────────────────────────────

    let getAll (store : Store) =
        task {
            let! rows =
                selectTask store.Db {
                    for t in main.Todos do
                        select t
                }

            return rows |> List.ofSeq |> List.map toTodo
        }

    let get (store : Store) (id : Guid) =
        task {
            let! result =
                selectTask store.Db {
                    for t in main.Todos do
                        where (t.Id = id)
                        tryHead
                }

            return result |> Option.map toTodo
        }

    let upsert (store : Store) (todo : Todo) =
        task {
            let! _ =
                insertTask store.Db {
                    for t in main.Todos do
                        entity (toRow todo)
                        onConflictDoUpdate t.Id (t.Title, t.Completed)
                }

            return ()
        }

    let update (store : Store) (id : Guid) (title : string) (completed : bool) =
        task {
            use! shared = store.Db.OpenContextAsync ()
            shared.BeginTransaction ()

            let! _rowsAffected =
                updateTask shared {
                    for t in main.Todos do
                        set t.Title title
                        set t.Completed completed
                        where (t.Id = id)
                }

            let! result =
                selectTask shared {
                    for t in main.Todos do
                        where (t.Id = id)
                        tryHead
                }

            shared.CommitTransaction ()
            return result |> Option.map toTodo
        }

    let delete (store : Store) (id : Guid) =
        task {
            let! rows =
                deleteTask store.Db {
                    for t in main.Todos do
                        where (t.Id = id)
                }

            return rows > 0
        }

module Api =
    open System
    open System.Threading.Tasks

    open Oxpecker
    open Oxpecker.OpenApi

    open ElmishTodos.Server.Auth
    open ElmishTodos.Server.Json
    open ElmishTodos.Shared.ApiError
    open ElmishTodos.Shared.Todo
    open Store

    module private Helpers =
        let notFound (msg : string) : EndpointHandler =
            fun ctx ->
                ctx.SetStatusCode 404

                Json.write ctx {
                    Error = "Not Found"
                    Details = msg
                    StatusCode = Some 404
                }

    module GetAll =
        /// GET /todos — list all items
        let handler (store : Store) : EndpointHandler =
            fun ctx ->
                task {
                    let! items = Store.getAll store
                    return! Json.write ctx items
                }

        let endpoint (store : Store) =
            route "/api/todos" (handler store)
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
                            op.Security <- ResizeArray [ Auth.oauthRequirement () ]
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

        let endpoint (store : Store) =
            routef "/api/todos/{%O:guid}" (handler store)
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
                            op.Security <- ResizeArray [ Auth.oauthRequirement () ]
                            Task.CompletedTask
                )
            )

    module Create =
        /// POST /todos — create an item
        let handler (store : Store) : EndpointHandler =
            fun ctx ->
                task {
                    let! (result : Result<Todo, string>) = Json.read ctx

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
                            do! Store.upsert store todo
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

        let endpoint (store : Store) =
            route "/api/todos" (handler store)
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
                            op.Security <- ResizeArray [ Auth.oauthRequirement () ]
                            Task.CompletedTask
                )
            )

    module Update =
        /// PUT /todos/{id} — replace an item
        let handler (store : Store) (id : Guid) : EndpointHandler =
            fun ctx ->
                task {
                    let! (result : Result<UpdateTodoRequest, string>) = Json.read ctx

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

        let endpoint (store : Store) =
            routef "/api/todos/{%O:guid}" (handler store)
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
                            op.Security <- ResizeArray [ Auth.oauthRequirement () ]
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

        let endpoint (store : Store) =
            routef "/api/todos/{%O:guid}" (handler store)
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
                            op.Security <- ResizeArray [ Auth.oauthRequirement () ]
                            Task.CompletedTask
                )
            )

    let endpoints (store : Store) : Oxpecker.RoutingTypes.Endpoint seq =
        [
            GET [ GetAll.endpoint store; Get.endpoint store ]

            POST [ Create.endpoint store ]

            PATCH [ Update.endpoint store ]

            DELETE [ Delete.endpoint store ]
        ]
        |> Seq.map (addFilter Auth.requireAuth)

/// This module defines the public API of the Todos feature slice
[<RequireQualifiedAccess>]
module Todos =
    type Store = Store.Store

    let endpoints = Api.endpoints
