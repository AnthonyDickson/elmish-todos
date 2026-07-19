import api_error
import effect
import gleam/dynamic/decode as dynamic_decode
import gleam/int
import gleam/json
import gleam/list
import gleam/option.{type Option, None, Some}
import gleam/string
import gleam/time/timestamp
import lustre/attribute
import lustre/element.{type Element, none, text}
import lustre/element/html
import lustre/event
import response
import todo_item
import youid/uuid

// ── Types ────────────────────────────────────────────────────────────────────

pub type Visibility {
  All
  Active
  Completed
}

pub type EditState {
  EditState(id: uuid.Uuid, new_title: String)
}

pub type TodoAction {
  UpdateCompleted(id: uuid.Uuid, previous_state: Bool)
  UpdateTitle(id: uuid.Uuid, previous_title: String)
  Create(id: uuid.Uuid)
  Delete(previous_todo: todo_item.Todo)
}

pub type Toast {
  Toast(id: uuid.Uuid, title: String, body: String)
}

pub type Model {
  Model(
    new_todo: String,
    todos: List(todo_item.Todo),
    visibility: Visibility,
    edit_state: Option(EditState),
    toasts: List(Toast),
  )
}

pub type Msg {
  ClientLoadedTodos(todos: List(todo_item.Todo))
  ClientFetchedTodos(result: Result(List(todo_item.Todo), api_error.ApiError))
  TodoActionFailed(action: TodoAction, error: api_error.ApiError)
  ToastDismissed(id: uuid.Uuid)
  UserChangedNewTodo(text: String)
  UserSubmittedNewTodo
  UserToggledCompletedStatus(id: uuid.Uuid)
  UserEnteredEditMode(id: uuid.Uuid)
  UserEditedTodo(text: String)
  UserExitedEditMode
  UserSubmittedEditedTodo
  UserDeletedTodo(id: uuid.Uuid)
  UserDeletedCompletedTodos
  UserChangedVisibility(visibility: Visibility)
  UserClickedLogout
  NoOp
}

// ── Constants ────────────────────────────────────────────────────────────────

const local_storage_key = "todomvc-lustre"

// ── Helpers ──────────────────────────────────────────────────────────────────

fn rollback(model: Model, action: TodoAction) -> Model {
  case action {
    UpdateCompleted(id, previous_state) ->
      Model(
        ..model,
        todos: list.map(model.todos, fn(t) {
          case t.id == id {
            True -> todo_item.Todo(..t, completed: previous_state)
            False -> t
          }
        }),
      )
    UpdateTitle(id, previous_title) ->
      Model(
        ..model,
        todos: list.map(model.todos, fn(t) {
          case t.id == id {
            True -> todo_item.Todo(..t, title: previous_title)
            False -> t
          }
        }),
      )
    Create(id) ->
      Model(..model, todos: list.filter(model.todos, fn(t) { t.id != id }))
    Delete(previous_todo) -> {
      let todos =
        list.append(model.todos, [previous_todo])
        |> list.sort(fn(a, b) {
          string.compare(uuid.to_string(a.id), uuid.to_string(b.id))
        })
      Model(..model, todos:)
    }
  }
}

fn create_toast(model: Model, action: TodoAction) -> Option(Toast) {
  case action {
    UpdateCompleted(id, previous_state) ->
      case list.find(model.todos, fn(t) { t.id == id }) {
        Ok(item) -> {
          let state_text = case previous_state {
            True -> "completed"
            False -> "not completed"
          }
          Some(Toast(
            id: uuid.v7(),
            title: "Could not sync changes",
            body: "Reverted the todo status from '"
              <> item.title
              <> "' to "
              <> state_text,
          ))
        }
        Error(Nil) -> None
      }
    UpdateTitle(id, previous_title) ->
      case list.find(model.todos, fn(t) { t.id == id }) {
        Ok(item) ->
          Some(Toast(
            id: uuid.v7(),
            title: "Could not sync changes",
            body: "Reverted the todo title from '"
              <> item.title
              <> "' to '"
              <> previous_title
              <> "'",
          ))
        Error(Nil) -> None
      }
    Create(id) ->
      case list.find(model.todos, fn(t) { t.id == id }) {
        Ok(item) ->
          Some(Toast(
            id: uuid.v7(),
            title: "Could not sync changes",
            body: "Reverted the creation of the todo '" <> item.title <> "'",
          ))
        Error(Nil) -> None
      }
    Delete(previous_todo) ->
      Some(Toast(
        id: uuid.v7(),
        title: "Could not sync changes",
        body: "Reverted the deletion of the todo '"
          <> previous_todo.title
          <> "'",
      ))
  }
}

