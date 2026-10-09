module BookingDotStudy.Tests.ReservationFoldTests

open System
open Xunit
open Swensen.Unquote
open BookingDotStudy.Domain.Aggregates.Reservation
open BookingDotStudy.Tests.ReservationTestHelpers

// Replaying the event stream must reconstruct the exact current state.

let private created (id: ReservationId) =
    ReservationCreated
        { Id = id
          MainGuest = alice
          Room = standardRoom
          Dates = defaultDates
          CreatedAt = fixedNow }

[<Fact>]
let ``fold - full lifecycle create -> confirm -> checkin -> checkout`` () =
    let id = mkReservationId None

    let events =
        [ created id
          ReservationConfirmed { Timestamp = fixedNow.AddDays 1.0 }
          GuestCheckedIn { Timestamp = fixedNow.AddDays 40.0 }
          GuestCheckedOut { Timestamp = fixedNow.AddDays 43.0 } ]

    match Reservation.fold events with
    | Some s ->
        test <@ s.Status = CheckedOut @>
        test <@ s.CheckedInAt = Some(fixedNow.AddDays 40.0) @>
        test <@ s.CheckedOutAt = Some(fixedNow.AddDays 43.0) @>
    | None -> failwith "Expected state to be rebuilt"

[<Fact>]
let ``fold - dependents added then one removed`` () =
    let id = mkReservationId None

    let events =
        [ created id
          DependentAdded { Dependent = mkDependent "Tommy"; Timestamp = fixedNow }
          DependentAdded { Dependent = mkDependent "Sara"; Timestamp = fixedNow }
          DependentRemoved { FullName = "Tommy"; Timestamp = fixedNow } ]

    match Reservation.fold events with
    | Some s -> test <@ s.Dependents |> List.map (fun d -> d.FullName) = [ "Sara" ] @>
    | None -> failwith "Expected state to be rebuilt"

[<Fact>]
let ``fold - reschedule and room change are reflected`` () =
    let id = mkReservationId None
    let newDates = stay (DateTime(2026, 3, 1)) 5

    let events =
        [ created id
          StayRescheduled { Dates = newDates; Timestamp = fixedNow }
          RoomChanged { Room = suite; Timestamp = fixedNow } ]

    match Reservation.fold events with
    | Some s ->
        test <@ s.Dates = newDates @>
        test <@ s.Room = suite @>
    | None -> failwith "Expected state to be rebuilt"

[<Fact>]
let ``fold - cancel keeps the reason`` () =
    let id = mkReservationId None

    let events =
        [ created id
          ReservationCancelled { Reason = Some "guest requested"; Timestamp = fixedNow } ]

    match Reservation.fold events with
    | Some s ->
        test <@ s.Status = Cancelled @>
        test <@ s.CancellationReason = Some "guest requested" @>
    | None -> failwith "Expected state to be rebuilt"

[<Fact>]
let ``fold - empty stream yields None`` () =
    test <@ Reservation.fold [] = None @>
