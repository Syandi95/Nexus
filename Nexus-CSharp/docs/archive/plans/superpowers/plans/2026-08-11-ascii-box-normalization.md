# ASCII Box Normalization V2 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deterministically repair border-edge mismatch in AI-emitted ASCII boxes at the single existing detection entry point, with byte-verbatim behavior for every ambiguous or tidy case.

**Architecture:** One pure static helper (`NormalizeAsciiDiagram`) inside `NexusMessageParser`, called exactly once at the tail of `TryParseAsciiDiagram`. The existing detection predicates (`IsAsciiBoxBorderLine` / `IsAsciiBoxContentLine`) are reused unchanged; detection logic is NOT duplicated. Only pure-border lines are rebuilt; content lines (including whitespace-only lines) are never modified.

**Tech Stack:** C# / .NET 8, xUnit integration tests (reflection into `Nexus.App`, mirroring `AIWorkspaceMessageParserTests.cs`), WPF host already provided by `WpfAppHost` for render-path tests.

## Global Constraints

- No new production class/service; no new dependency.
- `MeasureDocumentWidth` MUST NOT be modified.
- `MessageContentPresenter` MUST NOT be modified.
- Renderer MUST NOT be modified.
- `TryParseAsciiDiagram` remains the single detector; normalizer is a transform at its tail (one call site).
- Ambiguous or tidy ASCII MUST be returned byte-for-byte unchanged.
- Content lines MUST never be modified; whitespace-only lines MUST never be modified.
- Every non-whitespace content line is eligibility evidence: it MUST end with
  `|` at the same right edge `C`; any open-ended line or a different right edge
  makes the complete diagram verbatim.
- Only pure borders may be repaired; repair target is the content-proven edge `C`.
- Empty / border-only boxes MUST never be normalized; no capability may be added for them (`edge == -1` ⇒ verbatim).
- Every non-whitespace-only content line MUST end exactly at `C`; any open-ended
  line or a different right edge → verbatim.
- Whitespace-only lines are ignored (never content, never disqualify, never modified, preserved).
- Deterministic; single helper; single call site.
- O(N), N = total characters of `diagramLines`; exactly two linear passes.
- Border state is O(1): `borderCount`, `minBorderRight`, `maxBorderRight` — NO `List<int>`, NO `borderRights.Any(...)`.
- `bool[] isBorder` for O(1) membership (only size-N allocation); no unnecessary LINQ in the Normalizer.
- No WPF, no font measurement, no recursion, no full-response re-parse, no large object graph.

---

### Task 1: RED — regression tests for the Normalizer

**Files:**
- Create: `src/03_App/Nexus.Integration.Tests/AsciiDiagramNormalizationTests.cs`

**Interfaces:**
- Consumes: `NexusMessageParser.Parse(string, bool)` via reflection (same pattern as `AIWorkspaceMessageParserTests`), plus the private static `NormalizeAsciiDiagram(List<string>)` for the whitespace-only and empty-box cases that the parser cannot deliver to the helper.
- Produces: `AsciiDiagramNormalizationTests` with `SingleCodeBlockContent(string)` helper returning the only `CodeBlock.Content`.
- **Audit gate (no pass-by-rejection):** every parser-path fixture below is verified to produce exactly one `CodeBlock` (`Assert.Single` on `Blocks`). If a fixture failed `TryParseAsciiDiagram`, the test FAILS loudly instead of passing, so it can never green because the parser rejected the input. Fixtures that the parser's gather loop cannot deliver (interior whitespace-only line; border-only empty box — the loop only collects border/content lines and `collected.Count < 3` rejects border-only) invoke `NormalizeAsciiDiagram` directly, which excludes parser rejection by construction.

- [ ] **Step 1: Write the failing test class**

Before the existing repair tests, add these V2 contract cases:

```csharp
[Fact]
public void Normalize_MixedOpenAndClosedContent_ReturnsByteForByte()
{
    const string source =
        "+--------------------+\n" +
        "| Header             |\n" +
        "| AI Workspace\n" +
        "| Ready              |\n" +
        "+------------------+";
    Assert.Equal(source, SingleCodeBlockContent(source));
}

[Fact]
public void Normalize_ClosedContentWithDifferentRightEdges_ReturnsByteForByte()
{
    const string source =
        "+--------------------+\n" +
        "| Header       |\n" +
        "| AI Workspace         |\n" +
        "| Ready        |\n" +
        "+------------------+";
    Assert.Equal(source, SingleCodeBlockContent(source));
}
```

