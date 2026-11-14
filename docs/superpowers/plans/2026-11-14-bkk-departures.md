# `bkk departures` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `busynet bkk departures [STOP_ID]` rotates the next 3 departures from a BKK stop (default `BKK_F04808`) on the BUSY Bar.

**Architecture:** A new `Busynet.Bkk` library wraps the one FUTÁR endpoint (Refit, hand-parsed JSON, failures classified fatal/transient like `Busynet.Claude`). `Busynet.Bar` gets a two-row `DeparturesLayout`. The CLI feature merges a `Poller.Poll` stream (every `--interval`) with a rotation tick (every `--rotate`) into `DisplayWatch.RunAsync`; the state renders from `scheduler.Now`, so minutes stay fresh between polls.

**Tech Stack:** .NET 11 (`net11.0`, native AOT), Refit 16.4 (`RestService.ForGenerated`), System.Text.Json `JsonNode`, System.Reactive 7, System.CommandLine 3, xunit.v3, Microsoft.Reactive.Testing.

**Spec:** `docs/superpowers/specs/2026-11-14-bkk-departures-design.md`

## Global Constraints

- AOT-safe: no reflection serialization. Parse responses with `JsonNode` like `ClaudeUsageClient`; Refit via `RestService.ForGenerated<T>`. New library csproj sets `<IsAotCompatible>true</IsAotCompatible>`.
- Build must pass `dotnet build -c Release -warnaserror`; package versions only in `Directory.Packages.props` (no new packages needed).
- The API key travels in the query string (`?key=`): no exception message, log line or `ToString()` may contain it or the request URL. The named HTTP client calls `.RemoveAllLoggers()`.
- Base URL `https://futar.bkk.hu`, path `/api/query/v1/ws/otp/api/where/arrivals-and-departures-for-stop.json`.
- API key only from environment variable `BKK_API_KEY`; no option.
- Default stop `BKK_F04808`; `--interval` default 30 s, `--rotate` default 5 s, both ≥ 1.
- All bar text through `DisplayText` (printable ASCII only); accents are transliterated (`Kőbánya` → `Kobanya`), `►` becomes `>`.
- CLI contract unchanged: usage error exit 2, `BusynetException` one line + exit 1, Ctrl+C exit 0, bar cleared on exit.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. If 1Password signing fails, commit with `-c commit.gpgsign=false`.

## Review Focus

1. **A bus leaves between polls** — it must vanish from the rotation at once and the rest renumber (`2.` becomes `1.`), not linger at `0'` or negative. → `DeparturesStateTests.Departed_entries_drop_out_before_the_next_poll` (Task 3).
2. **The list shrinks mid-rotation** (3 → 1 after a poll) — the next frame must be a valid one, not an index error. → `DeparturesStateTests.Index_wraps_when_the_list_shrinks` (Task 3).
3. **One malformed stop time** (no route reference, no times) — skip that entry, keep the rest; never fail the whole poll. → `BkkDeparturesClientTests.Skips_entries_it_cannot_read` (Task 1).
4. **The 60-minute boundary and midnight** — `59'` at 59m59s, clock time at exactly 60m, `00:04` after midnight in the local zone. → `DepartureLabelsTests.Switches_to_clock_time_at_one_hour` (Task 3).
5. **A transport failure whose message quotes the URL** — the key must not leak through `HttpRequestException`. → `BkkDeparturesClientTests.Failure_messages_never_contain_the_key` (Task 1).

---

### Task 1: `Busynet.Bkk` — FUTÁR client

