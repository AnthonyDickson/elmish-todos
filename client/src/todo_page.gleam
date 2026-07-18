import api_error
import effect
import gleam/json
import gleam/list
import gleam/option.{type Option, None, Some}
import gleam/string
import gleam/time/calendar
import gleam/time/timestamp
import lustre/element.{type Element}
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
      Model(..model, todos: list.map(model.todos, fn(t) {
        case t.id == id {
          True -> todo_item.Todo(..t, completed: previous_state)
          False -> t
        }
      }))
    UpdateTitle(id, previous_title) ->
      Model(..model, todos: list.map(model.todos, fn(t) {
        case t.id == id {
          True -> todo_item.Todo(..t, title: previous_title)
          False -> t
        }
      }))
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
  let model = Model(
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
    ClientLoadedTodos(todos) ->
      #(Model(..model, todos:), effect.none())

    ClientFetchedTodos(Ok(todos)) ->
      #(Model(..model, todos:), save_todos(todos))

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
            Some(toast) ->
              #(
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

    UserChangedNewTodo(text) ->
      #(Model(..model, new_todo: text), effect.none())

    UserSubmittedNewTodo -> {
      let title = string.trim(model.new_todo)
      case title {
        "" -> #(Model(..model, new_todo: ""), effect.none())
        _ -> {
          let now =
            timestamp.system_time()
            |> timestamp.to_rfc3339(calendar.utc_offset)
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
            effect.post(
              "/api/todos",
              body,
              "application/json",
              fn(result) {
                case result {
                  Ok(_) -> NoOp
                  Error(err) ->
                    TodoActionFailed(
                      Create(item.id),
                      response.http_error_to_api_error(err),
                    )
                }
              },
            )
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
          let updated_item =
            todo_item.Todo(..item, completed: !item.completed)
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
        Ok(item) ->
          #(
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

    UserExitedEditMode ->
      #(Model(..model, edit_state: None), effect.none())

    UserSubmittedEditedTodo ->
      case model.edit_state {
        Some(EditState(id:, new_title:)) -> {
          let new_title = string.trim(new_title)
          case new_title {
            "" ->
              #(
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
                  #(
                    Model(..model, edit_state: None, todos:),
                    patch_effect,
                  )
                }
                Error(Nil) ->
                  #(Model(..model, edit_state: None), effect.none())
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

    UserChangedVisibility(visibility) ->
      #(Model(..model, visibility:), effect.none())

    UserClickedLogout ->
      #(model, effect.Redirect("/logout"))

    NoOp -> #(model, effect.none())
  }
}

// ── View ─────────────────────────────────────────────────────────────────────

pub fn view(_model: Model) -> Element(Msg) {
  todo
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
