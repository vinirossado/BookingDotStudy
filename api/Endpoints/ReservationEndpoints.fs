namespace BookingDotStudy.Api

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open BookingDotStudy.Shared
open BookingDotStudy.Domain.Aggregates.Reservation

/// HTTP surface for the Reservation aggregate. Each handler:
///   1. resolves the (scoped) services from ctx.RequestServices
///   2. reads the DTO, maps it to a command
///   3. calls the domain-service function
///   4. translates the result / typed domain exceptions to an IResult
module ReservationEndpoints =

    [<Literal>]
    let private Tag = "Reservations"

    let private domainService (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<ReservationDomainServiceFunctions>()

    let private queryService (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<IReservationQueryService>()

    /// Run a write that may throw a typed ReservationDomainException and turn it
    /// into the right HTTP status. onSuccess builds the success payload.
    let private execute (operation: string) (work: unit -> Task<'a>) (onSuccess: 'a -> IResult) : Task<IResult> =
        task {
            try
                let! result = work ()
                return onSuccess result
            with
            | :? ReservationNotFoundException as ex -> return Results.NotFound({| message = ex.Message |})
            | :? ReservationAlreadyExistsException as ex -> return Results.Conflict({| message = ex.Message |})
            | :? InvalidStatusTransitionException as ex -> return Results.Conflict({| message = ex.Message |})
            | :? DependentNotFoundException as ex -> return Results.NotFound({| message = ex.Message |})
            | :? ReservationValidationException as ex -> return Results.BadRequest({| message = ex.Message |})
            | ex -> return Results.Problem(detail = ex.Message, title = $"Error while {operation}")
        }

    /// Map an idempotent UpdateResult to HTTP.
    let private fromUpdateResult (id: string) (result: UpdateResult) : IResult =
        match result with
        | Updated -> Results.Ok({| updated = true |})
        | NoChanges -> Results.Ok({| updated = false |})
        | UpdateResult.NotFound -> Results.NotFound({| message = $"Reservation {id} not found" |})

    // ---- Handlers --------------------------------------------------------

    let private listReservations (ctx: HttpContext) : Task<IResult> =
        task {
            let! all = (queryService ctx).ListAll()
            return Results.Ok(all |> List.map ReservationResponse.fromState)
        }

    let private getReservation (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let! found = (queryService ctx).GetById(ReservationId.create id)

            match found with
            | Some s -> return Results.Ok(ReservationResponse.fromState s)
            | None -> return Results.NotFound({| message = $"Reservation {id} not found" |})
        }

    let private createReservation (ctx: HttpContext) : Task<IResult> =
        task {
            let! dto = ctx.Request.ReadFromJsonAsync<CreateReservationRequest>()
            let service = domainService ctx
            let id = ReservationId.newId ()

            let cmd: ReservationCommands.CreateReservation =
                { Id = id
                  MainGuest = GuestRequest.toDomain dto.MainGuest
                  Room = RoomRequest.toDomain dto.Room
                  Dates = DatesRequest.toDomain dto.Dates }

            return!
                execute "creating reservation" (fun () -> service.CreateReservationAsync cmd) (fun () ->
                    Results.Created($"/api/reservations/{ReservationId.value id}", {| id = ReservationId.value id |}))
        }

    let private confirmReservation (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let service = domainService ctx
            let cmd: ReservationCommands.ConfirmReservation = { ReservationId = ReservationId.create id }
            return! execute "confirming reservation" (fun () -> service.ConfirmReservationAsync cmd) (fromUpdateResult id)
        }

    let private checkIn (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let service = domainService ctx
            let cmd: ReservationCommands.CheckIn = { ReservationId = ReservationId.create id }
            return! execute "checking in" (fun () -> service.CheckInAsync cmd) (fromUpdateResult id)
        }

    let private checkOut (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let service = domainService ctx
            let cmd: ReservationCommands.CheckOut = { ReservationId = ReservationId.create id }
            return! execute "checking out" (fun () -> service.CheckOutAsync cmd) (fromUpdateResult id)
        }

    let private cancelReservation (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let! dto = ctx.Request.ReadFromJsonAsync<CancelReservationRequest>()
            let service = domainService ctx

            let cmd: ReservationCommands.CancelReservation =
                { ReservationId = ReservationId.create id; Reason = dto.Reason }

            return! execute "cancelling reservation" (fun () -> service.CancelReservationAsync cmd) (fromUpdateResult id)
        }

    let private reschedule (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let! dto = ctx.Request.ReadFromJsonAsync<RescheduleRequest>()
            let service = domainService ctx

            let cmd: ReservationCommands.RescheduleStay =
                { ReservationId = ReservationId.create id; Dates = DatesRequest.toDomain dto.Dates }

            return! execute "rescheduling stay" (fun () -> service.RescheduleStayAsync cmd) (fromUpdateResult id)
        }

    let private changeRoom (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let! dto = ctx.Request.ReadFromJsonAsync<ChangeRoomRequest>()
            let service = domainService ctx

            let cmd: ReservationCommands.ChangeRoom =
                { ReservationId = ReservationId.create id; Room = RoomRequest.toDomain dto.Room }

            return! execute "changing room" (fun () -> service.ChangeRoomAsync cmd) (fromUpdateResult id)
        }

    let private addDependent (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let! dto = ctx.Request.ReadFromJsonAsync<DependentRequest>()
            let service = domainService ctx

            let cmd: ReservationCommands.AddDependent =
                { ReservationId = ReservationId.create id; Dependent = DependentRequest.toDomain dto }

            return!
                execute "adding dependent" (fun () -> service.AddDependentAsync cmd) (fun () ->
                    Results.Ok({| added = true |}))
        }

    let private removeDependent (id: string) (ctx: HttpContext) : Task<IResult> =
        task {
            let fullName = ctx.Request.RouteValues.["fullName"] :?> string
            let service = domainService ctx

            let cmd: ReservationCommands.RemoveDependent =
                { ReservationId = ReservationId.create id; FullName = fullName }

            return!
                execute "removing dependent" (fun () -> service.RemoveDependentAsync cmd) (fun () ->
                    Results.Ok({| removed = true |}))
        }

    // ---- Route registration ---------------------------------------------

    let configureRoutes (endpoints: IEndpointRouteBuilder) =
        let getP = RouteHelpers.getWithParamAndTag Tag endpoints "id"
        let postP = RouteHelpers.postWithParamAndTag Tag endpoints "id"
        let putP = RouteHelpers.putWithParamAndTag Tag endpoints "id"

        RouteHelpers.getWithTag Tag endpoints "/api/reservations" listReservations
        RouteHelpers.postWithTag Tag endpoints "/api/reservations" createReservation
        getP "/api/reservations/{id}" getReservation
        postP "/api/reservations/{id}/confirm" confirmReservation
        postP "/api/reservations/{id}/check-in" checkIn
        postP "/api/reservations/{id}/check-out" checkOut
        postP "/api/reservations/{id}/cancel" cancelReservation
        putP "/api/reservations/{id}/dates" reschedule
        putP "/api/reservations/{id}/room" changeRoom
        postP "/api/reservations/{id}/dependents" addDependent
        // Remove a dependent by name. The handler reads {fullName} from the
        // route values itself; this helper supplies {id}.
        RouteHelpers.deleteWithParamAndTag
            Tag
            endpoints
            "id"
            "/api/reservations/{id}/dependents/{fullName}"
            removeDependent

        ()
