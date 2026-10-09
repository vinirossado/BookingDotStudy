namespace BookingDotStudy.Domain.Aggregates.Reservation

open System

/// The domain-service layer throws these typed exceptions (carrying the
/// strongly-typed ids) instead of returning Result. The endpoints catch them
/// and translate to HTTP status codes. Mirrors the pure Result-based core at
/// the service edge.
type ReservationDomainException(message: string, ?inner: exn) =
    inherit Exception(message, defaultArg inner null)

type ReservationNotFoundException(id: ReservationId) =
    inherit ReservationDomainException($"Reservation {ReservationId.value id} does not exist")
    member _.ReservationId = id

type ReservationAlreadyExistsException(id: ReservationId) =
    inherit ReservationDomainException($"Reservation {ReservationId.value id} already exists")
    member _.ReservationId = id

type InvalidStatusTransitionException(current: ReservationStatus, attempted: string) =
    inherit ReservationDomainException($"Cannot {attempted} a reservation in status {current}")
    member _.Current = current
    member _.Attempted = attempted

type DependentNotFoundException(fullName: string) =
    inherit ReservationDomainException($"Dependent '{fullName}' is not on this reservation")
    member _.FullName = fullName

type ReservationValidationException(message: string) =
    inherit ReservationDomainException(message)
