# busynet — `bkk departures` (design)

Date: 2026-11-14
Status: draft

## Intent

Show the next buses leaving one predefined Budapest public-transport stop on the BUSY Bar, so a glance at the bar
says whether it is time to walk to the stop. The user's stop is Kötő utca in Budaörs, platform towards Budapest
(`BKK_F04808`). The command takes an optional stop id argument and defaults to `BKK_F04808` when none is given.

Data comes from BKK's FUTÁR API (<https://opendata.bkk.hu/docs/futar-openapi.yaml>), which gives live predictions
for tracked vehicles and timetable times otherwise.

## Goals

- `busynet bkk departures [STOP_ID]` shows the **next 3 departures** from the stop, **rotating** one per frame.
- Each frame says **which of the 3** it is: `1.`, `2.`, `3.`.
- Each frame shows the line, the time and the destination.
- Time is **minutes until departure**; a departure **more than an hour away** shows its **clock time** instead.
- Live predictions are used when present; timetable-only departures are visibly marked.
- Same CLI contract, configuration and Ctrl+C behaviour as `claude limits` and `gh workflow`.

## Non-goals

- Several stops, switching stops with a button, or stop search/discovery from the CLI (stop ids are found once,
  e.g. via `stops-for-location`; see Reference).
- Filtering by line or mode: every departure from the stop counts.
- Service alerts, vehicle positions, trip planning.
- A generated client for the whole FUTÁR API.

## Anti-goals

- The API key appearing in any log line, exception message or stack trace, including at `--verbose`. FUTÁR takes
  the key **in the query string** (`?key=`), so the request URL itself is secret: the HTTP client must not log
  (`RemoveAllLoggers()`, as `AddClaude` does) and no exception may carry the request URL.
- A transient failure (network drop, sleep, 5xx, 429) ending the watch, or a fatal one (rejected key, unknown stop)
  being retried forever.
- Showing a departure that has already left or was cancelled.
- A stale minutes value: "4'" must not still read "4'" ten minutes later because no new poll arrived.

## Constraints

- Native AOT build (Homebrew binary): no reflection-based serialization; System.Text.Json source generation, Refit
  generated client (`RestService.ForGenerated`), as in `Busynet.Claude`.
- Display: 72x16 px, two text rows; text goes through `DisplayText.ToDisplayableAscii`, so Hungarian accents are
  stripped (`Kőbánya` → `Kobanya`); `►` in headsigns must become something displayable (`>`).
- `-warnaserror`, central package versions, tests in a matching `tests/Busynet.Bkk.Tests` project.

## Approaches

| | Approach | Trade-off |
|---|---|---|
| **A (picked)** | New `Busynet.Bkk` library with a hand-written Refit interface for the one endpoint and minimal DTOs | ~100 lines, AOT-safe, mirrors `Busynet.Claude`/`Busynet.GitHub`; further endpoints are added by hand |
| B | Generate a full client from the 6,300-line OpenAPI spec (Refitter/Kiota/NSwag) | Whole API at once, but thousands of generated lines for one call, a codegen tool in the build, AOT/trim risk |
| dropped | Read BKK's GTFS static timetable | No key needed, but no live predictions — the reason to use FUTÁR |

Picked A by the user: it matches how the project already talks to Claude and GitHub, and one endpoint does not
justify generated code.

## Behaviour (decided with the user)

```sh
busynet bkk departures [STOP_ID] [--interval <seconds>] [--rotate <seconds>] [--host <host>] [--token <token>] [--verbose]
```

Frame for departure *n* of up to 3:

```
┌────────────────────────────┐
│ 1. 922        17'          │   top row, normal font: position, line, time
│ Astoria M > Budakeszi      │   bottom row, small font: destination, scrolls if wider than the display
└────────────────────────────┘
```

- **Stop:** `STOP_ID` is optional; without it the stop is `BKK_F04808` (Kötő utca, towards Budapest). *(User-decided.)*
  An empty or whitespace value is a usage error (exit 2).
- **Position:** `1.`, `2.`, `3.` — the departure's rank in the current list.
- **Time:** whole minutes until departure, `17'`; `0'` when it is due now (under a minute). From 60 minutes away,
  the clock time `00:04` (local time, 24h). *(User-decided: minutes, clock time over an hour away.)*

## Design calls left open by the user (proposed, correctable)

