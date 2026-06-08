namespace ElmishTodos.Client.Pages.Todo

open System

open Elmish
open Feliz
open Feliz.Router

module Todo =
    // TODO: Create shared assembly for types/code used across both client and server.
    // TODO: Use shared Todo model so that Client and Server are synced
    type Todo = {
        Id : Guid
        Title : string
        Completed : bool
    }

    module Todo =
        let create title = {
            Id = Guid.CreateVersion7 ()
            Title = title
            Completed = false
        }

        let complete todo = { todo with Completed = true }

        let toggleComplete todo = {
            todo with
                Completed = not todo.Completed
        }

    type Visibility =
        | All
        | Active
        | Completed

    type Model = {
        NewTodo : string
        Todos : List<Todo>
        Visibility : Visibility
    }

    type Msg =
        | UserChangedNewTodo of string
        | UserSubmittedNewTodo
        | UserToggledCompletedStatus of Guid
        | UserDeletedTodo of Guid
        | UserDeletedCompletedTodos
        | UserChangedVisibility of Visibility

    let init () : Model * Cmd<Msg> =
        let model = {
            NewTodo = ""
            // TODO: Remove test data once connected to server
            Todos = [ Todo.create "Learn Elm" |> Todo.complete; Todo.create "Learn F#" ]
            Visibility = All
        }

        model, Cmd.none

    let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
        match msg with
        | UserChangedNewTodo text -> { model with NewTodo = text }, Cmd.none
        | UserSubmittedNewTodo ->
            let title = model.NewTodo.Trim ()

            let todos =
                if title.Length > 0 then
                    model.Todos @ [ Todo.create title ]
                else
                    model.Todos

            {
                model with
                    NewTodo = ""
                    Todos = todos
            },
            Cmd.none
        | UserToggledCompletedStatus id ->
            let todos =
                List.map (fun todo -> if todo.Id = id then Todo.toggleComplete todo else todo) model.Todos

            { model with Todos = todos }, Cmd.none
        | UserDeletedTodo id ->
            let todos = List.filter (fun todo -> todo.Id <> id) model.Todos
            { model with Todos = todos }, Cmd.none
        | UserDeletedCompletedTodos ->
            let todos = List.filter (fun todo -> not todo.Completed) model.Todos
            { model with Todos = todos }, Cmd.none
        | UserChangedVisibility visibility -> { model with Visibility = visibility }, Cmd.none

    let todoListItem (dispatch : Msg -> Unit) (todo : Todo) =
        Html.li [
            prop.classes [
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
            prop.key todo.Id
            prop.children [
                Html.input [
                    prop.type' "checkbox"
                    prop.className "w-5 mx-5"
                    prop.isChecked todo.Completed
                    prop.onCheckedChange (fun e -> dispatch (UserToggledCompletedStatus todo.Id))
                ]
                // TODO: Double clicking should turn the element into an editable text field
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
                (fun (active, completed) todo ->
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
            | Completed -> List.filter (fun todo -> todo.Completed) model.Todos

        let visibilityClasses visibility =
            let baseClasses = [ "p-1"; "rounded-sm"; "border-1" ]

            if visibility = model.Visibility then
                "border-rose-300/40" :: "border-solid" :: baseClasses
            else
                "border-rose-300/0"
                :: "hover:border-rose-300/20"
                :: "hover::border-solid"
                :: baseClasses

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
                                    dispatch (UserSubmittedNewTodo))
                        ]
                    ]
                    Html.ol [
                        prop.className "drop-shadow-sm"
                        prop.children (List.map (todoListItem dispatch) filteredTodos)
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
