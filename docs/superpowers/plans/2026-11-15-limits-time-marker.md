# Limits Time Marker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Draw a 1px white vertical line on the `claude limits` gauge at the share of the 5-hour / 7-day window that has elapsed.

**Architecture:** `LimitsState.Render` computes time progress (0-1) from `now`, the limit kind and `ResetsAt`, and passes it to `LimitGauge.From`, which stores a pixel `MarkerOffset`. `LimitsLayout.Build` draws a white rectangle at that offset on top of the fill.

**Tech Stack:** .NET 11, xUnit, native AOT (no reflection serialization).

**Spec:** `docs/superpowers/specs/2026-11-15-limits-time-marker-design.md`

## Global Constraints

- `LimitGauge` stays a value-equal record (redraw dedupe relies on `Equals`).
- Marker: rectangle id `limit-time`, `y=8`, width 1, height 8, colour `#FFFFFFFF`, x = `1 + MarkerOffset`, drawn after `limit-fill` and before `limit-label`.
- Windows: session 5 hours, weekly 7 days. No `ResetsAt` means no marker.
- `MarkerOffset = min(round(progress * TrackWidth), TrackWidth - 1)`, progress clamped to 0-1 (verified on the bar: 60% gives x=43).
- Build uses `-warnaserror`; run `dotnet test` from the repo root.
- Work on branch `feature/limits-time-marker` in the main clone (no worktrees).

## Review Focus

- `ResetsAt` already in the past (window overdue): progress clamps to 1, marker at the last track pixel (offset 69), no exception.
- `ResetsAt` more than one window away (clock skew): progress clamps to 0, marker at offset 0.
- `ResetsAt` null: no marker, label still renders.
- 100% elapsed must not draw outside the 70px track.
- Marker shows with an empty fill (track-coloured 1px fill) and with the high (red) fill.

---

### Task 1: Marker in the gauge and layout

**Files:**
- Modify: `src/Busynet.Bar/Layouts/LimitGauge.cs`
- Modify: `src/Busynet.Bar/Layouts/LimitsLayout.cs`
- Test: `tests/Busynet.Bar.Tests/Layouts/LimitsLayoutTests.cs`

**Interfaces:**
- Produces: `LimitGauge(string Label, int FillWidth, bool High, int? MarkerOffset = null)`; `static LimitGauge From(string label, double percent, double? timeProgress = null)` where `timeProgress` is 0-1 (values outside are clamped).

- [ ] **Step 1: Write failing tests** in `LimitsLayoutTests`:
  - `Marker_offset_is_proportional_and_clamped` (Theory): progress `0 -> 0`, `0.6 -> 42`, `1 -> 69`, `1.5 -> 69`, `-0.2 -> 0` via `LimitGauge.From("x", 10, p).MarkerOffset`.
  - `No_time_progress_means_no_marker`: `From("x", 10).MarkerOffset` is null and `Build(...).Elements` has 3 elements.
  - `Marker_is_a_white_1px_rectangle_over_the_fill`: for `From("x", 30, 0.6)`, `Elements[2]` is a `RectangleElement` with Id `limit-time`, X 43, Y 8, Width 1, Height 8, FillColors `["#FFFFFFFF"]`; `Elements[1]` is `limit-fill`; `Elements[3]` is the `TextElement`.
  - `Marker_is_drawn_on_an_empty_and_on_a_high_gauge`: both `From("x", 0, 0.5)` and `From("x", 95, 0.5)` contain `limit-time`.
  - Update `Label_is_made_displayable` to read the label as the last element (`Elements[^1]`).

- [ ] **Step 2: Run** `dotnet test tests/Busynet.Bar.Tests` - expect compile/test failures.

- [ ] **Step 3: Implement** the record parameter and `From` overload in `LimitGauge.cs` (clamp, round half away from zero is fine, cap at `TrackWidth - 1`), and in `LimitsLayout.Build` build the element list so the marker rectangle is inserted after the fill only when `MarkerOffset` is set. Add a `MarkerColor = "#FFFFFFFF"` constant next to the others.

- [ ] **Step 4: Run** `dotnet test tests/Busynet.Bar.Tests` - expect all pass (serialization and `BarDisplay` tests unaffected).

- [ ] **Step 5: Commit**

```bash
git add src/Busynet.Bar tests/Busynet.Bar.Tests
git commit -m "Draw a time marker on the limit gauge"
```

### Task 2: Compute time progress in the state, docs

**Files:**
- Modify: `src/Busynet.Cli/Features/ClaudeLimits/LimitKind.cs` (window lengths)
- Modify: `src/Busynet.Cli/Features/ClaudeLimits/LimitsState.cs`
- Modify: `tests/Busynet.Cli.Tests/Features/ClaudeLimits/ClaudeLimitsWatchTests.cs` (lines 99 and 215 expect the marker now)
- Create: `tests/Busynet.Cli.Tests/Features/ClaudeLimits/LimitsStateTests.cs`
- Modify: `README.md` (claude limits section), `docs/architecture.md` (`LimitGauge` description near line 157 and the class diagram near line 329, `LimitsState` section 6.1, gauge section near line 849)

**Interfaces:**
- Consumes: `LimitGauge.From(string, double, double?)` from Task 1.
- Produces: `static class LimitWindows { static TimeSpan Of(LimitKind kind) }` (5h / 7d), and `LimitsState.Render(now)` returning a gauge with the marker.

- [ ] **Step 1: Write failing tests** in `LimitsStateTests` (fixed `now`, `Usage` with only the shown window mattering). Each asserts `new LimitsState(usage, kind).Render(now)!.MarkerOffset`:
  - Session, `ResetsAt = now + 2h`: 60% elapsed, offset 42.
  - Session, `ResetsAt = now + 5h`: offset 0. Session, `ResetsAt = now`: offset 69.
  - Session, `ResetsAt = now - 1h` (overdue): offset 69. Session, `ResetsAt = now + 6h` (skew): offset 0.
  - Weekly, `ResetsAt = now + 3.5d`: offset 35 (round of 0.5 * 70).
  - Weekly with `ResetsAt = null`: `MarkerOffset` null and label `wk 40%`.
  - Also update `ClaudeLimitsWatchTests` lines 99 and 215 to expect `LimitGauge.From("5h 85% 3h49m", 85, 71.0 / 300)`.

- [ ] **Step 2: Run** `dotnet test tests/Busynet.Cli.Tests` - expect failures.

- [ ] **Step 3: Implement** `LimitWindows.Of` and in `LimitsState.Render` compute `progress = 1 - (ResetsAt - now) / window` when `ResetsAt` is set (else null), passing it to `LimitGauge.From`.

- [ ] **Step 4: Update docs** to mention the white time marker (README: one sentence; architecture: add `MarkerOffset` to the `LimitGauge` description/diagram and the rendering notes).

- [ ] **Step 5: Run** `dotnet test` from the repo root - expect all pass, no warnings.

- [ ] **Step 6: Smoke test on the bar** (optional, needs usage credentials): `dotnet run --project src/Busynet.Cli -- claude limits`, confirm the line at the expected position, Ctrl+C.

- [ ] **Step 7: Commit**

```bash
git add src tests README.md docs
git commit -m "Show elapsed window time as a marker on claude limits"
```
