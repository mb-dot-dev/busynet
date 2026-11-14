# busynet internals

This page explains how busynet works inside, using `busynet claude limits` (the only command so far) as the example.
The diagrams are Mermaid, so they render on GitHub and in most Markdown previewers.

- [1. The big picture](#1-the-big-picture)
- [2. Projects and dependencies](#2-projects-and-dependencies)
- [3. The event pipeline](#3-the-event-pipeline)
- [4. Class diagrams](#4-class-diagrams)
- [5. Sequence diagrams](#5-sequence-diagrams)
- [6. State machines](#6-state-machines)
- [7. Failures and exit codes](#7-failures-and-exit-codes)
- [8. Concurrency notes](#8-concurrency-notes)
- [9. Adding a new watch command](#9-adding-a-new-watch-command)

---

## 1. The big picture

busynet is a small **reactive (Rx.NET) state machine**. Two independent sources produce events:

| Source | What it emits | Where it lives |
| --- | --- | --- |
| A **timer-driven poller** | a fresh `Usage` from the Claude API every `--interval` seconds | `Poller` (Core) wrapping `IClaudeUsage` (Claude) |
| The bar's **status WebSocket** | `ButtonEvent`s decoded from protobuf frames | `ButtonEventStream` + `StatusSocket` (Bar) |

Each event becomes a **state update function** (`Func<LimitsState, LimitsState>`). The functions are merged into one
stream and folded into the current state with `Scan`, like a reducer in Redux or Elm. Each new state is rendered to a
view (`LimitGauge`). The view is drawn on the bar over HTTP only when it differs from the last view drawn.

```mermaid
flowchart LR
    subgraph Sources["Event sources"]
        T(["Timer every --interval"]) --> F["Claude usage fetch<br/>GET /api/oauth/usage"]
        WS(["Bar status WebSocket<br/>/api/status/ws"]) --> D["Protobuf decoder"]
    end

    F -- "Usage" --> U1["state.WithUsage(u)"]
    D -- "ButtonEvent (Start, Press)" --> U2["state.Toggle()"]

    U1 --> M{{"Merge"}}
    U2 --> M
    M -- "update functions" --> S["Scan: state = update(state)"]
    S -- "LimitsState" --> R["Render: LimitGauge"]
    R -- "view (if changed)" --> DR["Draw<br/>POST /api/display/draw"]
    DR --> BAR[("BUSY Bar screen")]
```

The generic machinery (`Poller`, `DisplayWatch`, `IDisplay<TView>`) is in **Busynet.Core** and knows nothing about
Claude or the bar. The feature (`ClaudeLimitsWatch`) only supplies the sources, the state type, the reducer functions
and the render function.

---

## 2. Projects and dependencies

```mermaid
flowchart TB
    Cli["<b>Busynet.Cli</b><br/>Program, BusynetApp,<br/>commands, DI, error handling"]
    Bar["<b>Busynet.Bar</b><br/>BUSY Bar HTTP display,<br/>status WebSocket, layouts"]
    Claude["<b>Busynet.Claude</b><br/>credentials, usage API client"]
    Core["<b>Busynet.Core</b><br/>Poller, DisplayWatch, IDisplay,<br/>process runner, exception base types"]

    Cli --> Bar
    Cli --> Claude
    Cli --> Core
    Bar --> Core
    Claude --> Core

    subgraph External["External libraries"]
        Rx["System.Reactive"]
        Refit["Refit (source-generated)"]
        SCL["System.CommandLine"]
        PB["Google.Protobuf"]
        MEDI["Microsoft.Extensions.*<br/>(DI, Http, Logging)"]
    end

    Core -.-> Rx
    Bar -.-> Refit
    Bar -.-> PB
    Claude -.-> Refit
    Cli -.-> SCL
    Cli -.-> MEDI
```

| Project | Responsibility | Key types |
| --- | --- | --- |
| `Busynet.Core` | Generic watch loop, polling, retry classification, child processes | `DisplayWatch`, `Poller`, `IDisplay<T>`, `ITransientFailure`, `BusynetException`, `ProcessRunner` |
| `Busynet.Bar` | Talking to the BUSY Bar: drawing, clearing, button events | `IBarFactory`, `IBarDisplay`, `ButtonEventStream`, `StatusSocket`, `LimitsLayout` |
| `Busynet.Claude` | Reading the Claude Code token and fetching usage | `IClaudeUsage`, `ClaudeUsageClient`, `ClaudeTokenSource` |
| `Busynet.Cli` | Command line, DI wiring, the `claude limits` feature, exit codes | `BusynetApp`, `ClaudeLimitsCommand`, `ClaudeLimitsWatch`, `LimitsState`, `ErrorHandler` |

---

## 3. The event pipeline

This is the core of the app. It is assembled in
[`ClaudeLimitsWatch.RunAsync`](../src/Busynet.Cli/Features/ClaudeLimits/ClaudeLimitsWatch.cs) and run by
[`DisplayWatch.RunAsync`](../src/Busynet.Core/Watching/DisplayWatch.cs).

```mermaid
flowchart TB
    subgraph Feature["ClaudeLimitsWatch (feature-specific)"]
        direction TB
        P["Poller.Poll(usage.GetUsageAsync,<br/>interval, initialDelay: interval)"]
        P --> PS["Select: u =#gt; s =#gt; s.WithUsage(u)"]

        B["IObservable#lt;ButtonEvent#gt;<br/>(shared, reconnecting)"]
        B --> BW["Where: Button == Start and IsPress"]
        BW --> BS["Select: _ =#gt; s =#gt; s.Toggle()"]

        PS --> MG["Merge"]
        BS --> MG
    end

    subgraph Generic["DisplayWatch (generic, Busynet.Core)"]
        direction TB
        C0["display.ClearAsync()<br/>(once, before subscribing)"]
        SW["StartWith(identity)<br/>so the seed state draws at once"]
        SC["Scan(initial, (state, update) =#gt; update(state))"]
        RD["Select(render)<br/>state.Render(scheduler.Now)"]
        NN["Where(view is not null)"]
        FA["Select(view =#gt; FromAsync(draw))<br/>skip if view == lastDrawn<br/>hold drawGate while drawing"]
        CC["Concat<br/>draws run one at a time, in order"]
        LT["LastOrDefaultAsync().ToTask(ct)<br/>awaits until cancelled or errored"]
        CX["finally: take drawGate, ClearAsync()<br/>(5 s timeout, never released)"]

        C0 --> SW --> SC --> RD --> NN --> FA --> CC --> LT --> CX
    end

    MG --> SW
```

Things to know about the pipeline:

- **The seed fetch is outside the pipeline.** `ClaudeLimitsCommand` fetches usage once *before* the watch starts.
  If that first fetch fails (even transiently), the command exits 1 without touching the bar. That is why the poller's
  `initialDelay` equals `interval`: the first in-pipeline fetch is one interval after the seed.
- **Events become functions.** Both sources map to `Func<LimitsState, LimitsState>`, so `Merge` can combine
  sources of different types, and `Scan` needs only one reducer: "apply the function".
- **A toggle never fetches.** It re-renders from the last fetched `Usage`. It neither shortens nor restarts the
  poller's wait.
- **Render reads the clock.** `Render(scheduler.Now)` computes the countdown (`3h49m`) at render time, so the label
  changes on every poll even when the usage percentage does not.
- **Draws are deduplicated by value.** `LimitGauge` is a record, so `Equals` compares label, fill width and colour flag.
  Redrawing an identical view would restart animations on the device, so it is skipped. `lastDrawn` is updated only
  after a *successful* draw. A view whose draw failed is therefore retried on the next update.
- **`Concat` serialises draws.** Each `FromAsync` is cold, and `Concat` subscribes to the next one only after the
  previous one finishes, so two HTTP draws never overlap.
- **The stream never completes on its own.** Neither the poller nor the button stream completes. The watch ends only
  through cancellation (Ctrl+C), a fatal error from `updates` (for example `ClaudeAuthException`), or a non-transient
  draw failure.

### Poller internals

```mermaid
flowchart LR
    start(["Subscribe"]) --> init["nextWait = initialDelay"]
    init --> cycle["Defer: wait = nextWait<br/>nextWait = interval"]
    cycle --> timer["Timer(wait, scheduler)"]
    timer --> fetch["FromAsync(fetch)"]
    fetch -- "success" --> emit["OnNext(value)"]
    fetch -- "ITransientFailure" --> transient["log at Debug, emit nothing<br/>nextWait = max(interval,<br/>clamp(RetryAfter, 0, 1h))"]
    fetch -- "any other exception" --> fatal(["OnError: the sequence ends"])
    emit --> repeat["Repeat"]
    transient --> repeat
    repeat --> cycle
```

---

## 4. Class diagrams

### 4.1 Core: the generic watch machinery

```mermaid
classDiagram
    direction LR

    class IDisplay~TView~ {
        <<interface>>
        +DrawAsync(TView view, CancellationToken) Task
        +ClearAsync(CancellationToken) Task
    }

    class DisplayWatch {
        <<static>>
        -ExitClearTimeout = 5s
        +RunAsync(initial, updates, render, display, logger, ct)$ Task
        -ClearOnExitAsync(display, drawGate, logger)$ Task
    }

    class Poller {
        <<static>>
        -MaxRetryAfter = 1h
        +Poll(fetch, interval, initialDelay, scheduler, logger)$ IObservable~T~
    }

    class ITransientFailure {
        <<interface>>
        +RetryAfter TimeSpan?
    }

    class TransientFailure {
        <<static>>
        +IsTransient(Exception, out retryAfter)$ bool
    }

    class BusynetException {
        <<abstract>>
    }

    class IProcessRunner {
        <<interface>>
        +RunAsync(fileName, arguments, timeout, ct) Task~ProcessOutcome~
    }
    class ProcessRunner
    class ProcessOutcome {
        <<record>>
        +ExitCode int
        +StandardOutput string
    }

    DisplayWatch ..> IDisplay~TView~ : draws on
    DisplayWatch ..> TransientFailure : classifies draw errors
    Poller ..> TransientFailure : classifies fetch errors
    TransientFailure ..> ITransientFailure
    IProcessRunner <|.. ProcessRunner
    ProcessRunner ..> ProcessOutcome
```

### 4.2 Bar: display and button events

```mermaid
classDiagram
    direction TB

    class IBarFactory {
        <<interface>>
        +CreateDisplay(BarConnection) IBarDisplay
        +CreateButtonEvents(BarConnection) IObservable~ButtonEvent~
    }
    class BarFactory {
        <<internal>>
        -IHttpClientFactory httpClients
        -ILoggerFactory loggerFactory
        -BarDisplayOptions options
    }
    IBarFactory <|.. BarFactory

    class BarConnection {
        <<record>>
        +HttpBaseAddress Uri
        +StatusSocketUri Uri
        +Token string?
        +Resolve(hostOption, tokenOption, environment)$ BarConnection
    }

    class IBarDisplay {
        <<interface>>
        +DrawAsync(DisplayElements, ct) Task
        +ClearAsync(ct) Task
    }
    class BarDisplay {
        <<internal>>
        -IBusyBarApi api
        -TimeSpan responseTimeout
    }
    class IBusyBarApi {
        <<Refit interface>>
        +DrawAsync(DisplayElements, ct) Task~HttpResponseMessage~
        +ClearAsync(applicationName, ct) Task~HttpResponseMessage~
    }
    note for IBusyBarApi "POST and DELETE /api/display/draw"
    IBarDisplay <|.. BarDisplay
    BarDisplay --> IBusyBarApi

    class ButtonEventStream {
        <<static>>
        +Create(frames, scheduler, logger)$ IObservable~ButtonEvent~
    }
    class StatusSocket {
        <<static>>
        +BuildUri(BarConnection)$ Uri
        +ReadFramesAsync(BarConnection, ct)$ IAsyncEnumerable~ByteArray~
    }
    class ButtonFrameDecoder {
        <<static>>
        +Decode(ReadOnlySpan~byte~)$ IReadOnlyList~ButtonEvent~
    }
    class ButtonEvent {
        <<record>>
        +Button BarButton
        +Action ButtonAction
        +IsPress bool
    }
    class BarButton {
        <<enum>>
        Ok
        Back
        Start
    }
    class ButtonAction {
        <<enum>>
        Press
        Release
    }

    BarFactory ..> BarDisplay : creates
    BarFactory ..> ButtonEventStream : creates
    ButtonEventStream ..> StatusSocket : frames source
    ButtonEventStream ..> ButtonFrameDecoder : decodes
    ButtonFrameDecoder ..> ButtonEvent
    ButtonEvent --> BarButton
    ButtonEvent --> ButtonAction

    class LimitGauge {
        <<record>>
        +Label string
        +FillWidth int
        +High bool
        +TrackWidth = 70$
        +HighThreshold = 85$
        +From(label, percent)$ LimitGauge
    }
    class LimitsLayout {
        <<static>>
        +Build(LimitGauge)$ DisplayElements
    }
    class DisplayElements {
        <<record>>
        +ApplicationName string
        +Elements IReadOnlyList~DisplayElement~
    }
    class DisplayElement {
        <<abstract record>>
        +Id string
        +X int
        +Y int
        +Display string
    }
    class TextElement {
        <<record>>
        +Text string
        +Font string
        +Color string?
    }
    class RectangleElement {
        <<record>>
        +Width int
        +Height int
        +FillColors IReadOnlyList~string~
        +BorderWidth int
    }
    LimitsLayout ..> LimitGauge
    LimitsLayout ..> DisplayElements : builds
    DisplayElements o-- DisplayElement
    DisplayElement <|-- TextElement
    DisplayElement <|-- RectangleElement
    IBarDisplay ..> DisplayElements
```

### 4.3 Claude: token and usage

```mermaid
classDiagram
    direction LR

    class IClaudeUsage {
        <<interface>>
        +GetUsageAsync(ct) Task~Usage~
    }
    class ClaudeUsageClient {
        <<internal>>
        -IClaudeTokenSource tokens
        -IClaudeUsageApi api
        -ClaudeUsageOptions options
        -ParseRetryAfter(response)$ TimeSpan?
        -Parse(body)$ Usage
    }
    class IClaudeUsageApi {
        <<Refit interface>>
        +GetUsageAsync(token, ct) Task~HttpResponseMessage~
    }
    note for IClaudeUsageApi "GET https://api.anthropic.com/api/oauth/usage"
    class IClaudeTokenSource {
        <<interface>>
        +GetTokenAsync(ct) Task~string~
    }
    class ClaudeTokenSource {
        -IProcessRunner runner
        -ClaudeCredentialOptions options
        -TimeProvider time
        -ReadKeychainAsync(ct)
        -ReadFile()
        -ExtractToken(blob)
    }
    class ClaudeCredentialOptions {
        <<record>>
        +UseKeychain bool
        +CredentialsFilePath string
    }
    class Usage {
        <<record>>
        +Session UsageWindow
        +Weekly UsageWindow
    }
    class UsageWindow {
        <<record>>
        +Utilization double
        +ResetsAt DateTimeOffset?
    }
    class IProcessRunner {
        <<interface>>
    }

    IClaudeUsage <|.. ClaudeUsageClient
    ClaudeUsageClient --> IClaudeTokenSource
    ClaudeUsageClient --> IClaudeUsageApi
    IClaudeTokenSource <|.. ClaudeTokenSource
    ClaudeTokenSource --> IProcessRunner : runs security CLI
    ClaudeTokenSource --> ClaudeCredentialOptions
    ClaudeUsageClient ..> Usage : returns
    Usage *-- "2" UsageWindow
```

### 4.4 Cli: the `claude limits` feature

```mermaid
classDiagram
    direction TB

    class BusynetApp {
        <<static>>
        +RunAsync(args, services, stdout, stderr, ct)$ Task~int~
        -BuildRoot(provider, stdout)$ RootCommand
    }
    class ClaudeLimitsCommand {
        -IBarFactory bars
        -IClaudeUsage usage
        -IScheduler scheduler
        -Func environment
        +Define(IServiceProvider)$ Command
        +ExecuteAsync(ClaudeLimitsSettings, ct) Task~int~
    }
    class ClaudeLimitsOptions {
        +Interval Option~int~
        +AddTo(Command)
        +Read(ParseResult) Tuple~BarSettings,int~
    }
    class BarOptions {
        +Host Option~string?~
        +Token Option~string?~
        +Verbose Option~bool~
    }
    class BarSettings {
        <<record>>
        +Host string?
        +Token string?
        +Verbose bool
    }
    class ClaudeLimitsSettings {
        <<record>>
        +Interval int
    }
    class ClaudeLimitsWatch {
        <<static>>
        +RunAsync(seed, usage, buttons, display, interval, scheduler, logger, ct)$ Task
    }
    class LimitsState {
        <<record>>
        +Usage Usage?
        +Kind LimitKind
        +Toggle() LimitsState
        +WithUsage(Usage) LimitsState
        +Render(now) LimitGauge?
    }
    class LimitKind {
        <<enum>>
        Session
        Weekly
    }
    class LimitLabels {
        <<static>>
        +Format(kind, window, now)$ string
    }
    class LimitsDisplay {
        -IBarDisplay bar
    }
    class IDisplay~LimitGauge~ {
        <<interface>>
    }
    class ErrorHandler {
        +Failure = 1$
        +UsageError = 2$
        +Handle(Exception, IServiceProvider?) int
    }
    class Verbosity {
        +IsVerbose bool
    }

    BusynetApp ..> ClaudeLimitsCommand : Define()
    BusynetApp ..> ErrorHandler
    ClaudeLimitsCommand ..> ClaudeLimitsOptions
    ClaudeLimitsOptions --> BarOptions
    BarSettings <|-- ClaudeLimitsSettings
    ClaudeLimitsCommand ..> ClaudeLimitsWatch : runs
    ClaudeLimitsCommand ..> LimitsDisplay : creates
    ClaudeLimitsCommand ..> Verbosity : sets
    ClaudeLimitsWatch ..> LimitsState : seeds and folds
    LimitsState --> LimitKind
    LimitsState ..> LimitLabels
    IDisplay~LimitGauge~ <|.. LimitsDisplay
    ErrorHandler ..> Verbosity : reads
```

### 4.5 Exception hierarchy

The hierarchy carries two separate decisions:

- **`BusynetException`**: expected and user-facing. The CLI prints only the message.
- **`ITransientFailure`**: worth retrying. The watch survives it.

```mermaid
classDiagram
    direction TB

    class Exception
    class BusynetException {
        <<abstract>>
    }
    class ITransientFailure {
        <<interface>>
        +RetryAfter TimeSpan?
    }

    class BarException {
        <<abstract>>
        +RetryAfter = null
    }
    class BarApiException {
        +StatusCode int
        +ErrorCode string?
    }
    class BarRequestException

    class ClaudeException
    class ClaudeAuthException
    class ClaudeTransientException {
        +RetryAfter TimeSpan?
    }

    class ConfigurationException
    class ProcessRunFailedException

    Exception <|-- BusynetException
    Exception <|-- ProcessRunFailedException
    BusynetException <|-- BarException
    BusynetException <|-- ClaudeException
    BusynetException <|-- ConfigurationException
    BarException <|-- BarApiException
    BarException <|-- BarRequestException
    ClaudeException <|-- ClaudeAuthException
    ClaudeException <|-- ClaudeTransientException
    ITransientFailure <|.. BarException
    ITransientFailure <|.. ClaudeTransientException
```

| Exception | Transient? | Effect while watching |
| --- | --- | --- |
| `BarApiException`, `BarRequestException` | yes | Draw is logged and skipped. The next update retries. |
| `ClaudeTransientException` (5xx, 429, network, bad body, keychain hiccup) | yes | Poll emits nothing. The next wait is `max(interval, Retry-After)`. |
| `ClaudeAuthException` (401/403, no or expired credentials) | no | Watch ends, bar is cleared, exit 1. |
| `ClaudeException` (other 4xx) | no | Watch ends, bar is cleared, exit 1. |
| `ConfigurationException` (bad `BUSYBOY_HOST` / `BUSYBOY_TOKEN`) | no | Thrown before the bar is touched, exit 1. |
| `ProcessRunFailedException` | n/a | Caught inside `ClaudeTokenSource` and turned into `ClaudeTransientException`. |

---

## 5. Sequence diagrams

### 5.1 Startup: from `Main` to the running watch

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Program
    participant App as BusynetApp
    participant SCL as System.CommandLine
    participant Cmd as ClaudeLimitsCommand
    participant Conn as BarConnection
    participant Factory as IBarFactory
    participant Usage as IClaudeUsage
    participant Watch as ClaudeLimitsWatch

    User->>Program: busynet claude limits --interval 60
    Program->>Program: hook Console.CancelKeyPress to a CancellationTokenSource
    Program->>App: RunAsync(args, services.AddBusynet(), stdout, stderr, ct)
    App->>App: BuildServiceProvider() and BuildRoot()
    App->>SCL: Parse(args)
    alt parse or validation errors
        SCL-->>App: errors
        App-->>Program: 2 (tokens redacted in messages)
    else parsed
        App->>SCL: InvokeAsync(configuration, ct)
        SCL->>Cmd: action(parse, ct)
        Cmd->>Cmd: Verbosity.IsVerbose = --verbose
        Cmd->>Cmd: resolve ClaudeLimitsCommand from DI
        Cmd->>Conn: Resolve(--host, --token, env)
        Note right of Conn: option, then $BUSYBOY_HOST,<br/>then 10.0.4.20
        Cmd->>Factory: CreateDisplay(connection)
        Note right of Factory: no network I/O yet
        Cmd->>Usage: GetUsageAsync(ct)  [seed fetch]
        alt seed fetch fails
            Usage-->>Cmd: throws
            Cmd-->>App: exception, ErrorHandler gives 1
        else ok
            Usage-->>Cmd: Usage
            Cmd->>Factory: CreateButtonEvents(connection)
            Factory-->>Cmd: cold, shared observable
            Cmd->>Watch: RunAsync(seed, usage, buttons, LimitsDisplay, interval, ...)
            Note over Watch: runs until Ctrl+C or a fatal error
            Watch-->>Cmd: completes
            Cmd-->>App: 0
        end
    end
    App-->>Program: exit code
```

### 5.2 Watch start-up and the first draw

```mermaid
sequenceDiagram
    autonumber
    participant Watch as DisplayWatch
    participant Disp as LimitsDisplay
    participant Bar as BarDisplay (HTTP)
    participant Poll as Poller
    participant Btn as ButtonEventStream
    participant WS as StatusSocket

    Watch->>Disp: ClearAsync()
    Disp->>Bar: DELETE /api/display/draw?application_name=busynet
    Note right of Bar: 400 "not displaying anything"<br/>is treated as success
    Watch->>Watch: subscribe to Merge(poller, toggles).StartWith(identity)
    par subscription fans out
        Watch->>Poll: subscribe, Timer(interval) starts
    and
        Watch->>Btn: subscribe (RefCount 0 to 1)
        Btn->>WS: ReadFramesAsync()
        WS->>WS: connect ws://host/api/status/ws?x-api-token=...
        WS->>WS: send {"enable": true}
    end
    Watch->>Watch: StartWith(identity), so Scan emits the seed state
    Watch->>Watch: Render(now) gives LimitGauge("5h 85% 3h49m", ...)
    Watch->>Disp: DrawAsync(gauge)
    Disp->>Disp: LimitsLayout.Build(gauge)
    Disp->>Bar: POST /api/display/draw {track, fill, label}
    Bar-->>Watch: 200, lastDrawn = gauge
```

### 5.3 A poll cycle: success, transient failure, fatal failure

```mermaid
sequenceDiagram
    autonumber
    participant Timer as Rx Timer (IScheduler)
    participant Poll as Poller
    participant Client as ClaudeUsageClient
    participant Tok as ClaudeTokenSource
    participant API as api.anthropic.com
    participant Watch as DisplayWatch
    participant Bar as BUSY Bar

    Timer->>Poll: tick after nextWait
    Poll->>Client: GetUsageAsync(ct)
    Client->>Tok: GetTokenAsync()
    Tok-->>Client: token (keychain or ~/.claude/.credentials.json)
    Client->>API: GET /api/oauth/usage (Bearer token, 15 s deadline)

    alt 200 OK
        API-->>Client: {five_hour, seven_day}
        Client-->>Poll: Usage
        Poll->>Watch: OnNext(s => s.WithUsage(u))
        Watch->>Watch: Scan, then Render(now)
        alt view differs from lastDrawn
            Watch->>Bar: POST /api/display/draw
        else same view
            Watch->>Watch: skip (no redraw, no animation restart)
        end
        Poll->>Poll: nextWait = interval, Repeat
    else 429 / 5xx / network / bad body
        API-->>Client: error
        Client-->>Poll: throws ClaudeTransientException(RetryAfter?)
        Poll->>Poll: log Debug, emit nothing
        Poll->>Poll: nextWait = max(interval, clamp(RetryAfter, 0, 1h))
        Note over Watch,Bar: the bar keeps showing the last view
    else 401 / 403 / expired token
        API-->>Client: 401
        Client-->>Poll: throws ClaudeAuthException
        Poll->>Watch: OnError(ClaudeAuthException)
        Watch->>Bar: clear (finally block)
        Watch-->>Watch: rethrow, exit 1 with "Run `claude` to log in again."
    end
```

### 5.4 A Start-button press

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Dev as BUSY Bar device
    participant WS as StatusSocket
    participant Stream as ButtonEventStream
    participant Dec as ButtonFrameDecoder
    participant Feat as ClaudeLimitsWatch filters
    participant Watch as DisplayWatch

    User->>Dev: presses Start
    Dev-->>WS: binary protobuf State frame
    WS-->>Stream: yield byte[]
    Stream->>Stream: reset back-off delay to 1 s
    Stream->>Dec: Decode(frame)
    Dec-->>Stream: [ButtonEvent(Start, Press)]
    Stream-->>Feat: OnNext(ButtonEvent)
    Feat->>Feat: Where Start and IsPress
    Feat-->>Watch: OnNext(s => s.Toggle())
    Watch->>Watch: Scan: Kind Session becomes Weekly
    Watch->>Watch: Render(now) gives "wk 40% 6d"
    Watch->>Dev: POST /api/display/draw
    Note over Feat,Watch: no fetch happens and the poll timer is untouched
    User->>Dev: releases Start
    Dev-->>WS: frame with Release
    WS-->>Feat: ButtonEvent(Start, Release)
    Feat->>Feat: filtered out
```

### 5.5 Ctrl+C shutdown and the draw gate

The draw gate (`SemaphoreSlim(1,1)`) keeps a draw from landing *after* the exit clear. Disposing the Rx subscription
cancels an in-flight draw's token but does not wait for it to finish.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Program
    participant Watch as DisplayWatch
    participant Gate as drawGate
    participant Draw as in-flight draw
    participant Bar as BUSY Bar

    Note over Draw,Bar: a draw may be in progress, holding the gate
    User->>Program: Ctrl+C
    Program->>Program: e.Cancel = true, cts.Cancel()
    Note right of Program: a second Ctrl+C is not intercepted<br/>and kills the process
    Program-->>Watch: token cancelled
    Watch->>Watch: ToTask throws OperationCanceledException (swallowed)
    Watch->>Watch: subscription disposed (poller timer, WebSocket RefCount to 0)
    Draw->>Draw: its token is cancelled
    Watch->>Gate: WaitAsync (5 s timeout)
    Draw-->>Gate: Release()
    Gate-->>Watch: acquired and never released
    Watch->>Bar: DELETE /api/display/draw (5 s timeout)
    Note over Watch: a failed clear is logged at Debug<br/>and does not change the outcome
    Watch-->>Program: returns, exit 0
```

### 5.6 Reading the Claude Code token

```mermaid
sequenceDiagram
    autonumber
    participant Client as ClaudeUsageClient
    participant Tok as ClaudeTokenSource
    participant Run as ProcessRunner
    participant Sec as macOS security CLI
    participant FS as ~/.claude/.credentials.json

    Client->>Tok: GetTokenAsync()
    opt macOS (UseKeychain)
        Tok->>Run: RunAsync("security", find-generic-password -s "Claude Code-credentials" -w, 10 s)
        Run->>Sec: spawn
        alt exit 0 with output
            Sec-->>Tok: JSON blob
        else exit 44 (not found)
            Sec-->>Tok: no blob, not a failure
        else other exit / timeout / spawn failure
            Sec-->>Tok: no blob, keychainFailed = true
        end
    end
    opt no blob yet
        Tok->>FS: ReadAllBytes
        FS-->>Tok: blob or nothing
    end
    alt still no blob
        Tok-->>Client: keychainFailed ? ClaudeTransientException : ClaudeAuthException
    else blob
        Tok->>Tok: parse claudeAiOauth.accessToken, check expiresAt
        Tok-->>Client: token, or ClaudeAuthException (unreadable or expired)
    end
```

The token is read again on **every** fetch and is never cached. busynet never refreshes or writes it. When Claude Code
itself refreshes the token, the next poll picks up the new one.

---

## 6. State machines

### 6.1 `LimitsState`: what the bar shows

`LimitsState(Usage?, LimitKind)` is an immutable record. Updates return a copy (`with`).

```mermaid
stateDiagram-v2
    [*] --> Session : seed Usage, Kind = Session

    Session --> Weekly : Start pressed / Toggle()
    Weekly --> Session : Start pressed / Toggle()

    Session --> Session : poll / WithUsage(u)
    Weekly --> Weekly : poll / WithUsage(u)

    note right of Session
        renders "5h 85% 3h49m"
        from Usage.Session
    end note
    note right of Weekly
        renders "wk 40% 6d"
        from Usage.Weekly
    end note
```

The gauge colour is a function of the rendered view: the track colour when `FillWidth == 0`, red when
`percent > 85`, coral otherwise.

### 6.2 `ButtonEventStream`: connection life cycle

The stream is lazy and shared (`Publish().RefCount()`). It never errors or completes: every failure becomes a
reconnect.

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Connecting : first subscriber (RefCount 0 to 1)

    Connecting --> Streaming : connected, sent enable message
    Connecting --> Backoff : connect throws

    Streaming --> Streaming : binary frame / delay = 1 s, decode, OnNext
    Streaming --> Backoff : server Close, or exception (log type name only)

    Backoff --> Connecting : after delay / delay = min(delay * 2, 30 s)

    Connecting --> Idle : last subscriber disposes (cts.Cancel)
    Streaming --> Idle : last subscriber disposes
    Backoff --> Idle : last subscriber disposes (pending timer disposed)
```

Liveness: the WebSocket sends keep-alive pings every 20 s with a 20 s timeout, so a connection that died silently
(Wi-Fi drop, laptop sleep) is noticed in about 40 s and reconnected.

---

## 7. Failures and exit codes

```mermaid
flowchart TD
    A["Exception or result reaches BusynetApp"] --> P{"Parse errors?"}
    P -- yes --> E2["print each error (unmatched values redacted)<br/><b>exit 2</b>"]
    P -- no --> C{"Ctrl+C requested?"}
    C -- yes --> E0["silent<br/><b>exit 0</b>"]
    C -- no --> B{"is BusynetException?"}
    B -- yes --> V{"--verbose?"}
    V -- no --> E1a["print Message (one line)<br/><b>exit 1</b>"]
    V -- yes --> E1b["print full exception<br/><b>exit 1</b>"]
    B -- no --> E1c["print full exception (a bug)<br/><b>exit 1</b>"]
```

Ctrl+C is checked in two places. `ClaudeLimitsCommand.ExecuteAsync` returns 0 for *any* exception seen after
cancellation, because SIGINT also kills the child `security` process and that failure is fallout. `ErrorHandler`
returns 0 for an `OperationCanceledException` once the token is cancelled.

### Secret hygiene

Bar and Claude tokens appear in URLs, query strings and headers, so the code avoids every path that could print them:

- All `HttpClient` loggers are removed (`RemoveAllLoggers()`).
- Transport failures report the **exception type name only**: `BarRequestException`, `ClaudeTransientException`,
  and the WebSocket reconnect log.
- `BarConnection.ToString()` prints `Token = ***`.
- `ConfigurationException` names the variable but never its value, and keeps no inner exception.
- Parse-error messages have unmatched argument values replaced by `***`.

---

## 8. Concurrency notes

```mermaid
flowchart LR
    subgraph TP["Thread pool"]
        T1["Rx DefaultScheduler<br/>poller timer and fetch"]
        T2["ListenAsync loop<br/>WebSocket frames"]
    end
    T1 -- OnNext --> M{{"Merge<br/>serialises OnNext"}}
    T2 -- OnNext --> M
    M --> S["Scan + Render<br/>one event at a time"]
    S --> Q["Concat queue"]
    Q --> D["one draw at a time<br/>inside drawGate"]
```

- **`Merge` serialises.** Rx guarantees that observers are not called concurrently, so `Scan` needs no lock even
  though poll results and button events arrive on different threads.
- **`Concat` queues draws.** If the bar is slow, later views wait their turn. Each draw re-checks `lastDrawn` when it
  starts, so a queued view equal to the one just drawn is skipped.
- **Fetches never overlap.** The poller's next `Timer` starts only after the previous fetch completes (`Repeat`
  re-subscribes on completion).
- **Time is injectable.** `IScheduler` comes from DI (`DefaultScheduler.Instance` in production). Tests pass a
  `TestScheduler` to drive the poller and the clock in `Render(scheduler.Now)` without waiting. The button stream's
  reconnect back-off also takes a scheduler parameter, but `BarFactory` always passes `DefaultScheduler.Instance`.
- **Verbosity is late-bound.** Loggers are created before `--verbose` is parsed. The log filter reads the
  `Verbosity` singleton at log time, and the command action sets it before resolving `ClaudeLimitsCommand`.

---

## 9. Adding a new watch command

The design lets a new "show X on the bar" command reuse the Core machinery.

```mermaid
flowchart LR
    A["Define a state record<br/>+ pure update methods"] --> B["Map each source to<br/>IObservable#lt;Func#lt;S,S#gt;#gt;<br/>(Poller.Poll, buttons, ...)"]
    B --> C["Write render: S to TView?<br/>(TView = value-equal record)"]
    C --> D["Implement IDisplay#lt;TView#gt;<br/>(layout to DisplayElements)"]
    D --> E["DisplayWatch.RunAsync(initial,<br/>Merge(...), render, display, ...)"]
```

DisplayWatch then provides the initial clear, deduplicated and serialised draws, transient-failure tolerance,
cancellation, and the guaranteed clear on exit.
