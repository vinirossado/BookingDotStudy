namespace BookingDotStudy.Domain.Aggregates.Reservation

open System
open System.Threading.Tasks
open Marten
open BookingDotStudy.Shared
open BookingDotStudy.Domain.Infrastructure

// ---- Capability aliases ---------------------------------------------------
// Dependencies are injected as plain functions, not interfaces. Tests stub
// them with lambdas; production supplies the Marten-backed versions.

type LoadState = ReservationId -> Task<ReservationState option>
type AppendEvent = ReservationId -> ReservationEvent -> Task<unit>
type GetUtcNow = unit -> DateTime

// ---- The Reservation event store ------------------------------------------
// Thin alias + concrete type over the generic SimpleEventStore. Stream keys
// become "reservation-{id}".

type IReservationSimpleEventStore = ISimpleEventStore<ReservationId, ReservationEvent, ReservationState>

type ReservationSimpleEventStore(session: IDocumentSession) =
    let inner =
        SimpleEventStore<ReservationId, ReservationEvent, ReservationState>(
            session,
            ReservationId.value,
            "reservation",
            Reservation.fold
        )
        :> IReservationSimpleEventStore

    interface IReservationSimpleEventStore with
        member _.LoadStateAsync(id) = inner.LoadStateAsync id
        member _.AppendEventAsync id event = inner.AppendEventAsync id event
        member _.AppendEventsAsync id events = inner.AppendEventsAsync id events

// ---- Per-command operations ----------------------------------------------
// Each loads state, runs the pure `Reservation.handle`, translates any
// ReservationError into a typed exception, and appends the resulting event(s).
// Commands backing an idempotent HTTP endpoint return UpdateResult.

[<RequireQualifiedAccess>]
module ReservationDomainService =

    let private raiseError (error: ReservationError) : 'a =
        match error with
        | ReservationNotFound id -> raise (ReservationNotFoundException id)
        | ReservationAlreadyExists id -> raise (ReservationAlreadyExistsException id)
        | InvalidStatusTransition(cur, attempted) -> raise (InvalidStatusTransitionException(cur, attempted))
        | DependentNotFound name -> raise (DependentNotFoundException name)
        | ValidationError msg -> raise (ReservationValidationException msg)

    /// Load, decide, append — the shared skeleton for every write. Returns the
    /// events that were produced (empty = no-op).
    let private execute
        (loadState: LoadState)
        (appendEvent: AppendEvent)
        (now: DateTime)
        (id: ReservationId)
        (command: ReservationCommand)
        : Task<ReservationEvent list> =
        task {
            let! state = loadState id

            match Reservation.handle now state command with
            | Error e -> return raiseError e
            | Ok events ->
                for event in events do
                    do! appendEvent id event

                return events
        }

    /// Shared shape for status-transition style commands that back an
    /// idempotent endpoint: NotFound when the reservation is missing, NoChanges
    /// when the command was a no-op, Updated when an event was produced.
    let private executeIdempotent
        (loadState: LoadState)
        (appendEvent: AppendEvent)
        (now: DateTime)
        (id: ReservationId)
        (command: ReservationCommand)
        : Task<UpdateResult> =
        task {
            let! state = loadState id

            match state with
            | None -> return UpdateResult.NotFound
            | Some _ ->
                let! events = execute loadState appendEvent now id command
                return if List.isEmpty events then NoChanges else Updated
        }

    let createReservationAsync (getUtcNow: GetUtcNow) (loadState: LoadState) (appendEvent: AppendEvent) (cmd: ReservationCommands.CreateReservation) : Task<unit> =
        task {
            let! _ = execute loadState appendEvent (getUtcNow ()) cmd.Id (CreateReservation cmd)
            return ()
        }

    let confirmReservationAsync getUtcNow loadState appendEvent (cmd: ReservationCommands.ConfirmReservation) : Task<UpdateResult> =
        executeIdempotent loadState appendEvent (getUtcNow ()) cmd.ReservationId (ConfirmReservation cmd)

    let checkInAsync getUtcNow loadState appendEvent (cmd: ReservationCommands.CheckIn) : Task<UpdateResult> =
        executeIdempotent loadState appendEvent (getUtcNow ()) cmd.ReservationId (CheckIn cmd)

    let checkOutAsync getUtcNow loadState appendEvent (cmd: ReservationCommands.CheckOut) : Task<UpdateResult> =
        executeIdempotent loadState appendEvent (getUtcNow ()) cmd.ReservationId (CheckOut cmd)

    let cancelReservationAsync getUtcNow loadState appendEvent (cmd: ReservationCommands.CancelReservation) : Task<UpdateResult> =
        executeIdempotent loadState appendEvent (getUtcNow ()) cmd.ReservationId (CancelReservation cmd)

    let rescheduleStayAsync getUtcNow loadState appendEvent (cmd: ReservationCommands.RescheduleStay) : Task<UpdateResult> =
        executeIdempotent loadState appendEvent (getUtcNow ()) cmd.ReservationId (RescheduleStay cmd)

    let changeRoomAsync getUtcNow loadState appendEvent (cmd: ReservationCommands.ChangeRoom) : Task<UpdateResult> =
        executeIdempotent loadState appendEvent (getUtcNow ()) cmd.ReservationId (ChangeRoom cmd)

    let addDependentAsync (getUtcNow: GetUtcNow) (loadState: LoadState) (appendEvent: AppendEvent) (cmd: ReservationCommands.AddDependent) : Task<unit> =
        task {
            let! _ = execute loadState appendEvent (getUtcNow ()) cmd.ReservationId (AddDependent cmd)
            return ()
        }

    let removeDependentAsync (getUtcNow: GetUtcNow) (loadState: LoadState) (appendEvent: AppendEvent) (cmd: ReservationCommands.RemoveDependent) : Task<unit> =
        task {
            let! _ = execute loadState appendEvent (getUtcNow ()) cmd.ReservationId (RemoveDependent cmd)
            return ()
        }

