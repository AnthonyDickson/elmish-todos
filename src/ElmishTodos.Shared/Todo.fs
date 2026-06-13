namespace ElmishTodos.Shared.Todo

open System

open ElmishTodos.Shared.Coders

/// <summary>A todo item stored in the in-memory todo list.</summary>
type Todo = {
    /// <summary>Unique identifier for the todo item.</summary>
    Id : Guid

    /// <summary>The title or description of the todo.</summary>
    Title : string

    /// <summary>Whether the todo has been completed.</summary>
    Completed : bool

    /// <summary>UTC timestamp when the todo was created.</summary>
    CreatedAt : DateTime
}

/// <summary>Payload for updating an existing todo item.</summary>
type UpdateTodoRequest = { Title : string; Completed : bool }

[<RequireQualifiedAccess>]
module Todo =
    let create (title : string) = {
        Id = Guid.CreateVersion7 ()
        Title = title
        Completed = false
        CreatedAt = DateTime.UtcNow
    }

    let complete (todo : Todo) = { todo with Completed = true }

    let toggleComplete (todo : Todo) = {
        todo with
            Completed = not todo.Completed
    }
