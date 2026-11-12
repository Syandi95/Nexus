using System.Collections.Immutable;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexus.App.AIWorkspace.Markdown;
using Nexus.App.Services;
using Nexus.App.ViewModels;

namespace Nexus.App.AIWorkspace.Rendering;

public sealed class MessageContentPresenter : ContentControl
{
    private ImmutableArray<MessageBlock> _renderedBlocks = ImmutableArray<MessageBlock>.Empty;
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(object),
            typeof(MessageContentPresenter),
            new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty FallbackTextProperty =
        DependencyProperty.Register(
            nameof(FallbackText),
            typeof(string),
            typeof(MessageContentPresenter),
            new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty SizeToContentProperty =
        DependencyProperty.Register(
            nameof(SizeToContent),
            typeof(bool),
            typeof(MessageContentPresenter),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public MessageContentPresenter()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
    }

    public object? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string FallbackText
    {
        get => (string)GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    public bool SizeToContent
    {
        get => (bool)GetValue(SizeToContentProperty);
        set => SetValue(SizeToContentProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!SizeToContent || double.IsInfinity(constraint.Width))
        {
            return base.MeasureOverride(constraint);
        }

        var width = Content is RichTextBox messageBox
            ? Math.Min(MeasureDocumentWidth(messageBox), constraint.Width)
            : constraint.Width;
        var wrapped = base.MeasureOverride(
            new Size(width, constraint.Height));
        return new Size(width, wrapped.Height);
    }

    private double MeasureDocumentWidth(RichTextBox messageBox)
    {
        var textWidth = 0d;
        foreach (var block in messageBox.Document.Blocks)
        {
            textWidth = Math.Max(
                textWidth,
                MeasureBlockWidth(block));
        }

        if (textWidth <= 0)
        {
            return 1;
        }

        // RichTextBox's flow formatter reserves additional line space even
        // with zero page/control padding. Runtime short-text and ASCII tests
        // prove this allowance is required; it is not bubble decoration.
        const double richTextLayoutOverhead = 24;
        return Math.Max(1, Math.Ceiling(textWidth +
            messageBox.Document.PagePadding.Left + messageBox.Document.PagePadding.Right)) +
            richTextLayoutOverhead;
    }

    private static double MeasureBlockWidth(Block block)
    {
        var width = block switch
        {
            Paragraph paragraph => MeasureParagraphWidth(paragraph),
            Table table => MeasureTableWidth(table),
            Section section => MeasureSectionWidth(section.Blocks),
            System.Windows.Documents.List list => MeasureListWidth(list),
            _ => 0
        };
        return width + block.Padding.Left + block.Padding.Right +
            block.BorderThickness.Left + block.BorderThickness.Right +
            block.Margin.Left + block.Margin.Right;
    }

    private static double MeasureTableWidth(Table table)
    {
        var columns = new double[table.Columns.Count];
        var spanningWidth = 0d;
        foreach (var group in table.RowGroups)
        {
            foreach (var row in group.Rows)
            {
                var column = 0;
                foreach (var cell in row.Cells)
                {
                    var width = 0d;
                    foreach (var child in cell.Blocks)
                    {
                        width = Math.Max(
                            width,
                            MeasureBlockWidth(child));
                    }
                    width += cell.Padding.Left + cell.Padding.Right +
                        cell.BorderThickness.Left + cell.BorderThickness.Right;
                    if (cell.ColumnSpan == 1 && column < columns.Length)
                        columns[column] = Math.Max(columns[column], width);
                    else
                        spanningWidth = Math.Max(spanningWidth, width);
                    column += cell.ColumnSpan;
                }
            }
        }

        return Math.Max(columns.Sum(), spanningWidth);
    }

    private static double MeasureListWidth(
        System.Windows.Documents.List list)
    {
        var width = 0d;
        foreach (var item in list.ListItems)
        {
            foreach (var child in item.Blocks)
            {
                width = Math.Max(width, MeasureBlockWidth(child));
            }
        }

        return width;
    }

    private static double MeasureSectionWidth(
        BlockCollection blocks)
    {
        var width = 0d;
        foreach (Block child in blocks)
        {
            width = Math.Max(width, MeasureBlockWidth(child));
        }

        return width;
    }

    // Measures the widest visual line of the paragraph using the REAL
    // typography of each inline (a CodeBlock's quoted Consolas content is
    // measured at Consolas, plain text at the UI font - exactly what is
    // rendered).  Newlines split the paragraph into logical lines.
    private static double MeasureParagraphWidth(
        Paragraph paragraph)
    {
        // Let WPF shape the complete line (including mixed fonts and nesting).
        var text = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        // Match the message flow's explicit pixel-aligned formatting contract.
        // PTS contexts are pooled per Dispatcher and may have been created by a
        // displayed Chat document; detached Ideal measurement is not equivalent.
        TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
        foreach (var run in EnumerateRuns(paragraph.Inlines))
        {
            text.Inlines.Add(new Run(run.Text)
            {
                FontFamily = run.FontFamily,
                FontStyle = run.FontStyle,
                FontWeight = run.FontWeight,
                FontStretch = run.FontStretch,
                FontSize = run.FontSize
            });
        }
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return text.DesiredSize.Width;
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return run;
                    break;
                case Span span:
                    foreach (var nested in EnumerateRuns(span.Inlines))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static void OnContentChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var presenter = (MessageContentPresenter)dependencyObject;
        if (args.Property == FallbackTextProperty && presenter.Document is MessageDocument)
            return;
        presenter.RebuildContent();
    }

    private void RebuildContent()
    {
        // The parsed document is authoritative; token-level fallback changes
        // must not rebuild a document which is awaiting its scheduled update.
        var blocks = Document is MessageDocument document
            ? document.Blocks
            : ImmutableArray.Create<MessageBlock>(
                new ParagraphBlock(
                    ImmutableArray.Create<MessageInline>(
                        new PlainTextInline(FallbackText ?? string.Empty))));

        if (Content is RichTextBox existingBox)
        {
            var visuals = existingBox.Document.Blocks.Cast<Block>().ToArray();
            var shared = Math.Min(blocks.Length, _renderedBlocks.Length);
            for (var index = 0; index < shared; index++)
            {
                if (BlocksEqual(_renderedBlocks[index], blocks[index]))
                    continue;
                if (TryUpdateBlock(visuals[index], _renderedBlocks[index], blocks[index]))
                    continue;
                existingBox.Document.Blocks.InsertBefore(visuals[index], BuildBlock(blocks[index]));
                existingBox.Document.Blocks.Remove(visuals[index]);
            }
            for (var index = shared; index < visuals.Length; index++)
                existingBox.Document.Blocks.Remove(visuals[index]);
            for (var index = shared; index < blocks.Length; index++)
                existingBox.Document.Blocks.Add(BuildBlock(blocks[index]));
            _renderedBlocks = blocks;
            return;
        }

        var bodyFontSize = GetMetric("NexusTypographyBodyCompactSize");
        var flowDocument = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity,
            FontSize = bodyFontSize,
            LineHeight = bodyFontSize *
                GetMetric("NexusAIWorkspaceMarkdownBodyLineHeightMultiplier"),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        foreach (var block in blocks)
        {
            flowDocument.Blocks.Add(BuildBlock(block));
        }

        var messageBox = new RichTextBox(flowDocument)
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsInactiveSelectionHighlightEnabled = true,
            IsDocumentEnabled = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        TextOptions.SetTextFormattingMode(messageBox, TextFormattingMode.Display);
        messageBox.SetResourceReference(
            Control.ForegroundProperty,
            "NexusForegroundPrimary");
        messageBox.SetResourceReference(
            Control.FontFamilyProperty,
            "NexusTypographyFontFamily");
        messageBox.SetResourceReference(
            Control.FontSizeProperty,
            "NexusTypographyBodyCompactSize");
        Content = messageBox;
        _renderedBlocks = blocks;
    }

    private static bool BlocksEqual(MessageBlock previous, MessageBlock current) =>
        (previous, current) switch
        {
            (ParagraphBlock a, ParagraphBlock b) => a.Inlines.SequenceEqual(b.Inlines),
            (HeadingBlock a, HeadingBlock b) => a.Level == b.Level && a.Inlines.SequenceEqual(b.Inlines),
            (CodeBlock a, CodeBlock b) => a == b,
            (HorizontalRuleBlock, HorizontalRuleBlock) => true,
            (QuoteBlock a, QuoteBlock b) => a.Blocks.Length == b.Blocks.Length &&
                a.Blocks.Zip(b.Blocks).All(pair => BlocksEqual(pair.First, pair.Second)),
            (ListBlock a, ListBlock b) => a.IsOrdered == b.IsOrdered && a.IsTaskList == b.IsTaskList && a.StartIndex == b.StartIndex &&
                a.Items.Length == b.Items.Length && a.Items.Zip(b.Items).All(pair =>
                    pair.First.IsChecked == pair.Second.IsChecked && pair.First.Indent == pair.Second.Indent &&
                    pair.First.Inlines.SequenceEqual(pair.Second.Inlines)),
            (ChoiceBlock a, ChoiceBlock b) => a.Question == b.Question && a.Options.SequenceEqual(b.Options),
            (TableBlock a, TableBlock b) => a.Headers.Length == b.Headers.Length &&
                a.Headers.Zip(b.Headers).All(pair => pair.First.Alignment == pair.Second.Alignment &&
                    pair.First.Inlines.SequenceEqual(pair.Second.Inlines)) &&
                a.Rows.Length == b.Rows.Length && a.Rows.Zip(b.Rows).All(pair =>
                    pair.First.Length == pair.Second.Length && pair.First.Zip(pair.Second).All(cell =>
                        cell.First.Alignment == cell.Second.Alignment && cell.First.Inlines.SequenceEqual(cell.Second.Inlines))),
            _ => false
        };

    private static bool TryUpdateBlock(Block visual, MessageBlock previous, MessageBlock current)
    {
        if (visual is Paragraph paragraph && previous is ParagraphBlock oldParagraph &&
            current is ParagraphBlock newParagraph)
        {
            var runs = paragraph.Inlines.ToArray();
            var shared = Math.Min(runs.Length, newParagraph.Inlines.Length);
            for (var index = 0; index < shared; index++)
            {
                if (oldParagraph.Inlines[index] == newParagraph.Inlines[index])
                    continue;
                if (runs[index] is Run run && oldParagraph.Inlines[index] is PlainTextInline &&
                    newParagraph.Inlines[index] is PlainTextInline text)
                    run.Text = text.Text;
                else
                {
                    paragraph.Inlines.InsertBefore(runs[index], BuildInline(newParagraph.Inlines[index]));
                    paragraph.Inlines.Remove(runs[index]);
                }
            }
            for (var index = shared; index < runs.Length; index++)
                paragraph.Inlines.Remove(runs[index]);
            for (var index = shared; index < newParagraph.Inlines.Length; index++)
                paragraph.Inlines.Add(BuildInline(newParagraph.Inlines[index]));
            return true;
        }
        if (visual is Table table && previous is CodeBlock oldCode && current is CodeBlock newCode &&
            oldCode.Language == newCode.Language)
        {
            var codeParagraph = (Paragraph)table.RowGroups[0].Rows[1].Cells[0].Blocks.FirstBlock;
            NexusSyntaxColorizer.PopulateInlines(codeParagraph, newCode.Language, newCode.Content);
            // Copy uses the latest source without replacing the header.
            var copy = (Hyperlink)((Paragraph)table.RowGroups[0].Rows[0].Cells[1].Blocks.FirstBlock)
                .Inlines.FirstInline;
            copy.Tag = newCode.Content;
            return true;
        }
        return false;
    }

    private Block BuildBlock(MessageBlock block)
    {
        return block switch
        {
            ChoiceBlock choice => BuildChoiceBlock(choice),
            ParagraphBlock paragraph => BuildParagraph(paragraph.Inlines),
            CodeBlock code => BuildCodeBlock(code),
            HeadingBlock heading => BuildHeading(heading),
            HorizontalRuleBlock => BuildHorizontalRule(),
            ListBlock list => BuildList(list),
            QuoteBlock quote => BuildQuote(quote),
            TableBlock table => BuildTable(table),
            _ => BuildParagraph(
                ImmutableArray.Create<MessageInline>(
                    new PlainTextInline(string.Empty)))
        };
    }

    private static Section BuildChoiceBlock(ChoiceBlock choice)
    {
        var container = new Section
        {
            Padding = new Thickness(12, 10, 12, 10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(
                0,
                GetMetric("NexusSpacing8"),
                0,
                GetMetric("NexusSpacing8"))
        };

        container.SetResourceReference(
            TextElement.BackgroundProperty,
            "SurfaceSecondaryBrush");
        container.SetResourceReference(
            Block.BorderBrushProperty,
            "NexusContentBorder");

        var questionText = new Paragraph(new Run(choice.Question))
        {
            Margin = new Thickness(0, 0, 0, 4),
            FontWeight = FontWeights.SemiBold
        };
        questionText.SetResourceReference(
            TextElement.ForegroundProperty,
            "NexusForegroundPrimary");
        questionText.SetResourceReference(
            TextElement.FontFamilyProperty,
            "NexusTypographyFontFamily");
        questionText.SetResourceReference(
            TextElement.FontSizeProperty,
            "NexusTypographyBodyCompactSize");

        container.Blocks.Add(questionText);
        foreach (var option in choice.Options)
            container.Blocks.Add(new Paragraph(new Run("• " + option)) { Margin = new Thickness(0) });
        return container;
    }

    private static Paragraph BuildParagraph(
        IEnumerable<MessageInline> inlines)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(
                0,
                0,
                0,
                GetMetric("NexusSpacing8"))
        };
        foreach (var inline in inlines)
        {
            paragraph.Inlines.Add(BuildInline(inline));
        }

        return paragraph;
    }

    private static Inline BuildInline(MessageInline inline)
    {
        var run = new Run(inline.Text);
        return inline switch
        {
            BoldInline => BuildEmphasis(new Bold(), inline.Text),
            ItalicInline => BuildEmphasis(new Italic(), inline.Text),
            StrikeInline => BuildEmphasis(new Span { TextDecorations = TextDecorations.Strikethrough }, inline.Text),
            InlineCodeInline => BuildInlineCode(run),
            LinkInline link => BuildLink(link),
            ImageInline image => BuildImagePlaceholder(image),
            _ => run
        };
    }

    private static Span BuildEmphasis(Span span, string text)
    {
        foreach (var inline in NexusMessageParser.ParseInlines(text))
            span.Inlines.Add(BuildInline(inline));
        return span;
    }

    private static Inline BuildLink(LinkInline link)
    {
        if (!Uri.TryCreate(link.Target, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return new Run(link.Text);
        }

        var hyperlink = new Hyperlink(new Run(link.Text))
        {
            NavigateUri = uri,
            Cursor = Cursors.Hand
        };
        hyperlink.Click += (_, _) =>
        {
            try
            {
                if (Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.OpenUrlInRightPanelBrowser is { } openInRightPanel && (uri.AbsoluteUri).StartsWith("http", System.StringComparison.OrdinalIgnoreCase)) { openInRightPanel(uri.AbsoluteUri); } else { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Keep the message readable when no browser is associated.
            }
        };
        return hyperlink;
    }

    private static Inline BuildImagePlaceholder(ImageInline image)
    {
        var run = new Run($"[Gambar: {image.Text}]") { ToolTip = image.Source };
        run.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
        return run;
    }

    private static Inline BuildInlineCode(Run run)
    {
        run.SetResourceReference(
            TextElement.FontFamilyProperty,
            "NexusTypographyCodeFontFamily");
        run.SetResourceReference(
            TextElement.BackgroundProperty,
            "SurfaceSecondaryBrush");
        return run;
    }

    private static Table BuildCodeBlock(CodeBlock code)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(
                0,
                GetMetric("NexusSpacing12"),
                0,
                GetMetric("NexusSpacing12")),
            BorderThickness = new Thickness(1)
        };
        table.SetResourceReference(
            Block.BorderBrushProperty,
            "NexusContentBorder");
        table.SetResourceReference(
            TextElement.BackgroundProperty,
            "SurfaceSecondaryBrush");

        var contentColumn = new TableColumn();
        // FlowDocument tables use native Auto sizing, not Grid star tracks.
        contentColumn.Width = new GridLength(0, GridUnitType.Auto);
        var copyColumn = new TableColumn();
        copyColumn.Width = new GridLength(0, GridUnitType.Auto);
        table.Columns.Add(contentColumn);
        table.Columns.Add(copyColumn);

        var rows = new TableRowGroup();

        var language = new Paragraph(new Run(code.Language))
        {
            Margin = new Thickness(0),
            LineHeight = double.NaN,
            LineStackingStrategy = LineStackingStrategy.MaxHeight,
            FontSize = 11
        };
        language.SetResourceReference(
            TextElement.ForegroundProperty,
            "TextSecondaryBrush");
        var headerRow = new TableRow();
        headerRow.Cells.Add(new TableCell(language)
        {
            Padding = new Thickness(10, 6, 4, 6),
            BorderThickness = new Thickness(0, 0, 0, 1)
        });
        headerRow.Cells[0].SetResourceReference(TableCell.BorderBrushProperty, "NexusContentBorder");
        headerRow.Cells.Add(BuildCodeCopy(code));
        rows.Rows.Add(headerRow);

        var content = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = double.NaN,
            LineStackingStrategy = LineStackingStrategy.MaxHeight
        };
        content.SetResourceReference(
            TextElement.FontFamilyProperty,
            "NexusTypographyCodeFontFamily");
        content.SetResourceReference(TextElement.FontSizeProperty, "NexusTypographyCodeSize");
        NexusSyntaxColorizer.PopulateInlines(content, code.Language, code.Content);
        var contentRow = new TableRow();
        contentRow.Cells.Add(new TableCell(content)
        {
            Padding = new Thickness(14, 8, 14, 12),
            ColumnSpan = 2
        });
        rows.Rows.Add(contentRow);

        table.RowGroups.Add(rows);
        return table;
    }

