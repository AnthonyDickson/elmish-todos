module App

open Browser.Dom
open Feliz
open Feliz.UseElmish
open Elmish

type Model = { Title : string }

type Msg = | Foo

let init () =
    document.title <- "Elmish Todos"
    { Model.Title = "Hello, Feliz!" }, Cmd.none

let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with
    | Foo -> { Model.Title = "Foo, Bar!" }, Cmd.none

let view (model : Model) (dispatch : Msg -> unit) =
    Html.h1 [
        prop.text model.Title
        prop.className "text-3xl font-bold underline text-red-500"
        prop.onClick (fun _ -> dispatch Foo)
    ]

[<ReactComponent>]
let TodosApp () =
    let model, dispatch = React.useElmish (init, update, [||])
    view model dispatch

ReactDOM.createRoot(document.getElementById "app").render (TodosApp ())
|> ignore
