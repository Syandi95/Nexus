# ASCII Box Normalization V2 — Design Spec

**Date:** 2026-08-11
**Status:** Approved (V2: every non-whitespace content line must close at the same edge `C`; otherwise verbatim)

## Problem

Gemma/Qwen sometimes emit ASCII boxes whose border lines do not share the same
closing-edge column (e.g. a bottom border one dash shorter than the top). The
renderer reproduces content faithfully, so the malformed geometry displays
as-is. Wrapping is already solved by `MeasureDocumentWidth`; this spec addresses
*content geometry* only.

## Scope (V2)

- Only raw, unfenced box-like text that already passed `TryParseAsciiDiagram`.
- NOT: Markdown tables, fenced/indented code, arbitrary ASCII art, semantic UI.

## Principle

Normalize ONLY damage provable deterministically. If the structure is
ambiguous, return verbatim. Never guess the model's intent. Content is never
modified in any branch. Correctness > simplicity > micro-optimization.

## Placement

`private static void NormalizeAsciiDiagram(List<string> diagramLines)` inside
`NexusMessageParser`, invoked exactly once at the tail of
`TryParseAsciiDiagram` (after the existing acceptance checks, immediately
before `diagramLines = collected`). One helper, one call site.

## Definitions

- **Pure border:** exactly two `+` (first and last non-space column) and every
  char between them is `-`. Strict enough that rebuild can safely pad or trim
  dashes. Stricter than the existing `IsAsciiBoxBorderLine` (which tolerates
  interior spaces); a border that is not pure is therefore ambiguous.
- **Content line:** every non-whitespace line collected between borders whose
  first non-space character is `|`, including an open-ended line that does not
  end in `|`. Open-ended content is therefore evidence against rectangular-box
  proof; it is not skipped while determining eligibility.
- **Whitespace-only line:** a line with no non-whitespace character. It is NOT
  a content line: ignored when determining eligibility and the edge `C`, never
  modified, and always preserved byte-for-byte. (Today `TryParseAsciiDiagram`'s
  gather loop can only deliver border/content lines to the Normalizer
  — `NexusMessageParser.cs:325-337` — so a whitespace-only line is unreachable
  via the parser path; the rule below is a contractual guarantee of the helper
  and is verified by a direct-invocation test.)
- **Content-defined edge `C`:** the shared last non-space column of **every**
  non-whitespace content line. Every such line must end in `|` at `C`; this is
  the ONLY proof that borders should end at `C`.

## Eligibility for repair (ALL required)

1. Every border line present in the diagram is pure.
2. There are at least two border lines (a top and a bottom), all sharing the
   same left column `L`.
3. At least one content line exists, and **every non-whitespace content line**
   ends in `|` at the same right-edge column `C`. Any open-ended content line
   makes the structure ambiguous and returns it verbatim.
4. No content line contains `+` anywhere.
5. Every non-whitespace-only content line's first non-space char is `|` at
   column `L`.
6. Every non-whitespace-only content line ends exactly at `C`; an earlier or
   later right edge, including an open-ended line, returns verbatim with no
   mutation.
7. `C > L + 1`.
8. The border right columns are not already all equal to `C`
   (`minBorderRight == maxBorderRight == C` ⇒ verbatim).

## Empty box — NEVER normalized

An empty / border-only box such as

```
+--------+
+-------+
```

has no content line, so no content-defined edge `C` exists.
Without proof of `C` the normalizer MUST NOT normalize — even when the two
borders visibly disagree. The guard `edge` stays `-1` → `edge <= L + 1` →
verbatim, byte-for-byte. This is a hard rule: **no new capability may ever be
added to repair border-only or empty boxes.** Additionally, `TryParseAsciiDiagram`
rejects border-only diagrams (`collected.Count < 3`), so the helper would not
see one anyway; the rule is verified by direct invocation.

## Repair action

Rebuild each border line to span exactly `[L, C]`:

```
' ' * L + '+' + '-' * (C - L - 1) + '+'
```

Short borders are padded with `-`; long borders are trimmed (removing only
dashes from a pure border). Content lines (including whitespace-only lines) are
NEVER modified.

## Verbatim fallback (every other outcome)

Byte-for-byte unchanged. Affects:

- Empty / border-only box (no content-defined edge `C`).
- No content line, or any open-ended content (KARTU / mixed layout ASCII).
- Content right-edge columns disagree.
- Any content line containing `+` (nested / multi-column / tabular structure).
- Non-pure border (including `+----+----+` multi-column borders).
- Border left edges differ, or content left edge differs from border left edge.
- Any open-ended content line or content right edge different from `C`.
- Fewer than two borders, or edge not > `L + 1`.
- Already tidy diagram (borders already agree with `C`).

Whitespace-only lines never trigger a verbatim return; they are skipped.

## Exclusions / non-goals (confirmed & frozen)

- No new production class/service; no new dependency.
- No capability to repair empty / border-only boxes.
- `MeasureDocumentWidth` is NOT touched.
- `MessageContentPresenter` is NOT touched.
- Renderer is NOT touched.
- No second ASCII path anywhere; `TryParseAsciiDiagram` remains the single
  detector; `IsAsciiBoxBorderLine` / `IsAsciiBoxContentLine` remain unchanged
  and are reused by the normalizer.

## Performance (audited)

- O(N), where N = total number of characters across all `diagramLines`.
- Exactly two linear passes over the already-collected diagram:
  1. One fused analysis pass — border purity/collect, content eligibility, edge
     determination, and closed-edge agreement run together (no re-scan of any
     line).
  2. One rebuild pass — touches border lines only.
- Allocation-free apart from `bool[] isBorder` (the only size-N structure).
  Border state is O(1) scalars: `borderCount`, `minBorderRight`,
  `maxBorderRight`, `left`, `edge`. **No `List<int>`**.
- No LINQ inside the Normalizer (membership via `bool[]`; mismatch via
  `minBorderRight`/`maxBorderRight` comparison; edge agreement via `edge`
  comparisons). The only per-line scan is `line.Contains('+')`, part of
  O(line length).
- No re-scan of the response, no recursion, no WPF, no font measurement, no
  large object graph.
- Zero additional cost for non-diagram text (fails fast as today).

## Tests (decision matrix — 17)

1. Broken bottom border → repaired to content edge.
2. Broken top border → repaired to content edge.
3. Tidy box → byte-for-byte identical.
4. Open-ended / KARTU (no trailing `|`) → verbatim.
5. Interior `+` in content → verbatim.
6. Inconsistent trailing `|` columns → verbatim.
7. Inconsistent left edge → verbatim.
8. Non-pure border → verbatim.
9. Nested / multi-column structure → verbatim.
10. Content preservation (content byte-identical after repair).
11. Open-ended content before a closed line → verbatim.
12. Whitespace-only line → ignored and preserved byte-for-byte.
13. Open-ended content after a closed line → verbatim.
14. Empty / border-only box (no content-defined `C`) → verbatim.
15. Mixed closed and open-ended content → verbatim, byte-for-byte.
16. All closed content with different trailing-pipe columns → verbatim.
17. Whitespace-only line between closed content → preserved and ignored while
    the closed content still proves `C`.

## Constraints (frozen)

- Deterministic.
- Single helper, single call site.
- Content lines never modified; whitespace-only lines never modified.
- Empty boxes never normalized; no capability added for them.
- Ambiguous or tidy ASCII returned byte-for-byte unchanged.
- Minimal runtime burden — O(N), two linear passes, O(1) border state.
- Avoid `List.Contains` inside loops (use `bool[]`); avoid unnecessary LINQ.
