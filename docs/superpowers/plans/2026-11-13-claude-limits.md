# busynet foundation + `claude limits` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 11 global tool `busynet` whose `claude limits` command shows Claude Code's 5-hour / weekly usage on a BUSY Bar, built on reusable components that later `text`, `clear` and `gh workflow` PRs reuse.

**Architecture:** Four projects. `Busynet.Core` holds the exception contract (`ITransientFailure`), a reactive poller, the generic `DisplayWatch` (clear-first, merge inputs, `Scan` state, render, skip-identical, serialised draw, clear-last) and a process runner. `Busynet.Bar` holds everything BUSY Bar (Refit HTTP API, display models, limits layout, protobuf button stream over WebSocket). `Busynet.Claude` holds credentials and the usage API. `Busynet.Cli` is the Spectre.Console.Cli host plus the `Features/ClaudeLimits` slice, the only code that combines a data source with the bar.

**Tech Stack:** .NET 11 / C#, Spectre.Console.Cli 0.57.2, Refit 16.4.0 (+ Refit.HttpClientFactory), System.Reactive 7.0.0, Google.Protobuf 3.36.2 + Grpc.Tools 2.85.0, Microsoft.Extensions.{DependencyInjection,Http,Logging.Console} 11.0.0, xunit.v3 4.0.3, Microsoft.Reactive.Testing 7.0.0, Microsoft.Extensions.TimeProvider.Testing 11.0.0, coverlet.collector 10.1.0.

**Spec:** `docs/superpowers/specs/2026-11-13-claude-limits-design.md` — read it first. Where both are silent, busyboy's behaviour (`../busyboy`, its `CLAUDE.md`) wins.

## Global Constraints

