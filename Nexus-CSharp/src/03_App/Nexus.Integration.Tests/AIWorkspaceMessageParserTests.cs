using System.Collections;
using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace Nexus.Integration.Tests;

public class AIWorkspaceMessageParserTests
{
    [Fact]
    public void Parse_FencedPowerShell_SeparatesTextAndCode()
    {
        var document = Parse(
            "Sebelum\n\n```powershell\n$x = 1\nWrite-Output $x\n```\n\nSesudah");

        var blocks = Blocks(document);

        Assert.Collection(
            blocks,
            block =>
            {
                Assert.Equal("ParagraphBlock", block.GetType().Name);
                Assert.Contains("Sebelum", InlineText(block));
            },
            block =>
            {
                Assert.Equal("CodeBlock", block.GetType().Name);
                Assert.Equal("powershell", StringProperty(block, "Language"));
                Assert.Equal("$x = 1\nWrite-Output $x", StringProperty(block, "Content"));
            },
            block =>
            {
                Assert.Equal("ParagraphBlock", block.GetType().Name);
                Assert.Contains("Sesudah", InlineText(block));
            });
    }

    [Fact]
    public void Parse_IndentedCode_RemovesOneIndentLevel()
    {
        var document = Parse(
            "Contoh:\n\n    function Test-Nexus {\n        Write-Output 'ok'\n    }\n\nSelesai");

        var code = Assert.Single(Blocks(document), block => block.GetType().Name == "CodeBlock");

        Assert.Equal("text", StringProperty(code, "Language"));
        Assert.Equal(
            "function Test-Nexus {\n    Write-Output 'ok'\n}",
            StringProperty(code, "Content"));
    }

    [Fact]
    public void Parse_LooksLikePowerShell_UsesFallbackOnlyForFinalRender()
    {
        const string source =
            "function Test-Nexus {\n" +
            "param($Name)\n" +
            "Write-Output $Name\n" +
            "$Name";

        var streamingDocument = Parse(source, includeCodeHeuristic: false);
        var finalDocument = Parse(source, includeCodeHeuristic: true);

        Assert.DoesNotContain(
            Blocks(streamingDocument),
            block => block.GetType().Name == "CodeBlock");
        var code = Assert.Single(
            Blocks(finalDocument),
            block => block.GetType().Name == "CodeBlock");
        Assert.Equal("powershell", StringProperty(code, "Language"));
        Assert.Equal(source, StringProperty(code, "Content"));
    }

    [Fact]
    public void Parse_LooksLikeCode_DoesNotClassifyExplanatoryResponse()
    {
        const string source =
            "Berikut penjelasan:\n" +
            "function Test-Nexus {\n" +
            "Write-Output 'ok'\n" +
            "$ok = $true\n" +
            "Kesimpulan selesai.";

        var document = Parse(source);

        Assert.DoesNotContain(
            Blocks(document),
            block => block.GetType().Name == "CodeBlock");
    }

    [Fact]
    public void Parse_Paragraph_CreatesBoldItalicAndInlineCode()
    {
        var document = Parse("Teks **tebal**, *miring*, dan `Get-Item`.");
        var paragraph = Assert.Single(Blocks(document));
        var inlineNames = Inlines(paragraph).Select(inline => inline.GetType().Name).ToList();

        Assert.Contains("BoldInline", inlineNames);
        Assert.Contains("ItalicInline", inlineNames);
        Assert.Contains("InlineCodeInline", inlineNames);
        Assert.Equal(
            "Teks tebal, miring, dan Get-Item.",
            InlineText(paragraph));
    }

    [Fact]
    public void Parse_LinksAndImages_PreserveTargetsWithoutLoadingMedia()
    {
        var paragraph = Assert.Single(Blocks(Parse("Baca [panduan](https://example.com) dan ![diagram](assets/diagram.png).")));
        var link = Assert.Single(Inlines(paragraph), inline => inline.GetType().Name == "LinkInline");
        var image = Assert.Single(Inlines(paragraph), inline => inline.GetType().Name == "ImageInline");

        Assert.Equal("panduan", StringProperty(link, "Text"));
        Assert.Equal("https://example.com", StringProperty(link, "Target"));
        Assert.Equal("diagram", StringProperty(image, "Text"));
        Assert.Equal("assets/diagram.png", StringProperty(image, "Source"));
    }