Keep the existing closed-content mismatch tests as the positive V2 proof.

```csharp
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace Nexus.Integration.Tests;

public class AsciiDiagramNormalizationTests
{
    private const string BrokenBottom =
        "+--------------------+\n" +
        "| Header             |\n" +
        "| AI Workspace       |\n" +
        "| Ready              |\n" +
        "+-------------------+";

    private const string BrokenTop =
        "+-------------------+\n" +
        "| Header             |\n" +
        "| AI Workspace       |\n" +
        "| Ready              |\n" +
        "+--------------------+";

    private const string Repaired =
        "+--------------------+\n" +
        "| Header             |\n" +
        "| AI Workspace       |\n" +
        "| Ready              |\n" +
        "+--------------------+";

    [Fact]
    public void Normalize_BrokenBottomBorder_RepairsToContentEdge()
    {
        Assert.Equal(Repaired, SingleCodeBlockContent(BrokenBottom));
    }

    [Fact]
    public void Normalize_BrokenTopBorder_RepairsToContentEdge()
    {
        Assert.Equal(Repaired, SingleCodeBlockContent(BrokenTop));
    }

    [Fact]
    public void Normalize_TidyBox_ReturnedByteForByte()
    {
        const string tidy =
            "+----------------------+\n" +
            "|                      |\n" +
            "|   TEST               |\n" +
            "|                      |\n" +
            "+----------------------+";
        Assert.Equal(tidy, SingleCodeBlockContent(tidy));
    }

    [Fact]
    public void Normalize_OpenEndedKartuStyle_ReturnedByteForByte()
    {
        const string openEnded =
            "+--------------------------+\n" +
            "| Baris yang sangat panjang sekali dan tanpa penutup kanan\n" +
            "| baris kedua tidak punya penutup kanan\n" +
            "+-------------------------+";
        Assert.Equal(openEnded, SingleCodeBlockContent(openEnded));
    }

    [Fact]
    public void Normalize_InteriorPlusInContent_ReturnedByteForByte()
    {
        const string interiorPlus =
            "+----------------+\n" +
            "| A + B          |\n" +
            "| C              |\n" +
            "+---------------+";
        Assert.Equal(interiorPlus, SingleCodeBlockContent(interiorPlus));
    }

    [Fact]
    public void Normalize_InconsistentTrailingPipes_ReturnedByteForByte()
    {
        const string inconsistent =
            "+----------------+\n" +
            "| Header          |\n" +
            "| Short  |\n" +
            "| Longer          |\n" +
            "+----------------+";
        Assert.Equal(inconsistent, SingleCodeBlockContent(inconsistent));
    }

    [Fact]
    public void Normalize_InconsistentLeftEdge_ReturnedByteForByte()
    {
        const string badLeft =
            "+----------------+\n" +
            " | Header         |\n" +
            "| Bottom          |\n" +
            "+----------------+";
        Assert.Equal(badLeft, SingleCodeBlockContent(badLeft));
    }

    [Fact]
    public void Normalize_NonPureBorder_ReturnedByteForByte()
    {
        const string nonPure =
            "+--------------+---+\n" +
            "| Header        |   |\n" +
            "|              +---+\n" +
            "+------------------+";
        Assert.Equal(nonPure, SingleCodeBlockContent(nonPure));
    }

    [Fact]
    public void Normalize_MultiColumnBorder_ReturnedByteForByte()
    {
        const string multiColumn =
            "+-----------------+-----------------+\n" +
            "| Komponen        | Status          |\n" +
            "+-----------------+-----------------+\n" +
            "| Konfigurasi     | Aktif           |\n" +
            "+-----------------+-----------------+";
        Assert.Equal(multiColumn, SingleCodeBlockContent(multiColumn));
    }

    [Fact]
    public void Normalize_RepairPreservesContentExactly()
    {
        const string broken =
            "+--------------------+\n" +
            "| Header             |\n" +
            "| AI Workspace       |\n" +
            "| Ready              |\n" +
            "+-------------------+";

        var expectedContentLines = new[]
        {
            "+--------------------+",
            "| Header             |",
            "| AI Workspace       |",
            "| Ready              |",
            "+--------------------+"
        };
        var actual = SingleCodeBlockContent(broken).Split('\n');
        Assert.Equal(expectedContentLines[1], actual[1]);
        Assert.Equal(expectedContentLines[2], actual[2]);
        Assert.Equal(expectedContentLines[3], actual[3]);
    }

    // Content stick-out guard, BEFORE the edge C is known: an open-ended line
    // longer than the eventual edge must disqualify the whole box -> verbatim.
    [Fact]
    public void Normalize_ContentExtendsBeyondDefinedEdge_ReturnedByteForByte()
    {
        const string extendsBeyond =
            "+-----------+\n" +
            "| abcdefghijklmnopqrstuvwxyz\n" +
            "| Short      |\n" +
            "+-----------+";
        Assert.Equal(extendsBeyond, SingleCodeBlockContent(extendsBeyond));
    }

    // Content stick-out guard, AFTER the edge C is already known: same guard,
    // different branch (edge >= 0 && last > edge). Also parser-path valid.
    [Fact]
    public void Normalize_ContentExtendsBeyondDefinedEdgeAfterEdge_ReturnedByteForByte()
    {
        const string extendsBeyond =
            "+-----------+\n" +
            "| Short      |\n" +
            "| abcdefghijklmnopqrstuvwxyz\n" +
            "+-----------+";
        Assert.Equal(extendsBeyond, SingleCodeBlockContent(extendsBeyond));
    }

    // Whitespace-only lines can't be delivered through TryParseAsciiDiagram's
    // gather loop (only border/content lines are collected), so the rule is
    // exercised two ways: (A) parser path shows a trailing whitespace-only line
    // stays OUTSIDE the diagram and the box is untouched; (B) direct invocation
    // of the private NormalizeAsciiDiagram shows an interior whitespace-only
    // line is ignored (repair still happens) and preserved byte-for-byte.
    [Fact]
    public void Normalize_WhitespaceOnlyLine_IsIgnoredAndPreserved()
    {
        const string box =
            "+--------+\n" +
            "| A      |\n" +
            "| B      |\n" +
            "+--------+";
        Assert.Equal(box, SingleCodeBlockContent(box + "\n        "));

        var lines = new System.Collections.Generic.List<string>
        {
            "+--------+",
            "| A      |",
            "        ",
            "| B      |",
            "+-------+"
        };

        NormalizeList(lines);
        Assert.Equal("+--------+", lines[0]);
        Assert.Equal("        ", lines[2]); // untouched, byte-for-byte
        Assert.Equal("| A      |", lines[1]);
        Assert.Equal("| B      |", lines[3]);
        Assert.Equal("+--------+", lines[4]);
    }

    // Empty / border-only box: no box-terminated content -> no proof of C ->
    // must NEVER be normalized, even though the two borders disagree.
    // TryParseAsciiDiagram rejects border-only diagrams (collected.Count < 3),
    // so this is exercised by direct invocation of private NormalizeAsciiDiagram.
    [Fact]
    public void Normalize_EmptyBoxNoContentEdge_ReturnedByteForByte()
    {
        var lines = new System.Collections.Generic.List<string>
        {
            "+--------+",
            "+-------+"
        };

        NormalizeList(lines);
        Assert.Equal("+--------+", lines[0]);
        Assert.Equal("+-------+", lines[1]);
    }

    // --- helpers (mirror existing test infra) ---

    // Throws loudly if the fixture does not produce exactly one CodeBlock, so a
    // parser-path test can never green because the parser rejected the input.
    private static string SingleCodeBlockContent(string source)
    {
        var document = Parse(source, true);
        var blocks = EnumerableProperty(document, "Blocks");
        var code = Assert.Single(
            blocks,
            block => block.GetType().Name == "CodeBlock");
        return StringProperty(code, "Content");
    }

    private static object Parse(string source, bool includeCodeHeuristic)
    {
        var parser = Activator.CreateInstance(Type_, nonPublic: true);
        var parse = Type_.GetMethod(
            "Parse",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null);
        Assert.NotNull(parse);
        return parse!.Invoke(parser!, new object[] { source, includeCodeHeuristic })!;
    }

    private static void NormalizeList(System.Collections.Generic.List<string> lines)
    {
        var method = Type_.GetMethod(
            "NormalizeAsciiDiagram",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { lines });
    }

    private static readonly Type Type_ = Type.GetType(
        "Nexus.App.AIWorkspace.Markdown.NexusMessageParser, Nexus.App",
        throwOnError: false)!
        ?? throw new XunitException("NexusMessageParser is missing.");

    private static System.Collections.Generic.List<object> EnumerableProperty(
        object instance,
        string propertyName)
    {
        var value = instance.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as IEnumerable;
        if (value is null)
        {
            throw new XunitException(
                $"{instance.GetType().Name}.{propertyName} is missing or not enumerable.");
        }

        return value.Cast<object>().ToList();
    }

    private static string StringProperty(object instance, string propertyName) =>
        instance.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as string
        ?? throw new XunitException($"{instance.GetType().Name}.{propertyName} is missing.");
}
```