**Files:**
- Create: `src/Busynet.Bkk/Busynet.Bkk.csproj` (copy of `Busynet.Claude.csproj` with `RootNamespace` `Busynet.Bkk`)
- Create: `src/Busynet.Bkk/BkkException.cs`
- Create: `src/Busynet.Bkk/Departures/Departure.cs`
- Create: `src/Busynet.Bkk/Departures/IFutarApi.cs`
- Create: `src/Busynet.Bkk/Departures/IBkkDepartures.cs`
- Create: `src/Busynet.Bkk/Departures/BkkDeparturesClient.cs`
- Create: `src/Busynet.Bkk/Departures/FutarOptions.cs`
- Create: `src/Busynet.Bkk/BkkServiceCollectionExtensions.cs`
- Create: `tests/Busynet.Bkk.Tests/Busynet.Bkk.Tests.csproj` (copy of `Busynet.Claude.Tests.csproj`, referencing `Busynet.Bkk`)
- Create: `tests/Busynet.Bkk.Tests/Departures/BkkDeparturesClientTests.cs`
- Modify: `Busynet.slnx` (add both projects)

**Interfaces:**
- Produces:
  - `public class BkkException(string message) : BusynetException(message)` — fatal.
  - `public sealed class BkkTransientException(string message, TimeSpan? retryAfter = null) : BkkException(message), ITransientFailure`.
  - `public sealed record Departure(string Line, string Headsign, DateTimeOffset Scheduled, DateTimeOffset? Predicted)` with `public DateTimeOffset When => Predicted ?? Scheduled;` and `public bool IsLive => Predicted is not null;`.
  - `public sealed record StopDepartures(string StopName, IReadOnlyList<Departure> Departures)` — `Departures` sorted by `When`, cancelled ones removed. Value equality is not needed (the view is compared, not this).
  - `public interface IBkkDepartures { Task<StopDepartures> GetDeparturesAsync(string apiKey, string stopId, CancellationToken ct); }`
  - `public sealed record FutarOptions(TimeSpan ResponseTimeout, int MinutesAfter, int Limit)` (not `BkkDeparturesOptions`: that name is the CLI's options class in Task 5) with `Default => new(TimeSpan.FromSeconds(15), 180, 10)`.
  - `public static IServiceCollection AddBkk(this IServiceCollection services)` — named client `"bkk"`, `BaseAddress = https://futar.bkk.hu`, `.RemoveAllLoggers()`, `TryAdd` for options, `IFutarApi`, `IBkkDepartures`.
  - `IFutarApi` (public, Refit): one `[Get(...)]` returning `Task<HttpResponseMessage>` with query params `key`, `stopId`, `minutesBefore`, `minutesAfter`, `limit` (`[AliasAs]` for exact names).

- [ ] **Step 1: Write the failing tests** in `BkkDeparturesClientTests.cs`, built like `ClaudeUsageClientTests` (`AddBkk()` then `AddHttpClient("bkk").ConfigurePrimaryHttpMessageHandler(() => fake)`, route `GET /api/query/v1/ws/otp/api/where/arrivals-and-departures-for-stop.json`). Fixture: an envelope `{currentTime, version: 2, status: "OK", code: 200, data: {entry: {stopId: "BKK_F04808", stopTimes: [...]}, references: {stops: {"BKK_F04808": {name: "Kötő utca"}}, trips: {...}, routes: {...}}}}` with: a live `922` (`stopHeadsign` "Astoria M ► Budakeszi", `departureTime` 1794697440, `predictedDepartureTime` 1794697500), a scheduled `940` with no `stopHeadsign` (trip `tripHeadsign` "Kamaraerdő") and only `arrivalTime` 1794700000, a `canceled: true` `988`, and a `40E` listed before the `922` but departing later. Tests:
  - `Parses_departures_in_time_order`: `StopName == "Kötő utca"`; lines `["922", "40E", "940"]` (cancelled gone, sorted by `When`); `922.Predicted == FromUnixTimeSeconds(1794697500)`, `IsLive`; `940.Headsign == "Kamaraerdő"`, `Scheduled == FromUnixTimeSeconds(1794700000)`, `!IsLive`.
  - `Sends_key_stop_and_window`: single call; host `futar.bkk.hu`; query has `key=SECRET-KEY`, `stopId=BKK_F04808`, `minutesBefore=0`, `minutesAfter=180`, `limit=10`.
  - `Skips_entries_it_cannot_read`: one extra stop time whose `tripId` has no trip reference and one with no times at all → result still has the 3 good lines.
  - `Rejected_key_is_fatal`: HTTP 401 plain text `Invalid API key...` → `BkkException` (not `ITransientFailure`), message `"BKK rejected the API key in BKK_API_KEY."`; same for 403.
  - `Unknown_stop_is_fatal`: HTTP 404 `{"status":"NOT_FOUND","code":404,"text":"Unknown stopId."}` → `BkkException` message `"Unknown BKK stop id: BKK_NOPE."`.
  - `Rate_limit_is_transient_with_retry_after`: 429 + `Retry-After: 20` → `BkkTransientException`, `RetryAfter == 20 s`.
  - `Server_error_and_bad_body_are_transient` (theory): 500; 200 with `not json`; 200 with `{"status":"OK"}` (no `data`) → `BkkTransientException`.
  - `Failure_messages_never_contain_the_key` (theory over 401, 404, 500, and `fake.Throw(..., new HttpRequestException("GET https://futar.bkk.hu/...?key=SECRET-KEY failed"))`): `Assert.DoesNotContain("SECRET-KEY", e.ToString())`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Busynet.Bkk.Tests -c Release`
Expected: build error — `AddBkk`/`IBkkDepartures` not defined.

- [ ] **Step 3: Implement** the files listed. `BkkDeparturesClient` (internal sealed) mirrors `ClaudeUsageClient`: linked deadline token with `ResponseTimeout`; status mapping 401/403 → fatal key message, 404 → fatal unknown stop, 429 → transient with clamped `Retry-After` (reuse the same parse), ≥500 → transient `"BKK API returned HTTP {status}"`, other ≥400 → fatal `"BKK API returned HTTP {status}"`; transport/timeout/`ApiRequestException` → transient `"BKK request failed: {innerType.Name}"`; JSON errors → transient `"BKK API returned an unexpected response"`. Per stop time: route = `references.routes[references.trips[tripId].routeId].shortName`; headsign = `stopHeadsign` ?? trip `tripHeadsign` ?? `""`; scheduled = `departureTime` ?? `arrivalTime` (Unix seconds); predicted = `predictedDepartureTime` ?? `predictedArrivalTime`; skip on any missing piece or `canceled == true`. Stop name from `references.stops[entry.stopId].name`, falling back to the stop id.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Busynet.Bkk.Tests -c Release`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Busynet.Bkk tests/Busynet.Bkk.Tests Busynet.slnx
git commit -m "Add BKK FUTÁR departures client"
```

---

### Task 2: Bar — transliteration and `DeparturesLayout`

**Files:**
- Modify: `src/Busynet.Bar/Display/DisplayText.cs`
- Create: `src/Busynet.Bar/Layouts/DepartureFrame.cs`
- Create: `src/Busynet.Bar/Layouts/DeparturesLayout.cs`
- Test: `tests/Busynet.Bar.Tests/Display/DisplayTextTests.cs` (add to existing), `tests/Busynet.Bar.Tests/Layouts/DeparturesLayoutTests.cs`

**Interfaces:**
- Produces:
  - `public static string DisplayText.Transliterate(string text)` — NFD-decompose, drop `UnicodeCategory.NonSpacingMark`, map `►`/`▶` to `>`, then `StripUndisplayable`. May return `""`.
  - `public sealed record DepartureFrame(string Header, string Destination)` — value-equal view.
  - `public static DisplayElements DeparturesLayout.Build(DepartureFrame frame)` with constants `X = 1`, `Width = 71`, `HeaderY = -2`, `DestinationY = 7`, `HeaderFont = "normal"`, `DestinationFont = "small"`, `ScrollRate = 1200`; element ids `"header"`, `"destination"`.

- [ ] **Step 1: Write failing tests**
  - `Transliterate_strips_hungarian_accents` (theory): `"Kőbánya alsó vasútállomás"` → `"Kobanya also vasutallomas"`; `"Astoria M ► Budakeszi"` → `"Astoria M > Budakeszi"`; `"Törökbálint, Márta utca"` → `"Torokbalint, Marta utca"`; `"🚌"` → `""`.
  - `DeparturesLayoutTests.Serializes_two_scrolling_rows`: `Build(new("1. 922 17'", "Astoria M > Budakeszi"))` serialized with `BarJson.Options` deep-equals `{application_name: "busynet", elements: [{type:"text", id:"header", x:1, y:-2, display:"front", text:"1. 922 17'", font:"normal", width:71, scroll_rate:1200}, {type:"text", id:"destination", x:1, y:7, display:"front", text:"Astoria M > Budakeszi", font:"small", width:71, scroll_rate:1200}]}`.
  - `Empty_destination_still_renders`: `Build(new("1. 40", ""))` destination text is `"?"` (a `TextElement` needs one character, as in `WorkflowLayout.Row`).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Busynet.Bar.Tests -c Release`
Expected: build error — `Transliterate`, `DeparturesLayout` not defined.

- [ ] **Step 3: Implement** the three members above. `Build` runs both texts through `DisplayText.ToDisplayableAscii` (the caller has already transliterated).

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Busynet.Bar.Tests -c Release`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Busynet.Bar tests/Busynet.Bar.Tests
git commit -m "Add departures layout and accent transliteration"
```

---

### Task 3: CLI — labels and rotation state

**Files:**
- Create: `src/Busynet.Cli/Features/BkkDepartures/DepartureLabels.cs`
- Create: `src/Busynet.Cli/Features/BkkDepartures/DeparturesState.cs`
- Modify: `src/Busynet.Cli/Busynet.Cli.csproj` (reference `../Busynet.Bkk/Busynet.Bkk.csproj`)
- Test: `tests/Busynet.Cli.Tests/Features/BkkDepartures/DepartureLabelsTests.cs`, `.../DeparturesStateTests.cs`

**Interfaces:**
- Consumes: `Departure`, `StopDepartures` (Task 1); `DepartureFrame`, `DisplayText.Transliterate` (Task 2).
- Produces:
  - `public static string DepartureLabels.Time(Departure d, DateTimeOffset now, TimeZoneInfo zone)` — `m = floor((d.When - now).TotalMinutes)`; `m < 60` → `$"{max(m,0)}'"`, else `HH:mm` of `d.When` in `zone` (invariant culture); prefixed `~` when `!d.IsLive`.
  - `public static string DepartureLabels.Header(int position, Departure d, DateTimeOffset now, TimeZoneInfo zone)` → `$"{position}. {d.Line} {Time(...)}"`.
  - `public sealed record DeparturesState(StopDepartures? Snapshot, int Index)` with `WithSnapshot(StopDepartures s)` (keeps `Index`), `Advance()` (`Index + 1`), `DepartureFrame? Render(DateTimeOffset now, TimeZoneInfo zone)`.
  - `public const int Shown = 3;`
- Render rule: `null` before a snapshot. `upcoming = Snapshot.Departures.Where(d => d.When >= now).Take(Shown)`. Empty → `new("no departures", Transliterate(StopName))`. Else `i = Index % upcoming.Count` → `new(Header(i + 1, upcoming[i], now, zone), Transliterate(upcoming[i].Headsign))`.

Tests use a fixed zone `TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(1), "test", "test")` and `now = 2026-11-14T22:47:00Z`.

- [ ] **Step 1: Write failing tests**
  - `Shows_whole_minutes_for_live_departures`: live, `When = now + 17m30s` → `"17'"`; `now + 30s` → `"0'"`.
  - `Marks_scheduled_departures`: scheduled-only `now + 17m` → `"~17'"`.
  - `Switches_to_clock_time_at_one_hour` (Review Focus 4): live `now + 59m59s` → `"59'"`; live `now + 60m` → `"00:47"` (23:47 UTC+1 rolls to 00:47); scheduled `now + 77m` (`2026-11-15T00:04Z`) → `"~01:04"`.
  - `Header_numbers_from_one`: `Header(1, live 922 at now+17m, ...)` → `"1. 922 17'"`.
  - `Renders_nothing_before_the_first_poll`: `new DeparturesState(null, 0).Render(...)` is `null`.
  - `Rotates_through_the_first_three`: snapshot of 5 departures; `Index` 0,1,2,3 render headers starting `"1. "`, `"2. "`, `"3. "`, `"1. "`; destination of the `"Astoria M ► Budakeszi"` entry is `"Astoria M > Budakeszi"`.
  - `Rotates_through_fewer_than_three`: 2 departures; `Index` 2 → header starts `"1. "`.
  - `No_departures_shows_the_stop_name`: empty list, `StopName "Kötő utca"` → `new DepartureFrame("no departures", "Koto utca")`.
  - `Departed_entries_drop_out_before_the_next_poll` (Review Focus 1): departures at `now - 1s`, `now + 5m`, `now + 9m`; `Index 0` → header `"1. ... 5'"`.
  - `Index_wraps_when_the_list_shrinks` (Review Focus 2): `new DeparturesState(threeItems, 2).WithSnapshot(oneItem).Render(...)` → header starts `"1. "`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Busynet.Cli.Tests -c Release --filter-class "*BkkDepartures*"`
Expected: build error — types not defined. (If the filter syntax differs for the xunit.v3 runner, use `-- --filter-class`.)

- [ ] **Step 3: Implement** `DepartureLabels` and `DeparturesState` per the signatures and render rule above.

- [ ] **Step 4: Run tests** — same command, all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Busynet.Cli tests/Busynet.Cli.Tests
git commit -m "Add departures labels and rotation state"
```

---

### Task 4: CLI — the watch (poll + rotation)

**Files:**
- Create: `src/Busynet.Cli/Features/BkkDepartures/DeparturesDisplay.cs`
- Create: `src/Busynet.Cli/Features/BkkDepartures/BkkDeparturesWatch.cs`
- Create: `tests/Busynet.Cli.Tests/Fakes/ScriptedDepartures.cs`
- Test: `tests/Busynet.Cli.Tests/Features/BkkDepartures/BkkDeparturesWatchTests.cs`

**Interfaces:**
- Consumes: `IBkkDepartures`, `StopDepartures` (Task 1); `DeparturesLayout`, `DepartureFrame` (Task 2); `DeparturesState` (Task 3); `Poller.Poll`, `DisplayWatch.RunAsync`, `IDisplay<T>` (Core).
- Produces:
  - `public sealed class DeparturesDisplay(IBarDisplay bar) : IDisplay<DepartureFrame>` — like `WorkflowDisplay` without icons.
  - `public static Task BkkDeparturesWatch.RunAsync(Func<CancellationToken, Task<StopDepartures>> fetch, IDisplay<DepartureFrame> display, TimeSpan interval, TimeSpan rotate, TimeZoneInfo zone, IScheduler scheduler, ILogger logger, CancellationToken cancellationToken)` (wrap in `#pragma warning disable S107` as `ClaudeLimitsWatch` does).
  - Pipeline: `Poller.Poll(fetch, interval, initialDelay: TimeSpan.Zero, scheduler, logger)` → `s => s.WithSnapshot(x)`, merged with `Observable.Interval(rotate, scheduler)` → `s => s.Advance()`; `DisplayWatch.RunAsync(new DeparturesState(null, 0), merged, s => s.Render(scheduler.Now, zone), display, logger, ct)`.
  - `ScriptedDepartures : IBkkDepartures`: queue of `Func<StopDepartures>` (a func may throw), last one repeating, counting `Polls` and recording each `(ApiKey, StopId)` in `Calls` — modeled on `Fakes/ScriptedUsage.cs`. Watch tests pass `h.Departures.GetDeparturesAsync` bound to fixed key/stop as `fetch`; Task 5 registers it as `IBkkDepartures`.

Tests follow `GitHubWorkflowWatchTests`' `Harness` (`TestScheduler`, `FakeDisplay<DepartureFrame>`, `RunTo` with slack); interval 30 s, rotate 5 s, UTC+1 test zone, departures built relative to `scheduler` start (`DateTimeOffset.UnixEpoch` for `TestScheduler`).

- [ ] **Step 1: Write failing tests**
  - `Polls_at_once_and_draws_the_first_departure`: `RunTo(0)` → 1 poll; calls `[Clear, frame "1. ..."]`.
  - `Rotates_every_rotate_interval`: 3 departures; `RunTo(15s)` → headers start `"1. "`, `"2. "`, `"3. "`, `"1. "`; still 1 poll.
  - `Polls_every_interval_without_restarting_rotation`: `RunTo(30s)` → 2 polls; the frame drawn at 30 s is position `(30/5) % 3 + 1 = 1` of the new list.
  - `Minutes_count_down_between_polls`: a single live departure at `+10m`, interval 1 h; after `RunTo(5m)` the last drawn header ends `"5'"` and the first ended `"10'"`.
  - `Transient_failure_keeps_rotating_the_last_list`: poll 1 ok, poll 2 throws `BkkTransientException` → frames keep rotating after 30 s; the watch is still running.
  - `Fatal_failure_ends_the_watch_after_clearing`: poll 1 throws `new BkkException("Unknown BKK stop id: X.")` → task faults with that exception; last call is `Clear`.

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/Busynet.Cli.Tests -c Release`, build error.

- [ ] **Step 3: Implement** `DeparturesDisplay`, `BkkDeparturesWatch`, `ScriptedDepartures`.

- [ ] **Step 4: Run tests** — all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Busynet.Cli tests/Busynet.Cli.Tests
git commit -m "Add departures watch with rotation"
```

---

### Task 5: CLI — command, options, wiring, docs, on-bar check

**Files:**
- Create: `src/Busynet.Cli/Features/BkkDepartures/BkkDeparturesSettings.cs`
- Create: `src/Busynet.Cli/Features/BkkDepartures/BkkDeparturesOptions.cs`
- Create: `src/Busynet.Cli/Features/BkkDepartures/BkkDeparturesCommand.cs`
- Modify: `src/Busynet.Cli/BusynetApp.cs` (`BuildRoot`: add group `bkk`, description `"BKK (Budapest public transport) on the bar."`)
- Modify: `src/Busynet.Cli/Infrastructure/ServiceRegistration.cs` (`services.AddBkk()`, `services.AddTransient<BkkDeparturesCommand>()`, `services.TryAddSingleton(TimeZoneInfo.Local)`)
- Modify: `README.md` (intro command list, Configuration table row `BKK API key | — | BKK_API_KEY | none (required for bkk)`, Usage section)
- Modify: `docs/architecture.md` (§2 projects table + flowchart node for `Busynet.Bkk`; new §11 `bkk departures`, same shape as §10)
- Test: `tests/Busynet.Cli.Tests/BkkDeparturesCommandTests.cs` (uses `ScriptedDepartures` from Task 4)

**Interfaces:**
- Consumes: everything above; `BarOptions`, `BarSettings`, `BarConnector`, `ConfigurationException`, `Verbosity`.
- Produces:
  - `public sealed record BkkDeparturesSettings(string? Host, string? Token, bool Verbose, string StopId, int Interval, int Rotate) : BarSettings(Host, Token, Verbose)` with `DefaultStopId = "BKK_F04808"`, `DefaultInterval = 30`, `DefaultRotate = 5`, `ApiKeyVariable = "BKK_API_KEY"`.
  - `BkkDeparturesOptions` modeled on `GitHubWorkflowOptions`: `Argument<string> StopId` named `STOP_ID`, `Arity = ArgumentArity.ZeroOrOne`, default `"BKK_F04808"`, description `"BKK stop id, e.g. BKK_F04808 (default: Kötő utca towards Budapest)."`, validator "STOP_ID must not be empty."; `--interval` ("Seconds between polls.") and `--rotate` ("Seconds each departure is shown.") with ≥ 1 validators as in `GitHubWorkflowOptions`.
  - `BkkDeparturesCommand(IBarFactory bars, IBkkDepartures departures, IScheduler scheduler, Func<string, string?> environment, TimeZoneInfo zone, ILogger<BkkDeparturesCommand> logger)`; `Description = "Rotate the next 3 departures from a BKK stop on the bar until Ctrl+C."`; `Define(provider)` creates command `"departures"`. `ExecuteAsync`: read `BKK_API_KEY` first — null/whitespace → `ConfigurationException("BKK_API_KEY is not set. Get a key at https://opendata.bkk.hu.")` before the bar is touched; then `BarConnector.Connect`, then `BkkDeparturesWatch.RunAsync(ct => departures.GetDeparturesAsync(key, settings.StopId, ct), new DeparturesDisplay(display), ...)`; same cancellation catch as `GitHubWorkflowCommand`.

- [ ] **Step 1: Write failing tests** in `BkkDeparturesCommandTests.cs` (pattern: `GitHubWorkflowCommandTests`, `AppHarness` with environment `{ "BKK_API_KEY": "SECRET-KEY" }`, fake `IBarFactory` and `IBkkDepartures` registered over the real ones, and a fixed `TimeZoneInfo` registered):
  - `Clears_draws_then_clears_on_cancel`: `bkk departures` → exit 0, empty stderr, calls `[Clear, DisplayElements{header "1. 922 …", destination "Astoria M > Budakeszi"}, …, Clear]`.
  - `Defaults_to_kotu_utca`: no argument → fake saw stop id `"BKK_F04808"` and key `"SECRET-KEY"`.
  - `Stop_id_argument_overrides_the_default`: `bkk departures BKK_F04807` → fake saw `"BKK_F04807"`.
  - `Missing_api_key_exits_1_before_touching_the_bar`: empty environment → exit 1, stderr `"BKK_API_KEY is not set. Get a key at https://opendata.bkk.hu.\n"`, no bar calls.
  - `Blank_stop_id_is_a_usage_error` and `Rotate_below_one_is_a_usage_error` (theory: `--rotate 0`, `--interval 0`): exit 2, fake never called.
  - `Unknown_stop_exits_1_with_its_message`: fake throws `new BkkException("Unknown BKK stop id: BKK_NOPE.")` → exit 1, stderr is that line, bar cleared.
  - Add `bkk departures --help` lists `STOP_ID`, `--interval`, `--rotate` to `RootCommandTests` if that file checks help per command.

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/Busynet.Cli.Tests -c Release`, build error / unknown command.

- [ ] **Step 3: Implement** settings, options, command, registration and the `bkk` group.

- [ ] **Step 4: Run the full suite and the warnings-as-errors build**

Run: `dotnet build -c Release -warnaserror && dotnet test -c Release`
Expected: build succeeds with 0 warnings; all tests pass.

- [ ] **Step 5: Update README and `docs/architecture.md`** per the Files list. README usage block:

```sh
busynet bkk departures [STOP_ID] [--interval <seconds>] [--rotate <seconds>]
                       [--host <host>] [--token <token>] [--verbose]
```

followed by a paragraph covering: default stop, 3 departures rotating with `1.`–`3.`, minutes vs. clock time after an hour, `~` = timetable only, `BKK_API_KEY` required (example `BKK_API_KEY=$(op read "op://Private/BKK OpenData API key/API key") busynet bkk departures`), Ctrl+C clears.

- [ ] **Step 6: Check on the real bar** (manual; ask the user if the bar is not reachable)

Run: `BKK_API_KEY=$(op read "op://Private/BKK OpenData API key/API key") dotnet run --project src/Busynet.Cli -c Release -- bkk departures --verbose`
Expected: frames `1.`–`3.` rotate every 5 s. Check that the header (`3. 922B ~00:04` is the widest likely case) fits 71 px in `normal` font; if not, switch `HeaderFont` to `condensed` (used by `LimitsLayout`) and update the Task 2 layout test. Check that a long destination scrolls far enough in 5 s to be read; if not, raise `DefaultRotate` and its test/README mentions. Ctrl+C clears the bar.

- [ ] **Step 7: Commit**

```bash
git add src/Busynet.Cli tests/Busynet.Cli.Tests README.md docs/architecture.md
git commit -m "Add bkk departures command"
```
