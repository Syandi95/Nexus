using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Nexus.App.AIWorkspace.Markdown;

internal sealed record MessageDocument(ImmutableArray<MessageBlock> Blocks);

public abstract record MessageBlock;

public sealed record ChoiceBlock(
    string Question,
    ImmutableArray<string> Options) : MessageBlock;

internal sealed record ParagraphBlock(
    ImmutableArray<MessageInline> Inlines) : MessageBlock;

internal sealed record CodeBlock(
    string Language,
    string Content) : MessageBlock;

internal sealed record HeadingBlock(
    int Level,
    ImmutableArray<MessageInline> Inlines) : MessageBlock;

internal sealed record HorizontalRuleBlock : MessageBlock;

internal sealed record ListBlock(
    bool IsOrdered,
    bool IsTaskList,
    ImmutableArray<ListItemBlock> Items,
    int StartIndex = 1) : MessageBlock;

internal sealed record ListItemBlock(
    ImmutableArray<MessageInline> Inlines,
    bool IsChecked = false,
    int Indent = 0);

internal sealed record QuoteBlock(
    ImmutableArray<MessageBlock> Blocks) : MessageBlock;

internal sealed record TableBlock(
    ImmutableArray<TableCellBlock> Headers,
    ImmutableArray<ImmutableArray<TableCellBlock>> Rows) : MessageBlock;

internal sealed record TableCellBlock(
    ImmutableArray<MessageInline> Inlines,
    System.Windows.TextAlignment Alignment = System.Windows.TextAlignment.Left);

internal abstract record MessageInline(string Text);

internal sealed record PlainTextInline(string Text) : MessageInline(Text);

internal sealed record BoldInline(string Text) : MessageInline(Text);

internal sealed record ItalicInline(string Text) : MessageInline(Text);
internal sealed record StrikeInline(string Text) : MessageInline(Text);

internal sealed record InlineCodeInline(string Text) : MessageInline(Text);
internal sealed record LinkInline(string Text, string Target) : MessageInline(Text);
internal sealed record ImageInline(string Text, string Source) : MessageInline(Text);

internal sealed partial class NexusMessageParser
{
    public MessageDocument Parse(
        string source,
        bool includeCodeHeuristic)
    {
        var normalized = NormalizeNewLines(source);
        var blocks = ImmutableArray.CreateBuilder<MessageBlock>();
        var matches = FencedCodeRegex().Matches(normalized);

        if (matches.Count == 0)
        {
            if (includeCodeHeuristic &&
                !ContainsIndentedCode(normalized) &&
                LooksLikeCode(normalized))
            {
                blocks.Add(new CodeBlock(
                    LooksLikePowerShell(normalized) ? "powershell" : "text",
                    normalized));
                return new MessageDocument(blocks.ToImmutable());
            }

            ParseBlocks(normalized, blocks);
            return new MessageDocument(blocks.ToImmutable());
        }

        var currentIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > currentIndex)
            {
                ParseBlocks(
                    normalized[currentIndex..match.Index],
                    blocks);
            }

            var language = match.Groups["language"].Value;
            if (!language.StartsWith(
                    "aiedit",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (language.Equals("choice", StringComparison.OrdinalIgnoreCase) &&
                    TryParseChoiceBlock(match.Groups["content"].Value, out var choiceBlock))
                {
                    blocks.Add(choiceBlock);
                }
                else
                {
                    blocks.Add(new CodeBlock(
                        string.IsNullOrWhiteSpace(language) ? "text" : language,
                        match.Groups["content"].Value.Trim('\n')));
                }
            }

            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < normalized.Length)
        {
            ParseBlocks(normalized[currentIndex..], blocks);
        }