- [ ] **Step 2: Run tests to verify they FAIL**

```bash
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~AsciiDiagramNormalizationTests" --nologo
```

Expected RED:
- FAIL: `Normalize_BrokenBottomBorder_RepairsToContentEdge`, `Normalize_BrokenTopBorder_RepairsToContentEdge` (borders stay mismatched today).
- FAIL: `Normalize_WhitespaceOnlyLine_IsIgnoredAndPreserved`, `Normalize_EmptyBoxNoContentEdge_ReturnedByteForByte` (their direct-invocation part cannot find `NormalizeAsciiDiagram` yet — `Assert.NotNull` fails; this is a legitimate RED, not pass-by-rejection).
- PASS: the existing preservation tests. V2's mixed-open/closed test is the
  required RED: V1 rebuilds the mismatched pure border from its closed rows.

- [ ] **Step 3: Commit RED**

```bash
git add src/03_App/Nexus.Integration.Tests/AsciiDiagramNormalizationTests.cs
git commit -m "test(ascii): RED for deterministic border normalization"
```

---

### Task 2: GREEN — implement the Normalizer helper

**Files:**
- Modify: `src/03_App/Nexus.App/AIWorkspace/Markdown/NexusMessageParser.cs`

**Interfaces:**
- Consumes: existing `IsAsciiBoxBorderLine`, `IsAsciiBoxContentLine` (unchanged), `System.Linq` (already imported at top of file — only needed by the pre-existing detection code, NOT by the Normalizer).
- Produces: `NormalizeAsciiDiagram(List<string>)` called as the last statement of `TryParseAsciiDiagram` before `diagramLines = collected`; private utilities `TryParseBorder`, `RebuildBorder`, `FirstNonSpaceColumn`, `LastNonSpaceColumn`.

