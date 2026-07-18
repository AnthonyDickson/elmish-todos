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
  Navigate(handler: fn(String) -> msg)
  PushUrl(url: String)
  ReplaceUrl(url: String)
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

/// Set up client-side routing: intercept clicks on internal links, listen for
/// back/forward navigation, and dispatch the initial URL. The handler receives
/// the full path (pathname + search + hash).
///
pub fn navigate(handler: fn(String) -> msg) -> Effect(msg) {
  Navigate(handler:)
}

/// Push a new URL onto the browser's history stack without a full page reload.
/// Dispatches no message.
///
pub fn push_url(url: String) -> Effect(msg) {
  PushUrl(url:)
}

/// Replace the current URL in the browser's history stack without a full page
/// reload. Dispatches no message.
///
pub fn replace_url(url: String) -> Effect(msg) {
  ReplaceUrl(url:)
}

@external(javascript, "./effect_ffi.mjs", "loadFromStore")
fn raw_load_from_store(key: String) -> String

@external(javascript, "./effect_ffi.mjs", "saveToStore")
fn raw_save_to_store(key: String, value: String) -> Nil

@external(javascript, "./effect_ffi.mjs", "initNavigation")
fn raw_init_navigation(handler: fn(String) -> Nil) -> Nil

@external(javascript, "./effect_ffi.mjs", "pushUrl")
fn raw_push_url(url: String) -> Nil

@external(javascript, "./effect_ffi.mjs", "replaceUrl")
fn raw_replace_url(url: String) -> Nil

@external(javascript, "./effect_ffi.mjs", "redirect")
fn raw_redirect(url: String) -> Nil

/// Transform an `Effect(a)` into an `Effect(b)` by applying a function to
/// every message the effect produces. This is the analogue of `Cmd.map` in
/// Elmish — it lets a parent component embed a child's effects.
///
pub fn map(effect: Effect(a), f: fn(a) -> b) -> Effect(b) {
  case effect {
    HttpRequest(method:, url:, body:, runner:) ->
      HttpRequest(method:, url:, body:, runner: fn(dispatch) {
        runner(fn(a) { dispatch(f(a)) })
      })
    LoadFromStore(key:, callback:) ->
      LoadFromStore(key:, callback: fn(result) { f(callback(result)) })
    SaveToStore(key:, value:) -> SaveToStore(key:, value:)
    Redirect(url:) -> Redirect(url:)
    After(delay:, message:) -> After(delay:, message: f(message))
    Navigate(handler:) -> Navigate(handler: fn(path) { f(handler(path)) })
    PushUrl(url:) -> PushUrl(url:)
    ReplaceUrl(url:) -> ReplaceUrl(url:)
    Batch(effects:) -> Batch(list.map(effects, fn(e) { map(e, f) }))
    None -> None
  }
}

/// Combine a list of effects into a single `Batch` effect.
///
pub fn batch(effects: List(Effect(msg))) -> Effect(msg) {
  Batch(effects)
}

/// A no-op effect — produces no messages.
///
pub fn none() -> Effect(msg) {
  None
}

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

    Navigate(handler:) -> {
      raw_init_navigation(fn(path) { dispatch(handler(path)) })
    }

    PushUrl(url:) -> raw_push_url(url)

    ReplaceUrl(url:) -> raw_replace_url(url)

    Batch(effects:) -> {
      effects
      |> list.each(fn(e) { run(e, dispatch) })
      Nil
    }

    None -> Nil
  }
}
