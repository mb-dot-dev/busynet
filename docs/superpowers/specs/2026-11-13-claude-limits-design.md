# busynet — foundation + `claude limits` (design)

Date: 2026-11-13
Status: approved 2026-11-13

## Intent

busynet is a .NET port of [busyboy](../../../../busyboy) (Python), a CLI that shows information on a BUSY Bar.
The goal is **full feature parity eventually**, delivered as separate PRs. This first PR lays the foundation —
solution layout, the shared building blocks every command will need — and ports exactly one command:
`claude limits`. Later PRs port `text`, `clear` and `gh workflow` on top of the same building blocks.

It ports *behaviour*, not Python functions. busyboy's `CLAUDE.md` is the behavioural reference; where this
document is silent, busyboy's observable behaviour wins.

## Goals

- `busynet claude limits` behaves like `busyboy claude limits` from the user's point of view (see Behaviour).
- Reusable components for the concerns every command shares, built now so `gh workflow` slots in later:
  bar connection settings, bar HTTP client, bar button stream, error hierarchy and retry-vs-fatal
  classification, the reactive watch pipeline, CLI error/exit-code handling, verbose logging, Ctrl+C.
- Public GitHub repo `mb-dot-dev/busynet`, created before the first push. CI builds and tests on every push/PR.

## Non-goals (this PR)

- `text`, `clear`, `gh workflow`, icon upload — later PRs. (`clear` the *operation* exists, because the watch
  uses it; `clear` the *command* does not.)
- NuGet publishing, release workflow, SonarQube.
- Refreshing or writing the Claude token. Read-only, like busyboy.
- Non-macOS keychains. Linux/Windows use the credentials-file fallback only.

## Anti-goals (would be a failure even if it "works")

- A token (bar `X-API-Token`, Claude OAuth token) appears in any log line, exception message, or stack trace —
  including at `--verbose`. Note that the bar's WebSocket carries its token **in the URL query string**
  (`?x-api-token=...`), so any HTTP/WebSocket logging must redact URLs, not just headers.
- An unconditional redraw every poll (restarts scroll animations; see Hardware facts).
- A transient failure (wifi drop, laptop sleep, 429, 5xx, bar unreachable mid-watch) ending the watch, or a
  fatal one (rejected/expired Claude token) being swallowed and retried forever.
- A button press triggering a Claude API fetch.
- Mirroring busyboy's module-per-file Python structure for its own sake.

## Constraints and decisions already made

| Topic | Decision |
|---|---|
| Runtime | **.NET 11** (`net11.0`), C# latest. SDK 11.0.100 is installed. |
| CLI | **System.CommandLine** for commands, options, help, validation (reflection-free, so Native-AOT-compatible; Spectre.Console.Cli was replaced for this reason). |
| Bar HTTP | **Refit** interface over the bar's HTTP API. Spec: `http://busybar.infra.home/openapi.yaml` (device API 27.15.0) — vendor a copy into `docs/references/openapi.yaml`. |
| Events | **Rx.NET** (`System.Reactive`) for the button stream *and* the watch loop itself (approach A, below). |
| Repo | Public GitHub repo `mb-dot-dev/busynet`; create with `gh repo create` before the first push. |
| Packaging | Installable as a .NET global tool: `Busynet.Cli` sets `PackAsTool`, `ToolCommandName=busynet`, `PackageId=busynet`. Installed locally from a packed folder, **not published** to NuGet: `dotnet pack -c Release -o ./nupkg` then `dotnet tool install --global --add-source ./nupkg busynet` (`update` for subsequent versions). README documents this; `nupkg/` is git-ignored. Verified as part of the PR: install, run `busynet --version`, uninstall. |

## Approaches considered

**A. Fully reactive watch (chosen).** Poll ticks and button presses are merged into one event stream, folded
into state with `Scan`, rendered to a screen description, de-duplicated against what was last drawn, and drawn.
Retry-vs-fatal, Retry-After back-off and clear-first/clear-last are reusable operators. Serialising both inputs
through one pipeline removes busyboy's lock. Cost: less familiar to read; tests use Rx's `TestScheduler`.

**B. Imperative async loop, Rx only for the button.** Closer to busyboy, easier to read, but the lock and the
timing logic come back and the reusable pieces are less clean.

**C. Generic Host + BackgroundService.** Free logging/config/shutdown, but heavyweight for one-shot commands
like `text` and awkward alongside the CLI parser's own app lifecycle.