    [Fact]
    public void Parse_OrderedAndUnorderedLists_PreserveOrderAndMarkers()
    {
        var document = Parse(
            "1. Pertama\n2. Kedua\n\n- Alpha\n* Beta");
        var lists = Blocks(document)
            .Where(block => block.GetType().Name == "ListBlock")
            .ToList();

        Assert.Equal(2, lists.Count);
        Assert.True(BoolProperty(lists[0], "IsOrdered"));
        Assert.False(BoolProperty(lists[1], "IsOrdered"));
        Assert.Equal(new[] { "Pertama", "Kedua" }, ListItemTexts(lists[0]));
        Assert.Equal(new[] { "Alpha", "Beta" }, ListItemTexts(lists[1]));
    }

    [Fact]
    public void Parse_Quote_CreatesQuoteBlockWithFormattedContent()
    {
        var document = Parse("> Gunakan **Nexus** dan `PowerShell`.");
        var quote = Assert.Single(Blocks(document));

        Assert.Equal("QuoteBlock", quote.GetType().Name);
        var nestedBlocks = EnumerableProperty(quote, "Blocks");
        var paragraph = Assert.Single(nestedBlocks);
        Assert.Equal("Gunakan Nexus dan PowerShell.", InlineText(paragraph));
        Assert.Contains(Inlines(paragraph), inline => inline.GetType().Name == "BoldInline");
        Assert.Contains(Inlines(paragraph), inline => inline.GetType().Name == "InlineCodeInline");
    }

