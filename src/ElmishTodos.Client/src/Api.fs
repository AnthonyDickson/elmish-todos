namespace ElmishTodos.Client.Api

open ElmishTodos.Shared.ApiError

type ApiResult<'T> =
    | Success of 'T
    | Failure of ApiError

module ApiResult =
    let ofException (error : exn) : ApiResult<'T> =
        ApiResult.Failure {
            Error = "Fetch Failed"
            Details = error.Message
        }

/// Helpers for interacting with the API
module Api =
    open Fable.Core.JS
    open Fetch

    open ElmishTodos.Shared.Coders

    let inline private decodeResponse (response : Response) (text : string) =
        if response.Ok then
            match Decode.fromString<'Data> text with
            | Ok responseData -> ApiResult.Success responseData
            | Error error ->
                ApiResult.Failure {
                    Error = "Decode Error"
                    Details = error
                }
        else
            match Decode.fromString<ApiError> text with
            | Ok apiError -> ApiResult.Failure apiError
            | Error error ->
                ApiResult.Failure {
                    Error = "Decode Error"
                    Details = error
                }


    /// <summary>Execute a get request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline get (url : string) : Promise<ApiResult<'Response>> =
        promise {
            let! response = fetch url [ Method HttpMethod.GET ]
            let! text = response.text ()

            return decodeResponse response text
        }

    /// <summary>Execute a post request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline post (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        promise {
            let! response =
                fetch url [
                    Method HttpMethod.POST
                    Body (data |> Encode.toString |> unbox)
                    requestHeaders [ ContentType "application/json" ]
                ]

            let! text = response.text ()

            return decodeResponse response text
        }

    /// <summary>Execute a put request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline put (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        promise {
            let! response =
                fetch url [
                    Method HttpMethod.PUT
                    Body (data |> Encode.toString |> unbox)
                    requestHeaders [ ContentType "application/json" ]
                ]

            let! text = response.text ()

            return decodeResponse response text
        }
