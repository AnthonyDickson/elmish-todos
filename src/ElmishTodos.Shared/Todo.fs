namespace ElmishTodos.Shared.Todo

open System

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

module Todo =
#if FABLE_COMPILER
    open Thoth.Json
#else
    open Thoth.Json.Net
#endif

// let encoder : Encoder<Todo> =
//     fun todo -> Encode.object [ "id", Encode.string person.Name; "age", Encode.int person.Age ]

// let decoder : Decoder<Todo> =
//     Decode.object (fun get -> {
//         Name = get.Required.Field "name" Decode.string
//         Age = get.Required.Field "age" Decode.int
//     })
