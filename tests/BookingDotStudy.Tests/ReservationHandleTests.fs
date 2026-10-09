module BookingDotStudy.Tests.ReservationHandleTests

open System
open Xunit
open Swensen.Unquote
open BookingDotStudy.Domain.Aggregates.Reservation
open BookingDotStudy.Tests.ReservationTestHelpers

// Pure decision function: Reservation.handle now state command -> Result<event list, error>

[<Fact>]
let ``handle - CreateReservation on empty state emits ReservationCreated`` () =
    let id = mkReservationId None

    let cmd =
        CreateReservation { Id = id; MainGuest = alice; Room = standardRoom; Dates = defaultDates }

    match Reservation.handle fixedNow None cmd with
    | Ok [ ReservationCreated e ] ->
        test <@ e.MainGuest.FullName = "Alice Johnson" @>
        test <@ e.Room.Number = "101" @>
    | other -> failwith $"Expected one ReservationCreated, got: {other}"

[<Fact>]
let ``handle - CreateReservation with blank guest name fails validation`` () =
    let cmd =
        CreateReservation
            { Id = mkReservationId None; MainGuest = mkGuest "  "; Room = standardRoom; Dates = defaultDates }

    match Reservation.handle fixedNow None cmd with
    | Error (ValidationError msg) -> test <@ msg.Contains "guest" @>
    | other -> failwith $"Expected ValidationError, got: {other}"

[<Fact>]
let ``handle - CreateReservation with checkout before checkin fails`` () =
    let bad = { CheckInDate = DateTime(2026, 2, 10); CheckOutDate = DateTime(2026, 2, 10) }

    let cmd =
        CreateReservation { Id = mkReservationId None; MainGuest = alice; Room = standardRoom; Dates = bad }

    match Reservation.handle fixedNow None cmd with
    | Error (ValidationError msg) -> test <@ msg.Contains "Check-out" @>
    | other -> failwith $"Expected ValidationError, got: {other}"

[<Fact>]
let ``handle - CreateReservation when it already exists fails`` () =
    let s = mkPendingState (mkReservationId None)

    let cmd =
        CreateReservation { Id = s.Id; MainGuest = alice; Room = standardRoom; Dates = defaultDates }

    match Reservation.handle fixedNow (Some s) cmd with
    | Error (ReservationAlreadyExists id) -> test <@ id = s.Id @>
    | other -> failwith $"Expected ReservationAlreadyExists, got: {other}"

[<Fact>]
let ``handle - Confirm a Pending reservation emits ReservationConfirmed`` () =
    let s = mkPendingState (mkReservationId None)

    match Reservation.handle fixedNow (Some s) (ConfirmReservation { ReservationId = s.Id }) with
    | Ok [ ReservationConfirmed _ ] -> ()
    | other -> failwith $"Expected ReservationConfirmed, got: {other}"

[<Fact>]
let ``handle - Confirm an already Confirmed reservation is idempotent (Ok [])`` () =
    let s = mkPendingState (mkReservationId None) |> withStatus Confirmed
    test <@ Reservation.handle fixedNow (Some s) (ConfirmReservation { ReservationId = s.Id }) = Ok [] @>

[<Fact>]
let ``handle - CheckIn from Confirmed emits GuestCheckedIn`` () =
    let s = mkPendingState (mkReservationId None) |> withStatus Confirmed

    match Reservation.handle fixedNow (Some s) (CheckIn { ReservationId = s.Id }) with
    | Ok [ GuestCheckedIn _ ] -> ()
    | other -> failwith $"Expected GuestCheckedIn, got: {other}"

[<Fact>]
let ``handle - CheckOut from CheckedIn emits GuestCheckedOut`` () =
    let s = mkPendingState (mkReservationId None) |> withStatus CheckedIn

    match Reservation.handle fixedNow (Some s) (CheckOut { ReservationId = s.Id }) with
    | Ok [ GuestCheckedOut _ ] -> ()
    | other -> failwith $"Expected GuestCheckedOut, got: {other}"

[<Fact>]
let ``handle - CheckOut on a Pending reservation is an invalid transition`` () =
    let s = mkPendingState (mkReservationId None)

    match Reservation.handle fixedNow (Some s) (CheckOut { ReservationId = s.Id }) with
    | Error (InvalidStatusTransition(Pending, "CheckOut")) -> ()
    | other -> failwith $"Expected InvalidStatusTransition, got: {other}"

[<Fact>]
let ``handle - Cancel a CheckedIn reservation is invalid`` () =
    let s = mkPendingState (mkReservationId None) |> withStatus CheckedIn

    match Reservation.handle fixedNow (Some s) (CancelReservation { ReservationId = s.Id; Reason = None }) with
    | Error (InvalidStatusTransition(CheckedIn, "Cancel")) -> ()
    | other -> failwith $"Expected InvalidStatusTransition, got: {other}"

[<Fact>]
let ``handle - AddDependent on a mutable reservation emits DependentAdded`` () =
    let s = mkPendingState (mkReservationId None)
    let cmd = AddDependent { ReservationId = s.Id; Dependent = mkDependent "Tommy Johnson" }

    match Reservation.handle fixedNow (Some s) cmd with
    | Ok [ DependentAdded e ] -> test <@ e.Dependent.FullName = "Tommy Johnson" @>
    | other -> failwith $"Expected DependentAdded, got: {other}"

[<Fact>]
let ``handle - AddDependent after check-out is invalid`` () =
    let s = mkPendingState (mkReservationId None) |> withStatus CheckedOut
    let cmd = AddDependent { ReservationId = s.Id; Dependent = mkDependent "Tommy" }

    match Reservation.handle fixedNow (Some s) cmd with
    | Error (InvalidStatusTransition(CheckedOut, "AddDependent")) -> ()
    | other -> failwith $"Expected InvalidStatusTransition, got: {other}"

[<Fact>]
let ``handle - RemoveDependent that is not present fails with DependentNotFound`` () =
    let s = mkPendingState (mkReservationId None)

    match Reservation.handle fixedNow (Some s) (RemoveDependent { ReservationId = s.Id; FullName = "Ghost" }) with
    | Error (DependentNotFound name) -> test <@ name = "Ghost" @>
    | other -> failwith $"Expected DependentNotFound, got: {other}"

[<Fact>]
let ``handle - Reschedule with same dates is idempotent (Ok [])`` () =
    let s = mkPendingState (mkReservationId None)
    let cmd = RescheduleStay { ReservationId = s.Id; Dates = s.Dates }
    test <@ Reservation.handle fixedNow (Some s) cmd = Ok [] @>

[<Fact>]
let ``handle - any command on a missing reservation returns ReservationNotFound`` () =
    let id = mkReservationId None

    match Reservation.handle fixedNow None (CheckIn { ReservationId = id }) with
    | Error (ReservationNotFound returned) -> test <@ returned = id @>
    | other -> failwith $"Expected ReservationNotFound, got: {other}"
