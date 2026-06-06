module App

open Browser.Dom
open Feliz
open Feliz.UseElmish
open Elmish

type Model = { Title : string }

type Msg = | Foo

let init () =
    let model = { Model.Title = "Hello, Feliz!" }
    let cmd = Cmd.ofEffect (fun _ -> document.title <- "Elmish TodoMVC")
    model, cmd

let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with
    | Foo -> { Model.Title = "Foo, Bar!" }, Cmd.none

let view (model : Model) (dispatch : Msg -> unit) =
    Html.main [
        prop.className "bg-gray-100 h-dvh flex h-screen justify-center"
        prop.children (
            Html.header [
                Html.h1 [
                    prop.text "todos"
                    prop.className "text-8xl text-rose-300/30 text-center m-5"
                    prop.onClick (fun _ -> dispatch Foo)
                ]
                Html.input [
                    prop.type' "text"
                    prop.autoFocus true
                    prop.placeholder "What needs to be done?"
                    prop.classes [
                        "text-gray-600"
                        "bg-gray-50"
                        "drop-shadow-md"
                        "focus-visible:outline-none"
                        "py-5"
                        "px-15"
                        "min-w-lg"
                        "text-xl"
                        "placeholder:text-xl"
                        "placeholder:text-gray-300"
                        "placeholder:italic"
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
