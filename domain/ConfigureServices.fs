namespace BookingDotStudy.Domain

open System
open Marten
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open BookingDotStudy.Domain.Aggregates.Reservation

/// DI registration for the domain layer. Two helpers composed by the API host:
///   configureDatabase - register Marten + the shared store options
///   addRepositories   - register the aggregate services with correct lifetimes
module ConfigureServices =

    /// Register Marten against Postgres with lightweight sessions.
    let configureDatabase (services: IServiceCollection) (configuration: IConfiguration) : IServiceCollection =
        let postgres = PostgresConfig.fromConfiguration configuration

        services
            .AddMarten(fun (options: StoreOptions) ->
                options.Connection(postgres.ConnectionString)
                // Learning project: let Marten create/patch the schema on startup.
                options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
                MartenStoreConfiguration.configureSharedOptions options)
            .UseLightweightSessions()
        |> ignore

        services

    /// Register the Reservation aggregate services.
    ///
    /// Lifetime rules (same as Aurora):
    ///   - Query service depends on IDocumentStore  -> SINGLETON
    ///   - Event store depends on IDocumentSession  -> SCOPED
    ///   - Domain service functions wrap the store  -> SCOPED
    let addRepositories (services: IServiceCollection) : IServiceCollection =
        services.AddSingleton<IReservationQueryService, ReservationQueryService>() |> ignore

        services.AddScoped<IReservationSimpleEventStore>(fun sp ->
            ReservationSimpleEventStore(sp.GetRequiredService<IDocumentSession>()) :> IReservationSimpleEventStore)
        |> ignore

        services.AddScoped<ReservationDomainServiceFunctions>(fun sp ->
            let eventStore = sp.GetRequiredService<IReservationSimpleEventStore>()
            let timeProvider = sp.GetRequiredService<TimeProvider>()
            ReservationDomainServiceFactory.createWithTimeProvider timeProvider eventStore)
        |> ignore

        services
