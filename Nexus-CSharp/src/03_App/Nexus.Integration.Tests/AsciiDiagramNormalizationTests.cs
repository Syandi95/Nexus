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

    // Every non-whitespace content line participates in rectangular proof.
    // V1 incorrectly used only already-closed lines to infer C, then repaired
    // this mixed layout. V2 must return it exactly as generated.
    [Fact]
    public void Normalize_MixedOpenAndClosedContent_ReturnedByteForByte()
    {
        const string mixed =
            "+--------------------+\n" +
            "| Header             |\n" +
            "| AI Workspace\n" +
            "| Ready              |\n" +
            "+------------------+";

        Assert.Equal(mixed, SingleCodeBlockContent(mixed));
    }

    [Fact]
    public void Normalize_AllClosedContentWithDifferentRightEdges_ReturnedByteForByte()
    {
        const string inconsistent =
            "+--------------------+\n" +
            "| Header       |\n" +
            "| AI Workspace         |\n" +
            "| Ready        |\n" +
            "+------------------+";

        Assert.Equal(inconsistent, SingleCodeBlockContent(inconsistent));
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
