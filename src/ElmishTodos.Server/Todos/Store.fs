namespace ElmishTodos.Server.Todos.Store

open System
open ElmishTodos.Server.Todos.Models

type TodoMessage =
    private
    | GetAll of AsyncReplyChannel<TodoItem list>
    | Get of Guid * AsyncReplyChannel<TodoItem option>
    | Upsert of TodoItem
    | Update of id: Guid * title : string * completed : bool * reply: AsyncReplyChannel<TodoItem option>
    | Delete of Guid * AsyncReplyChannel<bool>

type TodoStore = MailboxProcessor<TodoMessage>

module TodoStore =

    let start () : TodoStore =
        MailboxProcessor.Start (fun inbox ->
            let rec loop (state : Map<Guid, TodoItem>) =
                async {
                    let! msg = inbox.Receive ()

                    match msg with
                    | GetAll reply ->
                        reply.Reply (state.Values |> Seq.toList)
                        return! loop state
                    | Get (todoId, reply) ->
                        reply.Reply (state.TryFind todoId)
                        return! loop state
                    | Upsert todoItem -> return! loop <| state.Add (todoItem.Id, todoItem)
                    | Update (id = id; title = title; completed = completed; reply = reply;) ->
                        match state.TryFind id with
                        | Some todo ->
                            let updated = {
                                todo with
                                    Title = title
                                    Completed = completed
                            }

                            let nextState = state.Add (id, updated)

                            reply.Reply <| Some updated
                            return! loop nextState
                        | None ->
                            reply.Reply None
                            return! loop state
                    | Delete (id, reply) ->
                        let existed = state.ContainsKey id
                        let nextState = if existed then state.Remove id else state
                        reply.Reply existed
                        return! loop nextState
                }

            loop Map.empty)

    let getAll (todoStore : TodoStore) : Async<TodoItem list> = todoStore.PostAndAsyncReply GetAll

    let get (todoStore : TodoStore) (todoId : Guid) : Async<TodoItem option> =
        todoStore.PostAndAsyncReply (fun reply -> Get (todoId, reply))

    let upsert (todoStore : TodoStore) (todo : TodoItem) : unit = todoStore.Post (Upsert todo)

    let update (todoStore : TodoStore) (id : Guid) (title : string) (completed : bool) : Async<TodoItem option> =
        todoStore.PostAndAsyncReply (fun reply -> Update (id, title, completed, reply))

    let delete (todoStore : TodoStore) (todoId : Guid) : Async<bool> =
        todoStore.PostAndAsyncReply (fun reply -> Delete (todoId, reply))
