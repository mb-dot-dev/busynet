# busynet

Console application to display various information on a [BUSY Bar](https://busy.bar), written in .NET.

A port of [busyboy](https://github.com/mb-dot-dev/busyboy) (Python). Commands are being ported one at a time;
currently available: `busynet claude limits`.

## Install

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

The environment variable names match busyboy's on purpose, so both tools can share one configuration.
Command-line options override environment variables.

## Usage

```sh
busynet claude limits [--interval <seconds>] [--host <host>] [--token <token>] [--verbose]
```

Shows your Claude usage limits on the bar and refreshes them every `--interval` seconds (default 180). Pressing the
Start button toggles between the 5-hour and weekly limits. Labels look like `5h 85% 3h49m` (5-hour window: percent used, time until
reset) and `wk 40% 6d` (weekly window). Press Ctrl+C to stop; the bar is cleared on exit.

## CLI contract

- Success is silent and exits with `0`.
- A runtime failure prints its message as a single line to stderr and exits with `1`.
- A command-line parse or validation error exits with `2`.
- `--verbose` writes debug logs to stderr and prints the full exception instead of the one-line message.

## Development

```sh
dotnet build -c Release -warnaserror
dotnet test -c Release
```
