namespace BookingDotStudy.Domain.Aggregates.Reservation

open System

/// Each command is a record inside the `ReservationCommands` module; the
/// top-level `ReservationCommand` DU wraps them all so `handle` matches on one
/// union.
module ReservationCommands =

    type CreateReservation =
        { Id: ReservationId
          MainGuest: Guest
          Room: Room
          Dates: StayDates }

    type ConfirmReservation = { ReservationId: ReservationId }

    type CheckIn = { ReservationId: ReservationId }

    type CheckOut = { ReservationId: ReservationId }

    type CancelReservation =
        { ReservationId: ReservationId
          Reason: string option }

    /// Reschedule the stay (change check-in/check-out dates).
    type RescheduleStay =
        { ReservationId: ReservationId
          Dates: StayDates }

    /// Move the reservation to a different room.
    type ChangeRoom =
        { ReservationId: ReservationId
          Room: Room }

    type AddDependent =
        { ReservationId: ReservationId
          Dependent: Dependent }

    type RemoveDependent =
        { ReservationId: ReservationId
          FullName: string }

type ReservationCommand =
    | CreateReservation of ReservationCommands.CreateReservation
    | ConfirmReservation of ReservationCommands.ConfirmReservation
    | CheckIn of ReservationCommands.CheckIn
    | CheckOut of ReservationCommands.CheckOut
    | CancelReservation of ReservationCommands.CancelReservation
    | RescheduleStay of ReservationCommands.RescheduleStay
    | ChangeRoom of ReservationCommands.ChangeRoom
    | AddDependent of ReservationCommands.AddDependent
    | RemoveDependent of ReservationCommands.RemoveDependent
