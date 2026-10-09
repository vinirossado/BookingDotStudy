module BookingDotStudy.Api.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open BookingDotStudy.Domain
open BookingDotStudy.Api

/// Map every feature's routes. One line per aggregate/feature module.
let private configureEndpoints (endpoints: IEndpointRouteBuilder) =
    ReservationEndpoints.configureRoutes endpoints

/// Register all services: framework, Marten, and the aggregate repositories.
let private configureServices (services: IServiceCollection) (configuration: IConfiguration) =
    services.AddSingleton<TimeProvider>(TimeProvider.System) |> ignore
    services.AddEndpointsApiExplorer() |> ignore
    services.AddSwaggerGen() |> ignore

    // Domain layer wiring (Marten + aggregate services).
    ConfigureServices.configureDatabase services configuration |> ignore
    ConfigureServices.addRepositories services |> ignore

    services

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    configureServices builder.Services builder.Configuration |> ignore

    let app = builder.Build()

    app.UseSwagger() |> ignore
    app.UseSwaggerUI() |> ignore

    // Simple health check.
    app.MapGet("/health", Func<string>(fun () -> "ok")) |> ignore

    configureEndpoints app

    app.Run()
    0
