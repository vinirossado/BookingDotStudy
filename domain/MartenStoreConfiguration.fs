namespace BookingDotStudy.Domain

open Marten
open Marten.Newtonsoft
open JasperFx.Events
open JasperFx.Events.Projections
open BookingDotStudy.Domain.Infrastructure
open BookingDotStudy.Domain.Aggregates.Reservation

/// Shared Marten store options, kept in one place so any host (API, a CLI
/// projection rebuilder, integration tests) configures the store identically.
module MartenStoreConfiguration =

    let configureSharedOptions (options: StoreOptions) =
        // F#-friendly JSON: Newtonsoft + the transparent option converter so
        // `'T option` persists as null / bare value instead of a DU wrapper.
        options.UseNewtonsoftForSerialization(configure = fun settings -> settings.Converters.Add(FSharpOptionConverter()))

        // Stream ids are strings ("reservation-<id>"), not Guids.
        options.Events.StreamIdentity <- StreamIdentity.AsString

        // Register the event union explicitly so Marten can (de)serialize it.
        options.Events.AddEventType(typeof<ReservationEvent>)

        // Inline read-model projection (the aggregate snapshot). "Inline" means
        // the ReservationReadModel document is written in the SAME transaction as
        // the event append, so reads are strongly consistent and need no replay.
        options.Projections.Add(ReservationProjection(), ProjectionLifecycle.Inline)
