import gleam/javascript/promise
import gleam/list
import http_effect.{
  type HttpError, type HttpMethod, Delete, Get, Patch, Post, Put,
}

/// An inspectable description of a side effect. `update` returns one of these
/// alongside the new model; the `run` interpreter executes it against the real
/// world. Because `Effect` is pure data, tests can pattern-match on it without
/// a browser, mock HTTP, or fake localStorage.
///
/// All variants carry raw strings — the effect system describes I/O intent,
/// not data semantics. Callers own serialisation and deserialisation.
///
/// For HTTP effects, use the per-method functions below (`get`, `post`, …).
/// For custom HTTP behaviour (auth headers, non-standard methods), use
/// `http_effect.send` / `http_effect.send_with` directly.
///
pub type Effect(msg) {
  HttpRequest(
    method: HttpMethod,
    url: String,
    body: String,
    runner: fn(fn(msg) -> Nil) -> Nil,
  )
  LoadFromStore(key: String, callback: fn(Result(String, String)) -> msg)
  SaveToStore(key: String, value: String)
  Redirect(url: String)
  After(delay: Int, message: msg)
  Batch(effects: List(Effect(msg)))
  None
}

/// Wrap a promise from `http_effect.send` / `http_effect.send_with` into an
/// `HttpRequest` effect. Use this when you need custom HTTP behaviour (auth
/// headers, retry logic, non-standard methods) — build the promise with
/// `http_effect`, then pass it here. For everyday requests, use `get`,
/// `post`, `put`, `patch`, or `delete` directly.
///
/// ```gleam
/// let promise = http_effect.send_with(Post, url, body, "application/json", fn(req) {
///   request.set_header(req, "authorization", "Bearer " <> token)
/// })
/// let effect = effect.from_promise(Post, url, body, promise, TodoCreated)
/// ```
///
pub fn from_promise(
  method: HttpMethod,
  url: String,
  body: String,
  promise: promise.Promise(Result(String, HttpError)),
  callback: fn(Result(String, HttpError)) -> msg,
) -> Effect(msg) {
  HttpRequest(method:, url:, body:, runner: fn(dispatch: fn(msg) -> Nil) -> Nil {
    let _ = promise |> promise.map(fn(result) { dispatch(callback(result)) })
    Nil
  })
}

/// `GET` the given URL.
///
pub fn get(
  url: String,
  callback: fn(Result(String, HttpError)) -> msg,
) -> Effect(msg) {
  let promise = http_effect.send(Get, url, "", "")
  from_promise(Get, url, "", promise, callback)
}

/// `POST` a pre-serialised body to the given URL.
///
pub fn post(
  url: String,
  body: String,
  content_type: String,
  callback: fn(Result(String, HttpError)) -> msg,
) -> Effect(msg) {
  let promise = http_effect.send(Post, url, body, content_type)
  from_promise(Post, url, body, promise, callback)
}

/// `PUT` a pre-serialised body to the given URL (full replacement).
///
pub fn put(
  url: String,
  body: String,
  content_type: String,
  callback: fn(Result(String, HttpError)) -> msg,
) -> Effect(msg) {
  let promise = http_effect.send(Put, url, body, content_type)
  from_promise(Put, url, body, promise, callback)
}

/// `PATCH` a pre-serialised body to the given URL (partial update).
///
pub fn patch(
  url: String,
  body: String,
  content_type: String,
  callback: fn(Result(String, HttpError)) -> msg,
) -> Effect(msg) {
  let promise = http_effect.send(Patch, url, body, content_type)
  from_promise(Patch, url, body, promise, callback)
}

/// `DELETE` the resource at the given URL.
///
pub fn delete(
  url: String,
  callback: fn(Result(String, HttpError)) -> msg,
) -> Effect(msg) {
  let promise = http_effect.send(Delete, url, "", "")
  from_promise(Delete, url, "", promise, callback)
}

@external(javascript, "./effect_ffi.mjs", "loadFromStore")
fn raw_load_from_store(key: String) -> String

@external(javascript, "./effect_ffi.mjs", "saveToStore")
fn raw_save_to_store(key: String, value: String) -> Nil

@external(javascript, "./effect_ffi.mjs", "redirect")
fn raw_redirect(url: String) -> Nil

/// Execute an `Effect` against the real world. This is the single point where
/// the application touches browser APIs — all `update` logic stays pure.
///
/// Wired into Lustre via:
/// ```gleam
/// lustre_effect.from(fn(dispatch) { effect.run(effect, dispatch) })
/// ```
///
pub fn run(effect: Effect(msg), dispatch: fn(msg) -> Nil) -> Nil {
  case effect {
    HttpRequest(runner:, ..) -> runner(dispatch)

    LoadFromStore(key:, callback:) -> {
      let value = raw_load_from_store(key)
      let result = case value {
        "" -> Error("Not found")
        _ -> Ok(value)
      }
      dispatch(callback(result))
    }

    SaveToStore(key:, value:) -> raw_save_to_store(key, value)

    Redirect(url:) -> raw_redirect(url)

    After(delay:, message:) -> {
      let _ =
        promise.wait(delay)
        |> promise.tap(fn(_) { dispatch(message) })
      Nil
    }

    Batch(effects:) -> {
      effects
      |> list.each(fn(e) { run(e, dispatch) })
      Nil
    }

    None -> Nil
  }
}
