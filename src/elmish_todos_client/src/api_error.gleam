import gleam/dynamic/decode
import gleam/option

pub type ApiError {
  ApiError(error: String, details: String, status_code: option.Option(Int))
}

pub fn decoder() -> decode.Decoder(ApiError) {
  use error <- decode.field("error", decode.string)
  use details <- decode.field("details", decode.string)
  use status_code <- decode.field("statusCode", decode.optional(decode.int))
  decode.success(ApiError(error:, details:, status_code:))
}
