# claude limits: time-progress marker

## Intent
On `busynet claude limits`, draw a white vertical line on the gauge at the point the time window
has reached (5 hours for the session limit, 7 days for the weekly limit), so usage (the fill) can
be compared with elapsed time at a glance.

## Goals / non-goals
- Goal: a 1px white line on both views, positioned by elapsed share of the window.
- Non-goal: changing labels, colours, polling or the Start-button toggle.
- Anti-goal: hiding the fill; the line must stay visible on an empty, normal or red fill.

## Constraints
- `LimitGauge` stays a value-equal record so redraw deduplication keeps working.
- Native AOT: no new reflection-based serialization.
- No `ResetsAt` from the API means no marker (same as the label dropping its time).

## Approaches
1. **Separate white rectangle on top of the fill (chosen).** Simple, visible on any fill colour.
2. Leave a gap in the fill at the time position. Hides usage pixels; rejected.
3. Recolour one fill pixel. Invisible when the fill is shorter than the marker; rejected.

## Design (verified on the physical bar, 1px x 8px looked good)
- Progress = clamp(1 - (ResetsAt - now) / window, 0, 1); windows: 5h session, 7d weekly.
- `LimitGauge` gets `int? MarkerOffset` (0..TrackWidth-1 px within the track), computed by
  `LimitGauge.From(label, percent, timeProgress)`.
- `LimitsLayout.Build` appends `RectangleElement("limit-time", 1 + offset, 8, 1, 8, [#FFFFFFFF])`
  after the fill when `MarkerOffset` is set.
- `LimitsState.Render` computes the progress from `now`, the kind and the window's `ResetsAt`.
- Tests: progress maths (start, middle, end, past reset, no reset), layout (present/absent,
  position, clamped at the last pixel). Update README and `docs/architecture.md`.