- Target `net11.0`; `global.json` pins SDK `11.0.100`, `rollForward: latestFeature`.
- `Nullable` enabled, `TreatWarningsAsErrors` true, central package management (`Directory.Packages.props`), `RestorePackagesWithLockFile` true (lock files committed).
- Work on branch `claude-limits` **in the main clone — no git worktrees** (user's explicit instruction for this repo).
- Never put a bar token or Claude token in a log line, exception message, or `ToString()`. The bar WebSocket URL carries the token in its query string — never log that URL. Disable `IHttpClientFactory`'s built-in request logging (`ConfigureHttpClientDefaults(b => b.RemoveAllLoggers())`).
- Application name on every draw/clear: `"busynet"`.
- Env vars: `BUSYBOY_HOST` (default `10.0.4.20`), `BUSYBOY_TOKEN` (optional); `--host` / `--token` override.
- CLI contract: success silent, exit 0; `BusynetException` → its `Message` as one stderr line, exit 1; parse/validation error → exit 2; `--verbose` → Debug logs to stderr and full exception (`ToString()`) instead of the one line.
- No NuGet publish. Tool package id and command name: `busynet`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; PR body ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- GitHub Actions are pinned to commit SHAs with a version comment (busyboy convention), e.g. `actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1`.

## Review Focus

1. **A BUSY work session owns the display at startup** (bar answers `409` to draw) → command keeps running and draws once the display frees up; it does not exit. Pinned in Task 13 (`First_draw_rejected_with_409_is_retried_on_next_update`) and Task 14 (`Startup_succeeds_when_first_draw_is_rejected`).
2. **`--verbose` with a configured bar token and Claude token** → neither token appears anywhere in stderr or logs, including when the WebSocket fails to connect. Pinned in Task 14 (`Verbose_output_never_contains_tokens`).
3. **Laptop sleeps / wifi drops mid-watch** → poll and draw failures are swallowed, the button reconnects with back-off, the watch keeps running. Pinned in Tasks 3, 4 and 10.
4. **`--host` given with a scheme or port** (`http://bar.local`, `10.0.4.20:8080`, `https://…`) → works; WebSocket scheme follows (`ws`/`wss`). Pinned in Task 6 (`Host_forms_map_to_http_and_ws_uris`).
5. **Ctrl+C while the startup fetch is still in flight** → exits 0 without a traceback and without drawing. Pinned in Task 14 (`Cancel_during_startup_exits_zero`).

---

### Task 0: Publish the repository skeleton on `main`

**Files:**
- Create: `.gitignore` (`dotnet new gitignore`, plus `nupkg/`, `tool/`, `TestResults/`)
- Create: `LICENSE` (MIT, `Copyright (c) 2026 molnarbence.dev`, same text as `../busyboy/LICENSE`)
- Create: `README.md` (one paragraph: what busynet is, "port of busyboy, work in progress")
- Existing: `docs/superpowers/specs/2026-11-13-claude-limits-design.md`, `docs/superpowers/plans/2026-11-13-claude-limits.md`

- [ ] **Step 1:** `git init -b main` in `/Users/bence/repos/github.com/mb-dot-dev/busynet`; add the files above; commit `docs: add claude limits design and plan`.
- [ ] **Step 2:** `gh repo create mb-dot-dev/busynet --public --source . --remote origin --description "Display information on a BUSY Bar (.NET)"`
- [ ] **Step 3:** `git push -u origin main`. Verify: `gh repo view mb-dot-dev/busynet --json visibility,defaultBranchRef` → `"PUBLIC"`, `main`.
- [ ] **Step 4:** `git switch -c claude-limits`. All later tasks commit here.

### Task 1: Solution skeleton, Core exception contract, CI

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `Busynet.slnx`
- Create: `src/Busynet.Core/Busynet.Core.csproj`, `src/Busynet.Core/BusynetException.cs`, `src/Busynet.Core/ITransientFailure.cs`
- Create: `tests/Busynet.Core.Tests/Busynet.Core.Tests.csproj`, `tests/Busynet.Core.Tests/TransientFailureTests.cs`
- Create: `tests/Busynet.Testing/Busynet.Testing.csproj` (class library shared by test projects; empty for now)
- Create: `.github/workflows/ci.yaml`

**Interfaces:**
- Produces: `abstract class BusynetException(string message) : Exception(message)`; `interface ITransientFailure { TimeSpan? RetryAfter { get; } }`; `static class TransientFailure { bool IsTransient(Exception e, out TimeSpan? retryAfter); }` (true iff `e is ITransientFailure`).

- [ ] **Step 1:** Write `Directory.Build.props` (`TargetFramework net11.0`, `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`, `EnforceCodeStyleInBuild true`, `RestorePackagesWithLockFile true`) and `Directory.Packages.props` with every version from the Tech Stack line. Test projects reference `xunit.v3`, `xunit.runner.visualstudio` 4.0.1, `Microsoft.NET.Test.Sdk` 18.12.0, `coverlet.collector`.
- [ ] **Step 2: Failing test** `TransientFailureTests.IsTransient_reads_retry_after_from_marked_exceptions`: a test-local `class Flaky : BusynetException, ITransientFailure` with `RetryAfter = 5s` → `IsTransient` true, `retryAfter == 5s`; a plain `InvalidOperationException` → false, `null`.
- [ ] **Step 3:** `dotnet test` → FAIL (types missing). Implement. `dotnet test` → PASS.
- [ ] **Step 4:** `ci.yaml` per spec §CI: triggers `push` to `main`, `pull_request` to `main`, `workflow_dispatch`; concurrency `${{ github.workflow }}-${{ github.ref }}` cancel-in-progress; matrix `[ubuntu-latest, macos-latest]`; `actions/setup-dotnet` with `global-json-file: global.json` and NuGet cache keyed on `**/packages.lock.json`; `dotnet restore --locked-mode`; `dotnet build -c Release --no-restore -warnaserror`; `dotnet test -c Release --no-build --collect:"XPlat Code Coverage" --results-directory TestResults`; upload `TestResults` artifact (name includes matrix os). The pack/install smoke job is added in Task 15.
- [ ] **Step 5:** Commit `build: add solution skeleton, core exception contract and CI`.

### Task 2: Process runner (Core)

**Files:**
- Create: `src/Busynet.Core/Processes/IProcessRunner.cs`, `ProcessRunner.cs`, `ProcessOutcome.cs`, `ProcessRunFailedException.cs`
- Test: `tests/Busynet.Core.Tests/Processes/ProcessRunnerTests.cs`

**Interfaces:**
- Produces: `interface IProcessRunner { Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken); }`; `sealed record ProcessOutcome(int ExitCode, string StandardOutput)`; `sealed class ProcessRunFailedException(string message) : Exception(message)` — thrown when the process cannot start or exceeds `timeout` (process killed). Stderr is always discarded, never captured into a message.

- [ ] **Step 1: Failing tests** (Unix-only; mark `[Trait("os","unix")]` and skip on Windows via `Assert.Skip` when `OperatingSystem.IsWindows()`):
  - `Captures_stdout_and_exit_code`: `sh -c "printf hi; exit 44"` → `ExitCode 44`, `StandardOutput "hi"`.
  - `Stderr_is_not_captured`: `sh -c "echo secret 1>&2"` → `StandardOutput ""`.
  - `Timeout_throws_ProcessRunFailed`: `sh -c "sleep 5"`, timeout 200 ms → throws within 2 s.
  - `Missing_executable_throws_ProcessRunFailed`: `definitely-not-a-binary-xyz` → throws `ProcessRunFailedException`.
- [ ] **Step 2:** Run → FAIL. Implement with the .NET 11 `Process` APIs (load the `dotnet11:process-api-net11` skill first). Run → PASS.
- [ ] **Step 3:** Commit `feat(core): add process runner`.

### Task 3: Poller (Core)

**Files:**
- Create: `src/Busynet.Core/Watching/Poller.cs`
- Test: `tests/Busynet.Core.Tests/Watching/PollerTests.cs`

**Interfaces:**
- Produces: `static class Poller { IObservable<T> Poll<T>(Func<CancellationToken, Task<T>> fetch, TimeSpan interval, TimeSpan initialDelay, IScheduler scheduler, ILogger logger); }`

Behaviour: wait `initialDelay`, fetch, emit the value, wait `interval`, repeat. Fetches never overlap; the next wait starts when the previous fetch finishes. An exception implementing `ITransientFailure` emits nothing, is logged at Debug (`"Poll failed, keeping the display as it is: {Error}"`, message only), and the next wait is `max(interval, RetryAfter ?? interval)`. Any other exception terminates the sequence with `OnError`. Never completes on its own. Build it from pure Rx (`Observable.Timer(delay, scheduler)` + `Observable.FromAsync` + `Catch` + `Defer` recursion), not `async` loops, so `TestScheduler` drives it deterministically.

- [ ] **Step 1: Failing tests** (use `TestScheduler`, fetch fakes return completed tasks and record call times):
  - `First_fetch_happens_after_initial_delay`: initialDelay 180 s → no call at 179 s, one at 180 s.
  - `Fetches_repeat_every_interval`: interval 180 s, initialDelay 0 → calls at 0, 180, 360 s; values emitted in order.
  - `Transient_failure_is_swallowed_and_polling_continues`: second fetch throws transient (no RetryAfter) → no OnError, third call at +180 s.
  - `Retry_after_longer_than_interval_extends_the_wait`: RetryAfter 600 s, interval 180 s → next call 600 s later.
  - `Retry_after_shorter_than_interval_is_ignored`: RetryAfter 10 s → next call 180 s later.
  - `Fatal_failure_errors_the_sequence`: fetch throws a non-transient `BusynetException` → `OnError` with that exception, no further calls.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(core): add reactive poller with retry-after back-off`.

### Task 4: DisplayWatch (Core)

**Files:**
- Create: `src/Busynet.Core/Watching/IDisplay.cs`, `DisplayWatch.cs`
- Test: `tests/Busynet.Core.Tests/Watching/DisplayWatchTests.cs`, `tests/Busynet.Testing/FakeDisplay.cs`

**Interfaces:**
- Produces:
  - `interface IDisplay<in TView> { Task DrawAsync(TView view, CancellationToken cancellationToken); Task ClearAsync(CancellationToken cancellationToken); }`
  - `static class DisplayWatch { Task RunAsync<TState, TView>(TState initial, IObservable<Func<TState, TState>> updates, Func<TState, TView?> render, IDisplay<TView> display, ILogger logger, CancellationToken cancellationToken) where TView : class, IEquatable<TView>; }`
  - `Busynet.Testing.FakeDisplay<TView> : IDisplay<TView>` — records `Calls` (`"clear"` / the drawn view), lets a test make the next N draws throw a given exception, and fires an optional `OnDraw` callback.

Behaviour (spec §Core "Watch pipeline"):
1. `await display.ClearAsync(ct)` — any exception propagates (startup failure).
2. `updates.StartWith(s => s).Scan(initial, (s, f) => f(s)).Select(render)`, skip `null`, then for each view, **sequentially** (`Select(v => Observable.FromAsync(...)).Concat()`): skip if equal to the last **successfully** drawn view; otherwise draw; on success remember it; on an `ITransientFailure` log at Debug and keep the old "last drawn".
3. Run until `ct` is cancelled (return normally) or `updates` errors (rethrow that exception after step 4).
4. Always `ClearAsync(CancellationToken.None)` with a 5 s timeout on the way out; an exception from that clear is logged at Debug and never replaces the original exception.

- [ ] **Step 1: Failing tests** (`Subject<Func<int,int>>` as updates, `render = s => new View(s.ToString())` where `View` is a test record):
  - `Clears_before_first_draw_and_draws_initial_state` → calls `["clear", View("0")]`.
  - `Identical_render_is_not_redrawn`: push `s => s` twice → only one draw.
  - `Changed_render_is_drawn`: push `s => s + 1` → draws `0`, `1`.
  - `First_draw_rejected_with_409_is_retried_on_next_update`: first draw throws a transient exception; push `s => s` → `View("0")` drawn again (2 draw attempts).
  - `Null_render_draws_nothing`.
  - `Cancellation_clears_and_returns`: cancel → task completes successfully, last call `"clear"`.
  - `Fatal_update_error_clears_then_rethrows`: `updates.OnError(new FatalTest())` → task faults with `FatalTest`, last call `"clear"`.
  - `Clear_failure_on_exit_does_not_mask_original_error`: exit clear throws transient → task still faults with `FatalTest`.
  - `Startup_clear_failure_propagates_and_nothing_is_drawn`.
  - `Draws_never_overlap`: a draw that completes via a `TaskCompletionSource`; push two updates while it is pending → second draw starts only after the first completes.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(core): add display watch pipeline`.

### Task 5: Shared HTTP test router

**Files:**
- Create: `tests/Busynet.Testing/FakeHttpHandler.cs`
- Test: `tests/Busynet.Core.Tests/Testing/FakeHttpHandlerTests.cs` (sanity only)

**Interfaces:**
- Produces: `sealed class FakeHttpHandler : HttpMessageHandler` with `Add(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond)`, convenience `Add(HttpMethod, string path, HttpStatusCode status, string? json = null, IDictionary<string,string>? headers = null)`, `Throw(HttpMethod, string path, Exception e)`, and `IReadOnlyList<RecordedRequest> Calls` where `RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string,string> Headers, string? Body)`. Matching is by method + `Uri.AbsolutePath`, ignoring host and query (the busyboy `bar_transport` rule). An unregistered route throws `Xunit.Sdk.XunitException("Unregistered route: …")`. Routes registered more than once respond in order, the last one repeating.

- [ ] **Step 1:** Failing tests `Unregistered_route_fails_the_test`, `Records_body_and_headers`, `Sequential_registrations_respond_in_order`. Run → FAIL. Implement. Run → PASS.
- [ ] **Step 2:** Commit `test: add fake HTTP router`.

### Task 6: Bar connection settings

**Files:**
- Create: `src/Busynet.Bar/Busynet.Bar.csproj` (refs Core), `src/Busynet.Bar/BarConnection.cs`
- Test: `tests/Busynet.Bar.Tests/Busynet.Bar.Tests.csproj`, `tests/Busynet.Bar.Tests/BarConnectionTests.cs`

**Interfaces:**
- Produces: `sealed record BarConnection(Uri HttpBaseAddress, Uri StatusSocketUri, string? Token)` with `static BarConnection Resolve(string? hostOption, string? tokenOption, Func<string, string?> environment)`; constants `DefaultHost = "10.0.4.20"`, `HostVariable = "BUSYBOY_HOST"`, `TokenVariable = "BUSYBOY_TOKEN"`. Override `ToString()` / `PrintMembers` so `Token` renders as `***` when set. `StatusSocketUri` = base with scheme `ws`/`wss` and path `/api/status/ws`, **without** the token (the socket adds it).

- [ ] **Step 1: Failing tests:**
  - `Option_beats_environment_beats_default` (host and token separately).
  - `Blank_token_is_treated_as_absent` (`""` and `"  "` → `null`).
  - `Host_forms_map_to_http_and_ws_uris` (theory): `10.0.4.20` → `http://10.0.4.20/`, `ws://10.0.4.20/api/status/ws`; `10.0.4.20:8080` → `http://10.0.4.20:8080/`; `http://bar.local` → `http://bar.local/`; `https://bar.example` → `https://bar.example/`, `wss://bar.example/api/status/ws`.
  - `ToString_never_contains_token`.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(bar): add connection settings`.

### Task 7: Display models, JSON, limits layout (pure)

**Files:**
- Create: `src/Busynet.Bar/Display/DisplayElements.cs`, `DisplayElement.cs` (+ `TextElement`, `RectangleElement`), `BarColor.cs`, `DisplayText.cs`, `BarJson.cs`
- Create: `src/Busynet.Bar/Layouts/LimitGauge.cs`, `LimitsLayout.cs`
- Test: `tests/Busynet.Bar.Tests/Display/SerializationTests.cs`, `BarColorTests.cs`, `DisplayTextTests.cs`, `tests/Busynet.Bar.Tests/Layouts/LimitsLayoutTests.cs`

**Interfaces:**
- Produces:
  - `sealed record DisplayElements(string ApplicationName, IReadOnlyList<DisplayElement> Elements)`; `const string BarApplication.Name = "busynet"`.
  - `abstract record DisplayElement(string Id, int X, int Y, string Display = "front")` with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]`, `[JsonDerivedType(typeof(TextElement), "text")]`, `[JsonDerivedType(typeof(RectangleElement), "rectangle")]`.
  - `sealed record TextElement(string Id, string Text, string Font, int X, int Y, string? Color = null) : DisplayElement` — ctor throws `ArgumentException` if `Text` is empty or not `^[\x20-\x7E]+$`.
  - `sealed record RectangleElement(string Id, int X, int Y, int Width, int Height, IReadOnlyList<string> FillColors, string Fill = "solid", int BorderWidth = 0) : DisplayElement` — `BorderWidth` 0 deliberately (vendor default 1 draws a white outline).
  - `static class BarColor { string Normalize(string color); }` — `#RRGGBB` → `#RRGGBBFF`, `#rrggbbaa` → upper-case; anything else → `ArgumentException`.
  - `static class DisplayText { string ToDisplayableAscii(string text); }` — each char outside `0x20..0x7E` → `?`.
  - `static class BarJson { JsonSerializerOptions Options { get; } }` — `JsonNamingPolicy.SnakeCaseLower`, `DefaultIgnoreCondition = WhenWritingNull`.
  - `sealed record LimitGauge(string Label, int FillWidth, bool High)` with `static LimitGauge From(string label, double percent)`.
  - `static class LimitsLayout { DisplayElements Build(LimitGauge gauge); }`

Because `DisplayElements` holds a list, record equality is reference-based; **`LimitGauge` is the value compared for redraws** — two gauges are equal iff their payloads are.

Layout values (spec §Bar "Limits layout"): track `limit-track` x=1 y=8 w=70 h=8 `#1F1F1FFF`; fill `limit-fill` x=1 y=8 h=8, width `max(FillWidth, 1)`, colour `#1F1F1FFF` when `FillWidth == 0`, else `#B3261EFF` when `High`, else `#CD6E58FF`; label `limit-label` font `condensed` x=1 y=-2 colour `#E6E6E6FF`, text `DisplayText.ToDisplayableAscii(Label)`. Element order: track, fill, label. `FillWidth = clamp((int)Math.Round(percent / 100 * 70), 0, 70)` (default banker's rounding, as Python). `High = percent > 85`.

- [ ] **Step 1: Failing tests:**
  - `Limits_payload_serializes_to_bar_json`: `LimitsLayout.Build(LimitGauge.From("5h 85% 3h49m", 85))` serialised with `BarJson.Options` equals (JSON-semantic comparison via `JsonNode.DeepEquals`) the expected object: `application_name "busynet"`, three elements with `type`, `id`, `x`, `y`, `display "front"`, rectangle `width`/`height`/`fill "solid"`/`fill_colors`/`border_width 0`, text `text`/`font`/`color`; no `null` fields.
  - `Fill_width_is_proportional_and_clamped` (theory): 0→0, 50→35, 100→70, 150→70, -5→0, 0.5→0.
  - `An_empty_bar_still_draws_a_track_coloured_1px_fill` (percent 0 and 0.4).
  - `Above_85_percent_uses_the_high_colour` (85 → normal, 85.1 → high).
  - `Gauges_with_same_label_width_and_colour_are_equal` (`From("x", 50.0) == From("x", 50.2)`).
  - `Color_normalize` theory incl. invalid `"red"` → throws.
  - `TextElement_rejects_non_ascii_and_empty`; `ToDisplayableAscii("é ok") == "? ok"`.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(bar): add display models and limits layout`.

### Task 8: Bar HTTP client (Refit), errors, factory

**Files:**
- Create: `src/Busynet.Bar/Http/IBusyBarApi.cs`, `BarDisplay.cs`, `IBarDisplay.cs`
- Create: `src/Busynet.Bar/BarException.cs` (`BarException`, `BarApiException`, `BarRequestException`)
- Create: `src/Busynet.Bar/IBarFactory.cs`, `BarFactory.cs`, `BarServiceCollectionExtensions.cs`
- Create: `docs/references/openapi.yaml` — vendored copy of `http://busybar.infra.home/openapi.yaml` (device API 27.15.0; `curl -s` it)
- Test: `tests/Busynet.Bar.Tests/Http/BarDisplayTests.cs`

**Interfaces:**
- Consumes: `BarConnection` (Task 6), `DisplayElements`, `BarJson`, `BarApplication.Name` (Task 7), `ITransientFailure`, `BusynetException` (Task 1).
- Produces:
  - `interface IBusyBarApi { [Post("/api/display/draw")] Task<HttpResponseMessage> DrawAsync([Body] DisplayElements payload, CancellationToken ct); [Delete("/api/display/draw")] Task<HttpResponseMessage> ClearAsync([AliasAs("application_name")] string applicationName, CancellationToken ct); }`
  - `interface IBarDisplay { Task DrawAsync(DisplayElements payload, CancellationToken ct); Task ClearAsync(CancellationToken ct); }` — clear always scoped to `BarApplication.Name`.
  - `abstract class BarException : BusynetException, ITransientFailure` (`RetryAfter => null`); `BarApiException(int StatusCode, string Method, string Path, string Error, string? ErrorCode)` with `Message = "HTTP {status} | {METHOD} {path} | {error}[ | code={error_code}]"` (body's `error`/`error_code`, `error` falls back to the reason phrase when the body isn't the Error schema); `BarRequestException(string Method, string Path, string Detail)` with `Message = "request error | {METHOD} {path} | {Detail}"`, where `Detail` is the exception **type name** only.
  - `interface IBarFactory { IBarDisplay CreateDisplay(BarConnection connection); IObservable<ButtonEvent> CreateButtonEvents(BarConnection connection); }` (`CreateButtonEvents` lands in Task 10; leave it throwing `NotImplementedException` here.)
  - `static IServiceCollection AddBusyBar(this IServiceCollection services)` — registers named `HttpClient` `"busybar"` (timeout 10 s) and `IBarFactory`. `CreateDisplay` takes a client from `IHttpClientFactory.CreateClient("busybar")`, sets `BaseAddress = connection.HttpBaseAddress`, adds `X-API-Token` to `DefaultRequestHeaders` only when `Token` is non-null, and builds `RestService.For<IBusyBarApi>(client, new RefitSettings(new SystemTextJsonContentSerializer(BarJson.Options)))`.

Error mapping in `BarDisplay`: non-2xx → `BarApiException`; `HttpRequestException` or `TaskCanceledException` while `ct` is not cancelled → `BarRequestException`; caller cancellation → rethrow `OperationCanceledException`.

- [ ] **Step 1: Failing tests** (services with `AddBusyBar()` + `ConfigurePrimaryHttpMessageHandler(() => fake)` on `"busybar"`):
  - `Draw_posts_payload_to_draw_endpoint`: body JSON has `application_name "busynet"`.
  - `Clear_deletes_scoped_to_application`: `DELETE /api/display/draw`, query `application_name=busynet`.
  - `Token_is_sent_as_header_when_set` / `No_token_header_when_unset`.
  - `Error_response_maps_to_BarApiException_with_one_line_message`: 409 `{"error":"Requested priority level is below that of currently active app","error_code":"priority"}` → `Message == "HTTP 409 | POST /api/display/draw | Requested priority level is below that of currently active app | code=priority"`; is `ITransientFailure`.
  - `Transport_failure_maps_to_BarRequestException`: fake throws `HttpRequestException("boom http://x?token=SECRET")` → message `"request error | POST /api/display/draw | HttpRequestException"`, contains no `SECRET`.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(bar): add Refit display client and error mapping`.

### Task 9: Button frame decoding (protobuf)

**Files:**
- Create: `src/Busynet.Bar/Buttons/Protos/state.proto`, `src/Busynet.Bar/Buttons/ButtonEvent.cs`, `ButtonFrameDecoder.cs`
- Modify: `src/Busynet.Bar/Busynet.Bar.csproj` (`<Protobuf Include="Buttons/Protos/state.proto" GrpcServices="None" />`, refs Google.Protobuf, Grpc.Tools `PrivateAssets=all`)
- Test: `tests/Busynet.Bar.Tests/Buttons/ButtonFrameDecoderTests.cs`

**Interfaces:**
- Produces: `enum BarButton { Ok, Back, Start }`; `enum ButtonAction { Press, Release }`; `sealed record ButtonEvent(BarButton Button, ButtonAction Action) { bool IsPress => Action == ButtonAction.Press; }`; `static class ButtonFrameDecoder { IReadOnlyList<ButtonEvent> Decode(ReadOnlySpan<byte> frame); }` — returns empty for undecodable frames and unknown enum values; never throws.

`state.proto` (field numbers from busylib's descriptors; everything else is skipped as unknown fields):

```proto
syntax = "proto3";
package busynet.state;
option csharp_namespace = "Busynet.Bar.Buttons.Proto";

message State { repeated StateUpdate updates = 2; }
message StateUpdate { InputEvent input = 11; }
message InputEvent { ButtonEvent button_event = 1; }
message ButtonEvent { Button button = 1; ButtonAction action = 2; }
enum Button { OK = 0; BACK = 1; START = 2; }
enum ButtonAction { PRESS = 0; RELEASE = 1; }
```

- [ ] **Step 1: Failing tests:**
  - `Decodes_start_press_and_release_in_order` (build `State` with the generated types, `ToByteArray()`).
  - `Absent_enums_mean_ok_and_press`: a `ButtonEvent` with no fields set → `(Ok, Press)` (proto3 default).
  - `Updates_without_input_yield_nothing`.
  - `Unknown_fields_are_skipped`: hand-built bytes `09 <8 bytes>` (field 1 fixed64 timestamp) + `12 <len> 52 <len> <frame bytes>` (update with field 10) + a valid input update → exactly the one input event.
  - `Garbage_bytes_yield_nothing`.
  - `Unknown_enum_value_is_skipped` (button value 7).
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(bar): decode button events from status frames`.

### Task 10: Button event stream over WebSocket

**Files:**
- Create: `src/Busynet.Bar/Buttons/ButtonEventStream.cs`, `StatusSocket.cs`
- Modify: `src/Busynet.Bar/BarFactory.cs` (implement `CreateButtonEvents`)
- Test: `tests/Busynet.Bar.Tests/Buttons/ButtonEventStreamTests.cs`, `StatusSocketTests.cs`

**Interfaces:**
- Produces:
  - `static class ButtonEventStream { IObservable<ButtonEvent> Create(Func<CancellationToken, IAsyncEnumerable<byte[]>> frames, IScheduler scheduler, ILogger logger); }`
  - `static class StatusSocket { IAsyncEnumerable<byte[]> ReadFramesAsync(BarConnection connection, CancellationToken ct); static Uri BuildUri(BarConnection connection); }` — `BuildUri` appends `?x-api-token=<Uri.EscapeDataString(token)>` when set. Connects a `ClientWebSocket`, sends text `{"enable": true}`, yields each complete **binary** message (reassembling fragments), ignores text messages, ends when the server closes.
  - `BarFactory.CreateButtonEvents(connection)` = `ButtonEventStream.Create(ct => StatusSocket.ReadFramesAsync(connection, ct), DefaultScheduler.Instance, logger)`.

Stream behaviour (spec §Bar "Button stream"): lazy, shared (`Publish().RefCount()`), never errors or completes. When `frames` ends or throws (anything but our own cancellation), log Debug `"Button stream dropped, reconnecting in {Delay}: {ErrorType}"` (**type name only**), wait the back-off on `scheduler`, reconnect. Back-off 1 s, doubling, cap 30 s; reset to 1 s after any frame arrives. Disposing the last subscriber cancels the token passed to `frames`.

- [ ] **Step 1: Failing tests** (`TestScheduler`; `frames` fake is a list of scripted connections, each a sequence of frames then end/throw):
  - `Emits_decoded_events_from_frames`.
  - `Does_not_connect_until_subscribed`; `Two_subscribers_share_one_connection`.
  - `Reconnects_with_exponential_backoff_capped_at_30s`: six failing connections → connection attempts at 0, 1, 3, 7, 15, 31, 61 s.
  - `Backoff_resets_after_a_frame_arrives`.
  - `Never_errors_or_completes` (after a throwing connection).
  - `Disposing_last_subscriber_cancels_the_connection`.
  - `StatusSocket_uri_escapes_token`: token `a b&c` → query `x-api-token=a%20b%26c`; no token → no query.
  - `Log_messages_never_contain_the_connection_exception_message`: the connection throws `WebSocketException("ws://h?x-api-token=SECRET")` → captured log text has no `SECRET`.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(bar): add reconnecting button event stream`.

### Task 11: Claude credentials

**Files:**
- Create: `src/Busynet.Claude/Busynet.Claude.csproj` (refs Core), `src/Busynet.Claude/ClaudeException.cs` (`ClaudeException`, `ClaudeAuthException`, `ClaudeTransientException`), `Credentials/IClaudeTokenSource.cs`, `Credentials/ClaudeTokenSource.cs`, `Credentials/ClaudeCredentialOptions.cs`
- Test: `tests/Busynet.Claude.Tests/Busynet.Claude.Tests.csproj`, `tests/Busynet.Claude.Tests/Credentials/ClaudeTokenSourceTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner`, `ProcessOutcome`, `ProcessRunFailedException` (Task 2).
- Produces:
  - `class ClaudeException(string message) : BusynetException`; `sealed class ClaudeAuthException : ClaudeException`; `sealed class ClaudeTransientException(string message, TimeSpan? retryAfter = null) : ClaudeException, ITransientFailure`.
  - `interface IClaudeTokenSource { Task<string> GetTokenAsync(CancellationToken ct); }`
  - `sealed record ClaudeCredentialOptions(bool UseKeychain, string CredentialsFilePath)` with `static ClaudeCredentialOptions Default` = (`OperatingSystem.IsMacOS()`, `~/.claude/.credentials.json` via `Environment.SpecialFolder.UserProfile`).
  - `sealed class ClaudeTokenSource(IProcessRunner runner, ClaudeCredentialOptions options, TimeProvider time) : IClaudeTokenSource`

Exact copy (messages are user-facing, from busyboy):
- `"No Claude Code credentials found. Run `claude` to log in."` (auth)
- `"Claude Code credentials are unreadable. Run `claude` to log in."` (auth)
- `"The Claude Code token has expired. Run `claude` to refresh it."` (auth)
- `"Could not read the Claude Code credentials from the keychain."` (transient)

Algorithm: keychain = `security find-generic-password -s "Claude Code-credentials" -w`, timeout 10 s. Exit 0 with non-blank stdout → blob. Exit 44 → "not found" (not a failure). Any other exit, or `ProcessRunFailedException` → "failed". File consulted only when keychain gave no blob. File: missing / `IOException` → none; invalid UTF-8 (read with `new UTF8Encoding(false, true)`) → unreadable. No blob: keychain failed → transient, else → no-credentials. Blob: JSON object → `claudeAiOauth` object → `accessToken` non-empty string, else unreadable. `expiresAt`: absent → OK; JSON number (ms since epoch) `≤ now` → expired; any other JSON kind (string, bool) → unreadable. An expired/malformed keychain blob never falls through to the file.

- [ ] **Step 1: Failing tests** (fake runner, temp file path, `FakeTimeProvider`; port of busyboy `test_claude.py` credential cases): `Keychain_token_is_used`, `Missing_keychain_entry_falls_back_to_file`, `Failing_security_command_falls_back_to_file`, `Keychain_timeout_falls_back_to_file`, `Expired_keychain_token_does_not_fall_through`, `Expired_file_token_is_auth_error`, `No_credentials_anywhere_is_auth_error`, `Keychain_failure_without_file_is_transient` (exit 1 and timeout), `Non_keychain_platform_reads_file_only` (runner never called), `Malformed_json_is_unreadable` (theory: `not json`, `[]`, `{}`, `{"claudeAiOauth":{}}`, `{"claudeAiOauth":{"accessToken":""}}`), `Non_numeric_expires_at_is_unreadable` (`"soon"`, `true`), `Missing_expires_at_is_accepted`, `Non_utf8_file_is_unreadable`, `Messages_never_contain_the_blob`.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(claude): resolve Claude Code OAuth token`.

### Task 12: Claude usage API

**Files:**
- Create: `src/Busynet.Claude/Usage/IClaudeUsageApi.cs`, `Usage.cs`, `IClaudeUsage.cs`, `ClaudeUsageClient.cs`, `src/Busynet.Claude/ClaudeServiceCollectionExtensions.cs`
- Test: `tests/Busynet.Claude.Tests/Usage/ClaudeUsageClientTests.cs`

**Interfaces:**
- Consumes: `IClaudeTokenSource` (Task 11), `FakeHttpHandler` (Task 5).
- Produces:
  - `interface IClaudeUsageApi { [Get("/api/oauth/usage")] [Headers("anthropic-beta: oauth-2025-04-20", "User-Agent: claude-code/2.1.220")] Task<HttpResponseMessage> GetUsageAsync([Authorize("Bearer")] string token, CancellationToken ct); }`
  - `sealed record UsageWindow(double Utilization, DateTimeOffset? ResetsAt)`; `sealed record Usage(UsageWindow Session, UsageWindow Weekly)`.
  - `interface IClaudeUsage { Task<Usage> GetUsageAsync(CancellationToken ct); }` — resolves the token on **every** call, then fetches.
  - `static IServiceCollection AddClaude(this IServiceCollection services)` — Refit client for `IClaudeUsageApi`, base `https://api.anthropic.com`, timeout 15 s; registers `IClaudeTokenSource`, `ClaudeCredentialOptions.Default`, `IProcessRunner`, `TimeProvider.System` (TryAdd), `IClaudeUsage`.

Classification (copy from busyboy `claude.py`): 401/403 → `ClaudeAuthException("Anthropic rejected the Claude Code token. Run `claude` to log in again.")`; 429 → transient `"Claude usage API rate limited the request"` with `RetryAfter` from an integer `Retry-After` header clamped to `[0, 3600]` s (non-integer → null); ≥500 → transient `"Claude usage API returned HTTP {status}"`; other 4xx → `ClaudeException("Claude usage API returned HTTP {status}")`; `HttpRequestException` / timeout → transient `"Claude usage request failed: {ExceptionTypeName}"`. Body: read `five_hour` and `seven_day` objects; `utilization` must be a finite number; `resets_at` null/absent or an ISO-8601 string **with an explicit offset** (`Z` or `±hh:mm`); anything else (non-JSON, missing key, wrong type, offset-less timestamp) → transient `"Claude usage API returned an unexpected response"`. Messages never include the token or body.

- [ ] **Step 1: Failing tests** (port of busyboy `test_claude.py` usage cases): `Parses_the_real_payload_shape` (use busyboy's `PAYLOAD` fixture from `../busyboy/tests/test_claude.py`; session 3.0, weekly 26.0, session reset `2026-11-12T20:29:59.606009+00:00`), `Sends_required_headers` (Authorization `Bearer t`, `anthropic-beta`, `User-Agent`), `Null_resets_at_is_allowed`, `Rejected_token_is_auth_error` (401, 403), `Server_trouble_and_rate_limits_are_transient` (500, 503, 429 with/without `Retry-After: 90`), `Retry_after_is_clamped` (`999999999`→3600 s, `-5`→0, `0`→0, `90`→90 s), `Other_client_errors_are_fatal_but_neither_subclass` (404), `Connection_error_is_transient`, `Non_json_body_is_transient`, `Malformed_shape_is_transient` (theory: `{}`, `{"five_hour":{}}`, utilization `"x"`, `resets_at` `"2026-11-12T20:29:59"`), `Errors_never_contain_the_token`, `Token_is_resolved_on_every_call` (two calls → token source called twice).
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(claude): add usage API client`.

### Task 13: `ClaudeLimits` feature slice (labels, state, watch wiring)

**Files:**
- Create: `src/Busynet.Cli/Busynet.Cli.csproj` (Exe; refs Core, Bar, Claude), `src/Busynet.Cli/Features/ClaudeLimits/LimitKind.cs`, `LimitLabels.cs`, `LimitsState.cs`, `LimitsDisplay.cs`, `ClaudeLimitsWatch.cs`
- Test: `tests/Busynet.Cli.Tests/Busynet.Cli.Tests.csproj`, `tests/Busynet.Cli.Tests/Features/ClaudeLimits/LimitLabelsTests.cs`, `ClaudeLimitsWatchTests.cs`

**Interfaces:**
- Consumes: `Poller.Poll`, `DisplayWatch.RunAsync`, `IDisplay<T>` (Tasks 3–4); `IBarDisplay`, `LimitGauge`, `LimitsLayout`, `ButtonEvent` (Tasks 7–9); `Usage`, `UsageWindow`, `IClaudeUsage` (Task 12).
- Produces:
  - `enum LimitKind { Session, Weekly }`
  - `static class LimitLabels { string SessionCountdown(TimeSpan remaining); string WeeklyLabelTime(TimeSpan remaining); string Format(LimitKind kind, UsageWindow window, DateTimeOffset now); }`
  - `sealed record LimitsState(Usage? Usage, LimitKind Kind)` with `Toggle()`, `WithUsage(Usage)`, `LimitGauge? Render(DateTimeOffset now)` (null when `Usage` is null; percent = selected window's raw utilization).
  - `sealed class LimitsDisplay(IBarDisplay bar) : IDisplay<LimitGauge>` — `DrawAsync` = `bar.DrawAsync(LimitsLayout.Build(gauge))`.
  - `static class ClaudeLimitsWatch { Task RunAsync(Usage seed, IClaudeUsage usage, IObservable<ButtonEvent> buttons, IDisplay<LimitGauge> display, TimeSpan interval, IScheduler scheduler, ILogger logger, CancellationToken ct); }` — initial state `(seed, Session)`; updates = `Poller.Poll(usage.GetUsageAsync, interval, initialDelay: interval, …)` → `WithUsage`, merged with `buttons.Where(e => e.Button == BarButton.Start && e.IsPress)` → `Toggle`; render with `scheduler.Now`.

Label rules (port `watch.py`): `SessionCountdown`: floor to whole minutes, never negative; `≥1h` → `{h}h{mm:D2}m`, else `{m}m`. `WeeklyLabelTime`: `≥48h` → `{days}d` (whole days, floored); `≥1h` → `{hours}h` (whole hours); else `SessionCountdown`. `Format`: `"5h"`/`"wk"` + ` {round(clamp(utilization,0,100))}%` (banker's rounding) + ` {time}` only when `ResetsAt` is set.

- [ ] **Step 1: Failing label tests** — copy busyboy's tables verbatim:
  - Session: 3h59m→`3h59m`, 3h05m→`3h05m`, 60m→`1h00m`, 59m→`59m`, 0→`0m`, −5m→`0m`.
  - `Format` (now fixed): (Session, 85, 3h49m)→`5h 85% 3h49m`; (Weekly, 40, 6d2h)→`wk 40% 6d`; (Weekly, 40, 29h)→`wk 40% 29h`; (Weekly, 100, 47h59m)→`wk 100% 47h`; (Weekly, 40, 48h)→`wk 40% 2d`; (Session, 99.6, 1h)→`5h 100% 1h00m`; (Session, 150, 1h)→`5h 100% 1h00m`; (Session, −3, 1h)→`5h 0% 1h00m`; (Session, 0.4, 1h)→`5h 0% 1h00m`; (Session, 85, null)→`5h 85%`; (Session, 85, −1h)→`5h 85% 0m`; (Weekly, 100, 23h59m)→`wk 100% 23h`; (Weekly, 40, 5h05m)→`wk 40% 5h`; (Weekly, 40, 59m)→`wk 40% 59m`; (Weekly, 40, −1h)→`wk 40% 0m`.
- [ ] **Step 2:** Run → FAIL. Implement labels + state. Run → PASS.
- [ ] **Step 3: Failing watch tests** (`TestScheduler`, `FakeDisplay<LimitGauge>`, fake `IClaudeUsage` with scripted results, `Subject<ButtonEvent>`; port of busyboy `test_watch.py` limits/press cases):
  - `Draws_seed_session_view_immediately_without_fetching`.
  - `Polls_every_interval_and_redraws_on_visible_change`.
  - `Does_not_redraw_when_only_invisible_values_changed` (utilization 50.0 → 50.2).
  - `Start_press_switches_to_weekly_without_fetching`; `Second_press_returns_to_session`.
  - `Release_and_other_buttons_are_ignored`.
  - `Press_during_retry_after_wait_redraws_and_keeps_waiting` (429 RetryAfter 600 s → press at +10 s redraws; next fetch still at +600 s).
  - `Press_uses_current_time_for_label` (advance 30 min, press → countdown reflects it).
  - `Transient_fetch_failure_keeps_display`; `Auth_failure_ends_watch_after_clearing` (task faults with `ClaudeAuthException`, last call `"clear"`).
  - `Two_presses_around_a_failed_redraw_leave_display_consistent` (draw after first press throws `BarApiException` 409 → second press redraws session; final drawn gauge is session).
  - `Button_subscription_is_disposed_on_exit` (subject `HasObservers == false` after cancel and after auth failure).
- [ ] **Step 4:** Run → FAIL. Implement `LimitsDisplay` + `ClaudeLimitsWatch`. Run → PASS.
- [ ] **Step 5:** Commit `feat(cli): add claude limits feature slice`.

### Task 14: CLI host and `claude limits` command

**Files:**
- Create: `src/Busynet.Cli/Program.cs`, `src/Busynet.Cli/BusynetApp.cs`, `Infrastructure/TypeRegistrar.cs`, `Infrastructure/TypeResolver.cs`, `Infrastructure/BarCommandSettings.cs`, `Infrastructure/Verbosity.cs`, `Infrastructure/VerbosityInterceptor.cs`, `Infrastructure/ErrorHandler.cs`, `Infrastructure/ServiceRegistration.cs`, `Features/ClaudeLimits/ClaudeLimitsCommand.cs`, `Features/ClaudeLimits/ClaudeLimitsSettings.cs`
- Test: `tests/Busynet.Cli.Tests/ClaudeLimitsCommandTests.cs`, `tests/Busynet.Cli.Tests/ExitCodeTests.cs`, `tests/Busynet.Cli.Tests/Fakes/FakeBarFactory.cs` (+ `FakeBarDisplay : IBarDisplay`)

**Interfaces:**
- Consumes: everything above; `AddBusyBar()` (Task 8), `AddClaude()` (Task 12), `BarConnection.Resolve` (Task 6), `ClaudeLimitsWatch.RunAsync` (Task 13).
- Produces:
  - `static class BusynetApp { Task<int> RunAsync(string[] args, IServiceCollection services, TextWriter stderr, CancellationToken ct); }` — the testable entry point. `Program.Main` builds real services (`ServiceRegistration.AddBusynet`), wires `Console.CancelKeyPress` (`e.Cancel = true; cts.Cancel()`) and calls it with `Console.Error`.
  - `class BarCommandSettings : CommandSettings` — `--host <HOST>`, `--token <TOKEN>`, `--verbose`.
  - `sealed class ClaudeLimitsSettings : BarCommandSettings` — `--interval <SECONDS>` default 180; `Validate()` → error if `< 1`.
  - `sealed class Verbosity { bool IsVerbose { get; set; } }` (singleton); `VerbosityInterceptor : ICommandInterceptor` sets it from `BarCommandSettings`; logging filter `level >= (IsVerbose ? Debug : Warning)`; console logger writes everything to stderr (`LogToStandardErrorThreshold = Trace`).
  - `ServiceRegistration.AddBusynet(IServiceCollection)` — `AddBusyBar`, `AddClaude`, logging, `Verbosity`, `IScheduler` = `DefaultScheduler.Instance`, `Func<string,string?>` environment = `Environment.GetEnvironmentVariable`, `ConfigureHttpClientDefaults(b => b.RemoveAllLoggers())`.

App config: `SetApplicationName("busynet")`, `SetApplicationVersion(<AssemblyInformationalVersion without +commit suffix>)`, branch `claude` → command `limits` (description: "Watch Claude usage limits on the bar until Ctrl+C; the Start button switches between 5-hour and weekly."), `SetInterceptor`, `SetExceptionHandler(ErrorHandler.Handle)`. `ErrorHandler`: `CommandAppException` → message to stderr, return 2; `BusynetException` → verbose ? `ToString()` : `Message`, return 1; anything else → `ToString()`, return 1.

`ClaudeLimitsCommand : AsyncCommand<ClaudeLimitsSettings>` (Spectre 0.57 passes a `CancellationToken` to `ExecuteAsync` and accepts one in `RunAsync`; verify against the package and use them): resolve `BarConnection`; **startup fetch** `IClaudeUsage.GetUsageAsync` first (any exception — including transient — propagates → exit 1; nothing touches the bar); create display + buttons via `IBarFactory`; `ClaudeLimitsWatch.RunAsync(seed, …)` (it does the initial clear); `OperationCanceledException` from Ctrl+C at any point → return 0. (Fetch-before-clear matches busyboy's order; the spec's startup set is unchanged.)

- [ ] **Step 1: Failing tests** (`BusynetApp.RunAsync` with services = real registration, then overrides: fake `IClaudeUsage`, fake `IBarFactory` returning a `FakeBarDisplay` (records payloads, can cancel the CTS after N draws) and a `Subject<ButtonEvent>`, `StringWriter` stderr):
  - `Startup_auth_failure_prints_one_line_and_exits_1`: stderr is exactly the auth message + newline; bar never called.
  - `Startup_transient_failure_exits_1`.
  - `Draws_then_clears_on_cancel_and_exits_0`: calls `clear`, draw (label starts `5h `), `clear`; exit 0; stderr empty.
  - `Startup_succeeds_when_first_draw_is_rejected`: first draw throws `BarApiException` 409, cancel after → exit 0, stderr empty.
  - `Startup_clear_failure_exits_1` with `request error | DELETE /api/display/draw | HttpRequestException`.
  - `Cancel_during_startup_exits_zero`: usage fake blocks until cancelled → exit 0, stderr empty, no bar calls.
  - `Interval_below_1_is_usage_error_exit_2` (`--interval 0`).
  - `Unknown_option_exits_2`.
  - `Verbose_shows_stack_trace` (stderr contains `at ` lines on auth failure with `--verbose`).
  - `Verbose_output_never_contains_tokens`: `--verbose --token BARSECRET`, Claude token source returns `CLAUDESECRET`, real `BarFactory` over `FakeHttpHandler` (draw 409, clear 200) and a capturing `ILoggerProvider`; run one cycle and cancel → neither secret in stderr or any log line.
  - `ExitCodeTests.Version_prints_version_exit_0`.
- [ ] **Step 2:** Run → FAIL. Implement. Run → PASS.
- [ ] **Step 3:** Commit `feat(cli): add Spectre host and claude limits command`.

### Task 15: Package as a .NET tool, README, CI smoke test

**Files:**
- Modify: `src/Busynet.Cli/Busynet.Cli.csproj` (`PackAsTool true`, `ToolCommandName busynet`, `PackageId busynet`, `Version 0.1.0`, `Description`, `Authors`, `PackageLicenseExpression MIT`, `RepositoryUrl https://github.com/mb-dot-dev/busynet`, `PackageReadmeFile README.md`)
- Modify: `README.md`, `.github/workflows/ci.yaml`

- [ ] **Step 1:** Edit csproj. Verify locally:
  `dotnet pack src/Busynet.Cli -c Release -o ./nupkg && dotnet tool install --tool-path ./tool --add-source ./nupkg busynet && ./tool/busynet --version && ./tool/busynet claude limits --help`
  Expected: `0.1.0`, then help listing `--interval`, `--host`, `--token`, `--verbose`. Clean up `./tool`.
- [ ] **Step 2:** README: install (`dotnet pack -c Release -o ./nupkg` → `dotnet tool install --global --add-source ./nupkg busynet`, `dotnet tool update …` for upgrades, `dotnet tool uninstall --global busynet`), configuration table (`BUSYBOY_HOST` default `10.0.4.20`, `BUSYBOY_TOKEN` optional, `--host`/`--token`), `busynet claude limits` usage (interval, button toggle, label examples `5h 85% 3h49m` / `wk 40% 6d`, Ctrl+C clears), CLI contract (exit 0/1/2, `--verbose`), development (`dotnet test`).
- [ ] **Step 3:** CI: add job `tool-smoke` (ubuntu-latest, `needs: build`) running the Step 1 commands with `--tool-path ./tool`, and `actions/upload-artifact` of `nupkg/*.nupkg` named `busynet-nupkg`.
- [ ] **Step 4:** `dotnet build -c Release -warnaserror && dotnet test -c Release` → all PASS. Commit `build: package busynet as a .NET tool`.

### Task 16: Real-bar verification, push, PR

- [ ] **Step 1:** Install the tool globally from `./nupkg`. With `source ~/.zshrc && busybarenv` (exports `BUSYBOY_HOST`/`BUSYBOY_TOKEN` — never print them), run `busynet claude limits --interval 30`. Check by eye: label + bar appear within a few seconds; Start/Pause toggles `5h …` ↔ `wk …` immediately; Ctrl+C clears the display and exits 0. Run once with `--verbose` and confirm no token in output. If the user is not around to look at the bar, capture a frame via `GET /api/screen` (see `../busyboy/tools/capture_screen.py`) instead and report that the button was not exercised.
- [ ] **Step 2:** `git push -u origin claude-limits`; `gh pr create --base main --title "Foundation and claude limits command"` with a body summarising the components, the verification above, and ending with the attribution line. Wait for CI (`gh pr checks --watch`); fix any failure before reporting.
