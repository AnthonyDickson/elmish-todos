namespace LustreTodos.Server.DomainError

type DomainError =
    | ValidationFailed of string
    | NotFound of string
    | UserNotFound
    | DatabaseError of string * exn option
    | UnhandledException of string * exn option
