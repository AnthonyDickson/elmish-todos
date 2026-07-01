namespace ElmishTodos.Client.Api

open ElmishTodos.Shared.ApiError

type ApiResult<'T> =
    | Success of 'T
    | Failure of ApiError

module ApiResult =
    let ofException (error : exn) : ApiResult<'T> =
        Failure {
            Error = "Fetch Failed"
            Details = error.Message
            StatusCode = None
        }

/// Helpers for interacting with the API
module Api =
    open Fable.Core.JS
    open Fetch

    open ElmishTodos.Shared.Coders

    let inline private decodeResponse (response : Response) (text : string) =
        let statusCode = Some response.Status

        if response.Ok then
            match Decode.fromString<'Data> text with
            | Ok responseData -> Success responseData
            | Error error ->
                Failure {
                    Error = "Decode Error"
                    Details = error
                    StatusCode = statusCode
                }
        else
            match Decode.fromString<ApiError> text with
            | Ok apiError -> Failure apiError
            | Error error ->
                Failure {
                    Error = "Decode Error"
                    Details = error
                    StatusCode = statusCode
                }


    /// <summary>Execute a get request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline get (url : string) : Promise<ApiResult<'Response>> =
        promise {
            let! response = fetchUnsafe url [ Method HttpMethod.GET ]
            let! text = response.text ()

            return decodeResponse response text
        }

    /// <summary>Execute a post request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline post (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        promise {
            let! response =
                fetchUnsafe url [
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
                fetchUnsafe url [
                    Method HttpMethod.PUT
                    Body (data |> Encode.toString |> unbox)
                    requestHeaders [ ContentType "application/json" ]
                ]

            let! text = response.text ()

            return decodeResponse response text
        }

    /// <summary>Execute a patch request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline patch (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        promise {
            let! response =
                fetchUnsafe url [
                    Method HttpMethod.PATCH
                    Body (data |> Encode.toString |> unbox)
                    requestHeaders [ ContentType "application/json" ]
                ]

            let! text = response.text ()

            return decodeResponse response text
        }

    /// <summary>Execute a delete request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline delete (url : string) : Promise<ApiResult<unit>> =
        promise {
            let! response =
                fetchUnsafe url [ Method HttpMethod.DELETE; requestHeaders [ ContentType "application/json" ] ]

            let! text = response.text ()

            return decodeResponse response text
        }
