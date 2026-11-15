# busynet

.NET console app that shows information on a BUSY Bar. Commands: `claude limits`, `gh workflow`, `bkk departures`.
See `README.md` for usage and `docs/architecture.md` for internals.

## Build and test

- `dotnet test` runs all tests; the build uses `-warnaserror` and central package versions.
- Release builds are native AOT: no reflection-based serialization (System.Text.Json source generation, Refit
  generated clients).

## BKK FUTÁR API (`bkk departures`)

- OpenAPI spec: <https://opendata.bkk.hu/docs/futar-openapi.yaml> (a copy is in `docs/references/openapi.yaml`).
- The API key is read from the `BKK_API_KEY` environment variable only. Fetch it from 1Password with the reference
  `op://Private/BKK OpenData API key/API key`:

  ```sh
  BKK_API_KEY=$(op read "op://Private/BKK OpenData API key/API key") busynet bkk departures
  ```

- The key travels in the query string, so never log request URLs or put the key in output, commits or PRs.
- Without a key, stop ids can still be looked up in BKK's public GTFS `stops.txt`
  (<https://go.bkk.hu/api/static/v1/public-gtfs/budapest_gtfs.zip>); FUTÁR ids are the GTFS ids prefixed with `BKK_`.
- The default stop is `BKK_F04720` (Kisfaludy utca, Budaörs).

## Git

- Work on feature branches in the main clone (no worktrees); open PRs against `main`.
- Each `vX.Y.Z` tag triggers the release workflow.
