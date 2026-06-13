namespace ElmishTodos.Shared.Coders

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

module Decode =
    let inline cachedDecoder<'T> : Decoder<'T> =
        Decode.Auto.generateDecoderCached<'T> (caseStrategy = CamelCase, extra = Extra.empty)

    let inline fromString<'T> (json : string) : Result<'T, string> =
        Decode.fromString cachedDecoder<'T> json

module Encode =
    let inline cachedEncoder<'T> : Encoder<'T> =
        Encode.Auto.generateEncoderCached<'T> (caseStrategy = CamelCase, extra = Extra.empty, skipNullField = false)

    let inline toString<'T> (value : 'T) : string =
        let jsonValue = cachedEncoder<'T> value
        Encode.toString 4 jsonValue
