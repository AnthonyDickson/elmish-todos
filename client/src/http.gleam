import api_error
import gleam/dynamic/decode
import gleam/http.{Delete, Get, Patch, Post, Put}
import gleam/http/request
import gleam/http/response
import gleam/javascript/promise.{type Promise}
import gleam/json
import gleam/list
import gleam/option
import gleam/string
import gleam/fetch.{type FetchError}

/// Render a list of field-level decode errors into a human-readable string.
fn format_decode_errors(errors: List(decode.DecodeError)) -> String {
  errors
  |> list.map(fn(err) {
    let path = string.join(err.path, ".")
    "Expected " <> err.expected <> " at " <> path <> ", found " <> err.found
  })
  |> string.join("; ")
}

/// Render a top-level JSON parse error into a human-readable string.
fn format_decode_error(err: json.DecodeError) -> String {
  case err {
    json.UnexpectedEndOfInput -> "Unexpected end of JSON input"
    json.UnexpectedByte(byte) -> "Unexpected byte: " <> byte
    json.UnexpectedSequence(seq) -> "Unexpected sequence: " <> seq
    json.UnableToDecode(errors) -> format_decode_errors(errors)
  }
}

/// Render a fetch-level error into a human-readable string.
fn format_fetch_error(err: FetchError) -> String {
  case err {
    fetch.NetworkError(msg) -> "Network error: " <> msg
    fetch.UnableToReadBody -> "Unable to read response body"
    fetch.InvalidJsonBody -> "Invalid JSON body in response"
  }
}

/// Convert a `Promise(Result(a, FetchError))` into `Promise(Result(a, ApiError))`,
/// attaching the given status code to the error if the promise resolves to an error.
fn map_fetch_error(
  promise: Promise(Result(a, FetchError)),
  status_code: option.Option(Int),
) -> Promise(Result(a, api_error.ApiError)) {
  promise.await(promise, fn(result) {
    case result {
      Ok(a) -> promise.resolve(Ok(a))
      Error(err) -> promise.resolve(Error(api_error.ApiError(
        error: "Fetch error",
        details: format_fetch_error(err),
        status_code:,
      )))
    }
  })
}

/// Inspect a response: 2xx is decoded with the caller's decoder, otherwise the
/// body is parsed as an `ApiError`. Both paths include the HTTP status code on
/// failure.
fn decode_response(
  resp: response.Response(String),
  decoder: decode.Decoder(a),
) -> Result(a, api_error.ApiError) {
  case resp.status >= 200 && resp.status < 300 {
    True ->
      case json.parse(resp.body, using: decoder) {
        Ok(value) -> Ok(value)
        Error(err) -> Error(api_error.ApiError(
          error: "Decode error",
          details: format_decode_error(err),
          status_code: option.Some(resp.status),
        ))
      }
    False ->
      case json.parse(resp.body, using: api_error.decoder()) {
        Ok(err) -> Error(err)
        Error(err) -> Error(api_error.ApiError(
          error: "Unknown error",
          details: format_decode_error(err),
          status_code: option.Some(resp.status),
        ))
      }
  }
}

/// Send a request, read the text body, and decode the response — all errors are
/// surfaced as `ApiError`.
fn send(
  req: request.Request(String),
  decoder: decode.Decoder(a),
) -> Promise(Result(a, api_error.ApiError)) {
  use resp <- promise.try_await(map_fetch_error(fetch.send(req), option.None))
  let status = resp.status
  use text_resp <- promise.try_await(map_fetch_error(fetch.read_text_body(resp), option.Some(status)))
  promise.resolve(decode_response(text_resp, decoder))
}

/// `GET` the given URL and decode a successful 2xx response with the given decoder.
pub fn get(
  url: String,
  decoder: decode.Decoder(a),
) -> Promise(Result(a, api_error.ApiError)) {
  send(
    request.new() |> request.set_method(Get) |> request.set_path(url),
    decoder,
  )
}

/// `POST` a JSON body to the given URL and decode a successful 2xx response.
pub fn post(
  url: String,
  body: String,
  decoder: decode.Decoder(a),
) -> Promise(Result(a, api_error.ApiError)) {
  let req =
    request.new()
    |> request.set_method(Post)
    |> request.set_path(url)
    |> request.set_body(body)
    |> request.set_header("content-type", "application/json")
  send(req, decoder)
}

/// `PUT` a JSON body to the given URL and decode a successful 2xx response.
pub fn put(
  url: String,
  body: String,
  decoder: decode.Decoder(a),
) -> Promise(Result(a, api_error.ApiError)) {
  let req =
    request.new()
    |> request.set_method(Put)
    |> request.set_path(url)
    |> request.set_body(body)
    |> request.set_header("content-type", "application/json")
  send(req, decoder)
}

/// `PATCH` a JSON body to the given URL and decode a successful 2xx response.
pub fn patch(
  url: String,
  body: String,
  decoder: decode.Decoder(a),
) -> Promise(Result(a, api_error.ApiError)) {
  let req =
    request.new()
    |> request.set_method(Patch)
    |> request.set_path(url)
    |> request.set_body(body)
    |> request.set_header("content-type", "application/json")
  send(req, decoder)
}

/// `DELETE` the given URL and decode a successful 2xx response with the given decoder.
pub fn delete(
  url: String,
  decoder: decode.Decoder(a),
) -> Promise(Result(a, api_error.ApiError)) {
  send(
    request.new() |> request.set_method(Delete) |> request.set_path(url),
    decoder,
  )
}
