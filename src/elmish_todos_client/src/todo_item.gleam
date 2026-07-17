import gleam/dynamic/decode
import gleam/json
import youid/uuid

pub type Todo {
  Todo(id: uuid.Uuid, title: String, completed: Bool, created_at: String)
}

pub type UpdateTodoRequest {
  UpdateTodoRequest(title: String, completed: Bool)
}

fn uuid_decoder() -> decode.Decoder(uuid.Uuid) {
  decode.string
  |> decode.then(fn(s) {
    case uuid.from_string(s) {
      Ok(u) -> decode.success(u)
      Error(Nil) -> decode.failure(uuid.nil, "Uuid")
    }
  })
}

pub fn todo_decoder() -> decode.Decoder(Todo) {
  use id <- decode.field("id", uuid_decoder())
  use title <- decode.field("title", decode.string)
  use completed <- decode.field("completed", decode.bool)
  use created_at <- decode.field("createdAt", decode.string)
  decode.success(Todo(id:, title:, completed:, created_at:))
}

pub fn todos_decoder() -> decode.Decoder(List(Todo)) {
  decode.list(todo_decoder())
}

pub fn todo_to_json(item: Todo) -> json.Json {
  json.object([
    #("id", json.string(uuid.to_string(item.id))),
    #("title", json.string(item.title)),
    #("completed", json.bool(item.completed)),
    #("createdAt", json.string(item.created_at)),
  ])
}

pub fn update_todo_request_to_json(req: UpdateTodoRequest) -> json.Json {
  json.object([
    #("title", json.string(req.title)),
    #("completed", json.bool(req.completed)),
  ])
}
