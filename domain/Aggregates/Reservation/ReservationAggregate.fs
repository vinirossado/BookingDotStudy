namespace BookingDotStudy.Domain.Aggregates.Reservation

open System

/// The pure functional core of the aggregate. No I/O here — just:
///   - Apply.event : rebuild state from an event (the "evolve" function)
///   - Handle.command : decide which events a command produces (the "decide" function)
///   - fold : replay an event list into the current state
///
/// `handle` returns Result<ReservationEvent list, ReservationError>. A no-op
/// (idempotent) command returns `Ok []` so replaying it changes nothing.
[<RequireQualifiedAccess>]
module Reservation =

    // ---- Apply: event -> new state (immutable record updates) ----
    module Apply =

        let private created (e: ReservationEvents.ReservationCreated) : ReservationState =
            { Id = e.Id
              Status = Pending
              MainGuest = e.MainGuest
              Dependents = []
              Room = e.Room
              Dates = e.Dates
              CheckedInAt = None
              CheckedOutAt = None
              CancelledAt = None
              CancellationReason = None
              CreatedAt = e.CreatedAt
              UpdatedAt = e.CreatedAt }

        let private confirmed (s: ReservationState) (e: ReservationEvents.ReservationConfirmed) =
            { s with Status = Confirmed; UpdatedAt = e.Timestamp }

        let private checkedIn (s: ReservationState) (e: ReservationEvents.GuestCheckedIn) =
            { s with
                Status = CheckedIn
                CheckedInAt = Some e.Timestamp
                UpdatedAt = e.Timestamp }

        let private checkedOut (s: ReservationState) (e: ReservationEvents.GuestCheckedOut) =
            { s with
                Status = CheckedOut
                CheckedOutAt = Some e.Timestamp
                UpdatedAt = e.Timestamp }

        let private cancelled (s: ReservationState) (e: ReservationEvents.ReservationCancelled) =
            { s with
                Status = Cancelled
                CancelledAt = Some e.Timestamp
                CancellationReason = e.Reason
                UpdatedAt = e.Timestamp }

        let private rescheduled (s: ReservationState) (e: ReservationEvents.StayRescheduled) =
            { s with Dates = e.Dates; UpdatedAt = e.Timestamp }

        let private roomChanged (s: ReservationState) (e: ReservationEvents.RoomChanged) =
            { s with Room = e.Room; UpdatedAt = e.Timestamp }

        let private dependentAdded (s: ReservationState) (e: ReservationEvents.DependentAdded) =
            { s with
                Dependents = s.Dependents @ [ e.Dependent ]
                UpdatedAt = e.Timestamp }

        let private dependentRemoved (s: ReservationState) (e: ReservationEvents.DependentRemoved) =
            { s with
                Dependents = s.Dependents |> List.filter (fun d -> d.FullName <> e.FullName)
                UpdatedAt = e.Timestamp }

        /// Reject event/state combinations that cannot happen in a valid stream.
        let event (state: ReservationState option) (event: ReservationEvent) : ReservationState =
            match state, event with
            | None, ReservationCreated e -> created e
            | Some _, ReservationCreated _ -> failwith "Cannot apply ReservationCreated to an existing reservation"
            | None, _ -> failwith "Cannot apply event to a non-existent reservation"
            | Some s, ReservationConfirmed e -> confirmed s e
            | Some s, GuestCheckedIn e -> checkedIn s e
            | Some s, GuestCheckedOut e -> checkedOut s e
            | Some s, ReservationCancelled e -> cancelled s e
            | Some s, StayRescheduled e -> rescheduled s e
            | Some s, RoomChanged e -> roomChanged s e
            | Some s, DependentAdded e -> dependentAdded s e
            | Some s, DependentRemoved e -> dependentRemoved s e

    // ---- Handle: command -> Result<event list, error> ----
    module Handle =

        let private statusName =
            function
            | Pending -> "Pending"
            | Confirmed -> "Confirmed"
            | CheckedIn -> "CheckedIn"
            | CheckedOut -> "CheckedOut"
            | Cancelled -> "Cancelled"

        /// Validate the stay dates and the main guest for a new reservation.
        let private validateCreate (c: ReservationCommands.CreateReservation) =
            if String.IsNullOrWhiteSpace c.MainGuest.FullName then
                Some(ValidationError "Main guest full name is required")
            elif String.IsNullOrWhiteSpace c.Room.Number then
                Some(ValidationError "Room number is required")
            elif c.Dates.CheckOutDate.Date <= c.Dates.CheckInDate.Date then
                Some(ValidationError "Check-out date must be after check-in date")
            else
                None

        let private createReservation (now: DateTime) (c: ReservationCommands.CreateReservation) =
            match validateCreate c with
            | Some error -> Error error
            | None ->
                Ok
                    [ ReservationCreated
                          { Id = c.Id
                            MainGuest = c.MainGuest
                            Room = c.Room
                            Dates = c.Dates
                            CreatedAt = now } ]

        let private confirm (now: DateTime) (s: ReservationState) (_: ReservationCommands.ConfirmReservation) =
            match s.Status with
            | Confirmed -> Ok [] // idempotent
            | Pending -> Ok [ ReservationConfirmed { Timestamp = now } ]
            | other -> Error(InvalidStatusTransition(other, "Confirm"))

        let private checkIn (now: DateTime) (s: ReservationState) (_: ReservationCommands.CheckIn) =
            match s.Status with
            | CheckedIn -> Ok [] // idempotent
            | Pending
            | Confirmed -> Ok [ GuestCheckedIn { Timestamp = now } ]
            | other -> Error(InvalidStatusTransition(other, "CheckIn"))

        let private checkOut (now: DateTime) (s: ReservationState) (_: ReservationCommands.CheckOut) =
            match s.Status with
            | CheckedOut -> Ok [] // idempotent
            | CheckedIn -> Ok [ GuestCheckedOut { Timestamp = now } ]
            | other -> Error(InvalidStatusTransition(other, "CheckOut"))

        let private cancel (now: DateTime) (s: ReservationState) (c: ReservationCommands.CancelReservation) =
            match s.Status with
            | Cancelled -> Ok [] // idempotent
            | Pending
            | Confirmed -> Ok [ ReservationCancelled { Reason = c.Reason; Timestamp = now } ]
            | other -> Error(InvalidStatusTransition(other, "Cancel"))

        /// Reschedule/room changes are only sensible before check-out.
        let private isMutable (s: ReservationState) =
            match s.Status with
            | Pending
            | Confirmed
            | CheckedIn -> true
            | CheckedOut
            | Cancelled -> false

        let private reschedule (now: DateTime) (s: ReservationState) (c: ReservationCommands.RescheduleStay) =
            if not (isMutable s) then
                Error(InvalidStatusTransition(s.Status, "Reschedule"))
            elif c.Dates.CheckOutDate.Date <= c.Dates.CheckInDate.Date then
                Error(ValidationError "Check-out date must be after check-in date")
            elif s.Dates = c.Dates then
                Ok [] // idempotent
            else
                Ok [ StayRescheduled { Dates = c.Dates; Timestamp = now } ]

        let private changeRoom (now: DateTime) (s: ReservationState) (c: ReservationCommands.ChangeRoom) =
            if not (isMutable s) then
                Error(InvalidStatusTransition(s.Status, "ChangeRoom"))
            elif String.IsNullOrWhiteSpace c.Room.Number then
                Error(ValidationError "Room number is required")
            elif s.Room = c.Room then
                Ok [] // idempotent
            else
                Ok [ RoomChanged { Room = c.Room; Timestamp = now } ]

        let private addDependent (now: DateTime) (s: ReservationState) (c: ReservationCommands.AddDependent) =
            if not (isMutable s) then
                Error(InvalidStatusTransition(s.Status, "AddDependent"))
            elif String.IsNullOrWhiteSpace c.Dependent.FullName then
                Error(ValidationError "Dependent full name is required")
            elif s.Dependents |> List.exists (fun d -> d.FullName = c.Dependent.FullName) then
                Ok [] // already present: idempotent
            else
                Ok [ DependentAdded { Dependent = c.Dependent; Timestamp = now } ]

        let private removeDependent (now: DateTime) (s: ReservationState) (c: ReservationCommands.RemoveDependent) =
            if s.Dependents |> List.exists (fun d -> d.FullName = c.FullName) |> not then
                Error(DependentNotFound c.FullName)
            else
                Ok [ DependentRemoved { FullName = c.FullName; Timestamp = now } ]

        let private onMissing () = Error(ReservationNotFound(ReservationId ""))

        let command (now: DateTime) (state: ReservationState option) (command: ReservationCommand) =
            match state, command with
            | None, CreateReservation c -> createReservation now c
            | Some s, CreateReservation _ -> Error(ReservationAlreadyExists s.Id)
            // Any other command on a missing reservation -> NotFound (with its id).
            | None, ConfirmReservation c -> Error(ReservationNotFound c.ReservationId)
            | None, CheckIn c -> Error(ReservationNotFound c.ReservationId)
            | None, CheckOut c -> Error(ReservationNotFound c.ReservationId)
            | None, CancelReservation c -> Error(ReservationNotFound c.ReservationId)
            | None, RescheduleStay c -> Error(ReservationNotFound c.ReservationId)
            | None, ChangeRoom c -> Error(ReservationNotFound c.ReservationId)
            | None, AddDependent c -> Error(ReservationNotFound c.ReservationId)
            | None, RemoveDependent c -> Error(ReservationNotFound c.ReservationId)
            | Some s, ConfirmReservation c -> confirm now s c
            | Some s, CheckIn c -> checkIn now s c
            | Some s, CheckOut c -> checkOut now s c
            | Some s, CancelReservation c -> cancel now s c
            | Some s, RescheduleStay c -> reschedule now s c
            | Some s, ChangeRoom c -> changeRoom now s c
            | Some s, AddDependent c -> addDependent now s c
            | Some s, RemoveDependent c -> removeDependent now s c

    // ---- Public surface ----
    let apply = Apply.event
    let handle = Handle.command

    /// Replay an event list into the current state (None if the stream is empty
    /// or does not start with a ReservationCreated event).
    let fold (events: ReservationEvent list) : ReservationState option =
        events
        |> List.fold
            (fun state event ->
                match state, event with
                | None, ReservationCreated _ -> Some(apply None event)
                | Some current, _ -> Some(apply (Some current) event)
                | None, _ -> None)
            None