Chosen: **A** — it is what "Rx.NET handles events" means and yields the reusable pipeline `gh workflow` needs.

## Solution layout (my call)

```
busynet/
  Busynet.slnx
  Directory.Build.props          # net11.0, nullable, TreatWarningsAsErrors, analyzers
  Directory.Packages.props       # central package versions
  src/
    Busynet.Core/                # no I/O knowledge of bar/Claude/CLI
    Busynet.Bar/                 # everything BUSY Bar: Refit API, display models, layouts, button stream
    Busynet.Claude/              # credentials + usage API
    Busynet.Cli/                 # System.CommandLine app, commands, DI wiring, feature slices (Features/ClaudeLimits)
  tests/
    Busynet.Core.Tests/ Busynet.Bar.Tests/ Busynet.Claude.Tests/ Busynet.Cli.Tests/
  docs/references/openapi.yaml
  docs/superpowers/specs/, plans/
  .github/workflows/ci.yaml
```

Dependency rules (same spirit as busyboy's module boundaries): `Core` references nothing of ours. `Bar` and
`Claude` reference `Core` only and never each other. `Cli` references all three, and its feature slice
(`Features/ClaudeLimits`) is the **only** place that combines a data source with the bar — the counterpart of
busyboy's `watch.py`.

## Reusable components (cross-cutting concerns)

### Core
- **Exception hierarchy.** Root `BusynetException`. Retryability is expressed by an interface
  `ITransientFailure { TimeSpan? RetryAfter }` implemented by transient exception types — so the pipeline
  swallows "anything transient" without catching a shared parent and accidentally swallowing a fatal sibling
  (the trap busyboy's CLAUDE.md warns about).
- **Watch pipeline** — a generic reactive building block, roughly
  `Watch<TState, TView>(initial, inputs, reduce, render, draw, clear, scheduler)`:
  1. `clear` once before anything else (failure → fatal at startup).
  2. Merge input streams (poll results, button presses, …) → `Scan(reduce)` → `render` → skip if equal to the
     **last successfully drawn** view → `draw`.
  3. A failed draw does **not** update "last drawn", so the next render retries it. (A plain
     `DistinctUntilChanged` before the draw would get this wrong.)
  4. Transient failures are logged at Debug and leave the display
     as it is; fatal ones terminate the stream with that error.
  5. On any termination — completion, fatal error, cancellation — `clear` again; a failure to clear on exit is
     logged at Debug and never masks the original error.
- **Poller** — `Poll(fetch, interval, scheduler)`: sequential (no overlapping fetches); the next fetch starts
  `max(interval, retryAfter)` after the previous one finishes; a transient failure becomes a "fetch failed"
  event carrying its `RetryAfter`, a fatal one errors the stream. Untrusted Retry-After is capped at 1 hour.
- **Clock** — everything time-based takes an `IScheduler` / `TimeProvider` so tests run on virtual time.

### Bar
- **Connection settings** — `host` (default `10.0.4.20`) and optional `token`, from `BUSYBOY_HOST` /
  `BUSYBOY_TOKEN` (kept for compatibility with existing setups), overridable by `--host` / `--token`. Token is
  sent as `X-API-Token` when set, omitted otherwise.
- **Refit API** — hand-written interface for just the endpoints in use: `POST /api/display/draw` and
  `DELETE /api/display/draw?application_name=…`. Further endpoints (assets upload for `gh workflow`) are added
  by the PRs that need them.
- **Display models** — text, rectangle (image comes with `gh workflow`); serialised to the spec's field names.
  Every payload is scoped to `application_name = "busynet"`. Validation stricter than the spec where busyboy
  is (ASCII-only text etc.) — port as needed by the elements in use.
- **Error mapping** — a non-2xx response → `BarApiException`, transport failure/timeout → `BarRequestException`,
  both under `BarException`. Both implement `ITransientFailure` (from Core), which is how the Core pipeline
  treats bar failures as transient mid-watch without referencing `Bar`.
- **Button stream** — `IObservable<ButtonEvent>` over `ClientWebSocket` to `ws://{host}/api/status/ws`
  (`?x-api-token=` when a token is set), sends `{"enable": true}` after connecting, decodes **binary protobuf**
  `BSB_State.State` frames and emits `input.button_event`s. Recreate a minimal `.proto` covering only
  `State.updates (2)`, `StateUpdate.input (11)`, `InputEvent`, `ButtonEvent`, and the `Button` / `ButtonAction`
  enums, from busylib's generated descriptors (MIT); unknown fields are skipped by protobuf. Remember proto3
  defaults: an absent enum means its zero value (`OK`, `PRESS`). Text frames and undecodable frames are ignored.
  Semantics: lazy (connects on first subscription), shared (`Publish().RefCount()`), never errors or
  completes — drops reconnect with exponential back-off 1s→30s, logged at Debug; disposing the last subscription
  closes the socket.
- **Limits layout** — the one-limit payload: `condensed` label at x=1, y=-2, colour `#E6E6E6`; 70x8 track at
  x=1, y=8 colour `#1F1F1F`; fill on top, width `round(percent/100*70)` clamped 0..70, colour `#CD6E58`, or
  `#B3261E` above 85%; ids `limit-track`, `limit-fill`, `limit-label`. An empty bar's fill is drawn as a 1px
  track-coloured rectangle (a draw never removes omitted ids). Pure function, no I/O.

### Claude
- **Token resolution** — macOS: `security find-generic-password -s "Claude Code-credentials" -w` (10s timeout,
  stderr discarded); fall back to `~/.claude/.credentials.json` only if the entry is missing (exit 44) or the
  command failed. Parse `claudeAiOauth.accessToken` / `expiresAt` (ms epoch). Outcomes: no credentials /
  unreadable / expired → `ClaudeAuthException` (fatal); keychain command failed and no file →
  `ClaudeTransientException`. Re-resolved on **every** fetch (Claude Code refreshes its own token). On
  non-macOS, file only.
- **Usage API** — Refit `GET https://api.anthropic.com/api/oauth/usage` with `Authorization: Bearer`,
  `anthropic-beta: oauth-2025-04-20`, `User-Agent: claude-code/2.1.220`, 15s timeout. Read only `five_hour` and
  `seven_day` (`utilization` 0–100 float, `resets_at` optional ISO-8601 with offset); ignore everything else.
  401/403 → `ClaudeAuthException`; 429 → transient with Retry-After (delta-seconds only); 5xx, transport error,
  malformed body → transient; other 4xx → `ClaudeException` (fatal). No message ever contains the token or body.

### Cli
- `BusynetApp` builds the `System.CommandLine` tree by hand and a `ServiceProvider` over `Microsoft.Extensions.DependencyInjection`;
  `IHttpClientFactory` + Refit registration.
- **Shared settings base** (`BarOptions`/`BarSettings`: `--host`, `--token`, `--verbose`) inherited by every bar
  command.
- **Error → exit code**: success silent, exit 0; expected failure (`BusynetException`) → one line on stderr,
  exit 1; usage errors (parse or validation, including `busynet claude` with no subcommand) → message on stderr, exit 2.
  `--verbose`: Debug logging to stderr and the original exception's stack trace is shown.
- **Ctrl+C** → `CancellationToken` that disposes the watch subscription (triggering clear-on-exit), then exit 0.
- `--version`.

## Behaviour: `busynet claude limits [--interval SECONDS]`

- Default interval **180s**. Shows one limit at a time, **5-hour first**.
- Label formats (port busyboy's `watch.py` formatting exactly, with unit tests per boundary):
  - session: `5h 85% 3h49m`; `<1h` → `59m`; past → `0m`; `hours` + 2-digit minutes.
  - weekly: `wk 40% 6d` (≥48h, whole days); under 48h whole hours `47h`; under 1h `59m`.
  - percent rounded and clamped 0–100; time omitted when `resets_at` is null.
- Each **Start/Pause press** (press, not release; `START` button only) toggles session ↔ weekly and redraws at
  once from the last fetched usage using the current time. A press never fetches and does not reset the poll
  timer. A press before the first successful fetch only flips which limit will be shown.
- Redraw only when the drawn payload would differ (label text, fill width, fill colour) — not when the raw
  unrounded utilization changes.
- **Startup** (normal CLI contract — failure prints one line, exit 1): build settings, initial clear, resolve the
  Claude token, one real usage fetch. Missing/expired credentials, unreachable API or bar, unexpected
  response — including errors that would be transient mid-watch, like a 5xx on that first fetch — → exit 1.
  Startup **ends at the fetch**: its result seeds the pipeline, and the **first draw goes through the normal
  watch path**, so a failed first draw is swallowed and retried like any other (matches busyboy). This matters
  because the bar answers `409` to a draw while a higher-priority app — e.g. a BUSY work session — owns the
  display; the command must start quietly and draw once the display is free, not exit 1. busynet sends no
  explicit draw `priority` (as busyboy) — out of scope here.
- `--interval` must be ≥ 1 (option validation → usage error, exit 2). Ctrl+C during startup exits like Ctrl+C in
  the loop (clear, exit 0).
- Draws are serialised: a press and a poll arriving together never produce overlapping draw requests (e.g.
  `Select(draw).Concat()`).
- Colours given as `#RRGGBB` are normalised to the spec's `#RRGGBBAA` before sending (as busyboy does).
- **Loop**: transient Claude failures and bar failures are swallowed (Debug only); `ClaudeAuthException`/other
  fatal `ClaudeException` ends the watch, clears, prints one line, exit 1. Button-stream failures never end the
  watch. Ctrl+C clears and exits 0.

## Hardware facts to carry over (from busyboy CLAUDE.md, measured on a real bar)

- Front display 72x16 RGB. Scope everything to `application_name`, including clear.
- Do not use `align`; position with explicit `y`. Font glyph offsets: condensed offset 2, height 9.
- Elements with stable ids replace on redraw; a draw never removes omitted ids.
- Redrawing restarts scroll animation — the "skip identical redraw" rule is load-bearing.
- Filled rectangles need device API ≥ 24.3.0.

## Testing

- xUnit; `Microsoft.Reactive.Testing` `TestScheduler` for the pipeline, poller, back-off and button-toggle
  timing — no real waits anywhere.
- A fake `HttpMessageHandler` router (method + path, unregistered route = test failure) shared by Bar, Claude and
  Cli tests — the counterpart of busyboy's `bar_transport` fixture.
- The button stream is injected as `IObservable<ButtonEvent>`; the WebSocket adapter itself is tested by
  feeding it encoded protobuf frames through a seam (no real socket).
- No test may read the developer's real keychain, credentials file, or env (`BUSYBOY_*`): token resolution
  takes its process runner, file path and clock as dependencies.
- Cli tests run `BusynetApp.RunAsync` through an `AppHarness` (stdout/stderr captured), asserting stderr line and exit
  code.
- Port busyboy's `test_claude.py` / limits-related `test_watch.py` / `test_bar.py` cases as behaviour specs.

## CI (my call)

`.github/workflows/ci.yaml`, triggered on push to `main` and on every PR to `main` (plus `workflow_dispatch`):

- Matrix `ubuntu-latest` + `macos-latest` — macOS because the keychain path is macOS-only code.
- `actions/setup-dotnet` with `global.json` pinning the 11.0 SDK (`rollForward: latestFeature`); NuGet cache
  keyed on `Directory.Packages.props` / lock files.
- `dotnet restore --locked-mode` → `dotnet build -c Release --no-restore -warnaserror` →
  `dotnet test -c Release --no-build` (test results uploaded as an artifact; coverage collected with
  `coverlet`/`Microsoft.Testing.Extensions.CodeCoverage`, reported but no threshold gate yet).
- Tool smoke test (ubuntu only): `dotnet pack` the CLI, `dotnet tool install --tool-path ./tool --add-source
  ./nupkg busynet`, run `./tool/busynet --version` and `./tool/busynet --help`. Upload the `.nupkg` as a build
  artifact so a PR's build can be installed without cloning.
- Concurrency group per ref with `cancel-in-progress`. No secrets, no Sonar, no NuGet publish.
- Ships in the first push to `main` (with an empty-solution build) so the `claude-limits` PR is checked by it.

## Delivery (my call)

Nothing below runs until this spec is approved.

1. `git init` locally, create public repo `mb-dot-dev/busynet` with `gh repo create`, push `main` containing
   only README, `.gitignore`, LICENSE (MIT, matching busyboy) and this spec + plan.
2. Implement on branch `claude-limits`, checked out in the main clone (no worktrees); open a PR.

## Left to the builder

- Exact type and method names; whether the pipeline is one generic method or a small builder.
- JSON serializer settings (System.Text.Json, snake_case) and whether display models use source generation.
- Whether bar HTTP calls get a small transport-level retry (busylib did 2 retries); not required.
- Logging provider choice for `--verbose` (Microsoft.Extensions.Logging console to stderr is the default pick).
- `.proto` file location and `Grpc.Tools` vs. hand-rolled parsing (generated is preferred).
