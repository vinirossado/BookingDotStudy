namespace BookingDotStudy.Domain.Infrastructure

open System.Threading.Tasks
open Marten

/// Generic, aggregate-agnostic event store over a Marten stream.
///
/// `LoadStateAsync` fetches the whole stream for an id and folds it into the
/// current state; `AppendEventAsync` appends one event and saves. Streams are
/// keyed by the string `"{streamPrefix}-{id}"`.
type ISimpleEventStore<'TId, 'TEvent, 'TState> =
    abstract member LoadStateAsync: 'TId -> Task<'TState option>
    abstract member AppendEventAsync: 'TId -> 'TEvent -> Task<unit>
    abstract member AppendEventsAsync: 'TId -> 'TEvent list -> Task<unit>

type SimpleEventStore<'TId, 'TEvent, 'TState>
    (
        session: IDocumentSession,
        idToString: 'TId -> string,
        streamPrefix: string,
        foldEvents: 'TEvent list -> 'TState option
    ) =

    member private _.GetStreamKey(id: 'TId) = $"{streamPrefix}-{idToString id}"

    interface ISimpleEventStore<'TId, 'TEvent, 'TState> with

        member this.LoadStateAsync(id: 'TId) : Task<'TState option> =
            task {
                let streamKey = this.GetStreamKey id
                let! events = session.Events.FetchStreamAsync(streamKey)

                if events.Count = 0 then
                    return None
                else
                    let domainEvents =
                        events
                        |> Seq.map (fun e ->
                            match box e.Data with
                            | :? 'TEvent as evt -> evt
                            | _ -> failwith $"Unexpected event type in stream {streamKey}: {e.Data.GetType().Name}")
                        |> Seq.toList

                    return foldEvents domainEvents
            }

        member this.AppendEventAsync (id: 'TId) (event: 'TEvent) : Task<unit> =
            task {
                let streamKey = this.GetStreamKey id
                session.Events.Append(streamKey, event) |> ignore
                do! session.SaveChangesAsync()
            }

        member this.AppendEventsAsync (id: 'TId) (events: 'TEvent list) : Task<unit> =
            task {
                let streamKey = this.GetStreamKey id
                let boxed = events |> List.map box |> List.toArray
                session.Events.Append(streamKey, boxed) |> ignore
                do! session.SaveChangesAsync()
            }
