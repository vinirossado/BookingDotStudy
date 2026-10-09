namespace BookingDotStudy.IntegrationTests

open Xunit

/// Serialize DB-touching tests: the projection and the shared store are not
/// meant to run concurrently against the same schema in these tests.
[<CollectionDefinition(nameof ReservationProjectionCollection, DisableParallelization = true)>]
type ReservationProjectionCollection() =
    class
    end

[<Collection(nameof ReservationProjectionCollection)>]
module ReservationProjectionTests =

    open System
    open Swensen.Unquote
    open Marten
    open BookingDotStudy.Domain.Aggregates.Reservation
    open BookingDotStudy.IntegrationTests.MartenTestHelpers

    let private now = DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)

    let private alice: Guest =
        { FullName = "Alice Johnson"
          Email = Some "alice@example.com"
          Phone = Some "+1-555-0100"
          Document = Some "ID-12345" }

    let private room: Room = { Number = "101"; RoomType = "Standard" }
    let private dates: StayDates = { CheckInDate = DateTime(2026, 2, 10); CheckOutDate = DateTime(2026, 2, 13) }

    let private created (id: ReservationId) =
        ReservationCreated { Id = id; MainGuest = alice; Room = room; Dates = dates; CreatedAt = now }

    let private load (store: IDocumentStore) (id: ReservationId) =
        task {
            use session = store.QuerySession()
            return! session.LoadAsync<ReservationReadModel>($"reservation-{ReservationId.value id}")
        }

    [<Fact>]
    let ``inline projection - create produces a queryable read model with nights computed`` () =
        task {
            let store = createStore ()
            let id = freshId ()

            do! appendEvents store id [ created id ]

            let! rm = load store id

            test <@ not (obj.ReferenceEquals(rm, null)) @>
            test <@ rm.Status = "Pending" @>
            test <@ rm.MainGuest.FullName = "Alice Johnson" @>
            test <@ rm.MainGuest.Email = Some "alice@example.com" @>
            test <@ rm.Nights = 3 @>
        }

    [<Fact>]
    let ``inline projection - full lifecycle lands on CheckedOut with timestamps`` () =
        task {
            let store = createStore ()
            let id = freshId ()

            do!
                appendEvents
                    store
                    id
                    [ created id
                      ReservationConfirmed { Timestamp = now.AddDays 1.0 }
                      GuestCheckedIn { Timestamp = now.AddDays 40.0 }
                      GuestCheckedOut { Timestamp = now.AddDays 43.0 } ]

            let! rm = load store id

            test <@ rm.Status = "CheckedOut" @>
            test <@ rm.CheckedInAt = Some(now.AddDays 40.0) @>
            test <@ rm.CheckedOutAt = Some(now.AddDays 43.0) @>
        }

    [<Fact>]
    let ``inline projection - dependents are reflected in the read model`` () =
        task {
            let store = createStore ()
            let id = freshId ()

            do!
                appendEvents
                    store
                    id
                    [ created id
                      DependentAdded { Dependent = { FullName = "Tommy"; Document = None; Age = Some 8 }; Timestamp = now }
                      DependentAdded { Dependent = { FullName = "Sara"; Document = None; Age = Some 11 }; Timestamp = now }
                      DependentRemoved { FullName = "Tommy"; Timestamp = now } ]

            let! rm = load store id

            test <@ rm.Dependents |> List.map (fun d -> d.FullName) = [ "Sara" ] @>
            test <@ rm.Dependents.[0].Age = Some 11 @>
        }

    [<Fact>]
    let ``ReservationQueryService - GetById reads the projected read model`` () =
        task {
            let store = createStore ()
            let id = freshId ()

            do! appendEvents store id [ created id; ReservationConfirmed { Timestamp = now.AddDays 1.0 } ]

            let queryService = ReservationQueryService(store) :> IReservationQueryService
            let! state = queryService.GetById id

            match state with
            | Some s ->
                test <@ s.Status = Confirmed @>
                test <@ s.MainGuest.FullName = "Alice Johnson" @>
                test <@ s.Room.RoomType = "Standard" @>
            | None -> failwith "Expected the projected reservation state to be found"
        }

    [<Fact>]
    let ``ReservationQueryService - ListAll returns the projected reservation`` () =
        task {
            let store = createStore ()
            let id = freshId ()

            do! appendEvents store id [ created id ]

            let queryService = ReservationQueryService(store) :> IReservationQueryService
            let! all = queryService.ListAll()

            test <@ all |> List.exists (fun r -> r.Id = id) @>
        }