fn save_todos(todos: List(todo_item.Todo)) -> effect.Effect(msg) {
  let json_str =
    todos
    |> list.map(todo_item.todo_to_json)
    |> json.preprocessed_array
    |> json.to_string
  effect.SaveToStore(local_storage_key, json_str)
}

fn fetch_todos() -> effect.Effect(Msg) {
  effect.get("/api/todos", fn(result) {
    case result {
      Ok(body) ->
        ClientFetchedTodos(response.decode_success(
          body,
          todo_item.todos_decoder(),
        ))
      Error(http_err) ->
        ClientFetchedTodos(Error(response.http_error_to_api_error(http_err)))
    }
  })
}

fn load_todos_from_store() -> effect.Effect(Msg) {
  effect.LoadFromStore(key: local_storage_key, callback: fn(store_result) {
    case store_result {
      Ok(value) ->
        case json.parse(value, using: todo_item.todos_decoder()) {
          Ok(todos) -> ClientLoadedTodos(todos)
          Error(_) -> NoOp
        }
      Error(_) -> NoOp
    }
  })
}

// ── Init ─────────────────────────────────────────────────────────────────────

pub fn init() -> #(Model, effect.Effect(Msg)) {
  let model =
    Model(
      new_todo: "",
      todos: [],
      visibility: All,
      edit_state: None,
      toasts: [],
    )

  #(model, effect.batch([fetch_todos(), load_todos_from_store()]))
}

// ── Update ───────────────────────────────────────────────────────────────────