- **Command name:** `busynet bkk departures [STOP_ID]`, a new `bkk` group next to `claude` and `gh`.
- **API key:** environment variable `BKK_API_KEY` only — no `--key` option, so the key never sits in shell history
  or `ps`. Missing → configuration error, exit 1, message names the variable. Usage example in the README:
  `BKK_API_KEY=$(op read "op://…") busynet bkk departures`.
- **Timetable-only marker:** a departure without `predictedDepartureTime` shows `~` before its time (`~17'`,
  `~00:04`). Live ones show no marker.
- **Timings:** `--rotate` (default 5 s) per frame; `--interval` (default 30 s) between API polls; both ≥ 1.
- **Rotation vs. redraws:** a redraw happens on every rotation tick (that is the point), and the minutes are
  recomputed from the clock on each tick, so they stay correct between polls. A new poll does not restart the
  rotation; the next tick simply shows position *k* of the new list (wrapping if the list shrank).
- **Which departures:** request `minutesBefore=0`, `minutesAfter` large enough to find 3 at night (e.g. 180), a small
  `limit`; drop `canceled: true`, drop any whose departure time is already past at render time, sort by effective
  time, take 3.
- **Fewer than 3:** rotate through what there is. **None:** a single static frame `no departures` (top row) with the
  stop name on the bottom row.
- **Before the first poll succeeds:** nothing drawn (same as `gh workflow`).
- **Failures:** HTTP 401 (`Invalid API key…`, plain text body) → fatal: message "BKK API key rejected", exit 1.
  HTTP 404 with `"Unknown stopId."` → fatal, exit 1, names the stop id. Both are detected on the first poll so a typo
  fails fast. Network errors, timeouts, 429, 5xx → transient: keep the current list (minutes still tick), retry
  next interval, log at `--verbose`. On exit the bar is cleared, as the other commands do.

## Reference: the API as observed (2026-11-14)

- Base `https://futar.bkk.hu/api/query/v1/ws/otp/api/where/`; endpoint
  `arrivals-and-departures-for-stop.json?key=…&stopId=BKK_F04808&minutesBefore=0&minutesAfter=…&limit=…`.
- Success envelope: `{ currentTime (ms), version, status: "OK", code: 200, text, data }`.
  `data.entry.stopTimes[]`: `tripId`, `stopHeadsign`, `departureTime`/`arrivalTime` (Unix **seconds**),
  optional `predictedDepartureTime`/`predictedArrivalTime`, `canceled`. A trip's line is
  `data.references.trips[tripId].routeId` → `data.references.routes[routeId].shortName` (e.g. `922`, `40E`).
  Fall back to `trips[tripId].tripHeadsign` when `stopHeadsign` is absent, and to `arrivalTime` when there is no
  departure time.
- Errors: unknown stop → HTTP 404, JSON `status: "NOT_FOUND"`, `text: "Unknown stopId."`; missing or bad key →
  HTTP 401, plain-text body.
- Stops around the user (47.4606410, 18.9503113): `BKK_F04808` Kötő utca towards Budapest (922 → Astoria M,
  940, 988, 40 …); `BKK_F04807` Kötő utca the other way. `BKK_CS…` ids are parent stations, not platforms.

## Structure (approach A)

- `src/Busynet.Bkk`: `IFutarApi` (Refit, one GET), DTOs + `JsonSerializerContext`, `BkkDeparturesClient` mapping
  the envelope to `Departure(Line, Headsign, Scheduled, Predicted?)` and classifying failures into a
  `BkkException` (fatal/transient, matching the existing exception hierarchy), `AddBkk()` registration with the
  logging-free named HTTP client.
- `src/Busynet.Bar/Layouts/DeparturesLayout.cs`: builds the two text rows (stable element ids).
- `src/Busynet.Cli/Features/BkkDepartures/`: command, options, settings, state (list + rotation index + `now` →
  frame text), watch combining the `Poller` with a rotation timer through `DisplayWatch`.
- README usage section and `docs/architecture.md` entry.

## Left to the builder

- Exact column positions/fonts on the top row (measure on the bar as `WorkflowLayout` did), and whether
  `--rotate` 5 s gives a long destination enough time to scroll; raise the default if not.
- How the rotation tick and the poll are merged in the Rx pipeline, as long as the behaviour above holds and it is
  testable with the existing test scheduler.
- Retry/backoff details, reusing what `Poller` already does.
