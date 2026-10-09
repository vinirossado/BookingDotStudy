namespace BookingDotStudy.Domain.Aggregates.Reservation

open System
open JasperFx.Events
open Marten.Events.Aggregation

/// The read model (a.k.a. snapshot) materialized by the inline projection.
///
/// A plain, queryable document with a string `Id` (the Marten stream key, e.g.
/// "reservation-<guid>"). Kept SEPARATE from the domain `ReservationState`
/// (whose Id is a DU) so the read side can evolve independently and Marten can
/// identify documents by a simple settable property.
///
/// [<CLIMutable>] gives Marten the parameterless constructor + settable members
/// it needs to rehydrate from JSON.
[<CLIMutable>]
type ReservationReadModel =
    { mutable Id: string // Marten document identity = the event stream key
      ReservationId: string
      Status: string
      MainGuest: Guest
      Dependents: Dependent list
      Room: Room
      Dates: StayDates
      Nights: int
      CheckedInAt: DateTime option
      CheckedOutAt: DateTime option
      CancelledAt: DateTime option
      CancellationReason: string option
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module ReservationReadModel =

    let private statusText =
        function
        | Pending -> "Pending"
        | Confirmed -> "Confirmed"
        | CheckedIn -> "CheckedIn"
        | CheckedOut -> "CheckedOut"
        | Cancelled -> "Cancelled"

    let private statusOf =
        function
        | "Confirmed" -> Confirmed
        | "CheckedIn" -> CheckedIn
        | "CheckedOut" -> CheckedOut
        | "Cancelled" -> Cancelled
        | _ -> Pending

    /// Rehydrate the aggregate state from the read model. Total, because the
    /// read model is only ever written from a valid ReservationState.
    let toState (rm: ReservationReadModel) : ReservationState =
        { Id = ReservationId.create rm.ReservationId
          Status = statusOf rm.Status
          MainGuest = rm.MainGuest
          Dependents = rm.Dependents
          Room = rm.Room
          Dates = rm.Dates
          CheckedInAt = rm.CheckedInAt
          CheckedOutAt = rm.CheckedOutAt
          CancelledAt = rm.CancelledAt
          CancellationReason = rm.CancellationReason
          CreatedAt = rm.CreatedAt
          UpdatedAt = rm.UpdatedAt }

    /// Project a folded aggregate state onto the read model, keyed by streamKey.
    let ofState (streamKey: string) (state: ReservationState) : ReservationReadModel =
        { Id = streamKey
          ReservationId = ReservationId.value state.Id
          Status = statusText state.Status
          MainGuest = state.MainGuest
          Dependents = state.Dependents
          Room = state.Room
          Dates = state.Dates
          Nights = StayDates.nights state.Dates
          CheckedInAt = state.CheckedInAt
          CheckedOutAt = state.CheckedOutAt
          CancelledAt = state.CancelledAt
          CancellationReason = state.CancellationReason
          CreatedAt = state.CreatedAt
          UpdatedAt = state.UpdatedAt }

/// Inline single-stream projection: Marten keeps a ReservationReadModel document
/// per event stream, updated in the SAME transaction that appends the event.
/// Reads then become a direct document load/query — no stream replay.
///
/// We override `Evolve` and delegate to the aggregate's own pure functions
/// (`Reservation.apply`) so the projection and the write side can never drift.
type ReservationProjection() =
    inherit SingleStreamProjection<ReservationReadModel, string>()

    override _.Evolve(snapshot: ReservationReadModel, id: string, e: IEvent) : ReservationReadModel =
        match e.Data with
        | :? ReservationEvent as domainEvent ->
            let previousState =
                if obj.ReferenceEquals(snapshot, null) then
                    None
                else
                    Some(ReservationReadModel.toState snapshot)

            let newState = Reservation.apply previousState domainEvent
            ReservationReadModel.ofState id newState
        | _ -> snapshot
