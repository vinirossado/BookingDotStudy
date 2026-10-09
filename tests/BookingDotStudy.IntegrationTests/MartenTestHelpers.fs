module BookingDotStudy.IntegrationTests.MartenTestHelpers

open Marten
open BookingDotStudy.Domain
open BookingDotStudy.Domain.Aggregates.Reservation

/// Build a Marten store using the SAME shared configuration as the running app
/// (serializer, string stream identity, event type, inline ReservationProjection),
/// so the integration tests exercise the real projection wiring.
///
/// Points at a dedicated `integration_tests` database on the docker-compose
/// Postgres (host port 5433). Override with INTEGRATION_CONNECTION_STRING.
let connectionString () =
    match System.Environment.GetEnvironmentVariable "INTEGRATION_CONNECTION_STRING" with
    | null
    | "" -> "Host=localhost;Port=5433;Database=integration_tests;Username=booking;Password=booking;"
    | v -> v

let createStore () : IDocumentStore =
    DocumentStore.For(fun (options: StoreOptions) ->
        options.Connection(connectionString ())
        options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
        MartenStoreConfiguration.configureSharedOptions options)
    :> IDocumentStore

/// The event store writes to a stream; because the projection is Inline, the
/// ReservationReadModel document is produced in the same SaveChanges call.
let appendEvents (store: IDocumentStore) (id: ReservationId) (events: ReservationEvent list) =
    task {
        use session = store.LightweightSession()
        let eventStore = ReservationSimpleEventStore(session) :> IReservationSimpleEventStore
        do! eventStore.AppendEventsAsync id events
    }

/// A fresh, unique id per test (integration data persists between runs).
let freshId () = ReservationId.newId ()
