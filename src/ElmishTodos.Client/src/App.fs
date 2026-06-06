module App

open Browser.Dom
open Feliz
open Feliz.UseElmish
open Elmish

type Todo = { Title : string; Completed : bool }

module Todo =
    let create title = { Title = title; Completed = false }

type Model = { NewTodo : string; Todos : List<Todo> }

type Msg =
    | UserChangedNewTodo of string
    | UserSubmittedNewTodo of string

let init () : Model * Cmd<Msg> =
#if DEBUG
    let model = {
        NewTodo = ""
        Todos = [ Todo.create "Learn Elm" ]
    }
#else
    let model = { NewTodo = ""; Todos = [] }
#endif

    let cmd = Cmd.ofEffect (fun _ -> document.title <- "Elmish TodoMVC")
    model, cmd

let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with
    | UserChangedNewTodo text -> { model with NewTodo = text }, Cmd.none
    | UserSubmittedNewTodo title ->
        {
            NewTodo = ""
            Todos = model.Todos @ [ Todo.create model.NewTodo ]
        },
        Cmd.none

let todoListItem todo =
    Html.p [
        prop.text todo.Title
        prop.classes [ "text-gray-600"; "bg-gray-50"; "py-5"; "px-15"; "min-w-lg"; "text-2xl" ]
    ]

let view (model : Model) (dispatch : Msg -> unit) =
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
                            "bg-gray-50"
                            "drop-shadow-md"
                            "focus-visible:outline-none"
                            "py-5"
                            "px-15"
                            "min-w-lg"
                            "text-2xl"
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
                    prop.children (List.map todoListItem model.Todos)
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
