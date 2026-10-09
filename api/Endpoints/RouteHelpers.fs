namespace BookingDotStudy.Api

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Builder

/// Thin wrappers over the Minimal API map methods, adding a Swagger tag and a
/// consistent way to pull a route parameter out and hand it to the handler.
/// Mirrors Aurora's RouteHelpers so endpoint files stay declarative.
module RouteHelpers =

    let getWithTag (tag: string) (endpoints: IEndpointRouteBuilder) (pattern: string) (handler: HttpContext -> Task<IResult>) =
        endpoints
            .MapGet(pattern, Func<HttpContext, Task<IResult>>(handler))
            .WithTags(tag)
        |> ignore

    let postWithTag (tag: string) (endpoints: IEndpointRouteBuilder) (pattern: string) (handler: HttpContext -> Task<IResult>) =
        endpoints
            .MapPost(pattern, Func<HttpContext, Task<IResult>>(handler))
            .WithTags(tag)
        |> ignore

    let private withParam
        (map: IEndpointRouteBuilder -> string -> Func<HttpContext, Task<IResult>> -> RouteHandlerBuilder)
        (tag: string)
        (endpoints: IEndpointRouteBuilder)
        (param: string)
        (pattern: string)
        (handler: string -> HttpContext -> Task<IResult>)
        =
        let wrapped =
            Func<HttpContext, Task<IResult>>(fun ctx ->
                let value = ctx.Request.RouteValues.[param] :?> string
                handler value ctx)

        (map endpoints pattern wrapped).WithTags(tag) |> ignore

    let getWithParamAndTag tag endpoints param pattern handler =
        withParam (fun e p h -> e.MapGet(p, h)) tag endpoints param pattern handler

    let postWithParamAndTag tag endpoints param pattern handler =
        withParam (fun e p h -> e.MapPost(p, h)) tag endpoints param pattern handler

    let putWithParamAndTag tag endpoints param pattern handler =
        withParam (fun e p h -> e.MapPut(p, h)) tag endpoints param pattern handler

    let deleteWithParamAndTag tag endpoints param pattern handler =
        withParam (fun e p h -> e.MapDelete(p, h)) tag endpoints param pattern handler
