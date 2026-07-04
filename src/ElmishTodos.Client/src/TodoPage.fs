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

    open Browser.Dom
    open Browser.WebStorage
    open Elmish
    open Feliz
    open Feliz.Router

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

    type TodoAction =
        | UpdateCompleted of id : Guid * previousState : bool
        | UpdateTitle of id : Guid * previousTitle : string
        | Create of id : Guid
        | Delete of previousTodo : Todo

    type Toast = {
        Id : Guid
        Title : string
        Body : string
    }

    type Model = {
        NewTodo : string
        Todos : Todo list
        Visibility : Visibility
        EditState : EditState option
        Toasts : Toast list
    }

    type Msg =
        /// The client loaded todos from local storage
        | ClientLoadedTodos of Todo list
        /// The client loaded todos from the API
        | ClientFetchedTodos of ApiResult<Todo list>
        | TodoActionFailed of TodoAction * ApiError
        | ToastDismissed of Guid
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
        | UserClickedLogout
        | NoOp

    let localStorageKey = "todomvc-elmish"

    let init () : Model * Cmd<Msg> =
        let model = {
            NewTodo = ""
            Todos = []
            Visibility = All
            EditState = None
            Toasts = []
        }

        model, Cmd.OfPromise.perform (fun () -> Api.get "/api/todos") () ClientFetchedTodos

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

    let private rollback model action =
        match action with
        | UpdateCompleted (id, previousState) -> {
            model with
                Todos =
                    List.map
                        (fun (t : Todo) ->
                            if t.Id = id then
                                { t with Completed = previousState }
                            else
                                t)
                        model.Todos
          }
        | UpdateTitle (id, previousTitle) -> {
            model with
                Todos =
                    List.map (fun (t : Todo) -> if t.Id = id then { t with Title = previousTitle } else t) model.Todos
          }
        | Create id -> {
            model with
                Todos = List.filter (fun t -> t.Id <> id) model.Todos
          }
        | Delete previousTodo -> {
            model with
                // Since the todos use V7 UUIDs, we can reinsert the todo
                // into its original index simply by sorting by ID.
                Todos = previousTodo :: model.Todos |> List.sortBy _.Id
          }

    let private createToast model action =
        match action with
        | UpdateCompleted (id, previousState) ->
            match List.tryFind (fun (t : Todo) -> t.Id = id) model.Todos with
            | Some todo ->
                Some {
                    Id = Guid.CreateVersion7 ()
                    Title = "Could not sync changes"
                    Body =
                        sprintf
                            "Reverted the todo status from '%s' to %s"
                            todo.Title
                            (if previousState then "completed" else "not completed")
                }
            | None -> None
        | UpdateTitle (id, previousTitle) ->
            List.tryFind (fun (t : Todo) -> t.Id = id) model.Todos
            |> Option.map (fun todo -> {
                Id = Guid.CreateVersion7 ()
                Title = "Could not sync changes"
                Body = sprintf "Reverted the todo title from '%s' to '%s'" todo.Title previousTitle
            })
        | Create id ->
            List.tryFind (fun (t : Todo) -> t.Id = id) model.Todos
            |> Option.map (fun todo -> {
                Id = Guid.CreateVersion7 ()
                Title = "Could not sync changes"
                Body = sprintf "Reverted the creation of the todo '%s'" todo.Title
            })
        | Delete previousTodo ->
            Some {
                Id = Guid.CreateVersion7 ()
                Title = "Could not sync changes"
                Body = sprintf "Reverted the deletion of the todo '%s'" previousTodo.Title
            }

    let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
        match msg with
        | ClientLoadedTodos todos -> { model with Todos = todos }, Cmd.none
        | ClientFetchedTodos (Success todos) -> { model with Todos = todos }, Cmd.none
        | ClientFetchedTodos (Failure error) ->
            if error.StatusCode = Some 401 then
                model, Cmd.ofEffect (fun _ -> window.location.assign "/login")
            else
                let toast = {
                    Id = Guid.CreateVersion7 ()
                    Title = "Could not sync todos"
                    Body = "Falling back to local data"
                }

                let model = {
                    model with
                        Toasts = model.Toasts @ [ toast ]
                }

                let cmd =
                    Cmd.ofEffect (fun dispatch ->
                        window.setTimeout ((fun () -> dispatch (ToastDismissed toast.Id)), 5000, [])
                        |> ignore)

                model, cmd
        | TodoActionFailed (action, error) ->
            let updatedModel = rollback model action

            if error.StatusCode = Some 401 then
                updatedModel, Cmd.ofEffect (fun _ -> window.location.assign "/login")
            else
                match createToast model action with
                | Some toast ->
                    let updatedModel = {
                        updatedModel with
                            Toasts = updatedModel.Toasts @ [ toast ]
                    }

                    let cmd =
                        Cmd.ofEffect (fun dispatch ->
                            window.setTimeout ((fun () -> dispatch (ToastDismissed toast.Id)), 5000, [])
                            |> ignore)

                    updatedModel, cmd
                | None ->
                    eprintfn "Could not create toast for:\n%O\n%O" model action
                    updatedModel, Cmd.none
        | ToastDismissed id ->
            let updatedToasts = List.filter (fun toast -> toast.Id <> id) model.Toasts
            let updatedModel = { model with Toasts = updatedToasts }
            updatedModel, Cmd.none
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
                Cmd.OfPromise.perform (Api.post "/api/todos") todo (function
                    | Success _ -> NoOp
                    | Failure error -> TodoActionFailed (Create todo.Id, error))
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
                Cmd.OfPromise.perform
                    (Api.patch $"/api/todos/%O{id}")
                    {
                        UpdateTodoRequest.Completed = updatedTodo.Completed
                        UpdateTodoRequest.Title = updatedTodo.Title
                    }
                    (function
                     | Success _ -> NoOp
                     | Failure error ->
                         TodoActionFailed (UpdateCompleted (updatedTodo.Id, not updatedTodo.Completed), error))
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
                    Cmd.OfPromise.perform
                        (Api.patch $"/api/todos/%O{id}")
                        {
                            UpdateTodoRequest.Completed = todo.Completed
                            UpdateTodoRequest.Title = newTitle
                        }
                        (function
                         | Success _ -> NoOp
                         | Failure error -> TodoActionFailed (UpdateTitle (todo.Id, todo.Title), error)))
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
            match List.tryFind (fun (todo : Todo) -> todo.Id = id) model.Todos with
            | Some todoToRemove ->
                let todos = List.filter (fun (todo : Todo) -> todo.Id <> id) model.Todos

                { model with Todos = todos },
                Cmd.OfPromise.perform (fun () -> Api.delete $"/api/todos/%O{id}") () (function
                    | Success _ -> NoOp
                    | Failure error -> TodoActionFailed (Delete todoToRemove, error))
            | None -> model, Cmd.none
        | UserDeletedCompletedTodos ->
            let completed, active = model.Todos |> List.partition _.Completed
            let cmds = completed |> List.map (fun todo -> Cmd.ofMsg (UserDeletedTodo todo.Id))

            { model with Todos = active }, Cmd.batch cmds
        | UserChangedVisibility visibility -> { model with Visibility = visibility }, Cmd.none
        | UserClickedLogout -> model, Cmd.ofEffect (fun _ -> window.location.assign "/logout")
        | NoOp -> model, Cmd.none

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

    let private viewToast dispatch toast =
        Html.div [
            prop.classes [
                "pointer-events-auto"
                "bg-gray-50"
                "border"
                "border-gray-200"
                "border-l-4"
                "border-l-amber-400/40"
                "shadow-lg"
                "p-4"
                "max-w-sm"
                "animate-[toast-in_0.3s_ease-out]"
            ]
            prop.role "alert"
            prop.key toast.Id
            prop.children [
                Html.div [
                    prop.classes [ "flex"; "justify-between"; "items-start"; "gap-3" ]
                    prop.children [
                        Html.div [
                            Html.p [
                                prop.classes [ "text-sm"; "font-medium"; "text-gray-600" ]
                                prop.text toast.Title
                            ]
                            Html.p [ prop.classes [ "text-sm"; "text-gray-500"; "mt-1" ]; prop.text toast.Body ]
                        ]
                        Html.button [
                            prop.classes [
                                "text-gray-300"
                                "hover:text-gray-500"
                                "shrink-0"
                                "text-lg"
                                "leading-none"
                                "cursor-pointer"
                            ]
                            prop.ariaLabel "Dismiss"
                            prop.text "x"
                            prop.onClick (fun _ -> dispatch (ToastDismissed toast.Id))
                        ]

                    ]
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
            prop.children [
                if not model.Toasts.IsEmpty then
                    Html.div [
                        prop.classes [
                            "fixed"
                            "top-4"
                            "right-4"
                            "z-50"
                            "flex"
                            "flex-col"
                            "gap-2"
                            "pointer-events-none"
                        ]
                        prop.children (List.map (viewToast dispatch) model.Toasts)

                    ]

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
                    Html.footer [
                        prop.className "flex justify-end py-2 px-5 min-w-lg"
                        prop.children [
                            Html.button [
                                prop.text "Logout"
                                prop.className "text-sm text-gray-400 hover:text-gray-600"
                                prop.onClick (fun _ -> dispatch UserClickedLogout)
                            ]
                        ]
                    ]
                ]
            ]
        ]
