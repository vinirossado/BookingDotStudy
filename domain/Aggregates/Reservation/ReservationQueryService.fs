namespace BookingDotStudy.Domain.Aggregates.Reservation

open System.Threading.Tasks
open Marten

/// Read side. Backed by the inline projection: instead of fetching and folding
/// the event stream on every read, we load the pre-computed
/// ReservationReadModel document (the snapshot) that Marten keeps up to date on
/// each append.
///
/// Registered as a SINGLETON in DI because it depends on IDocumentStore, not a
/// scoped IDocumentSession. Returns ReservationState so the API/DTOs stay
/// decoupled from the read-model shape.
type IReservationQueryService =
    abstract member GetById: ReservationId -> Task<ReservationState option>
    abstract member ListAll: unit -> Task<ReservationState list>

type ReservationQueryService(store: IDocumentStore) =

    let streamKeyOf (ReservationId id) = $"reservation-{id}"

    interface IReservationQueryService with

        member _.GetById(id: ReservationId) : Task<ReservationState option> =
            task {
                use session = store.QuerySession()
                let! readModel = session.LoadAsync<ReservationReadModel>(streamKeyOf id)

                return
                    if obj.ReferenceEquals(readModel, null) then
                        None
                    else
                        Some(ReservationReadModel.toState readModel)
            }

        member _.ListAll() : Task<ReservationState list> =
            task {
                use session = store.QuerySession()
                let! readModels = session.Query<ReservationReadModel>().ToListAsync()
                return readModels |> Seq.map ReservationReadModel.toState |> Seq.toList
            }
