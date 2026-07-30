namespace LustreTodos.Server

type DomainError =
    | ValidationFailed of string
    | NotFound of string
    | DatabaseError of string * exn option
    | UnhandledException of string * exn option