    private static TableCell BuildCodeCopy(CodeBlock code)
    {
        var copyGlyph = new Run(NexusIcon.GetIconGlyph("Copy"))
        {
            FontSize = 11
        };
        copyGlyph.SetResourceReference(
            TextElement.FontFamilyProperty,
            "NexusTypographyIconFontFamily");
        copyGlyph.SetResourceReference(
            TextElement.ForegroundProperty,
            "TextSecondaryBrush");
        var copyLabel = new Run(" Salin")
        {
            FontSize = 11
        };
        copyLabel.SetResourceReference(
            TextElement.ForegroundProperty,
            "TextSecondaryBrush");

        var hyperlink = new Hyperlink
        {
            TextDecorations = null,
            Tag = code.Content,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        hyperlink.Inlines.Add(copyGlyph);
        hyperlink.Inlines.Add(copyLabel);

        DispatcherTimer? feedbackTimer = null;
        hyperlink.Click += (_, _) =>
        {
            if (NexusClipboard.SetText((string)hyperlink.Tag))
            {
                copyGlyph.Text = NexusIcon.GetIconGlyph("Check");
                hyperlink.ToolTip = "Tersalin";
                feedbackTimer?.Stop();
                feedbackTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                feedbackTimer.Tick += (_, _) =>
                {
                    feedbackTimer?.Stop();
                    feedbackTimer = null;
                    copyGlyph.Text = NexusIcon.GetIconGlyph("Copy");
                    hyperlink.ToolTip = "Salin kode";
                };
                feedbackTimer.Start();
            }
        };

        var cell = new TableCell(
            new Paragraph(hyperlink)
            {
                Margin = new Thickness(0),
                LineHeight = double.NaN,
                LineStackingStrategy = LineStackingStrategy.MaxHeight,
                TextAlignment = TextAlignment.Right
            })
        {
            Padding = new Thickness(0, 6, 12, 6),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "NexusAccentPrimary");
        cell.SetResourceReference(TableCell.BorderBrushProperty, "NexusContentBorder");
        return cell;
    }

    private static System.Windows.Documents.List BuildList(ListBlock list)
    {
        var documentList = new System.Windows.Documents.List
        {
            Padding = new Thickness(GetMetric("NexusSpacing24"), 0, 0, 0),
            Margin = new Thickness(
                0,
                0,
                0,
                GetMetric("NexusSpacing8")),
            MarkerStyle = list.IsTaskList
                ? TextMarkerStyle.None
                : list.IsOrdered
                ? TextMarkerStyle.Decimal
                : TextMarkerStyle.Disc,
            StartIndex = list.StartIndex
        };
        var levels = new Stack<(int Indent, System.Windows.Documents.List List)>();
        levels.Push((list.Items.IsEmpty ? 0 : list.Items[0].Indent, documentList));
        foreach (var item in list.Items)
        {
            while (levels.Count > 1 && item.Indent < levels.Peek().Indent)
                levels.Pop();
            if (item.Indent > levels.Peek().Indent && levels.Peek().List.ListItems.LastListItem is { } parent)
            {
                var nested = new System.Windows.Documents.List
                {
                    MarkerStyle = documentList.MarkerStyle,
                    Padding = documentList.Padding,
                    Margin = new Thickness(0)
                };
                parent.Blocks.Add(nested);
                levels.Push((item.Indent, nested));
            }
            var paragraph = BuildParagraph(item.Inlines);
            paragraph.Margin = new Thickness(0);
            if (list.IsTaskList)
            {
                paragraph.Inlines.InsertBefore(
                    paragraph.Inlines.FirstInline,
                    new Run(item.IsChecked ? "\u2611 " : "\u2610 "));
            }

            levels.Peek().List.ListItems.Add(new ListItem(paragraph));
        }

        return documentList;
    }

    private Section BuildQuote(QuoteBlock quote)
    {
        var section = new Section
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(
                0,
                GetMetric("NexusSpacing8"),
                0,
                GetMetric("NexusSpacing8"))
        };
        section.SetResourceReference(
            Block.BorderBrushProperty,
            "AccentPrimaryBrush");
        foreach (var block in quote.Blocks)
        {
            section.Blocks.Add(BuildBlock(block));
        }

        return section;
    }

