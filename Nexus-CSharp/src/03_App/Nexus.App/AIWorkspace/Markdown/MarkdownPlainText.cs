using System.Collections.Immutable;
using System.Text;

namespace Nexus.App.AIWorkspace.Markdown;

internal static class MarkdownPlainText
{
    public static string ToPlainText(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source ?? string.Empty;
        }

        var document = new NexusMessageParser().Parse(
            source,
            includeCodeHeuristic: false);
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            AppendBlock(builder, block);
        }

        var result = builder.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(result) ? source : result;
    }

    private static void AppendBlock(StringBuilder builder, MessageBlock block)
    {
        switch (block)
        {
            case ChoiceBlock choice:
                AppendLines(builder, choice.Question);
                foreach (var option in choice.Options)
                    builder.Append("• ").Append(option).Append('\n');
                builder.Append('\n');
                break;
            case ParagraphBlock paragraph:
                AppendLines(builder, FlattenInlines(paragraph.Inlines));
                break;
            case HeadingBlock heading:
                AppendLines(builder, FlattenInlines(heading.Inlines));
                break;
            case CodeBlock code:
                AppendLines(builder, code.Content.TrimEnd());
                break;
            case HorizontalRuleBlock:
                break;
            case ListBlock list:
                AppendList(builder, list);
                break;
            case QuoteBlock quote:
                foreach (var inner in quote.Blocks)
                {
                    AppendBlock(builder, inner);
                }
                break;
            case TableBlock table:
                AppendTable(builder, table);
                break;
        }
    }

    private static void AppendList(StringBuilder builder, ListBlock list)
    {
        var number = list.StartIndex;
        foreach (var item in list.Items)
        {
            builder.Append(' ', item.Indent);
            if (list.IsTaskList)
            {
                builder.Append(item.IsChecked ? "[x] " : "[ ] ");
            }
            else if (list.IsOrdered)
            {
                builder.Append(number).Append(". ");
            }
            else
            {
                builder.Append("• ");
            }

            builder.Append(FlattenInlines(item.Inlines));
            builder.Append('\n');
            number++;
        }

        builder.Append('\n');
    }

    private static void AppendTable(StringBuilder builder, TableBlock table)
    {
        AppendTableRow(builder, table.Headers);
        foreach (var row in table.Rows)
        {
            AppendTableRow(builder, row);
        }

        builder.Append('\n');
    }

    private static void AppendTableRow(
        StringBuilder builder,
        ImmutableArray<TableCellBlock> row)
    {
        for (var i = 0; i < row.Length; i++)
        {
            if (i > 0)
            {
                builder.Append("  ");
            }

            builder.Append(FlattenInlines(row[i].Inlines));
        }

        builder.Append('\n');
    }

    private static void AppendLines(StringBuilder builder, string text)
    {
        builder.Append(text.TrimEnd());
        builder.Append("\n\n");
    }

    private static string FlattenInlines(ImmutableArray<MessageInline> inlines)
    {
        if (inlines.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            builder.Append(inline is BoldInline or ItalicInline or StrikeInline
                ? FlattenInlines(NexusMessageParser.ParseInlines(inline.Text))
                : inline.Text);
        }

        return builder.ToString();
    }
}
