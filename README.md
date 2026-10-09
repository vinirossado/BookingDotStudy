# BookingDotStudy

A small F# learning project that mirrors the architecture of the **Aurora**
backend: an **event-sourced aggregate** persisted with **Marten 9** on
**PostgreSQL**, a **domain service** built from injected capability functions,
an **inline projection / snapshot** read model, and **ASP.NET Core Minimal API**
endpoints.

The domain is a tiny **hotel booking**: you create a reservation for a main
guest, add dependents, assign a room and dates, then move it through its
lifecycle (`Pending → Confirmed → CheckedIn → CheckedOut`, or `Cancelled`).
Everything is deliberately small so you can read the whole thing in one sitting
and see each layer of the pattern.

## Architecture at a glance

```
                        WRITE side                                 READ side
HTTP request                                            HTTP request
   │                                                       │
   ▼                                                       ▼
ReservationEndpoints (DTO -> command)                   ReservationEndpoints
   │                                                       │
   ▼                                                       ▼
ReservationDomainServiceFunctions                       IReservationQueryService
   │  (load state, Reservation.handle, append events)     │  (load the snapshot)
   ▼                                                       ▼
ISimpleEventStore (Marten)                              ReservationReadModel document
   │  append to the "reservation-<id>" stream              ▲
   ▼                                                       │ Inline projection
PostgreSQL  mt_events  ─────────────────────────────────────┘  (ReservationProjection)
            (source of truth)                            mt_doc_reservationreadmodel
                                                         (snapshot / read model)
```

**Command/Query split (CQRS):** writes append events to the stream; a Marten
**inline projection** (`ReservationProjection`) folds each new event into a
`ReservationReadModel` document **in the same transaction**, so reads are a
direct document load — no stream replay. The read model is effectively the
aggregate's **snapshot**, kept always-current.

### Projects

| Project | Role |
|---|---|
| `shared/BookingDotStudy.Shared` | Cross-cutting types (`UpdateResult`). |
| `domain/BookingDotStudy.Domain` | The aggregate, event store, projection, Marten config, DI. |
| `api/BookingDotStudy.Api` | Minimal API host + endpoints + DTOs. |
| `tests/BookingDotStudy.Tests` | xUnit + Unquote unit tests (pure, no DB). |
| `tests/BookingDotStudy.IntegrationTests` | xUnit + real Postgres: the inline projection / read model. |

### The Reservation aggregate (`domain/Aggregates/Reservation/`)

Split by concern, in F# compile order:

1. `ReservationDomain.fs` — `ReservationId`, value objects (`Guest`, `Dependent`, `Room`, `StayDates`), `ReservationStatus`, `ReservationState`, `ReservationError`.
2. `ReservationCommands.fs` — command records + the `ReservationCommand` DU.
3. `ReservationEvents.fs` — event records + the `ReservationEvent` DU.
4. `ReservationAggregate.fs` — the **pure core**: `Reservation.handle` (command → `Result<event list, error>`, incl. the status-transition rules), `Reservation.apply` (event → state), `Reservation.fold` (replay).
5. `ReservationProjection.fs` — the **inline read model**: `ReservationReadModel` document + `ReservationProjection` (a Marten `SingleStreamProjection`) whose `Evolve` delegates to `Reservation.apply`.
6. `ReservationExceptions.fs` — typed exceptions used at the service edge.
7. `ReservationDomainService.fs` — capability aliases (`LoadState`/`AppendEvent`/`GetUtcNow`), the per-command async operations, the `ReservationDomainServiceFunctions` record, and the factory.
8. `ReservationQueryService.fs` — read side (loads the projected `ReservationReadModel`, no replay).

Key idea: the **decision** (`handle`) and **evolution** (`apply`/`fold`) are
pure and trivially testable; the **service** supplies I/O as plain functions,
so tests stub them with lambdas (no mocking framework). The **projection**
reuses the exact same `Reservation.apply`, so the write side and the read model
can never drift apart.

### Lifecycle & rules (in `Reservation.handle`)

| Status | Allowed commands |
|---|---|
| `Pending` | Confirm, CheckIn, Cancel, Reschedule, ChangeRoom, Add/RemoveDependent |
| `Confirmed` | CheckIn, Cancel, Reschedule, ChangeRoom, Add/RemoveDependent |
| `CheckedIn` | CheckOut, Reschedule, ChangeRoom, Add/RemoveDependent |
| `CheckedOut` | (terminal) |
| `Cancelled` | (terminal) |

Invalid transitions return `InvalidStatusTransition` → **409 Conflict**;
validation failures (blank guest name, check-out ≤ check-in) return
`ValidationError` → **400 Bad Request**.

### Projections & snapshots

- **Projection:** `ReservationProjection : SingleStreamProjection<ReservationReadModel, string>`
  is registered **Inline** in `MartenStoreConfiguration.fs`. On every append,
  Marten calls `Evolve(snapshot, streamKey, event)`; we rebuild the state with
  `Reservation.apply` and store a `ReservationReadModel` (including a computed
  `Nights`).
- **Snapshot:** the row in `mt_doc_reservationreadmodel` *is* the snapshot — the
  current folded state of the stream, written in the same transaction as the
  event.