    private static Table BuildTable(TableBlock tableBlock)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(
                0,
                GetMetric("NexusSpacing12"),
                0,
                GetMetric("NexusSpacing12"))
        };
        for (var index = 0; index < tableBlock.Headers.Length; index++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        var header = new TableRow();
        foreach (var cell in tableBlock.Headers)
        {
            var paragraph = BuildParagraph(cell.Inlines);
            paragraph.TextAlignment = cell.Alignment;
            paragraph.SetResourceReference(
                TextElement.FontWeightProperty,
                "NexusTypographyStrongWeight");
            header.Cells.Add(BuildTableCell(
                paragraph,
                isHeader: true));
        }
        group.Rows.Add(header);

        foreach (var row in tableBlock.Rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row)
            {
                var paragraph = BuildParagraph(cell.Inlines);
                paragraph.TextAlignment = cell.Alignment;
                tableRow.Cells.Add(BuildTableCell(
                    paragraph,
                    isHeader: false));
            }
            group.Rows.Add(tableRow);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static TableCell BuildTableCell(
        Paragraph paragraph,
        bool isHeader)
    {
        paragraph.Margin = new Thickness(0);
        var cell = new TableCell(paragraph)
        {
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(0.5)
        };
        cell.SetResourceReference(
            TableCell.BorderBrushProperty,
            "NexusContentBorder");
        if (isHeader)
        {
            cell.SetResourceReference(
                TextElement.BackgroundProperty,
                "SurfaceSecondaryBrush");
        }

        return cell;
    }

    private static Paragraph BuildHeading(HeadingBlock heading)
    {
        var paragraph = BuildParagraph(heading.Inlines);
        paragraph.SetResourceReference(
            TextElement.FontFamilyProperty,
            "NexusTypographyFontFamily");
        paragraph.SetResourceReference(
            TextElement.FontWeightProperty,
            "NexusTypographyStrongWeight");
        paragraph.SetResourceReference(
            TextElement.FontSizeProperty,
            heading.Level switch
            {
                1 => "NexusTypographyDisplaySize",
                2 => "NexusTypographyTitleSize",
                3 => "NexusTypographySubtitleSize",
                4 => "NexusTypographyBodySize",
                5 => "NexusTypographyBodyCompactSize",
                _ => "NexusTypographyBodySmallSize"
            });
        paragraph.Margin = new Thickness(
            0,
            GetMetric("NexusSpacing12"),
            0,
            GetMetric("NexusSpacing4"));
        paragraph.LineHeight = double.NaN;
        paragraph.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        return paragraph;
    }

    private static BlockUIContainer BuildHorizontalRule()
    {
        var rule = new Border
        {
            Height = 1
        };
        rule.SetResourceReference(
            Border.BackgroundProperty,
            "NexusStrokeSubtle");
        return new BlockUIContainer(rule)
        {
            Margin = new Thickness(
                0,
                GetMetric("NexusSpacing8"),
                0,
                GetMetric("NexusSpacing8"))
        };
    }

    private static double GetMetric(string key)
    {
        return Application.Current.FindResource(key) is double value
            ? value
            : throw new InvalidOperationException(
                $"Resource '{key}' must be a Double.");
    }
}
