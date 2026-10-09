-- BookingDotStudy — sample queries for exploring the event store & read model.
--
-- Connect:
--   docker exec -it booking-study-postgres psql -U booking -d booking_study
-- Or run a single query non-interactively:
--   docker exec booking-study-postgres psql -U booking -d booking_study -c "<query>"
--
-- Marten tables used here:
--   mt_events                     one row per event (the source of truth)
--   mt_doc_reservationreadmodel   one row per reservation snapshot (JSONB in `data`)
--   mt_streams                    one row per event stream (one reservation)

-- =====================================================================
-- 1. EVENT STORE (mt_events)
-- =====================================================================

-- 1a. Every event, in global order. `data` is the event payload (JSONB).
select seq_id, stream_id, version, type, timestamp
from mt_events
order by seq_id;

-- 1b. The full history of ONE reservation (replace the id).
--     stream_id is "reservation-<guid>".
select version, type, timestamp, data
from mt_events
where stream_id = 'reservation-<PASTE-ID>'
order by version;

-- 1c. How many events of each type exist.
select type, count(*) as n
from mt_events
group by type
order by n desc;

-- 1d. Pull a specific field out of an event payload.
--     Example: the main guest name on every ReservationCreated event.
select stream_id,
       data #>> '{MainGuest,FullName}' as main_guest,
       data #>> '{Room,Number}'        as room
from mt_events
where type = 'reservation_created'
order by stream_id;

-- 1e. Rebuild a reservation's timeline as a readable log.
select version,
       type,
       timestamp
from mt_events
where stream_id = 'reservation-<PASTE-ID>'
order by version;

-- =====================================================================
-- 2. READ MODEL / SNAPSHOT (mt_doc_reservationreadmodel)
-- =====================================================================

-- 2a. All reservations, flattened from the JSONB snapshot.
select id,
       data ->> 'Status'                 as status,
       data #>> '{MainGuest,FullName}'   as main_guest,
       data #>> '{Room,Number}'          as room,
       data ->> 'Nights'                 as nights,
       data #>> '{Dates,CheckInDate}'    as check_in,
       data #>> '{Dates,CheckOutDate}'   as check_out
from mt_doc_reservationreadmodel
order by id;

-- 2b. Count reservations by status.
select data ->> 'Status' as status, count(*) as n
from mt_doc_reservationreadmodel
group by data ->> 'Status'
order by n desc;

-- 2c. Reservations currently in-house (checked in, not yet out).
select id,
       data #>> '{MainGuest,FullName}' as main_guest,
       data #>> '{CheckedInAt}'        as checked_in_at
from mt_doc_reservationreadmodel
where data ->> 'Status' = 'CheckedIn'
order by checked_in_at;

-- 2d. Dependents per reservation (expand the JSONB array).
select r.id,
       r.data #>> '{MainGuest,FullName}' as main_guest,
       dep ->> 'FullName'                as dependent,
       dep ->> 'Age'                     as age
from mt_doc_reservationreadmodel r
cross join lateral jsonb_array_elements(r.data -> 'Dependents') as dep
order by r.id;

-- 2e. Reservations with at least one dependent.
select id,
       data #>> '{MainGuest,FullName}'            as main_guest,
       jsonb_array_length(data -> 'Dependents')   as dependents
from mt_doc_reservationreadmodel
where jsonb_array_length(data -> 'Dependents') > 0
order by dependents desc;

-- 2f. Total nights booked across all non-cancelled reservations.
select sum((data ->> 'Nights')::int) as total_nights
from mt_doc_reservationreadmodel
where data ->> 'Status' <> 'Cancelled';

-- 2g. The full snapshot JSON for one reservation, pretty-printed.
select jsonb_pretty(data)
from mt_doc_reservationreadmodel
where id = 'reservation-<PASTE-ID>';

-- =====================================================================
-- 3. STREAMS (mt_streams) & cross-checks
-- =====================================================================

-- 3a. One row per reservation stream, with its current version (event count).
select id, version, type, created, updated
from mt_streams
order by updated desc;

-- 3b. Sanity check: event-store vs read-model agree on the set of ids.
--     (strips the "reservation-" prefix from the read-model id to compare.)
select s.id as stream_id,
       case when r.id is null then 'MISSING in read model' else 'ok' end as read_model
from mt_streams s
left join mt_doc_reservationreadmodel r on r.id = s.id
order by s.id;

-- 3c. Events per stream vs the stream's recorded version (should match).
select e.stream_id,
       count(*)      as events,
       max(e.version) as max_version
from mt_events e
group by e.stream_id
order by e.stream_id;
