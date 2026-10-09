module BookingDotStudy.Tests.ReservationTestHelpers

open System
open BookingDotStudy.Domain.Aggregates.Reservation

/// Shared builders so tests don't repeat big record literals. Extend these
/// rather than constructing ReservationState inline across many tests.

let mkReservationId (id: string option) =
    match id with
    | Some v -> ReservationId.create v
    | None -> ReservationId.newId ()

let fixedNow = DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)

/// A fully-populated main guest.
let alice: Guest =
    { FullName = "Alice Johnson"
      Email = Some "alice@example.com"
      Phone = Some "+1-555-0100"
      Document = Some "ID-12345" }

let mkGuest (fullName: string) : Guest =
    { FullName = fullName; Email = None; Phone = None; Document = None }

let mkDependent (fullName: string) : Dependent =
    { FullName = fullName; Document = None; Age = None }

let standardRoom: Room = { Number = "101"; RoomType = "Standard" }
let suite: Room = { Number = "900"; RoomType = "Suite" }

let stay (checkIn: DateTime) (nights: int) : StayDates =
    { CheckInDate = checkIn; CheckOutDate = checkIn.AddDays(float nights) }

let defaultDates = stay (DateTime(2026, 2, 10)) 3

/// A freshly created (Pending) reservation.
let mkPendingState (id: ReservationId) : ReservationState =
    { Id = id
      Status = Pending
      MainGuest = alice
      Dependents = []
      Room = standardRoom
      Dates = defaultDates
      CheckedInAt = None
      CheckedOutAt = None
      CancelledAt = None
      CancellationReason = None
      CreatedAt = fixedNow
      UpdatedAt = fixedNow }

let withStatus (status: ReservationStatus) (s: ReservationState) = { s with Status = status }