pub fn update(model: Model, msg: Msg) -> #(Model, effect.Effect(Msg)) {
  case msg {
    ClientLoadedTodos(todos) -> #(Model(..model, todos:), effect.none())

    ClientFetchedTodos(Ok(todos)) -> #(
      Model(..model, todos:),
      save_todos(todos),
    )

    ClientFetchedTodos(Error(error)) ->
      case error.status_code {
        Some(401) -> #(model, effect.Redirect("/login"))
        _ -> {
          let toast =
            Toast(
              id: uuid.v7(),
              title: "Could not sync todos",
              body: "Falling back to local data",
            )
          #(
            Model(..model, toasts: list.append(model.toasts, [toast])),
            effect.batch([effect.After(5000, ToastDismissed(toast.id))]),
          )
        }
      }

    TodoActionFailed(action, error) -> {
      let updated_model = rollback(model, action)
      case error.status_code {
        Some(401) -> #(updated_model, effect.Redirect("/login"))
        _ ->
          case create_toast(model, action) {
            Some(toast) -> #(
              Model(
                ..updated_model,
                toasts: list.append(updated_model.toasts, [toast]),
              ),
              effect.batch([effect.After(5000, ToastDismissed(toast.id))]),
            )
            None -> #(updated_model, effect.none())
          }
      }
    }

    ToastDismissed(id) -> {
      let toasts = list.filter(model.toasts, fn(t) { t.id != id })
      #(Model(..model, toasts:), effect.none())
    }

    UserChangedNewTodo(text) -> #(Model(..model, new_todo: text), effect.none())

    UserSubmittedNewTodo -> {
      let title = string.trim(model.new_todo)
      case title {
        "" -> #(Model(..model, new_todo: ""), effect.none())
        _ -> {
          let now = timestamp.system_time()
          let item =
            todo_item.Todo(
              id: uuid.v7(),
              title:,
              completed: False,
              created_at: now,
            )
          let body =
            item
            |> todo_item.todo_to_json
            |> json.to_string
          let create_effect =
            effect.post("/api/todos", body, "application/json", fn(result) {
              case result {
                Ok(_) -> NoOp
                Error(err) ->
                  TodoActionFailed(
                    Create(item.id),
                    response.http_error_to_api_error(err),
                  )
              }
            })
          #(
            Model(
              ..model,
              new_todo: "",
              todos: list.append(model.todos, [item]),
            ),
            create_effect,
          )
        }
      }
    }

    UserToggledCompletedStatus(id) ->
      case list.find(model.todos, fn(t) { t.id == id }) {
        Ok(item) -> {
          let updated_item = todo_item.Todo(..item, completed: !item.completed)
          let todos =
            list.map(model.todos, fn(t) {
              case t.id == id {
                True -> updated_item
                False -> t
              }
            })
          let request =
            todo_item.UpdateTodoRequest(
              title: updated_item.title,
              completed: updated_item.completed,
            )
          let body =
            request
            |> todo_item.update_todo_request_to_json
            |> json.to_string
          let url = "/api/todos/" <> uuid.to_string(id)
          let patch_effect =
            effect.patch(url, body, "application/json", fn(result) {
              case result {
                Ok(_) -> NoOp
                Error(err) ->
                  TodoActionFailed(
                    UpdateCompleted(updated_item.id, item.completed),
                    response.http_error_to_api_error(err),
                  )
              }
            })
          #(Model(..model, todos:), patch_effect)
        }
        Error(Nil) -> #(model, effect.none())
      }

    UserEnteredEditMode(id) ->
      case list.find(model.todos, fn(t) { t.id == id }) {
        Ok(item) -> #(
          Model(
            ..model,
            edit_state: Some(EditState(id:, new_title: item.title)),
          ),
          effect.none(),
        )
        Error(Nil) -> #(model, effect.none())
      }

    UserEditedTodo(text) -> {
      let edit_state =
        option.map(model.edit_state, fn(e) { EditState(..e, new_title: text) })
      #(Model(..model, edit_state:), effect.none())
    }

    UserExitedEditMode -> #(Model(..model, edit_state: None), effect.none())

    UserSubmittedEditedTodo ->
      case model.edit_state {
        Some(EditState(id:, new_title:)) -> {
          let new_title = string.trim(new_title)
          case new_title {
            "" -> #(
              Model(..model, edit_state: None),
              effect.After(0, UserDeletedTodo(id)),
            )
            _ ->
              case list.find(model.todos, fn(t) { t.id == id }) {
                Ok(item) -> {
                  let todos =
                    list.map(model.todos, fn(t) {
                      case t.id == id {
                        True -> todo_item.Todo(..t, title: new_title)
                        False -> t
                      }
                    })
                  let request =
                    todo_item.UpdateTodoRequest(
                      title: new_title,
                      completed: item.completed,
                    )
                  let body =
                    request
                    |> todo_item.update_todo_request_to_json
                    |> json.to_string
                  let url = "/api/todos/" <> uuid.to_string(id)
                  let patch_effect =
                    effect.patch(url, body, "application/json", fn(result) {
                      case result {
                        Ok(_) -> NoOp
                        Error(err) ->
                          TodoActionFailed(
                            UpdateTitle(item.id, item.title),
                            response.http_error_to_api_error(err),
                          )
                      }
                    })
                  #(Model(..model, edit_state: None, todos:), patch_effect)
                }
                Error(Nil) -> #(Model(..model, edit_state: None), effect.none())
              }
          }
        }
        None -> #(Model(..model, edit_state: None), effect.none())
      }

    UserDeletedTodo(id) ->
      case list.find(model.todos, fn(t) { t.id == id }) {
        Ok(item_to_remove) -> {
          let todos = list.filter(model.todos, fn(t) { t.id != id })
          let url = "/api/todos/" <> uuid.to_string(id)
          let delete_effect =
            effect.delete(url, fn(result) {
              case result {
                Ok(_) -> NoOp
                Error(err) ->
                  TodoActionFailed(
                    Delete(item_to_remove),
                    response.http_error_to_api_error(err),
                  )
              }
            })
          #(Model(..model, todos:), delete_effect)
        }
        Error(Nil) -> #(model, effect.none())
      }

    UserDeletedCompletedTodos -> {
      let completed = list.filter(model.todos, fn(t) { t.completed })
      let active = list.filter(model.todos, fn(t) { !t.completed })
      let delete_effects =
        list.map(completed, fn(item) {
          effect.After(0, UserDeletedTodo(item.id))
        })
      #(Model(..model, todos: active), effect.batch(delete_effects))
    }

    UserChangedVisibility(visibility) -> #(
      Model(..model, visibility:),
      effect.none(),
    )

    UserClickedLogout -> #(model, effect.Redirect("/logout"))

    NoOp -> #(model, effect.none())
  }
}