// ---- The bundled service surface -----------------------------------------
// A record of partially-applied functions. This is what endpoints depend on.

type ReservationDomainServiceFunctions =
    { CreateReservationAsync: ReservationCommands.CreateReservation -> Task<unit>
      ConfirmReservationAsync: ReservationCommands.ConfirmReservation -> Task<UpdateResult>
      CheckInAsync: ReservationCommands.CheckIn -> Task<UpdateResult>
      CheckOutAsync: ReservationCommands.CheckOut -> Task<UpdateResult>
      CancelReservationAsync: ReservationCommands.CancelReservation -> Task<UpdateResult>
      RescheduleStayAsync: ReservationCommands.RescheduleStay -> Task<UpdateResult>
      ChangeRoomAsync: ReservationCommands.ChangeRoom -> Task<UpdateResult>
      AddDependentAsync: ReservationCommands.AddDependent -> Task<unit>
      RemoveDependentAsync: ReservationCommands.RemoveDependent -> Task<unit> }

[<RequireQualifiedAccess>]
module ReservationDomainServiceFactory =

    /// Build the service from raw capabilities. The seam used by unit tests.
    let createWith (getUtcNow: GetUtcNow) (loadState: LoadState) (appendEvent: AppendEvent) : ReservationDomainServiceFunctions =
        { CreateReservationAsync = ReservationDomainService.createReservationAsync getUtcNow loadState appendEvent
          ConfirmReservationAsync = ReservationDomainService.confirmReservationAsync getUtcNow loadState appendEvent
          CheckInAsync = ReservationDomainService.checkInAsync getUtcNow loadState appendEvent
          CheckOutAsync = ReservationDomainService.checkOutAsync getUtcNow loadState appendEvent
          CancelReservationAsync = ReservationDomainService.cancelReservationAsync getUtcNow loadState appendEvent
          RescheduleStayAsync = ReservationDomainService.rescheduleStayAsync getUtcNow loadState appendEvent
          ChangeRoomAsync = ReservationDomainService.changeRoomAsync getUtcNow loadState appendEvent
          AddDependentAsync = ReservationDomainService.addDependentAsync getUtcNow loadState appendEvent
          RemoveDependentAsync = ReservationDomainService.removeDependentAsync getUtcNow loadState appendEvent }

    /// Production wiring: real time + the Marten-backed event store.
    let create (eventStore: IReservationSimpleEventStore) =
        createWith (fun () -> DateTime.UtcNow) eventStore.LoadStateAsync eventStore.AppendEventAsync

    /// Deterministic time for tests that need a fixed clock.
    let createWithTimeProvider (timeProvider: TimeProvider) (eventStore: IReservationSimpleEventStore) =
        createWith (fun () -> timeProvider.GetUtcNow().UtcDateTime) eventStore.LoadStateAsync eventStore.AppendEventAsync

    /// Inject stub load/append functions directly (unit tests).
    let createWithMocks (loadState: LoadState) (appendEvent: AppendEvent) =
        createWith (fun () -> DateTime.UtcNow) loadState appendEvent