- [ ] **Step 1: Add the single call site inside TryParseAsciiDiagram**

In `TryParseAsciiDiagram` (currently `NexusMessageParser.cs:339-352`), change the final block to:

```csharp
        if (collected.Count < 3 ||
            !IsAsciiBoxBorderLine(collected[^1]) ||
            !collected
                .Skip(1)
                .Take(collected.Count - 2)
                .Any(IsAsciiBoxContentLine))
        {
            return false;
        }

        NormalizeAsciiDiagram(collected);
        diagramLines = collected;
        consumedLines = collected.Count;
        return true;
```

- [ ] **Step 2: Add the audited helper + private utilities**

Insert directly after `TryParseAsciiDiagram` (before `IsAsciiBoxBorderLine`). Border collection and content analysis are fused into ONE linear pass; a second linear pass rebuilds borders. Every non-whitespace content line must close at the same edge. No mutation happens until all checks have passed. Border state is O(1) scalars (NO `List<int>`, NO LINQ).

```csharp
    // Repairs ONLY border-edge mismatch that can be proven deterministically.
    // Content lines are never modified and whitespace-only lines are ignored
    // (never content, never disqualify, never modified).  Empty or tidy or
    // otherwise ambiguous diagrams are left byte-for-byte unchanged (the list
    // is untouched).
    private static void NormalizeAsciiDiagram(List<string> diagramLines)
    {
        var count = diagramLines.Count;
        var isBorder = new bool[count];
        var borderCount = 0;
        var minBorderRight = int.MaxValue;
        var maxBorderRight = int.MinValue;
        var left = -1;
        var edge = -1;

        for (var i = 0; i < count; i++)
        {
            var line = diagramLines[i];
            if (IsAsciiBoxBorderLine(line))
            {
                if (!TryParseBorder(line, out var borderLeft, out var borderRight))
                {
                    return; // non-pure border -> ambiguous -> verbatim
                }

                if (left == -1)
                {
                    left = borderLeft;
                }
                else if (borderLeft != left)
                {
                    return; // borders must share a single left edge
                }

                isBorder[i] = true;
                borderCount++;
                minBorderRight = Math.Min(minBorderRight, borderRight);
                maxBorderRight = Math.Max(maxBorderRight, borderRight);
                continue;
            }

            var leading = FirstNonSpaceColumn(line);
            if (leading < 0)
            {
                continue; // whitespace-only line: ignored, untouched, preserved
            }

            if (!IsAsciiBoxContentLine(line) ||
                line.Contains('+'))
            {
                return; // nested or tabular structure -> verbatim
            }

            if (leading != left)
            {
                return; // content left edge must match the border left edge
            }

            var last = LastNonSpaceColumn(line);
            if (line[last] != '|')
            {
                return; // every content line must close the same rectangular box
            }

            if (edge == -1)
            {
                edge = last;
            }
            else if (edge != last)
            {
                return; // content right edges disagree -> verbatim
            }
        }

        if (borderCount < 2 ||
            edge <= left + 1 ||
            (minBorderRight == maxBorderRight && maxBorderRight == edge))
        {
            return; // needs top+bottom border, a content-proven C, a real mismatch
        }

        for (var i = 0; i < count; i++)
        {
            if (isBorder[i])
            {
                diagramLines[i] = RebuildBorder(left, edge);
            }
        }
    }

    // Pure border: exactly two '+' (first and last non-space column), only '-'
    // between them.  Strict enough that rebuild can safely pad or trim dashes.
    private static bool TryParseBorder(string line, out int left, out int right)
    {
        left = FirstNonSpaceColumn(line);
        right = LastNonSpaceColumn(line);
        if (left < 0 ||
            right <= left ||
            line[left] != '+' ||
            line[right] != '+')
        {
            return false;
        }

        for (var i = left + 1; i < right; i++)
        {
            if (line[i] != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string RebuildBorder(int left, int right) =>
        new string(' ', left) +
        '+' +
        new string('-', right - left - 1) +
        '+';

    private static int FirstNonSpaceColumn(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastNonSpaceColumn(string line)
    {
        for (var i = line.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return -1;
    }
```

