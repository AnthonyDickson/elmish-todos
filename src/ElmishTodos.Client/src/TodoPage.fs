namespace ElmishTodos.Client.TodoPage

[<RequireQualifiedAccess>]
module private Todo =
    open System

    open ElmishTodos.Shared.Todo

    let create (title : string) = {
        Id = Guid.CreateVersion7 ()
        Title = title
        Completed = false
        CreatedAt = DateTime.UtcNow
    }

    let toggleComplete (todo : Todo) = {
        todo with
            Completed = not todo.Completed
    }

module TodoPage =
    open System

    open Browser.WebStorage
    open Elmish
    open Fable.Core.JS
    open Feliz
    open Feliz.Router
    open Fetch

    open ElmishTodos.Shared.ApiError
    open ElmishTodos.Shared.Coders
    open ElmishTodos.Shared.Todo
    open ElmishTodos.Client.Api

    type Visibility =
        | All
        | Active
        | Completed

    type EditState = {
        /// This corresponds to the Todo that is being edited.
        Id : Guid
        NewTitle : string
    }

    type Model = {
        NewTodo : string
        Todos : Todo list
        Visibility : Visibility
        EditState : EditState option
    }

    type Msg =
        /// The client loaded todos from local storage
        | ClientLoadedTodos of Todo list
        /// The client loaded todos from the API
        | ClientFetchedTodos of ApiResult<Todo list>
        | ClientPostedTodo of ApiResult<Todo>
        | ClientPutTodo of ApiResult<Todo>
        | UserChangedNewTodo of string
        | UserSubmittedNewTodo
        | UserToggledCompletedStatus of Guid
        | UserEnteredEditMode of Guid
        | UserEditedTodo of string
        | UserExitedEditMode
        | UserSubmittedEditedTodo
        | UserDeletedTodo of Guid
        | UserDeletedCompletedTodos
        | UserChangedVisibility of Visibility

    let localStorageKey = "todomvc-elmish"

    let init () : Model * Cmd<Msg> =
        let model = {
            NewTodo = ""
            Todos = []
            Visibility = All
            EditState = None
        }

        model,
        Cmd.OfPromise.either
            (fun () -> Api.get "/api/todos")
            ()
            ClientFetchedTodos
            (ApiResult.ofException >> ClientFetchedTodos)

    let initWithLocalStorage () : Model * Cmd<Msg> =
        let model, cmd = init ()

        let cmds =
            Cmd.batch [
                cmd
                Cmd.ofEffect (fun dispatch ->
                    let todosJson = localStorage.getItem localStorageKey

                    match Decode.fromString todosJson with
                    | Ok todos -> dispatch (ClientLoadedTodos todos)
                    | Error err -> eprintfn $"could not load todos from local storage: %s{err}")
            ]

        model, cmds


    let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
        match msg with
        | ClientLoadedTodos todos -> { model with Todos = todos }, Cmd.none
        | ClientFetchedTodos (ApiResult.Success todos) -> { model with Todos = todos }, Cmd.none
        | ClientFetchedTodos (ApiResult.Failure error) ->
            eprintfn $"Failed to get todo: {error}"
            model, Cmd.none
        | ClientPostedTodo (ApiResult.Success _) ->
            printfn "Posted todo successfully"
            model, Cmd.none
        | ClientPostedTodo (ApiResult.Failure error) ->
            eprintfn $"Failed to post todo: {error}"
            model, Cmd.none
        | ClientPutTodo (ApiResult.Success _) ->
            printfn "Put todo successfully"
            model, Cmd.none
        | ClientPutTodo (ApiResult.Failure error) ->
            eprintfn $"Failed to put todo: {error}"
            model, Cmd.none
        | UserChangedNewTodo text -> { model with NewTodo = text }, Cmd.none
        | UserSubmittedNewTodo ->
            let title = model.NewTodo.Trim ()
            let todo = Todo.create title

            if title.Length > 0 then
                {
                    model with
                        NewTodo = ""
                        Todos = model.Todos @ [ todo ]
                },
                Cmd.OfPromise.either
                    (Api.post "/api/todos")
                    todo
                    ClientPostedTodo
                    (ApiResult.ofException >> ClientPostedTodo)
            else
                { model with NewTodo = "" }, Cmd.none
        | UserToggledCompletedStatus id ->
            let updatedTodo =
                List.tryFind (fun (todo : Todo) -> todo.Id = id) model.Todos
                |> Option.map Todo.toggleComplete

            match updatedTodo with
            | Some updatedTodo ->
                let todos =
                    List.map (fun (todo : Todo) -> if todo.Id = updatedTodo.Id then updatedTodo else todo) model.Todos

                { model with Todos = todos },
                Cmd.OfPromise.either
                    (Api.put $"/api/todos/%O{id}")
                    {
                        UpdateTodoRequest.Completed = updatedTodo.Completed
                        UpdateTodoRequest.Title = updatedTodo.Title
                    }
                    ClientPutTodo
                    (ApiResult.ofException >> ClientPutTodo)
            | None -> model, Cmd.none
        | UserEnteredEditMode id ->
            let updatedModel =
                List.tryFind (fun (todo : Todo) -> todo.Id = id) model.Todos
                |> Option.map (fun todo -> {
                    model with
                        EditState = Some { Id = id; NewTitle = todo.Title }
                })
                |> Option.defaultValue model

            updatedModel, Cmd.none
        | UserEditedTodo text ->
            let nextEditState =
                Option.map (fun editState -> { editState with NewTitle = text }) model.EditState

            { model with EditState = nextEditState }, Cmd.none
        | UserExitedEditMode -> { model with EditState = None }, Cmd.none
        | UserSubmittedEditedTodo ->
            let applyEdit id newTitle =
                model.Todos
                |> List.tryFind (fun (todo : Todo) -> todo.Id = id)
                |> Option.map (fun todo ->
                    let todos =
                        List.map
                            (fun (todo : Todo) ->
                                if todo.Id = id then
                                    { todo with Title = newTitle }
                                else
                                    todo)
                            model.Todos

                    {
                        model with
                            EditState = None
                            Todos = todos
                    },
                    Cmd.OfPromise.either
                        (Api.patch $"/api/todos/%O{id}")
                        {
                            UpdateTodoRequest.Completed = todo.Completed
                            UpdateTodoRequest.Title = newTitle
                        }
                        ClientPutTodo
                        (ApiResult.ofException >> ClientPutTodo))
                |> Option.defaultValue ({ model with EditState = None }, Cmd.none)

            match model.EditState with
            | Some { Id = id; NewTitle = newTitle } ->
                let newTitle = newTitle.Trim ()

                if newTitle.Length > 0 then
                    applyEdit id newTitle
                else
                    { model with EditState = None }, Cmd.ofMsg (UserDeletedTodo id)
            | None -> { model with EditState = None }, Cmd.none
        | UserDeletedTodo id ->
            let todos = List.filter (fun (todo : Todo) -> todo.Id <> id) model.Todos
            { model with Todos = todos }, Cmd.none
        | UserDeletedCompletedTodos ->
            let todos = List.filter (fun (todo : Todo) -> not todo.Completed) model.Todos
            { model with Todos = todos }, Cmd.none
        | UserChangedVisibility visibility -> { model with Visibility = visibility }, Cmd.none

    let updateWithLocalStorage (msg : Msg) (model : Model) : Model * Cmd<Msg> =
        let model', cmd = update msg model

        let cmds =
            Cmd.batch [
                cmd
                Cmd.ofEffect (fun _ ->
                    let todosJson = Encode.toString model'.Todos
                    localStorage.setItem (localStorageKey, todosJson))
            ]

        model', cmds


    let todoListItem (dispatch : Msg -> Unit) (editState : EditState option) (todo : Todo) =
        let liClasses = [
            "bg-gray-50"
            "py-5"
            "min-w-xl"
            "text-2xl"
            "border-t-1"
            "border-gray-200"
            "flex"
            "items-center"
            "group"
        ]

        match editState with
        | Some editState when editState.Id = todo.Id ->
            Html.li [
                prop.classes liClasses
                prop.key (editState.Id.ToString () + "+edit")
                prop.children (
                    Html.input [
                        prop.type' "text"
                        prop.autoFocus true
                        prop.value editState.NewTitle
                        prop.onChange (fun text -> dispatch (UserEditedTodo text))
                        prop.onKeyDown (fun e ->
                            if e.key = "Enter" then
                                dispatch UserSubmittedEditedTodo)
                        prop.onBlur (fun _ -> dispatch UserExitedEditMode)
                        prop.placeholder "What needs to be done?"
                        prop.classes [
                            "text-gray-600"
                            "text-2xl"
                            "bg-gray-50"
                            "focus-visible:outline-none"
                            "px-15"
                            "min-w-xl"
                            "placeholder:text-2xl"
                            "placeholder:text-gray-300"
                            "placeholder:italic"
                        ]
                    ]
                )
            ]
        | _ ->
            Html.li [
                prop.classes liClasses
                prop.key todo.Id
                prop.onDoubleClick (fun _ -> dispatch (UserEnteredEditMode todo.Id))
                prop.children [
                    Html.input [
                        prop.type' "checkbox"
                        prop.className "w-5 mx-5"
                        prop.isChecked todo.Completed
                        prop.onCheckedChange (fun _ -> dispatch (UserToggledCompletedStatus todo.Id))
                    ]

                    Html.p [
                        prop.text todo.Title
                        prop.className (
                            if todo.Completed then
                                "line-through text-gray-300"
                            else
                                "text-gray-600"
                        )
                    ]

                    Html.button [
                        prop.text "x"
                        prop.className "ml-auto mx-5 w-5 text-red-400/0 group-hover:text-red-400"
                        prop.onClick (fun _ -> dispatch (UserDeletedTodo todo.Id))
                    ]
                ]
            ]

    let view (model : Model) (dispatch : Msg -> unit) =
        let activeCount, completedCount =
            List.fold
                (fun (active, completed) (todo : Todo) ->
                    if todo.Completed then
                        active, completed + 1
                    else
                        active + 1, completed)
                (0, 0)
                model.Todos

        let todoCount = activeCount + completedCount

        let filteredTodos =
            match model.Visibility with
            | All -> model.Todos
            | Active -> List.filter (fun todo -> not todo.Completed) model.Todos
            | Completed -> List.filter _.Completed model.Todos

        let visibilityClasses visibility =
            let baseClasses = [ "p-1"; "rounded-sm"; "border-1" ]

            if visibility = model.Visibility then
                "border-rose-300/40" :: baseClasses
            else
                "border-rose-300/0" :: "hover:border-rose-300/20" :: baseClasses

        Html.div [
            prop.className "bg-gray-100 h-dvh flex h-screen justify-center"
            prop.children (
                Html.main [
                    Html.header [
                        Html.h1 [
                            prop.text "todos"
                            prop.className "text-8xl text-rose-300/30 text-center m-5"
                        ]
                        Html.input [
                            prop.type' "text"
                            prop.autoFocus true
                            prop.value model.NewTodo
                            prop.placeholder "What needs to be done?"
                            prop.classes [
                                "text-gray-600"
                                "text-2xl"
                                "bg-gray-50"
                                "drop-shadow-sm"
                                "focus-visible:outline-none"
                                "py-5"
                                "px-15"
                                "min-w-xl"
                                "placeholder:text-2xl"
                                "placeholder:text-gray-300"
                                "placeholder:italic"
                            ]
                            prop.onChange (fun (e : string) -> dispatch (UserChangedNewTodo e))
                            prop.onKeyDown (fun e ->
                                if e.key = "Enter" then
                                    dispatch UserSubmittedNewTodo)
                        ]
                    ]
                    Html.ol [
                        prop.className "drop-shadow-sm"
                        prop.children (List.map (todoListItem dispatch model.EditState) filteredTodos)
                    ]
                    if todoCount > 0 then
                        Html.footer [
                            prop.classes [
                                "text-gray-500"
                                "text-sm"
                                "bg-gray-50"
                                "drop-shadow-sm"
                                "py-2"
                                "px-5"
                                "min-w-lg"
                                "border-t-1"
                                "border-gray-200"
                                "flex"
                                "justify-between"
                            ]
                            prop.children [
                                Html.p [
                                    prop.className "pt-1"
                                    prop.children [
                                        Html.strong [ prop.text activeCount ]
                                        Html.text (if activeCount = 1 then " item left" else " items left")
                                    ]
                                ]
                                Html.div [
                                    prop.className "flex gap-2"
                                    prop.children [
                                        Html.anchor [
                                            prop.text "All"
                                            prop.href (Router.format [])
                                            prop.classes (visibilityClasses All)
                                        ]
                                        Html.anchor [
                                            prop.text "Active"
                                            prop.href (Router.format [ "active" ])
                                            prop.classes (visibilityClasses Active)
                                        ]
                                        Html.anchor [
                                            prop.text "Completed"
                                            prop.href (Router.format [ "completed" ])
                                            prop.classes (visibilityClasses Completed)
                                        ]
                                    ]
                                ]
                                Html.button [
                                    prop.text $"Clear completed ({completedCount})"

                                    prop.classes (
                                        "hover:underline" :: if completedCount = 0 then [ "invisible" ] else []
                                    )
                                    prop.onClick (fun _ -> dispatch UserDeletedCompletedTodos)
                                ]
                            ]
                        ]
                    else
                        Html.none
                ]
            )
        ]
