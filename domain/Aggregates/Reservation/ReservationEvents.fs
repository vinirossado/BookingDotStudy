namespace BookingDotStudy.Domain.Aggregates.Reservation

open System

/// Events are the source of truth. A record per event inside `ReservationEvents`,
/// wrapped by the top-level `ReservationEvent` DU. Every event carries a
/// Timestamp so the fold keeps UpdatedAt (and the lifecycle timestamps) accurate.
module ReservationEvents =

    type ReservationCreated =
        { Id: ReservationId
          MainGuest: Guest
          Room: Room
          Dates: StayDates
          CreatedAt: DateTime }

    type ReservationConfirmed = { Timestamp: DateTime }

    type GuestCheckedIn = { Timestamp: DateTime }

    type GuestCheckedOut = { Timestamp: DateTime }

    type ReservationCancelled =
        { Reason: string option
          Timestamp: DateTime }

    type StayRescheduled =
        { Dates: StayDates
          Timestamp: DateTime }

    type RoomChanged =
        { Room: Room
          Timestamp: DateTime }

    type DependentAdded =
        { Dependent: Dependent
          Timestamp: DateTime }

    type DependentRemoved =
        { FullName: string
          Timestamp: DateTime }

type ReservationEvent =
    | ReservationCreated of ReservationEvents.ReservationCreated
    | ReservationConfirmed of ReservationEvents.ReservationConfirmed
    | GuestCheckedIn of ReservationEvents.GuestCheckedIn
    | GuestCheckedOut of ReservationEvents.GuestCheckedOut
    | ReservationCancelled of ReservationEvents.ReservationCancelled
    | StayRescheduled of ReservationEvents.StayRescheduled
    | RoomChanged of ReservationEvents.RoomChanged
    | DependentAdded of ReservationEvents.DependentAdded
    | DependentRemoved of ReservationEvents.DependentRemoved
