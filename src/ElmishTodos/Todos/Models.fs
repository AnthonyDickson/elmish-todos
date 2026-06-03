namespace ElmishTodos.Todos.Models

open System

// ── Domain types ──────────────────────────────────────────────────────────────

/// <summary>A todo item stored in the in-memory todo list.</summary>
type TodoItem = {
    /// <summary>Unique identifier for the todo item.</summary>
    Id : Guid

    /// <summary>The title or description of the todo.</summary>
    Title : string

    /// <summary>Whether the todo has been completed.</summary>
    Completed : bool

    /// <summary>UTC timestamp when the todo was created.</summary>
    CreatedAt : DateTime
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

type CreateTodoRequest = { Title : string }

type UpdateTodoRequest = { Title : string; Completed : bool }

// ── Error DTO ────────────────────────────────────────────────────────────────

type ApiError = { Error : string; Details : string }