- **Reads** (`ReservationQueryService`) do a primary-key `LoadAsync` or a
  `Query<ReservationReadModel>()` — plain SELECTs, never a stream replay.

Inspect both after a lifecycle run:

```bash
# the snapshot / read model
docker exec booking-study-postgres psql -U booking -d booking_study \
  -c "select id, data->>'Status', data->>'Nights' from mt_doc_reservationreadmodel;"

# the event stream (source of truth)
docker exec booking-study-postgres psql -U booking -d booking_study \
  -c "select version, type from mt_events order by seq_id;"
```

## Prerequisites

- .NET SDK **10.0.401** (see `global.json`)
- Docker (for PostgreSQL)

## Run it

```bash
# 1. Start Postgres (host port 5433 -> container 5432)
docker compose up -d

# 2. Build + unit tests
dotnet build
dotnet test tests/BookingDotStudy.Tests/BookingDotStudy.Tests.fsproj

# 3. Run the API (Swagger UI at http://localhost:5080/swagger)
dotnet run --project api/BookingDotStudy.Api.fsproj
```

Marten creates/updates its schema automatically on first run
(`AutoCreateSchemaObjects = All`).

### Integration tests (real Postgres)

They run against an `integration_tests` database on the same Postgres container
and exercise the real inline projection:

```bash
docker compose up -d
docker exec booking-study-postgres \
  psql -U booking -d booking_study -c "CREATE DATABASE integration_tests;"   # once

dotnet test tests/BookingDotStudy.IntegrationTests/BookingDotStudy.IntegrationTests.fsproj
# or just `dotnet test` to run unit + integration together
```

Each test uses a fresh GUID id, so data may accumulate between runs by design —
drop/recreate `integration_tests` if you want a clean slate.

## Try the API

Prefer a GUI? Import the ready-made **Postman collection** in
[`postman/`](postman/README.md) — it covers the full lifecycle plus the error
cases, auto-captures the new `reservationId`, and asserts every response.
Otherwise, use `curl`:

```bash
B=http://localhost:5080

# Create a reservation -> { "id": "..." }
ID=$(curl -s -X POST $B/api/reservations -H 'Content-Type: application/json' -d '{
  "MainGuest": {"FullName":"Alice Johnson","Email":"alice@example.com","Phone":"+1-555-0100","Document":"ID-12345"},
  "Room": {"Number":"101","RoomType":"Standard"},
  "Dates": {"CheckInDate":"2026-02-10","CheckOutDate":"2026-02-13"}
}' | sed -E 's/.*"id":"([^"]+)".*/\1/')

curl -s -X POST $B/api/reservations/$ID/dependents -H 'Content-Type: application/json' \
  -d '{"FullName":"Tommy Johnson","Age":8}'
curl -s -X POST $B/api/reservations/$ID/confirm
curl -s -X POST $B/api/reservations/$ID/check-in
curl -s $B/api/reservations/$ID            # status CheckedIn, nights, main guest + dependents
curl -s -X POST $B/api/reservations/$ID/check-out
curl -s $B/api/reservations                # list all
```

### Guest, dependents & lifecycle

Each reservation carries the **main guest** (`FullName` required; `Email`,
`Phone`, `Document` optional) and a list of **dependents** (`FullName` required;
`Document`, `Age` optional). The lifecycle timestamps (`checkedInAt`,
`checkedOutAt`, `cancelledAt`) and the computed `nights` all come from folding
the event stream — nothing is stored by hand.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/reservations` | List all reservations |
| POST | `/api/reservations` | Create a reservation (Pending) |
| GET | `/api/reservations/{id}` | Get one reservation |
| POST | `/api/reservations/{id}/confirm` | Confirm (Pending → Confirmed) |
| POST | `/api/reservations/{id}/check-in` | Check in (→ CheckedIn) |
| POST | `/api/reservations/{id}/check-out` | Check out (→ CheckedOut) |
| POST | `/api/reservations/{id}/cancel` | Cancel (Pending/Confirmed only) |
| PUT | `/api/reservations/{id}/dates` | Reschedule the stay |
| PUT | `/api/reservations/{id}/room` | Change room |
| POST | `/api/reservations/{id}/dependents` | Add a dependent |
| DELETE | `/api/reservations/{id}/dependents/{fullName}` | Remove a dependent |
| GET | `/health` | Health check |

## Changing an event's shape (schema note)

Events are the source of truth, so changing the **shape** of an existing event
means older persisted events no longer deserialize. For a learning project the
simplest reset is to drop the data volume:

```bash
docker compose down -v && docker compose up -d
```

In a real system you would instead keep the old event shape and add an
**upcaster** (Marten supports this) that transforms the old JSON into the new
shape on read — never a destructive wipe.

## What to learn / extend next

- Add a `Payment` concept (deposit, balance, status) as new commands/events.
- Promote `Guest` to its own **`Guest`/`Customer` aggregate** (register people once, reference them by id across reservations).
- Switch the projection from **Inline** to **Async** (`ProjectionLifecycle.Async` + the projection daemon) to see eventual consistency — the read-model code stays the same.
- Add a **multi-stream projection**, e.g. "arrivals today" or "occupancy by room", querying across all reservations.

## Stop

```bash
docker compose down          # keep the data volume
docker compose down -v       # also delete the data
```
