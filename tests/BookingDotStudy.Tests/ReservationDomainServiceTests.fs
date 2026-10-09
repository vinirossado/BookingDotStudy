module BookingDotStudy.Tests.ReservationDomainServiceTests

open System
open System.Threading.Tasks
open Xunit
open Swensen.Unquote
open BookingDotStudy.Shared
open BookingDotStudy.Domain.Aggregates.Reservation
open BookingDotStudy.Tests.ReservationTestHelpers

// The service is mocked the "Aurora way": LoadState / AppendEvent are plain
// functions, stubbed with lambdas; a mutable captures appended events.

let private clock () = fixedNow

[<Fact>]
let ``createReservationAsync - appends a ReservationCreated event`` () =
    task {
        let id = mkReservationId None
        let loadState: LoadState = fun _ -> task { return None }
        let mutable appended = []
        let appendEvent: AppendEvent = fun _ e -> task { appended <- e :: appended }
        let service = ReservationDomainServiceFactory.createWith clock loadState appendEvent

        do! service.CreateReservationAsync { Id = id; MainGuest = alice; Room = standardRoom; Dates = defaultDates }

        match appended with
        | [ ReservationCreated e ] -> test <@ e.MainGuest.FullName = "Alice Johnson" @>
        | _ -> failwith "Expected a single ReservationCreated event"
    }

[<Fact>]
let ``confirmReservationAsync - returns NotFound when the reservation does not exist`` () =
    task {
        let loadState: LoadState = fun _ -> task { return None }
        let appendEvent: AppendEvent = fun _ _ -> task { () }
        let service = ReservationDomainServiceFactory.createWith clock loadState appendEvent

        let! result = service.ConfirmReservationAsync { ReservationId = mkReservationId None }

        test <@ result = UpdateResult.NotFound @>
    }

[<Fact>]
let ``confirmReservationAsync - returns NoChanges when already confirmed`` () =
    task {
        let s = mkPendingState (mkReservationId None) |> withStatus Confirmed
        let loadState: LoadState = fun _ -> task { return Some s }
        let mutable appended = false
        let appendEvent: AppendEvent = fun _ _ -> task { appended <- true }
        let service = ReservationDomainServiceFactory.createWith clock loadState appendEvent

        let! result = service.ConfirmReservationAsync { ReservationId = s.Id }

        test <@ result = NoChanges @>
        test <@ not appended @>
    }

[<Fact>]
let ``checkOutAsync - throws InvalidStatusTransitionException from Pending`` () =
    task {
        let s = mkPendingState (mkReservationId None)
        let loadState: LoadState = fun _ -> task { return Some s }
        let appendEvent: AppendEvent = fun _ _ -> task { () }
        let service = ReservationDomainServiceFactory.createWith clock loadState appendEvent

        let! _ =
            Assert.ThrowsAsync<InvalidStatusTransitionException>(fun () ->
                service.CheckOutAsync { ReservationId = s.Id } :> Task)

        ()
    }

[<Fact>]
let ``addDependentAsync - appends a DependentAdded event`` () =
    task {
        let s = mkPendingState (mkReservationId None)
        let loadState: LoadState = fun _ -> task { return Some s }
        let mutable appended = []
        let appendEvent: AppendEvent = fun _ e -> task { appended <- e :: appended }
        let service = ReservationDomainServiceFactory.createWith clock loadState appendEvent

        do! service.AddDependentAsync { ReservationId = s.Id; Dependent = mkDependent "Tommy" }

        match appended with
        | [ DependentAdded e ] -> test <@ e.Dependent.FullName = "Tommy" @>
        | _ -> failwith "Expected a single DependentAdded event"
    }
