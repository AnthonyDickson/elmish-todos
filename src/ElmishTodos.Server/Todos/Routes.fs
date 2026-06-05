namespace ElmishTodos.Server.Todos.Routes

module TodoRoutes =
    open Microsoft.OpenApi
    open Oxpecker
    open Oxpecker.OpenApi
    open ElmishTodos.Server.Auth
    open ElmishTodos.Server.Middleware
    open ElmishTodos.Server.Todos.Handlers
    open ElmishTodos.Server.Todos.Models
    open ElmishTodos.Server.Todos.Store
    open System.Collections.Generic
    open System.Threading.Tasks

    let private bearerRequirement () : OpenApiSecurityRequirement =
        let schemeRef =
            OpenApiSecuritySchemeReference ("bearerAuth", null, "SecuritySchemes")

        let requirement = OpenApiSecurityRequirement ()
        requirement[schemeRef] <- ResizeArray<string> ()
        requirement

    let endpoints (store : TodoStore) : Endpoint list = [
        GET [
            route "/todos" (Handlers.getTodos store)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [| ResponseBody typeof<TodoItem array> |],
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
                        ResponseBody typeof<TodoItem>
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
                        ResponseBody typeof<TodoItem array>
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
                        ResponseBody (typeof<TodoItem>, statusCode = 201)
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
                        ResponseBody typeof<TodoItem>
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