    [Fact]
    public void Parse_MarkdownTable_CreatesHeaderAndBodyRows()
    {
        var document = Parse(
            "| Fitur | Status |\n" +
            "| --- | :---: |\n" +
            "| Table | Aktif |\n" +
            "| Theme | **Dinamis** |");

        var table = Assert.Single(
            Blocks(document),
            block => block.GetType().Name == "TableBlock");
        var headers = EnumerableProperty(table, "Headers");
        var rows = EnumerableProperty(table, "Rows");

        Assert.Equal(new[] { "Fitur", "Status" }, headers.Select(InlineText));
        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new[] { "Table", "Aktif" },
            EnumerableItems(rows[0]).Select(InlineText));
        Assert.Equal(
            new[] { "Theme", "Dinamis" },
            EnumerableItems(rows[1]).Select(InlineText));
    }

    [Fact]
    public void Parse_AsciiBoxDiagram_ClassifiesAsCodeBlockText()
    {
        var document = Parse(
            "+----------------------+\n" +
            "|                      |\n" +
            "|   TEST               |\n" +
            "|                      |\n" +
            "+----------------------+");

        var code = Assert.Single(
            Blocks(document),
            block => block.GetType().Name == "CodeBlock");
        Assert.Equal("text", StringProperty(code, "Language"));
        Assert.Equal(
            "+----------------------+\n" +
            "|                      |\n" +
            "|   TEST               |\n" +
            "|                      |\n" +
            "+----------------------+",
            StringProperty(code, "Content"));
    }

    [Fact]
    public void Parse_AsciiBoxDiagram_MultipleColumns_ClassifiesAsCodeBlockText()
    {
        var document = Parse(
            "+-----------------+-----------------+\n" +
            "| Komponen        | Status          |\n" +
            "+-----------------+-----------------+\n" +
            "| Konfigurasi     | Aktif           |\n" +
            "+-----------------+-----------------+");

        var code = Assert.Single(
            Blocks(document),
            block => block.GetType().Name == "CodeBlock");
        Assert.Equal("text", StringProperty(code, "Language"));
        Assert.StartsWith(
            "+-----------------+-----------------+",
            StringProperty(code, "Content"));
        Assert.EndsWith(
            "+-----------------+-----------------+",
            StringProperty(code, "Content"));
    }

    [Fact]
    public void Parse_AsciiBoxDiagram_FollowedByParagraph_KeepsSeparateBlocks()
    {
        var document = Parse(
            "+--------+\n" +
            "| Nexus  |\n" +
            "+--------+\n" +
            "\n" +
            "Penjelasan diagram.");

        var blocks = Blocks(document);
        Assert.Collection(
            blocks,
            block => Assert.Equal("CodeBlock", block.GetType().Name),
            block => Assert.Equal("ParagraphBlock", block.GetType().Name));
    }

    [Fact]
    public void Parse_AsciiBoxDiagram_InsideFencedCode_StaysFencedCodeBlock()
    {
        var document = Parse(
            "```text\n" +
            "+------+\n" +
            "|  AI  |\n" +
            "+------+\n" +
            "```");

        var code = Assert.Single(
            Blocks(document),
            block => block.GetType().Name == "CodeBlock");
        Assert.Equal("text", StringProperty(code, "Language"));
        Assert.Equal(
            "+------+\n" +
            "|  AI  |\n" +
            "+------+",
            StringProperty(code, "Content"));
    }

    [Fact]
    public void Parse_ParagraphWithPipeAndPlus_StaysParagraphBlock()
    {
        var document = Parse(
            "Gabungan tanda: A + B berada di | sisi | kanan dan sama panjang.");

        var paragraph = Assert.Single(Blocks(document));
        Assert.Equal("ParagraphBlock", paragraph.GetType().Name);
        Assert.Equal(
            "Gabungan tanda: A + B berada di | sisi | kanan dan sama panjang.",
            InlineText(paragraph));
    }

    [Fact]
    public void Parse_RealAIModelSelectorDiagram_ClassifiesAsCodeBlockText()
    {
        var document = Parse(
            "+------------------------------------+\n" +
            "|            AI MODEL SELECTOR       |\n" +
            "+------------------------------------+\n" +
            "| [ KARTU 1 ]    [ KARTU 2 ]         |\n" +
            "| Deskripsi sangat panjang yang membentang di dalam baris\n" +
            "| [ PILIH ]           [ PILIH ]      |\n" +
            "+------------------------------------+");

        var code = Assert.Single(
            Blocks(document),
            block => block.GetType().Name == "CodeBlock");
        Assert.Equal("text", StringProperty(code, "Language"));
        Assert.Equal(
            "+------------------------------------+\n" +
            "|            AI MODEL SELECTOR       |\n" +
            "+------------------------------------+\n" +
            "| [ KARTU 1 ]    [ KARTU 2 ]         |\n" +
            "| Deskripsi sangat panjang yang membentang di dalam baris\n" +
            "| [ PILIH ]           [ PILIH ]      |\n" +
            "+------------------------------------+",
            StringProperty(code, "Content"));
    }

    [Fact]
    public void Parse_FencedChoiceBlock_ReturnsChoiceBlockWithQuestionAndOptions()
    {
        var document = Parse(
            "```choice\n" +
            "? Pendekatan mana yang ingin digunakan?\n" +
            "- Opsi A: Entity Framework\n" +
            "- Opsi B: Dapper\n" +
            "```");

        var blocks = Blocks(document);
        var choice = Assert.Single(blocks);
        Assert.Equal("ChoiceBlock", choice.GetType().Name);
        Assert.Equal("Pendekatan mana yang ingin digunakan?", StringProperty(choice, "Question"));

        var options = EnumerableProperty(choice, "Options").Cast<string>().ToArray();
        Assert.Equal(2, options.Length);
        Assert.Equal("Opsi A: Entity Framework", options[0]);
        Assert.Equal("Opsi B: Dapper", options[1]);
    }

    [Fact]
    public void Parse_UnfencedChoiceBlock_ReturnsChoiceBlock()
    {
        var document = Parse(
            "choice\n" +
            "? Question\n" +
            "- Opt1\n" +
            "- Opt2");

        var blocks = Blocks(document);
        var choice = Assert.Single(blocks);
        Assert.Equal("ChoiceBlock", choice.GetType().Name);
        Assert.Equal("Question", StringProperty(choice, "Question"));

        var options = EnumerableProperty(choice, "Options").Cast<string>().ToArray();
        Assert.Equal(2, options.Length);
        Assert.Equal("Opt1", options[0]);
        Assert.Equal("Opt2", options[1]);
    }

    [Fact]
    public void Parse_BracketedLocalModelFormat_ReturnsChoiceBlockWithFallbackQuestion()
    {
        var document = Parse(
            "choice NEXUS:\n" +
            "[SQLite]\n" +
            "[LiteDB]\n" +
            "[EF Core + SQLite]");

        var blocks = Blocks(document);
        var choice = Assert.Single(blocks);
        Assert.Equal("ChoiceBlock", choice.GetType().Name);
        Assert.Equal("Pilih salah satu opsi berikut:", StringProperty(choice, "Question"));

        var options = EnumerableProperty(choice, "Options").Cast<string>().ToArray();
        Assert.Equal(3, options.Length);
        Assert.Equal("SQLite", options[0]);
        Assert.Equal("LiteDB", options[1]);
        Assert.Equal("EF Core + SQLite", options[2]);
    }

    [Fact]
    public void Parse_AsteriskOptions_ReturnsChoiceBlock()
    {
        var document = Parse(
            "choice:\n" +
            "? Question\n" +
            "* Option A\n" +
            "* Option B");

        var blocks = Blocks(document);
        var choice = Assert.Single(blocks);
        Assert.Equal("ChoiceBlock", choice.GetType().Name);
        Assert.Equal("Question", StringProperty(choice, "Question"));

        var options = EnumerableProperty(choice, "Options").Cast<string>().ToArray();
        Assert.Equal(2, options.Length);
        Assert.Equal("Option A", options[0]);
        Assert.Equal("Option B", options[1]);
    }

    [Fact]
    public void Parse_UnfencedInvalidLowerBound_DoesNotEmitChoiceBlock()
    {
        var document = Parse(
            "choice\n" +
            "- Only one");

        var blocks = Blocks(document);
        Assert.DoesNotContain(blocks, b => b.GetType().Name == "ChoiceBlock");
    }

    [Fact]
    public void Parse_UnfencedInvalidUpperBound_DoesNotEmitChoiceBlock()
    {
        var document = Parse(
            "choice\n" +
            "- A\n" +
            "- B\n" +
            "- C\n" +
            "- D\n" +
            "- E");

        var blocks = Blocks(document);
        Assert.DoesNotContain(blocks, b => b.GetType().Name == "ChoiceBlock");
    }

    [Fact]
    public void Parse_UnfencedInterruptedBlock_DoesNotAbsorbSubsequentTextIntoChoice()
    {
        var document = Parse(
            "choice\n" +
            "? Pilih langkah\n" +
            "- Opsi 1\n" +
            "- Opsi 2\n" +
            "Ini teks biasa setelah blok pilihan.\n" +
            "- Bukan bagian dari choice");

        var blocks = Blocks(document);
        Assert.Equal(3, blocks.Count);
        Assert.Equal("ChoiceBlock", blocks[0].GetType().Name);
        var options = EnumerableProperty(blocks[0], "Options").Cast<string>().ToArray();
        Assert.Equal(2, options.Length);
        Assert.Equal("Opsi 1", options[0]);
        Assert.Equal("Opsi 2", options[1]);

        Assert.Equal("ParagraphBlock", blocks[1].GetType().Name);
        Assert.Contains("Ini teks biasa", InlineText(blocks[1]));
        Assert.Equal("ListBlock", blocks[2].GetType().Name);
    }

    [Theory]
    [InlineData("```choice\n? Pertanyaan tanpa opsi\n```")]
    [InlineData("```choice\n? Hanya satu opsi\n- Opsi A\n```")]
    [InlineData("```choice\n? Lima opsi\n- A\n- B\n- C\n- D\n- E\n```")]
    [InlineData("```choice\n? Pertanyaan\n1. Opsi A\n2. Opsi B\n```")]
    public void Parse_ChoiceBlock_BoundaryAndFallbacks_FallsBackToCodeBlock(string invalidChoiceBlock)
    {
        var document = Parse(invalidChoiceBlock);
        var blocks = Blocks(document);
        var code = Assert.Single(blocks);
        Assert.Equal("CodeBlock", code.GetType().Name);
        Assert.Equal("choice", StringProperty(code, "Language"));
    }

    [Fact]
    public void Parse_MixedContentWithChoice_PreservesBlockOrder()
    {
        var document = Parse(
            "Mohon tentukan pilihan berikut:\n\n" +
            "```choice\n" +
            "? Pilih mode eksekusi\n" +
            "- Mode Cepat\n" +
            "- Mode Teliti\n" +
            "```\n\n" +
            "Terima kasih atas pilihannya.");

        var blocks = Blocks(document);
        Assert.Equal(3, blocks.Count);
        Assert.Equal("ParagraphBlock", blocks[0].GetType().Name);
        Assert.Contains("Mohon tentukan", InlineText(blocks[0]));

        Assert.Equal("ChoiceBlock", blocks[1].GetType().Name);
        Assert.Equal("Pilih mode eksekusi", StringProperty(blocks[1], "Question"));
        var options = EnumerableProperty(blocks[1], "Options").Cast<string>().ToArray();
        Assert.Equal(2, options.Length);

        Assert.Equal("ParagraphBlock", blocks[2].GetType().Name);
        Assert.Contains("Terima kasih", InlineText(blocks[2]));
    }

    private static object Parse(string source, bool includeCodeHeuristic = true)
    {
        var parserType = Type.GetType(
            "Nexus.App.AIWorkspace.Markdown.NexusMessageParser, Nexus.App",
            throwOnError: false);
        if (parserType is null)
        {
            throw new XunitException(
                "NexusMessageParser is missing; RED until the approved parser is implemented.");
        }

        var parser = Activator.CreateInstance(parserType, nonPublic: true);
        if (parser is null)
        {
            throw new XunitException("NexusMessageParser could not be constructed.");
        }

        var parse = parserType.GetMethod(
            "Parse",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null);
        if (parse is null)
        {
            throw new XunitException(
                "Expected Parse(string, bool) on NexusMessageParser.");
        }

        return parse.Invoke(parser, new object[] { source, includeCodeHeuristic })
            ?? throw new XunitException("Parser returned null.");
    }

    private static List<object> Blocks(object document) =>
        EnumerableProperty(document, "Blocks");

    private static List<object> Inlines(object block) =>
        EnumerableProperty(block, "Inlines");

    private static List<object> EnumerableProperty(object instance, string propertyName)
    {
        var value = instance.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as IEnumerable;
        if (value is null)
        {
            throw new XunitException(
                $"{instance.GetType().Name}.{propertyName} is missing or is not enumerable.");
        }

        return value.Cast<object>().ToList();
    }

    private static List<object> EnumerableItems(object instance)
    {
        if (instance is not IEnumerable value)
        {
            throw new XunitException(
                $"{instance.GetType().Name} is not enumerable.");
        }

        return value.Cast<object>().ToList();
    }

    private static string InlineText(object paragraph) =>
        string.Concat(Inlines(paragraph).Select(inline => StringProperty(inline, "Text")));

    private static string StringProperty(object instance, string propertyName) =>
        instance.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as string
        ?? throw new XunitException($"{instance.GetType().Name}.{propertyName} is missing.");

    private static bool BoolProperty(object instance, string propertyName) =>
        instance.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as bool?
        ?? throw new XunitException($"{instance.GetType().Name}.{propertyName} is missing.");

    private static string[] ListItemTexts(object listBlock) =>
        EnumerableProperty(listBlock, "Items")
            .Select(InlineText)
            .ToArray();
}
