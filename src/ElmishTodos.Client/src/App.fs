module App


open System

open Browser.Dom
open Elmish
open Feliz
open Feliz.Router
open Feliz.UseElmish

open ElmishTodos.Client.Pages.Todo


type Page = TodoPage

type Model = {
    CurrentPage : Page
    TodoPage : Todo.Model
}

type Msg =
    | UrlChanged of string list
    | TodoPageMsg of Todo.Msg

let init () : Model * Cmd<Msg> =
    let model, cmd = Todo.initWithLocalStorage ()

    let model = {
        TodoPage = model
        CurrentPage = TodoPage
    }

    let cmd =
        Cmd.batch [
            Cmd.map TodoPageMsg cmd
            Cmd.ofEffect (fun _ -> document.title <- "Elmish TodoMVC")
        ]

    model, cmd


let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    let urlToVisibility =
        function
        | [] -> Todo.All
        | [ "active" ] -> Todo.Active
        | [ "completed" ] -> Todo.Completed
        | _ -> model.TodoPage.Visibility

    match msg with
    | UrlChanged segments ->
        let todoModel, cmd =
            Todo.updateWithLocalStorage (Todo.UserChangedVisibility (urlToVisibility segments)) model.TodoPage

        { model with TodoPage = todoModel }, Cmd.map TodoPageMsg cmd
    | TodoPageMsg innerMsg ->
        let innerModel, innerCmd = Todo.updateWithLocalStorage innerMsg model.TodoPage
        let innerCmd = Cmd.map TodoPageMsg innerCmd
        { model with TodoPage = innerModel }, innerCmd

let view (model : Model) (dispatch : Msg -> unit) =
    let page =
        match model.CurrentPage with
        | TodoPage -> Todo.view model.TodoPage (TodoPageMsg >> dispatch)

    React.router [ router.onUrlChanged (UrlChanged >> dispatch); router.children page ]

[<ReactComponent>]
let TodosApp () =
    let model, dispatch = React.useElmish (init, update, [||])
    view model dispatch

ReactDOM.createRoot(document.getElementById "app").render (TodosApp ())
|> ignore
