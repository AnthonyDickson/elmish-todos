namespace ElmishTodos.Client.Api

open ElmishTodos.Shared.ApiError

type ApiResult<'T> =
    | Success of 'T
    | Failure of ApiError

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
    let inline private request url requestProperties =
        promise {
            try
                let! response = fetchUnsafe url requestProperties
                let! text = response.text ()
                return decodeResponse response text
            with error ->
                return
                    Failure {
                        Error = "Fetch Failed"
                        Details = error.Message
                        StatusCode = None
                    }

        }

    /// <summary>Execute a get request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline get (url : string) : Promise<ApiResult<'Response>> = request url [ Method HttpMethod.GET ]

    /// <summary>Execute a post request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline post (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        request url [
            Method HttpMethod.POST
            Body (data |> Encode.toString |> unbox)
            requestHeaders [ ContentType "application/json" ]
        ]

    /// <summary>Execute a put request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline put (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        request url [
            Method HttpMethod.PUT
            Body (data |> Encode.toString |> unbox)
            requestHeaders [ ContentType "application/json" ]
        ]

    /// <summary>Execute a patch request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline patch (url : string) (data : 'Data) : Promise<ApiResult<'Response>> =
        request url [
            Method HttpMethod.PATCH
            Body (data |> Encode.toString |> unbox)
            requestHeaders [ ContentType "application/json" ]
        ]


    /// <summary>Execute a delete request as a promise.</summary>
    /// <remarks>This function is inlined for Fable to resolve the generic type</remarks>
    let inline delete (url : string) : Promise<ApiResult<unit>> =
        request url [ Method HttpMethod.DELETE; requestHeaders [ ContentType "application/json" ] ]
