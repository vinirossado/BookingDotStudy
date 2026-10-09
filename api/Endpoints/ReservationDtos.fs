namespace BookingDotStudy.Api

open System
open BookingDotStudy.Domain.Aggregates.Reservation

/// Request/response DTOs. [<CLIMutable>] lets ASP.NET's JSON binder populate
/// the records on deserialize. They are deliberately separate from the domain
/// types so the wire contract can evolve independently of the aggregate.

[<CLIMutable>]
type GuestRequest =
    { FullName: string
      Email: string option
      Phone: string option
      Document: string option }

[<CLIMutable>]
type DependentRequest =
    { FullName: string
      Document: string option
      Age: int option }

[<CLIMutable>]
type RoomRequest = { Number: string; RoomType: string }

[<CLIMutable>]
type DatesRequest =
    { CheckInDate: DateTime
      CheckOutDate: DateTime }

[<CLIMutable>]
type CreateReservationRequest =
    { MainGuest: GuestRequest
      Room: RoomRequest
      Dates: DatesRequest }

[<CLIMutable>]
type CancelReservationRequest = { Reason: string option }

[<CLIMutable>]
type RescheduleRequest = { Dates: DatesRequest }

[<CLIMutable>]
type ChangeRoomRequest = { Room: RoomRequest }

// ---- Responses ----

type GuestResponse =
    { FullName: string
      Email: string option
      Phone: string option
      Document: string option }

type DependentResponse =
    { FullName: string
      Document: string option
      Age: int option }

type RoomResponse = { Number: string; RoomType: string }

type DatesResponse =
    { CheckInDate: DateTime
      CheckOutDate: DateTime }

type ReservationResponse =
    { Id: string
      Status: string
      MainGuest: GuestResponse
      Dependents: DependentResponse list
      Room: RoomResponse
      Dates: DatesResponse
      Nights: int
      CheckedInAt: DateTime option
      CheckedOutAt: DateTime option
      CancelledAt: DateTime option
      CancellationReason: string option
      CreatedAt: DateTime
      UpdatedAt: DateTime }

// ---- Mapping ----

module GuestRequest =
    let toDomain (dto: GuestRequest) : Guest =
        { FullName = dto.FullName
          Email = dto.Email
          Phone = dto.Phone
          Document = dto.Document }

module DependentRequest =
    let toDomain (dto: DependentRequest) : Dependent =
        { FullName = dto.FullName
          Document = dto.Document
          Age = dto.Age }

module RoomRequest =
    let toDomain (dto: RoomRequest) : Room =
        { Number = dto.Number; RoomType = dto.RoomType }

module DatesRequest =
    let toDomain (dto: DatesRequest) : StayDates =
        { CheckInDate = dto.CheckInDate
          CheckOutDate = dto.CheckOutDate }

module ReservationResponse =

    let private statusText =
        function
        | Pending -> "Pending"
        | Confirmed -> "Confirmed"
        | CheckedIn -> "CheckedIn"
        | CheckedOut -> "CheckedOut"
        | Cancelled -> "Cancelled"

    let private guest (g: Guest) : GuestResponse =
        { FullName = g.FullName; Email = g.Email; Phone = g.Phone; Document = g.Document }

    let private dependent (d: Dependent) : DependentResponse =
        { FullName = d.FullName; Document = d.Document; Age = d.Age }

    let fromState (s: ReservationState) : ReservationResponse =
        { Id = ReservationId.value s.Id
          Status = statusText s.Status
          MainGuest = guest s.MainGuest
          Dependents = s.Dependents |> List.map dependent
          Room = { Number = s.Room.Number; RoomType = s.Room.RoomType }
          Dates = { CheckInDate = s.Dates.CheckInDate; CheckOutDate = s.Dates.CheckOutDate }
          Nights = StayDates.nights s.Dates
          CheckedInAt = s.CheckedInAt
          CheckedOutAt = s.CheckedOutAt
          CancelledAt = s.CancelledAt
          CancellationReason = s.CancellationReason
          CreatedAt = s.CreatedAt
          UpdatedAt = s.UpdatedAt }