// ── View ─────────────────────────────────────────────────────────────────────

fn view_toast(toast: Toast) -> Element(Msg) {
  html.div(
    [
      attribute.class(
        "pointer-events-auto bg-gray-50 border border-gray-200 border-l-4 border-l-amber-400/40 shadow-lg p-4 max-w-sm animate-[toast-in_0.3s_ease-out]",
      ),
      attribute.role("alert"),
    ],
    [
      html.div([attribute.class("flex justify-between items-start gap-3")], [
        html.div([], [
          html.p([attribute.class("text-sm font-medium text-gray-600")], [
            text(toast.title),
          ]),
          html.p([attribute.class("text-sm text-gray-500 mt-1")], [
            text(toast.body),
          ]),
        ]),
        html.button(
          [
            attribute.class(
              "text-gray-300 hover:text-gray-500 shrink-0 text-lg leading-none cursor-pointer",
            ),
            attribute.aria_label("Dismiss"),
            event.on_click(ToastDismissed(toast.id)),
          ],
          [text("x")],
        ),
      ]),
    ],
  )
}

fn todo_list_item(
  edit_state: Option(EditState),
  item: todo_item.Todo,
) -> Element(Msg) {
  let li_classes =
    "bg-gray-50 py-5 min-w-xl text-2xl border-t-1 border-gray-200 flex items-center group"

  case edit_state {
    Some(EditState(id:, new_title:)) if id == item.id ->
      html.li([attribute.class(li_classes)], [
        html.input([
          attribute.type_("text"),
          attribute.autofocus(True),
          attribute.value(new_title),
          event.on_input(fn(text) { UserEditedTodo(text) }),
          event.on_keydown(fn(key) {
            case key {
              "Enter" -> UserSubmittedEditedTodo
              _ -> NoOp
            }
          }),
          event.on_blur(UserExitedEditMode),
          attribute.placeholder("What needs to be done?"),
          attribute.class(
            "text-gray-600 text-2xl bg-gray-50 focus-visible:outline-none px-15 min-w-xl placeholder:text-2xl placeholder:text-gray-300 placeholder:italic",
          ),
        ]),
      ])
    _ ->
      html.li(
        [
          attribute.class(li_classes),
          event.on(
            "dblclick",
            dynamic_decode.success(UserEnteredEditMode(item.id)),
          ),
        ],
        [
          html.input([
            attribute.type_("checkbox"),
            attribute.class("w-5 mx-5"),
            attribute.checked(item.completed),
            event.on_check(fn(_) { UserToggledCompletedStatus(item.id) }),
          ]),
          html.p(
            [
              attribute.class(case item.completed {
                True -> "line-through text-gray-300"
                False -> "text-gray-600"
              }),
            ],
            [text(item.title)],
          ),
          html.button(
            [
              attribute.class(
                "ml-auto mx-5 w-5 text-red-400/0 group-hover:text-red-400",
              ),
              event.on_click(UserDeletedTodo(item.id)),
            ],
            [text("x")],
          ),
        ],
      )
  }
}

fn visibility_classes(
  visibility: Visibility,
  model_visibility: Visibility,
) -> String {
  let base = "p-1 rounded-sm border-1"
  case visibility == model_visibility {
    True -> "border-rose-300/40 " <> base
    False -> "border-rose-300/0 hover:border-rose-300/20 " <> base
  }
}