- [ ] **Step 3: Run the new tests to verify they PASS**

```bash
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~AsciiDiagramNormalizationTests" --nologo
```

Expected: all 16 PASS.

- [ ] **Step 4: Commit GREEN**

```bash
git add src/03_App/Nexus.App/AIWorkspace/Markdown/NexusMessageParser.cs
git commit -m "feat(ascii): deterministically normalize border-edge mismatch"
```

---

### Task 3: Full regression suite

**Files:** none (tests only)

- [ ] **Step 1: Run parser/rendering/ASCII/WPF suites together**

```bash
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~AIWorkspaceMessageParserTests|FullyQualifiedName~AIWorkspaceRenderingTests|FullyQualifiedName~ASCIIProbeRuntimeTests|FullyQualifiedName~ASCIIFinalProbeTests|FullyQualifiedName~AsciiCodeBlockSizeToContentTests|FullyQualifiedName~ZzDiagnosticTests|FullyQualifiedName~AsciiDiagramNormalizationTests" --nologo
```

Expected: 0 failed. Existing fixtures (RealDiagram KARTU-style, multi-column boxes, tidy boxes, empty boxes) are tidy/open-ended/empty → verbatim → unchanged.

- [ ] **Step 2: Build the app to confirm no compile regressions**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj --nologo
```

Expected: 0 errors.

- [ ] **Step 3: Confirm no source drift beyond Tasks 1-2**

```bash
git status --short
```

Expected: only Task 1 (test) and Task 2 (parser) files changed from this plan.

---

### Task 4: Runtime verification

**Files:** none (observational only; no production changes)

- [ ] **Step 1: Verify render-path size-to-content still green**

```bash
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~AsciiCodeBlockSizeToContentTests" --nologo
```

Expected: PASS (measured >= required; visual lines == logical lines) on the unmodified RealDiagram — proves `MeasureDocumentWidth` path unaffected.

- [ ] **Step 2: Confirm normal responses unaffected**

```bash
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~AIWorkspaceMessageParserTests" --nologo
```

Expected: 0 failed (paragraphs, lists, tables, fenced code untouched).

- [ ] **Step 3: Confirm only the expected source files differ from baseline**

```bash
git status --short
```

Expected: `AsciiDiagramNormalizationTests.cs` (added), `NexusMessageParser.cs` (modified). No changes to `MeasureDocumentWidth`, `MessageContentPresenter`, renderer, or any other production file.
