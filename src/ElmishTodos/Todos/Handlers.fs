namespace ElmishTodos.Todos.Handlers

open System
open Oxpecker
open ElmishTodos.Todos.Models
open ElmishTodos.Todos.Store
open ElmishTodos.Middleware

module Handlers =
    /// GET /todos — list all items
    let getTodos (store : TodoStore) : EndpointHandler =
        fun ctx ->
            task {
                let! items = TodoStore.getAll store
                return! ctx.WriteJson items
            }

    /// GET /todos/{id} — get one item
    let getTodo (store : TodoStore) (id : Guid) : EndpointHandler =
        fun ctx ->
            task {
                let! todo = TodoStore.get store id

                match todo with
                | Some item -> return! ctx.WriteJson item
                | None -> return! Middleware.notFound $"Todo {id} not found" ctx
            }

    /// GET /private-todos — protected demo route
    let getPrivateTodos (store : TodoStore) : EndpointHandler =
        fun ctx ->
            task {
                let! items = TodoStore.getAll store
                return! ctx.WriteJson items
            }

    /// POST /todos — create an item
    let createTodo (store : TodoStore) : EndpointHandler =
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

                    TodoStore.upsert store item
                    ctx.SetStatusCode 201
                    return! ctx.WriteJson item
            }

    /// PUT /todos/{id} — replace an item
    let updateTodo (store : TodoStore) (id : Guid) : EndpointHandler =
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
                    let! updated = TodoStore.update store id (req.Title.Trim ()) req.Completed

                    match updated with
                    | Some updated -> return! ctx.WriteJson updated
                    | None -> return! Middleware.notFound $"Todo {id} not found" ctx
            }

    /// DELETE /todos/{id} — remove an item
    let deleteTodo (store : TodoStore) (id : Guid) : EndpointHandler =
        fun ctx ->
            task {
                let! deleted = TodoStore.delete store id

                if deleted then
                    ctx.SetStatusCode 204
                    return ()
                else
                    return! Middleware.notFound $"Todo {id} not found" ctx
            }
