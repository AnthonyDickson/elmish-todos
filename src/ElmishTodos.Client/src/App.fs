module App

open Browser.Dom
open Feliz
open Feliz.UseElmish
open Elmish

type Todo = {
    Id : int
    Title : string
    Completed : bool
}

module Todo =
    let create id title = {
        Id = id
        Title = title
        Completed = false
    }

    let complete todo = { todo with Completed = true }

    let toggleComplete todo = {
        todo with
            Completed = not todo.Completed
    }

type Model = {
    NewTodo : string
    NextId : int
    Todos : List<Todo>
}

type Msg =
    | UserChangedNewTodo of string
    | UserSubmittedNewTodo of string
    | UserToggledCompletedStatus of id : int
    | UserDeletedTodo of id : int

let init () : Model * Cmd<Msg> =
    let model = {
        NewTodo = ""
        NextId = 2
        Todos = [ Todo.create 0 "Learn Elm" |> Todo.complete; Todo.create 1 "Learn F#" ]

    }

    let cmd = Cmd.ofEffect (fun _ -> document.title <- "Elmish TodoMVC")
    model, cmd

let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with
    | UserChangedNewTodo text -> { model with NewTodo = text }, Cmd.none
    | UserSubmittedNewTodo title ->
        {
            NewTodo = ""
            NextId = model.NextId + 1
            Todos = model.Todos @ [ Todo.create model.NextId model.NewTodo ]
        },
        Cmd.none
    | UserToggledCompletedStatus id ->
        let todos =
            List.map (fun todo -> if todo.Id = id then Todo.toggleComplete todo else todo) model.Todos

        { model with Todos = todos }, Cmd.none
    | UserDeletedTodo id ->
        let todos = List.filter (fun todo -> todo.Id <> id) model.Todos
        { model with Todos = todos }, Cmd.none

let todoListItem (dispatch : Msg -> Unit) (todo : Todo) =
    Html.div [
        prop.classes [
            "bg-gray-50"
            "py-5"
            "min-w-lg"
            "text-2xl"
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
    let todoCount = List.length model.Todos

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
                            "drop-shadow-md"
                            "focus-visible:outline-none"
                            "py-5"
                            "px-15"
                            "min-w-lg"
                            "placeholder:text-2xl"
                            "placeholder:text-gray-300"
                            "placeholder:italic"
                        ]
                        prop.onChange (fun (e : string) -> dispatch (UserChangedNewTodo e))
                        prop.onKeyDown (fun e ->
                            if e.key = "Enter" then
                                dispatch (UserSubmittedNewTodo model.NewTodo))
                    ]
                ]
                Html.section [
                    prop.className "drop-shadow-md"
                    prop.children (List.map (todoListItem dispatch) model.Todos)
                ]
                Html.footer [
                    prop.classes [
                        "text-gray-500"
                        "text-sm"
                        "bg-gray-50"
                        "drop-shadow-md"
                        "py-2"
                        "px-5"
                        "min-w-lg"
                        "border-t-1"
                        "border-gray-200"
                    ]
                    prop.children [
                        Html.p [
                            prop.text (
                                if todoCount = 1 then
                                    $"{todoCount} item left"
                                else
                                    $"{todoCount} items left"
                            )
                        ]
                    ]
                ]
            ]
        )
    ]

[<ReactComponent>]
let TodosApp () =
    let model, dispatch = React.useElmish (init, update, [||])
    view model dispatch

ReactDOM.createRoot(document.getElementById "app").render (TodosApp ())
|> ignore