        return new MessageDocument(blocks.ToImmutable());
    }

    private static void ParseBlocks(
        string text,
        ImmutableArray<MessageBlock>.Builder output)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var lines = text.Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            if (IsIndentedCodeStart(lines[index]) &&
                !TryGetListItem(lines[index], out _, out _, out _, out _))
            {
                var codeLines = new List<string>();
                while (index < lines.Length)
                {
                    var line = lines[index];
                    if (TryRemoveOneIndent(line, out var codeLine))
                    {
                        codeLines.Add(codeLine);
                        index++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        codeLines.Add(string.Empty);
                        index++;
                        continue;
                    }

                    break;
                }

                output.Add(new CodeBlock(
                    "text",
                    string.Join('\n', codeLines).TrimEnd()));
                continue;
            }

            if (TryParseUnfencedChoice(
                    lines,
                    index,
                    out var choiceBlock,
                    out var choiceConsumed))
            {
                output.Add(choiceBlock);
                index += choiceConsumed;
                continue;
            }

            if (TryParseAsciiDiagram(
                    lines,
                    index,
                    out var diagramLines,
                    out var diagramConsumed))
            {
                output.Add(new CodeBlock(
                    "text",
                    string.Join('\n', diagramLines)));
                index += diagramConsumed;
                continue;
            }

            if (TryParseTable(
                    lines,
                    index,
                    out var table,
                    out var consumedLines))
            {
                output.Add(table);
                index += consumedLines;
                continue;
            }

            if (TryGetHeading(lines[index], out var headingLevel, out var headingText))
            {
                output.Add(new HeadingBlock(headingLevel, ParseInlines(headingText)));
                index++;
                continue;
            }

            if (IsHorizontalRule(lines[index]))
            {
                output.Add(new HorizontalRuleBlock());
                index++;
                continue;
            }

            if (TryGetListItem(
                    lines[index],
                    out var isOrdered,
                    out var isTaskList,
                    out _,
                    out _))
            {
                var items = ImmutableArray.CreateBuilder<ListItemBlock>();
                var orderedMatch = OrderedListRegex().Match(lines[index]);
                var startIndex = orderedMatch.Success &&
                    int.TryParse(orderedMatch.Groups["number"].Value, out var number) && number > 0
                    ? number : 1;
                while (index < lines.Length &&
                       TryGetListItem(
                           lines[index],
                           out var currentOrdered,
                           out var currentTaskList,
                           out var itemText,
                           out var isChecked) &&
                       currentOrdered == isOrdered &&
                       currentTaskList == isTaskList)
                {
                    items.Add(new ListItemBlock(
                        ParseInlines(itemText),
                        isChecked,
                        lines[index].TakeWhile(char.IsWhiteSpace)
                            .Sum(character => character == '\t' ? 4 : 1)));
                    index++;
                }

                output.Add(new ListBlock(
                    isOrdered,
                    isTaskList,
                    items.ToImmutable(),
                    startIndex));
                continue;
            }

            if (IsQuoteLine(lines[index]))
            {
                var quoteLines = new List<string>();
                while (index < lines.Length && IsQuoteLine(lines[index]))
                {
                    quoteLines.Add(RemoveQuoteMarker(lines[index]));
                    index++;
                }

                var quoteBlocks = ImmutableArray.CreateBuilder<MessageBlock>();
                ParseBlocks(string.Join('\n', quoteLines), quoteBlocks);
                output.Add(new QuoteBlock(quoteBlocks.ToImmutable()));
                continue;
            }

            var paragraphLines = new List<string>();
            while (index < lines.Length &&
                   !string.IsNullOrWhiteSpace(lines[index]) &&
                   !IsIndentedCodeStart(lines[index]) &&
                   !IsTableStart(lines, index) &&
                   !TryGetHeading(lines[index], out _, out _) &&
                   !IsHorizontalRule(lines[index]) &&
                   !IsQuoteLine(lines[index]) &&
                   !TryGetListItem(lines[index], out _, out _, out _, out _))
            {
                paragraphLines.Add(lines[index]);
                index++;
            }

            if (paragraphLines.Count > 0)
            {
                output.Add(new ParagraphBlock(
                    ParseInlines(string.Join('\n', paragraphLines))));
            }
        }
    }

    private static bool TryParseTable(
        IReadOnlyList<string> lines,
        int startIndex,
        out TableBlock table,
        out int consumedLines)
    {
        table = new TableBlock(
            ImmutableArray<TableCellBlock>.Empty,
            ImmutableArray<ImmutableArray<TableCellBlock>>.Empty);
        consumedLines = 0;
        if (startIndex + 1 >= lines.Count ||
            !TrySplitTableRow(lines[startIndex], out var headers) ||
            !TrySplitTableRow(lines[startIndex + 1], out var separators) ||
            headers.Count == 0 ||
            separators.Count != headers.Count ||
            separators.Any(separator =>
                !TableSeparatorRegex().IsMatch(separator)))
        {
            return false;
        }

        var rows =
            ImmutableArray.CreateBuilder<ImmutableArray<TableCellBlock>>();
        var index = startIndex + 2;
        while (index < lines.Count &&
               !string.IsNullOrWhiteSpace(lines[index]) &&
               TrySplitTableRow(lines[index], out var cells) &&
               cells.Count == headers.Count)
        {
            rows.Add(cells
                .Select((cell, column) => new TableCellBlock(ParseInlines(cell), GetTableAlignment(separators[column])))
                .ToImmutableArray());
            index++;
        }

        table = new TableBlock(
            headers
                .Select((header, column) => new TableCellBlock(ParseInlines(header), GetTableAlignment(separators[column])))
                .ToImmutableArray(),
            rows.ToImmutable());
        consumedLines = index - startIndex;
        return true;
    }

    private static bool TryParseAsciiDiagram(
        IReadOnlyList<string> lines,
        int startIndex,
        out List<string> diagramLines,
        out int consumedLines)
    {
        diagramLines = new List<string>();
        consumedLines = 0;

        if (startIndex + 2 >= lines.Count ||
            !IsAsciiBoxBorderLine(lines[startIndex]))
        {
            return false;
        }

        var collected = new List<string> { lines[startIndex] };
        var index = startIndex + 1;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (IsAsciiBoxBorderLine(line) ||
                IsAsciiBoxContentLine(line))
            {
                collected.Add(line);
                index++;
                continue;
            }

            break;
        }

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
    }

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

    private static bool IsAsciiBoxBorderLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 &&
               trimmed[0] == '+' &&
               trimmed[^1] == '+' &&
               trimmed.Contains('-') &&
               trimmed.All(character =>
                   character == '+' ||
                   character == '-' ||
                   char.IsWhiteSpace(character));
    }

    private static bool IsAsciiBoxContentLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 &&
               trimmed[0] == '|';
    }

    private static bool IsTableStart(
        IReadOnlyList<string> lines,
        int index) =>
        TryParseTable(lines, index, out _, out _);

    private static bool TrySplitTableRow(
        string line,
        out List<string> cells)
    {
        cells = new List<string>();
        var trimmed = line.Trim();
        if (!trimmed.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        cells = trimmed
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
        return cells.Count > 0;
    }

    internal static ImmutableArray<MessageInline> ParseInlines(string text)
    {
        var inlines = ImmutableArray.CreateBuilder<MessageInline>();
        var plainStart = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '\\' && index + 1 < text.Length &&
                "\\`*_{}[]()#+-.!|>~".Contains(text[index + 1]))
            {
                AddPlainText(text, plainStart, index, inlines);
                inlines.Add(new PlainTextInline(text[index + 1].ToString()));
                index += 2;
                plainStart = index;
                continue;
            }

            if (index + 1 < text.Length && text[index] == '~' && text[index + 1] == '~')
            {
                var closing = text.IndexOf("~~", index + 2, StringComparison.Ordinal);
                if (closing > index + 2)
                {
                    AddPlainText(text, plainStart, index, inlines);
                    inlines.Add(new StrikeInline(text[(index + 2)..closing]));
                    index = closing + 2;
                    plainStart = index;
                    continue;
                }
            }

            var image = text[index] == '!' && index + 1 < text.Length && text[index + 1] == '[';
            if (text[index] == '[' || image)
            {
                var labelStart = index + (image ? 2 : 1);
                var closeLabel = text.IndexOf("](", labelStart, StringComparison.Ordinal);
                var closeTarget = closeLabel >= 0 ? text.IndexOf(')', closeLabel + 2) : -1;
                if (closeLabel > labelStart && closeTarget > closeLabel + 2)
                {
                    AddPlainText(text, plainStart, index, inlines);
                    var label = text[labelStart..closeLabel];
                    var target = text[(closeLabel + 2)..closeTarget];
                    inlines.Add(image
                        ? new ImageInline(label, target)
                        : new LinkInline(label, target));
                    index = closeTarget + 1;
                    plainStart = index;
                    continue;
                }
            }

            if (text[index] == '`')
            {
                var closing = text.IndexOf('`', index + 1);
                if (closing > index + 1)
                {
                    AddPlainText(text, plainStart, index, inlines);
                    inlines.Add(new InlineCodeInline(
                        text[(index + 1)..closing]));
                    index = closing + 1;
                    plainStart = index;
                    continue;
                }
            }

            if (index + 1 < text.Length &&
                text[index] == '*' &&
                text[index + 1] == '*')
            {
                var closing = text.IndexOf(
                    "**",
                    index + 2,
                    StringComparison.Ordinal);
                // In **bold *italic*** the last two stars close bold.
                if (closing >= 0 && closing + 2 < text.Length && text[closing + 2] == '*')
                {
                    closing++;
                }
                if (closing > index + 2)
                {
                    AddPlainText(text, plainStart, index, inlines);
                    inlines.Add(new BoldInline(
                        text[(index + 2)..closing]));
                    index = closing + 2;
                    plainStart = index;
                    continue;
                }
            }

            if (text[index] == '*' &&
                (index + 1 >= text.Length || text[index + 1] != '*'))
            {
                var closing = text.IndexOf('*', index + 1);
                while (closing >= 0 && closing + 1 < text.Length && text[closing + 1] == '*')
                {
                    closing = text.IndexOf('*', closing + 2);
                }
                if (closing > index + 1)
                {
                    AddPlainText(text, plainStart, index, inlines);
                    inlines.Add(new ItalicInline(
                        text[(index + 1)..closing]));
                    index = closing + 1;
                    plainStart = index;
                    continue;
                }
            }

            index++;
        }

        AddPlainText(text, plainStart, text.Length, inlines);
        if (inlines.Count == 0)
        {
            inlines.Add(new PlainTextInline(string.Empty));
        }

        return inlines.ToImmutable();
    }

    private static void AddPlainText(
        string text,
        int start,
        int end,
        ImmutableArray<MessageInline>.Builder output)
    {
        if (end > start)
        {
            output.Add(new PlainTextInline(text[start..end]));
        }
    }

    private static bool TryGetListItem(
        string line,
        out bool isOrdered,
        out bool isTaskList,
        out string content,
        out bool isChecked)
    {
        var ordered = OrderedListRegex().Match(line);
        if (ordered.Success)
        {
            isOrdered = true;
            isTaskList = false;
            content = ordered.Groups["content"].Value;
            isChecked = false;
            return true;
        }

        var unordered = UnorderedListRegex().Match(line);
        if (unordered.Success)
        {
            isOrdered = false;
            var task = TaskListRegex().Match(unordered.Groups["content"].Value);
            isTaskList = task.Success;
            content = task.Success
                ? task.Groups["content"].Value
                : unordered.Groups["content"].Value;
            isChecked = task.Success &&
                string.Equals(
                    task.Groups["state"].Value,
                    "x",
                    StringComparison.OrdinalIgnoreCase);
            return true;
        }

        isOrdered = false;
        isTaskList = false;
        content = string.Empty;
        isChecked = false;
        return false;
    }

    private static bool TryGetHeading(
        string line,
        out int level,
        out string content)
    {
        var match = HeadingRegex().Match(line);
        level = match.Success
            ? match.Groups["marker"].Value.Length
            : 0;
        content = match.Success ? match.Groups["content"].Value : string.Empty;
        return match.Success;
    }

    private static bool IsHorizontalRule(string line) =>
        HorizontalRuleRegex().IsMatch(line);

    private static bool IsQuoteLine(string line) =>
        QuoteRegex().IsMatch(line);

    private static string RemoveQuoteMarker(string line)
    {
        var match = QuoteRegex().Match(line);
        return match.Success ? match.Groups["content"].Value : line;
    }

    private static bool IsIndentedCodeStart(string line) =>
        TryRemoveOneIndent(line, out var content) &&
        !string.IsNullOrWhiteSpace(content);

    private static bool ContainsIndentedCode(string text) =>
        text.Split('\n').Any(IsIndentedCodeStart);

    private static bool LooksLikeCode(string text)
    {
        if (ExplanatoryTextRegex().IsMatch(text))
        {
            return false;
        }

        var nonEmptyLines = 0;
        var codeLines = 0;
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            nonEmptyLines++;
            if (PowerShellCodeLineRegex().IsMatch(line))
            {
                codeLines++;
            }
        }

        return nonEmptyLines >= 4 &&
               codeLines >= 3 &&
               (double)codeLines / nonEmptyLines >= 0.65;
    }

    private static bool LooksLikePowerShell(string text) =>
        PowerShellCodeLineRegex().IsMatch(text);

    private static bool TryRemoveOneIndent(
        string line,
        out string content)
    {
        if (line.StartsWith('\t'))
        {
            content = line[1..];
            return true;
        }

        if (line.StartsWith("    ", StringComparison.Ordinal))
        {
            content = line[4..];
            return true;
        }

        content = string.Empty;
        return false;
    }

    private static bool TryParseChoiceBlock(
        string content,
        [NotNullWhen(true)] out ChoiceBlock? choiceBlock)
    {
        choiceBlock = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lines = content.Split('\n');
        string? question = null;
        var options = ImmutableArray.CreateBuilder<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (line.StartsWith("?", StringComparison.Ordinal))
            {
                if (question != null)
                {
                    return false;
                }

                var qText = line.Length > 1 && line[1] == ' '
                    ? line[2..].Trim()
                    : line[1..].Trim();
                if (string.IsNullOrEmpty(qText))
                {
                    return false;
                }

                question = qText;
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) ||
                     line.StartsWith("* ", StringComparison.Ordinal))
            {
                var optText = line[2..].Trim();
                if (string.IsNullOrEmpty(optText))
                {
                    return false;
                }

                options.Add(optText);
            }
            else if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
            {
                var optText = line[1..^1].Trim();
                if (string.IsNullOrEmpty(optText))
                {
                    return false;
                }

                options.Add(optText);
            }
            else if (question == null && options.Count == 0 && line.EndsWith('?'))
            {
                question = line;
            }
            else
            {
                return false;
            }
        }

        if (options.Count < 2 || options.Count > 4)
        {
            return false;
        }

        question ??= "Pilih salah satu opsi berikut:";
        choiceBlock = new ChoiceBlock(question, options.ToImmutable());
        return true;
    }

    private static System.Windows.TextAlignment GetTableAlignment(string separator) =>
        separator.EndsWith(':')
            ? separator.StartsWith(':') ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Right
            : System.Windows.TextAlignment.Left;

    private static bool TryParseUnfencedChoice(
        string[] lines,
        int index,
        [NotNullWhen(true)] out ChoiceBlock? choiceBlock,
        out int consumed)
    {
        choiceBlock = null;
        consumed = 0;

        if (index >= lines.Length)
        {
            return false;
        }

        var headerLine = lines[index].Trim();
        const string fallbackQuestion = "Pilih salah satu opsi berikut:";
        bool isHeader = false;

        if (headerLine.Equals("choice", StringComparison.OrdinalIgnoreCase) ||
            headerLine.Equals("choice:", StringComparison.OrdinalIgnoreCase))
        {
            isHeader = true;
        }
        else if (headerLine.StartsWith("choice nexus", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = headerLine["choice nexus".Length..].Trim();
            if (suffix == string.Empty || suffix == ":")
            {
                isHeader = true;
            }
        }

        if (!isHeader)
        {
            return false;
        }

        string? question = null;
        var options = ImmutableArray.CreateBuilder<string>();
        var currentIndex = index + 1;
        var blankCount = 0;

        while (currentIndex < lines.Length)
        {
            var rawLine = lines[currentIndex];
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line))
            {
                if (question == null && options.Count == 0 && blankCount == 0)
                {
                    blankCount++;
                    currentIndex++;
                    continue;
                }

                break;
            }

            if (line.StartsWith("?", StringComparison.Ordinal))
            {
                if (question != null || options.Count > 0)
                {
                    break;
                }

                var qText = line.Length > 1 && line[1] == ' '
                    ? line[2..].Trim()
                    : line[1..].Trim();
                if (string.IsNullOrEmpty(qText))
                {
                    break;
                }

                question = qText;
                currentIndex++;
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) ||
                     line.StartsWith("* ", StringComparison.Ordinal))
            {
                var optText = line[2..].Trim();
                if (string.IsNullOrEmpty(optText))
                {
                    break;
                }

                options.Add(optText);
                currentIndex++;
            }
            else if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
            {
                var optText = line[1..^1].Trim();
                if (string.IsNullOrEmpty(optText))
                {
                    break;
                }

                options.Add(optText);
                currentIndex++;
            }
            else if (question == null && options.Count == 0 && line.EndsWith('?'))
            {
                question = line;
                currentIndex++;
            }
            else
            {
                // Non-choice content reached: terminate contiguous block
                break;
            }

            if (options.Count > 4)
            {
                break;
            }
        }

        if (options.Count < 2 || options.Count > 4)
        {
            return false;
        }

        question ??= fallbackQuestion;
        choiceBlock = new ChoiceBlock(question, options.ToImmutable());
        consumed = currentIndex - index;
        return true;
    }

    private static string NormalizeNewLines(string? source) =>
        (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    [GeneratedRegex(
        "```(?<language>[a-zA-Z0-9_+\\-.]*)[ \\t]*\\n(?<content>.*?)(?:```|$)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(
        "^\\s*(?<number>\\d+)\\.\\s+(?<content>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(
        "^\\s*[-*+]\\s+(?<content>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(
        "^\\[(?<state> |x|X)\\]\\s+(?<content>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TaskListRegex();

    [GeneratedRegex(
        "^\\s{0,3}(?<marker>#{1,6})\\s+(?<content>.+?)\\s*#*\\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(
        "^\\s{0,3}(?:-{3,}|\\*{3,}|_{3,})\\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(
        "^\\s*>\\s?(?<content>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(
        "^:?-{3,}:?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(
        "Berikut|Saran|Penjelasan|Fitur|Intinya|Contoh|Expected|Kesimpulan",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplanatoryTextRegex();

    [GeneratedRegex(
        "^\\s*(#|\\$|function\\s+|param\\s*\\(|if\\s*\\(|foreach\\s*\\(|try\\s*\\{|catch\\s*\\{|else\\s*\\{|Import-Module|Write-Host|\\. )",
        RegexOptions.Multiline | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellCodeLineRegex();
}
