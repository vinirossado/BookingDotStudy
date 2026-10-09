namespace BookingDotStudy.Domain.Aggregates.Reservation

open System

/// Strongly-typed id. A single-case DU prevents mixing a ReservationId up with
/// any other string. The companion module gives `value`/`create` helpers.
type ReservationId = ReservationId of string

module ReservationId =
    let value (ReservationId id) = id
    let create (id: string) = ReservationId id
    let newId () = ReservationId(Guid.NewGuid().ToString())

/// The main guest on the booking. FullName is always required; the rest are
/// optional (enforced in Reservation.handle).
type Guest =
    { FullName: string
      Email: string option
      Phone: string option
      Document: string option }

/// A dependent travelling under the reservation (child, partner, etc.).
type Dependent =
    { FullName: string
      Document: string option
      Age: int option }

/// The room booked for the stay.
type Room =
    { Number: string
      RoomType: string }

/// The nights booked. CheckInDate/CheckOutDate are dates (time-of-day is the
/// hotel's business; we keep it simple with DateTime at midnight).
type StayDates =
    { CheckInDate: DateTime
      CheckOutDate: DateTime }

module StayDates =
    /// Number of nights = whole days between check-in and check-out.
    let nights (dates: StayDates) =
        (dates.CheckOutDate.Date - dates.CheckInDate.Date).Days

/// Where the reservation is in its lifecycle, derived purely from the events.
type ReservationStatus =
    | Pending // created, not yet confirmed
    | Confirmed // confirmed, guest not arrived
    | CheckedIn // guest is in-house
    | CheckedOut // stay completed
    | Cancelled // cancelled before check-in

/// The aggregate state, rebuilt by folding the event stream.
type ReservationState =
    { Id: ReservationId
      Status: ReservationStatus
      MainGuest: Guest
      Dependents: Dependent list
      Room: Room
      Dates: StayDates
      CheckedInAt: DateTime option
      CheckedOutAt: DateTime option
      CancelledAt: DateTime option
      CancellationReason: string option
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// Domain errors returned by the pure `handle` function.
type ReservationError =
    | ReservationNotFound of ReservationId
    | ReservationAlreadyExists of ReservationId
    | InvalidStatusTransition of current: ReservationStatus * attempted: string
    | DependentNotFound of fullName: string
    | ValidationError of string
