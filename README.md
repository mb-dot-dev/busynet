# busynet

Console application to display various information on a [BUSY Bar](https://busy.bar), written in .NET.

A port of [busyboy](https://github.com/mb-dot-dev/busyboy) (Python). Commands are being ported one at a time;
currently available: `busynet claude limits`, `busynet gh workflow` and `busynet bkk departures`.

## Install

### Homebrew (macOS arm64, Linux x64)

Installs a prebuilt native AOT binary from the [mb-dot-dev/tap](https://github.com/mb-dot-dev/homebrew-tap) tap
(no .NET runtime needed). Other platforms should use the .NET tool below.

```sh
brew install mb-dot-dev/tap/busynet
```

Upgrade or remove it:

```sh
brew update && brew upgrade busynet
brew uninstall busynet
```

Each `vX.Y.Z` tag publishes a GitHub release and updates the formula automatically.

### .NET tool

busynet is packaged as a .NET tool and is not published to NuGet. Pack it locally and install from the folder:

```sh
dotnet pack src/Busynet.Cli -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg busynet
```

Upgrade after packing a new version, or remove it:

```sh
dotnet tool update --global --add-source ./nupkg busynet
dotnet tool uninstall --global busynet
```

## Configuration

| Setting          | Option    | Environment variable | Default      |
| ---------------- | --------- | -------------------- | ------------ |
| BUSY Bar host    | `--host`  | `BUSYBOY_HOST`       | `10.0.4.20` |
| BUSY Bar token   | `--token` | `BUSYBOY_TOKEN`      | none (optional) |
| BKK API key      | none      | `BKK_API_KEY`        | none (required by `bkk departures`) |

The environment variable names match busyboy's on purpose, so both tools can share one configuration.
Command-line options override environment variables.

## Usage

```sh
busynet claude limits [--interval <seconds>] [--host <host>] [--token <token>] [--verbose]
```

Shows your Claude usage limits on the bar and refreshes them every `--interval` seconds (default 180). Pressing the
Start button toggles between the 5-hour and weekly limits. Labels look like `5h 85% 3h49m` (5-hour window: percent used, time until
reset) and `wk 40% 6d` (weekly window). Press Ctrl+C to stop; the bar is cleared on exit.

```sh
busynet gh workflow <WORKFLOW> [--branch <branch>] [--repo <owner/name>] [--interval <seconds>]
                               [--host <host>] [--token <token>] [--verbose]
```

Watches a GitHub Actions workflow and shows its latest run on the bar, polling every `--interval` seconds (default
10). `WORKFLOW` is a workflow id, file name (`ci.yaml`) or display name (`CI`). The top row shows the repository; the
bottom row shows the open pull request (`#12 CI`), or the branch when there is none (`main CI`); a status icon matching
GitHub's own sits to the left. `--branch` and `--repo` default to the current checkout's branch and its `origin`
remote. The GitHub token comes from the `gh` CLI (`gh auth login`), else from `GITHUB_TOKEN`. Press Ctrl+C to stop;
the bar is cleared on exit.

```sh
busynet bkk departures [STOP_ID] [--interval <seconds>] [--rotate <seconds>]
                       [--host <host>] [--token <token>] [--verbose]
```

Rotates the next 3 departures from a Budapest public-transport stop on the bar, one every `--rotate` seconds
(default 5), polling the [BKK FUTÁR API](https://opendata.bkk.hu) every `--interval` seconds (default 30). `STOP_ID`
defaults to `BKK_F04720` (Kisfaludy utca, Budaörs). The top row shows the position, line and time
(`1. 922 17'`); the bottom row shows the destination. Times are minutes until departure, or the clock time once a
departure is an hour or more away; a `~` marks a timetable time with no live prediction. The API key is read from
`BKK_API_KEY` only, so it never lands in shell history:

```sh
BKK_API_KEY=$(op read "op://<vault>/<item>/<field>") busynet bkk departures
```

Press Ctrl+C to stop; the bar is cleared on exit.

## CLI contract

- Success is silent and exits with `0`.
- A runtime failure prints its message as a single line to stderr and exits with `1`.
- A command-line parse or validation error exits with `2`.
- `--verbose` writes debug logs to stderr and prints the full exception instead of the one-line message.

## Development

See [docs/architecture.md](docs/architecture.md) for how the app works inside (event pipeline, class and sequence
diagrams).

```sh
dotnet build -c Release -warnaserror
dotnet test -c Release
```