pub fn view(model: Model) -> Element(Msg) {
  let #(active_count, completed_count) =
    list.fold(model.todos, #(0, 0), fn(acc, t) {
      let #(active, completed) = acc
      case t.completed {
        True -> #(active, completed + 1)
        False -> #(active + 1, completed)
      }
    })

  let todo_count = active_count + completed_count

  let filtered_todos = case model.visibility {
    All -> model.todos
    Active -> list.filter(model.todos, fn(t) { !t.completed })
    Completed -> list.filter(model.todos, fn(t) { t.completed })
  }

  html.div([attribute.class("bg-gray-100 h-dvh flex h-screen justify-center")], [
    case model.toasts {
      [] -> none()
      toasts ->
        html.div(
          [
            attribute.class(
              "fixed top-4 right-4 z-50 flex flex-col gap-2 pointer-events-none",
            ),
          ],
          list.map(toasts, view_toast),
        )
    },
    html.main([], [
      html.header([], [
        html.h1(
          [
            attribute.class("text-8xl text-rose-300/30 text-center m-5"),
          ],
          [text("todos")],
        ),
        html.input([
          attribute.type_("text"),
          attribute.autofocus(True),
          attribute.value(model.new_todo),
          attribute.placeholder("What needs to be done?"),
          attribute.class(
            "text-gray-600 text-2xl bg-gray-50 drop-shadow-sm focus-visible:outline-none py-5 px-15 min-w-xl placeholder:text-2xl placeholder:text-gray-300 placeholder:italic",
          ),
          event.on_input(fn(text) { UserChangedNewTodo(text) }),
          event.on_keydown(fn(key) {
            case key {
              "Enter" -> UserSubmittedNewTodo
              _ -> NoOp
            }
          }),
        ]),
      ]),
      html.ol(
        [attribute.class("drop-shadow-sm")],
        list.map(filtered_todos, fn(t) { todo_list_item(model.edit_state, t) }),
      ),
      case todo_count > 0 {
        True ->
          html.footer(
            [
              attribute.class(
                "text-gray-500 text-sm bg-gray-50 drop-shadow-sm py-2 px-5 min-w-lg border-t-1 border-gray-200 flex justify-between",
              ),
            ],
            [
              html.p([attribute.class("pt-1")], [
                html.strong([], [text(int.to_string(active_count))]),
                text(case active_count {
                  1 -> " item left"
                  _ -> " items left"
                }),
              ]),
              html.div([attribute.class("flex gap-2")], [
                html.a(
                  [
                    attribute.href("/"),
                    attribute.class(visibility_classes(All, model.visibility)),
                  ],
                  [text("All")],
                ),
                html.a(
                  [
                    attribute.href("/active"),
                    attribute.class(visibility_classes(Active, model.visibility)),
                  ],
                  [text("Active")],
                ),
                html.a(
                  [
                    attribute.href("/completed"),
                    attribute.class(visibility_classes(
                      Completed,
                      model.visibility,
                    )),
                  ],
                  [text("Completed")],
                ),
              ]),
              html.button(
                [
                  attribute.class(
                    "hover:underline"
                    <> {
                      case completed_count {
                        0 -> " invisible"
                        _ -> ""
                      }
                    },
                  ),
                  event.on_click(UserDeletedCompletedTodos),
                ],
                [
                  text(
                    "Clear completed (" <> int.to_string(completed_count) <> ")",
                  ),
                ],
              ),
            ],
          )
        False -> none()
      },
      html.footer([attribute.class("flex justify-end py-2 px-5 min-w-lg")], [
        html.button(
          [
            attribute.class("text-sm text-gray-400 hover:text-gray-600"),
            event.on_click(UserClickedLogout),
          ],
          [text("Logout")],
        ),
      ]),
    ]),
  ])
}

// ── Update with storage ──────────────────────────────────────────────────────

pub fn update_with_storage(
  model: Model,
  msg: Msg,
) -> #(Model, effect.Effect(Msg)) {
  let #(new_model, effects) = update(model, msg)
  let todos_json =
    new_model.todos
    |> list.map(todo_item.todo_to_json)
    |> json.preprocessed_array
    |> json.to_string
  #(
    new_model,
    effect.batch([effects, effect.SaveToStore(local_storage_key, todos_json)]),
  )
}
