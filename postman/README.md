# Postman collection

A ready-to-run Postman collection for the BookingDotStudy hotel Reservation API.

## Files

| File | Purpose |
|---|---|
| `BookingDotStudy.postman_collection.json` | The requests (full lifecycle + error cases) with test scripts. |
| `BookingDotStudy.postman_environment.json` | The `BookingDotStudy - Local` environment (`baseUrl`, `reservationId`). |

## Import into Postman

1. **Import** both JSON files.
2. Pick the **BookingDotStudy - Local** environment (or rely on the collection
   defaults for `baseUrl` and `reservationId`).
3. Start the API:
   ```bash
   docker compose up -d
   dotnet run --project api/BookingDotStudy.Api.fsproj
   ```
4. Run **Create reservation** first — it saves the new id into `reservationId`,
   so every following request targets it. Then run the rest top-to-bottom, or
   use the **Collection Runner**.

## What's inside

Lifecycle (run in order):

1. `Health check`
2. `Create reservation` → stores `reservationId` (status Pending)
3. `List reservations`
4. `Get reservation by id` (asserts `nights = 3`)
5. `Add dependent`
6. `Change room`
7. `Reschedule stay`
8. `Confirm reservation` (Pending → Confirmed)
9. `Check in` (→ CheckedIn)
10. `Check out` (→ CheckedOut)
11. `Remove dependent`

Error cases:

- `Cancel after checkout (expect 409)` — invalid status transition
- `Get unknown reservation (expect 404)`
- `Create with checkout before checkin (expect 400)`

## Run from the command line (optional)

```bash
npx newman run BookingDotStudy.postman_collection.json \
  -e BookingDotStudy.postman_environment.json
```

Expected: 14 requests, 20 assertions, 0 failures.
