using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Conversation.Manager;
using Xunit;
using Xunit.Sdk;

namespace Nexus.Integration.Tests;

public class AIWorkspaceRenderingTests
{
    private const string MarkdownFixture =
        "# Heading\n\nParagraph **bold *italic***, ~~strike~~, `inline code`, and \\*literal\\*.\n\n" +
        "- Parent\n  - Nested item with wrapping content\n- Final item\n\n" +
        "> Quoted content that wraps naturally with the surrounding reading frame.\n\n" +
        "```csharp\n    using System;\n\npublic class Example { }\n```\n\n" +
        "| Header 1 | Header 2 |\n| --- | --- |\n| Value | Long content with natural wrapping |\n\n" +
        "```choice\n? Choose a format?\n- Markdown\n- Plain text\n```\n\nFinal paragraph.";

    [Fact]
    public void Streaming_PreservesDocumentCompletedBlocksSelectionAndGrowingRun()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("Completed paragraph.\n\nGrowing"));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var flow = box.Document;
            var completed = flow.Blocks.FirstBlock;
            var growing = (Paragraph)flow.Blocks.LastBlock;
            var run = growing.Inlines.FirstInline;
            box.Selection.Select(completed.ContentStart, completed.ContentEnd);
            var selected = box.Selection.Text;
            presenter.GetType().GetProperty("FallbackText")!.SetValue(presenter, "Unparsed token update");
            presenter.GetType().GetProperty("Document")!.SetValue(presenter,
                Parse("Completed paragraph.\n\nGrowing response"));
            Assert.Same(box, ((ContentControl)presenter).Content);
            Assert.Same(flow, box.Document);
            Assert.Same(completed, flow.Blocks.FirstBlock);
            Assert.Same(growing, flow.Blocks.LastBlock);
            Assert.Same(run, growing.Inlines.FirstInline);
            Assert.Equal(selected, box.Selection.Text);
            Assert.Equal("Growing response", ((Run)run).Text);
        });
    }

    [Fact]
    public void Streaming_OpenFenceKeepsCodeTableAndCopyUsesLatestContent()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("```csharp\n    first"));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var table = Assert.IsType<Table>(box.Document.Blocks.FirstBlock);
            var copy = FindCodeBlockCopyHyperlink(box.Document)!;
            presenter.GetType().GetProperty("Document")!.SetValue(presenter,
                Parse("```csharp\n    first\nsecond\n```"));
            Assert.Same(table, box.Document.Blocks.FirstBlock);
            Assert.Same(copy, FindCodeBlockCopyHyperlink(box.Document));
            var before = ((Run)copy.Inlines.LastInline).Text;
            var previous = WpfAppHost.WithClipboardRetry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);
            try
            {
                copy.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, copy));
                Assert.Equal("    first\nsecond", WpfAppHost.WithClipboardRetry(Clipboard.GetText));
                Assert.Equal(before, ((Run)copy.Inlines.LastInline).Text);
            }
            finally
            {
                if (previous is null) WpfAppHost.WithClipboardRetry(() => { Clipboard.Clear(); return true; });
                else WpfAppHost.WithClipboardRetry(() => { Clipboard.SetText(previous); return true; });
            }
        });
    }

    [Fact]
    public void NestedInlineFormattingStrikeAndEscapes_RenderWithoutLiteralMarkers()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("**bold *italic*** and *italic **bold*** ~~strike~~ \\*literal\\*"));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var paragraph = Assert.IsType<Paragraph>(box.Document.Blocks.FirstBlock);
            var bold = Assert.Single(paragraph.Inlines.OfType<Bold>());
            Assert.Single(bold.Inlines.OfType<Italic>());
            var italic = Assert.Single(paragraph.Inlines.OfType<Italic>());
            Assert.Single(italic.Inlines.OfType<Bold>());
            Assert.Contains(paragraph.Inlines.OfType<Span>(), span =>
                span.TextDecorations == TextDecorations.Strikethrough);
            Assert.Equal("bold italic and italic bold strike *literal*\r\n",
                new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text);
        });
    }

    [Fact]
    public void NestedListAndChoice_PreserveHierarchyAndSelectableOptions()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("- Parent\n  - Child\n    - Grandchild\n- Sibling\n\n" +
                "```choice\n? Format?\n- Markdown\n- Plain text\n```"));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var list = Assert.IsType<System.Windows.Documents.List>(box.Document.Blocks.FirstBlock);
            Assert.Equal(2, list.ListItems.Count);
            var nested = Assert.Single(list.ListItems.FirstListItem.Blocks.OfType<System.Windows.Documents.List>());
            Assert.Single(nested.ListItems.FirstListItem.Blocks.OfType<System.Windows.Documents.List>());
            box.SelectAll();
            Assert.Contains("Grandchild", box.Selection.Text);
            Assert.Contains("Markdown", box.Selection.Text);
            Assert.Contains("Plain text", box.Selection.Text);
        });
    }

    [Fact]
    public void TableAlignmentOrderedStartAndMessageCopy_PreserveVisibleMarkdownContent()
    {
        RunSta(() =>
        {
            var source = "3. Three\n4. Four\n\n| Left | Center | Right |\n| :--- | :---: | ---: |\n| A | B | C |\n\n" +
                "**bold *italic*** ~~strike~~\n\n```choice\n? Format?\n- Markdown\n- Plain text\n```";
            var presenter = CreatePresenter(Parse(source));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var list = Assert.IsType<System.Windows.Documents.List>(box.Document.Blocks.FirstBlock);
            Assert.Equal(3, list.StartIndex);
            var table = Assert.Single(box.Document.Blocks.OfType<Table>());
            Assert.Equal(TextAlignment.Center, ((Paragraph)table.RowGroups[0].Rows[1].Cells[1].Blocks.FirstBlock).TextAlignment);
            Assert.Equal(TextAlignment.Right, ((Paragraph)table.RowGroups[0].Rows[0].Cells[2].Blocks.FirstBlock).TextAlignment);
            var plainTextType = Type.GetType("Nexus.App.AIWorkspace.Markdown.MarkdownPlainText, Nexus.App", true)!;
            var copied = (string)plainTextType.GetMethod("ToPlainText")!.Invoke(null, new[] { source })!;
            Assert.Contains("3. Three", copied);
            Assert.Contains("bold italic strike", copied);
            Assert.Contains("Markdown", copied);
            Assert.Contains("Plain text", copied);
            Assert.DoesNotContain("**", copied);
        });
    }

    [Fact]
    public void Streaming_ScrollFollowsTailButRespectsReadingOlderContent()
    {
        RunSta(() =>
        {
            var vm = new ConversationViewModel(Mock.Of<IConversationManagerService>(), Mock.Of<IConversationRuntimeWorkflow>());
            var source = string.Join("\n\n", Enumerable.Repeat("A long response paragraph with selectable content.", 50));
            var message = new ChatMessageItemViewModel { Role = "assistant", Content = source };
            var apply = typeof(ChatMessageItemViewModel).GetMethod("ApplyFinalDocument", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(message, new[] { Parse(source) });
            vm.Messages.Add(message);
            var view = new Nexus.App.Views.ConversationView { DataContext = vm };
            Layout(view, 900);
            var scroll = (ScrollViewer)view.FindName("AIChatScroll");
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.InRange(Math.Abs(scroll.ScrollableHeight - scroll.VerticalOffset), 0, 1);
            scroll.ScrollToTop();
            Layout(view, 900);
            apply.Invoke(message, new[] { Parse(source + "\n\nStream appended below.") });
            Layout(view, 900);
            Assert.Equal(0, scroll.VerticalOffset);
            scroll.ScrollToBottom();
            Layout(view, 900);
            apply.Invoke(message, new[] { Parse(source + "\n\nStream appended below. More tokens.") });
            Layout(view, 900);
            Assert.InRange(Math.Abs(scroll.ScrollableHeight - scroll.VerticalOffset), 0, 1);
        });
    }

    [Fact]
    public void InlineTypeChange_DoesNotLeaveMonospaceStylingOnPlainText()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("`code`"));
            presenter.GetType().GetProperty("Document")!.SetValue(presenter, Parse("plain"));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var run = (Run)((Paragraph)box.Document.Blocks.FirstBlock).Inlines.FirstInline;
            Assert.Equal(box.FontFamily, run.FontFamily);
        });
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(1200)]
    [InlineData(900)]
    [InlineData(780)]
    [InlineData(600)]
    public void WorkspaceRuntime_ReadingBoundariesWelcomeAndResizeMatchInBothThemes(double width)
    {
        RunSta(() =>
        {
            foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                ThemeManager.ApplyTheme(theme);
                var vm = new ConversationViewModel(Mock.Of<IConversationManagerService>(), Mock.Of<IConversationRuntimeWorkflow>());
                var view = new Nexus.App.Views.ConversationView { DataContext = vm };
                view.SetResourceReference(Control.BackgroundProperty, "NexusSurfaceCanvas");
                var window = new Window
                {
                    Content = view, Width = width, Height = 800,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000
                };
                window.Show();
                try
                {
                    Layout(view, width);
                    var host = (FrameworkElement)view.FindName("AIComposerHost");
                    var title = (FrameworkElement)view.FindName("AIWelcomeTitle");
                    var composer = (FrameworkElement)view.FindName("AIInputBorder");
                    Assert.Same(host, composer.Parent);
                    var titleCenter = title.TranslatePoint(new Point(title.ActualWidth / 2, 0), view).X;
                    var composerCenter = composer.TranslatePoint(new Point(composer.ActualWidth / 2, 0), view).X;
                    Assert.InRange(Math.Abs(titleCenter - composerCenter), 0, 1);
                    SaveSnapshot(view, $"nexus-rendering-{theme}-{width}-empty.png");
                    vm.Messages.Add(new ChatMessageItemViewModel { Role = "user", Content = "Halo" });
                    var assistant = new ChatMessageItemViewModel { Role = "assistant", Content = MarkdownFixture };
                    typeof(ChatMessageItemViewModel).GetMethod("ApplyFinalDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(assistant, new[] { Parse(MarkdownFixture) });
                    vm.Messages.Add(assistant);
                    foreach (var sidebar in new[] { false, true, false })
                    {
                        if (vm.IsConversationPanelVisible != sidebar)
                        {
                            vm.ToggleSidebarCommand.Execute(null);
                            WaitForSidebarAnimation();
                        }
                        window.Width = width + 100;
                        Layout(view, width + 100);
                        window.Width = width;
                        Layout(view, width);
                        var reading = (FrameworkElement)view.FindName("AIChatReadingFrame");
                        var left = reading.TranslatePoint(new Point(), view).X;
                        var composerLeft = composer.TranslatePoint(new Point(), view).X;
                        Assert.InRange(Math.Abs(left - composerLeft), 0, 1);
                        Assert.InRange(Math.Abs(reading.ActualWidth - composer.ActualWidth), 0, 1);
                        Assert.True(composer.ActualWidth > 100);
                        Assert.True(composerLeft >= 0);
                        Assert.True(composerLeft + composer.ActualWidth <= width);
                        var box = VisualDescendants(view).OfType<RichTextBox>().Last();
                        var codeTable = box.Document.Blocks.OfType<Table>().First();
                        var language = (Paragraph)codeTable.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock;
                        var languageStart = language.ContentStart.GetNextInsertionPosition(LogicalDirection.Forward)!;
                        var nextLine = languageStart.GetLineStartPosition(1);
                        Assert.True(nextLine is null || nextLine.CompareTo(language.ContentEnd) > 0,
                            "Language label must fit on one line inside its own paragraph.");
                        SaveSnapshot(view, $"nexus-rendering-{theme}-{width}-sidebar-{sidebar}.png");
                    }
                    vm.Messages.Clear();
                }
                finally { window.Close(); }
            }
        });
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, 800));
        view.Arrange(new Rect(0, 0, width, 800));
        view.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
    }

    private static void WaitForSidebarAnimation()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void SaveSnapshot(FrameworkElement view, string name)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), "opencode", name));
        encoder.Save(stream);
    }

    [Fact]
    public void MarkdownLink_RendersAsNativeHyperlink_AndImageAsSafePlaceholder()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("[panduan](https://example.com) ![diagram](assets/diagram.png)"));
            var messageBox = Assert.Single(VisualDescendants(presenter).OfType<RichTextBox>());
            var paragraph = Assert.IsType<Paragraph>(Assert.Single(messageBox.Document.Blocks));
            var hyperlink = Assert.Single(paragraph.Inlines.OfType<Hyperlink>());

            Assert.Equal("https://example.com/", hyperlink.NavigateUri.AbsoluteUri);
            Assert.Contains("[Gambar: diagram]", new TextRange(
                messageBox.Document.ContentStart, messageBox.Document.ContentEnd).Text);
            Assert.Empty(VisualDescendants(presenter).OfType<Image>());
        });
    }

    [Fact]
    public void ModelComboBox_SelectedValueUsesDisplayMemberTemplate()
    {
        var stylePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Nexus.App", "Themes", "Components", "InputControls.xaml"));
        var document = XDocument.Load(stylePath);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = document
            .Descendants(presentation + "Style")
            .Single(element =>
                (string?)element.Attribute(x + "Key") == "NexusComboBoxStyle");
        var selectedPresenter = style
            .Descendants(presentation + "ContentPresenter")
            .Single();

        Assert.Equal(
            "{Binding SelectionBoxItemTemplate, RelativeSource={RelativeSource AncestorType=ComboBox}}",
            (string?)selectedPresenter.Attribute("ContentTemplate"));
        Assert.Equal(
            "{Binding SelectionBoxItemTemplateSelector, RelativeSource={RelativeSource AncestorType=ComboBox}}",
            (string?)selectedPresenter.Attribute("ContentTemplateSelector"));

        var itemStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                (string?)element.Attribute(x + "Key") == "NexusComboBoxItemStyle");
        var itemPresenter = itemStyle
            .Descendants(presentation + "ContentPresenter")
            .Single();
        Assert.Equal(
            "{TemplateBinding ContentTemplate}",
            (string?)itemPresenter.Attribute("ContentTemplate"));
        Assert.Equal(
            "{TemplateBinding ContentTemplateSelector}",
            (string?)itemPresenter.Attribute("ContentTemplateSelector"));
    }

    [Fact]
    public void UserAndAssistantMessages_RenderWithReadOnlySelectableControls()
    {
        RunSta(() =>
        {
            var document = Parse("Teks **tebal** dan `inline code`.");
            var userPresenter = CreatePresenter(document);
            var assistantPresenter = CreatePresenter(document);

            AssertSelectableContent(userPresenter);
            AssertSelectableContent(assistantPresenter);
        });
    }

    [Fact]
    public void CodeBlock_RendersAsSelectableReadOnlyControlWithoutInternalScrollbars()
    {
        RunSta(() =>
        {
            var document = Parse(
                "```powershell\nWrite-Output 'Nexus'\n```");
            var presenter = CreatePresenter(document);
            var messageBox = Assert.Single(
                VisualDescendants(presenter).OfType<RichTextBox>());
            var source = new TextRange(
                messageBox.Document.ContentStart,
                messageBox.Document.ContentEnd).Text;

            Assert.True(messageBox.IsReadOnly);
            Assert.Equal(new Thickness(0), messageBox.BorderThickness);
            Assert.Equal(ScrollBarVisibility.Disabled, messageBox.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, messageBox.HorizontalScrollBarVisibility);
            Assert.Contains("powershell", source);
            Assert.Contains("Write-Output 'Nexus'", source);
        });
    }

    [Fact]
    public void CodeBlockHeader_CopyActionCopiesOnlyCodeContent()
    {
        RunSta(() =>
        {
            var document = Parse(
                "Sebelum blok\n\n```powershell\nWrite-Output 'Nexus'\n```\n\nSebelum blok (copy langkah sendiri).");
            var presenter = CreatePresenter(document);
            var messageBox = Assert.Single(
                VisualDescendants(presenter).OfType<RichTextBox>());
            var hyperlink = FindCodeBlockCopyHyperlink(messageBox.Document);
            Assert.NotNull(hyperlink);

            var previous = WpfAppHost.WithClipboardRetry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);
            try
            {
                hyperlink.RaiseEvent(
                    new RoutedEventArgs(Hyperlink.ClickEvent, hyperlink));
                Assert.Equal(
                    "Write-Output 'Nexus'",
                    WpfAppHost.WithClipboardRetry(() => Clipboard.GetText()));
            }
            finally
            {
                if (previous is null)
                {
                    Clipboard.Clear();
                }
                else
                {
                    Clipboard.SetText(previous);
                }
            }
        });
    }

    private static Hyperlink? FindCodeBlockCopyHyperlink(FlowDocument document)
    {
        foreach (var table in document.Blocks.OfType<Table>())
        {
            foreach (var row in table.RowGroups.SelectMany(group => group.Rows))
            {
                foreach (var cell in row.Cells)
                {
                    var paragraph = cell.Blocks.FirstOrDefault() as Paragraph;
                    if (paragraph == null)
                    {
                        continue;
                    }

                    var hyperlink = paragraph.Inlines
                        .OfType<Hyperlink>()
                        .FirstOrDefault();
                    if (hyperlink != null)
                    {
                        return hyperlink;
                    }
                }
            }
        }

        return null;
    }

    [Fact]
    public void MessageSelection_CrossesParagraphListQuoteCodeAndFinalParagraph()
    {
        RunSta(() =>
        {
            var document = Parse(
                "Awal pesan.\n\n" +
                "- Item daftar\n\n" +
                "> Kutipan\n\n" +
                "```powershell\nWrite-Output 'Nexus'\n```\n\n" +
                "| Fitur | Status |\n" +
                "| --- | --- |\n" +
                "| Table | Aktif |\n\n" +
                "Akhir pesan.");
            var presenter = CreatePresenter(document);
            var messageBox = Assert.Single(
                VisualDescendants(presenter).OfType<RichTextBox>());

            messageBox.Selection.Select(
                messageBox.Document.ContentStart,
                messageBox.Document.ContentEnd);
            var selection = messageBox.Selection.Text;

            Assert.Contains("Awal pesan.", selection);
            Assert.Contains("Item daftar", selection);
            Assert.Contains("Kutipan", selection);
            Assert.Contains("Write-Output 'Nexus'", selection);
            Assert.Contains("Fitur", selection);
            Assert.Contains("Aktif", selection);
            Assert.Contains("Akhir pesan.", selection);
        });
    }

    [Fact]
    public void Table_RendersInsideTheSingleSelectableDocument()
    {
        RunSta(() =>
        {
            var document = Parse(
                "| Fitur | Status |\n" +
                "| --- | --- |\n" +
                "| Table | Aktif |");
            var presenter = CreatePresenter(document);
            var messageBox = Assert.Single(
                VisualDescendants(presenter).OfType<RichTextBox>());
            var table = Assert.Single(messageBox.Document.Blocks.OfType<Table>());

            Assert.Equal(2, table.RowGroups[0].Rows.Count);
            Assert.Equal(2, table.RowGroups[0].Rows[0].Cells.Count);
            messageBox.Selection.Select(
                messageBox.Document.ContentStart,
                messageBox.Document.ContentEnd);
            Assert.Contains("Fitur", messageBox.Selection.Text);
            Assert.Contains("Aktif", messageBox.Selection.Text);
        });
    }

    [Fact]
    public void ExistingMessageColors_UpdateWhenThemeResourcesChange()
    {
        RunSta(() =>
        {
            var document = Parse(
                "Paragraph **bold** *italic* `code`.\n\n" +
                "- List\n\n> Quote\n\n" +
                "```powershell\nWrite-Output 'Nexus'\n```\n\n" +
                "| Fitur | Status |\n" +
                "| --- | --- |\n" +
                "| Table | Aktif |");
            var presenter = CreatePresenter(
                document,
                resources: new Dictionary<string, object>
                {
                    ["NexusForegroundPrimary"] = Brushes.DarkRed,
                    ["TextSecondaryBrush"] = Brushes.DarkGreen,
                    ["SurfaceSecondaryBrush"] = Brushes.Beige,
                    ["NexusContentBorder"] = Brushes.Orange,
                    ["AccentPrimaryBrush"] = Brushes.Blue
                });
            var messageBox = Assert.Single(
                VisualDescendants(presenter).OfType<RichTextBox>());

            Assert.Same(Brushes.DarkRed, messageBox.Foreground);
            presenter.Resources["NexusForegroundPrimary"] = Brushes.Navy;
            presenter.Resources["SurfaceSecondaryBrush"] = Brushes.LightBlue;
            presenter.UpdateLayout();

            Assert.Same(Brushes.Navy, messageBox.Foreground);
            var tables = messageBox.Document.Blocks
                .OfType<Table>()
                .ToList();
            Assert.Equal(2, tables.Count);
            var codeTable = tables[0];
            Assert.Same(Brushes.LightBlue, codeTable.Background);
            var markdownHeader = tables[1].RowGroups[0].Rows[0].Cells[0];
            Assert.Same(Brushes.LightBlue, markdownHeader.Background);
            Assert.Same(Brushes.Orange, markdownHeader.BorderBrush);
        });
    }

    [Fact]
    public void UserBubblePresenter_ShortContentDoesNotConsumeMaxWidth()
    {
        RunSta(() =>
        {
            var shortPresenter = CreatePresenter(
                Parse("Halo"),
                sizeToContent: true,
                availableWidth: 680);
            var longPresenter = CreatePresenter(
                Parse(string.Join(' ', Enumerable.Repeat(
                    "Paragraf panjang untuk menguji wrapping.", 30))),
                sizeToContent: true,
                availableWidth: 680);

            Assert.True(
                shortPresenter.DesiredSize.Width < 240,
                $"Short bubble requested {shortPresenter.DesiredSize.Width}px.");
            Assert.True(
                longPresenter.DesiredSize.Width > shortPresenter.DesiredSize.Width,
                $"Long={longPresenter.DesiredSize.Width}, short={shortPresenter.DesiredSize.Width}.");
            Assert.True(
                longPresenter.DesiredSize.Width <= 680,
                $"Long bubble requested {longPresenter.DesiredSize.Width}px.");
            Assert.True(
                longPresenter.DesiredSize.Height > shortPresenter.DesiredSize.Height,
                $"Long={longPresenter.DesiredSize.Height}, short={shortPresenter.DesiredSize.Height}.");
            var shortBox = Assert.Single(
                VisualDescendants(shortPresenter).OfType<RichTextBox>());
            var firstPosition = shortBox.Document.ContentStart
                .GetNextInsertionPosition(LogicalDirection.Forward);
            Assert.NotNull(firstPosition);
            Assert.True(
                firstPosition!.GetLineStartPosition(1) is null,
                "A one-word user bubble must remain on one visual line.");
        });
    }

    [Fact]
    public void MessageActions_AreOutsideSelectablePresenterAndHaveNoBubbleClickBinding()
    {
        var document = LoadConversationView();
        var presenter = document.Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "MessageContentPresenter");
        Assert.NotNull(presenter);

        var itemTemplate = presenter!.Ancestors()
            .First(element => element.Name.LocalName == "DataTemplate");
        var actionButtons = itemTemplate.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToList();

        Assert.Contains(
            actionButtons,
            button => ((string?)button.Attribute("Command"))?.Contains(
                "CopyMessageCommand",
                StringComparison.Ordinal) == true);
        Assert.Contains(
            actionButtons,
            button => ((string?)button.Attribute("Command"))?.Contains(
                "RegenerateMessageCommand",
                StringComparison.Ordinal) == true);
        Assert.Empty(presenter.Descendants().Where(element => element.Name.LocalName == "Button"));
        Assert.Null(presenter.Attribute("MouseLeftButtonDown"));
        Assert.Null(presenter.Attribute("MouseBinding"));
    }

    [Fact]
    public void MessageActions_UseNativeIconsAndExposeCopyForBothRoles()
    {
        var document = LoadConversationView();
        var itemTemplate = document.Descendants()
            .Single(element => element.Name.LocalName == "MessageContentPresenter")
            .Ancestors()
            .First(element => element.Name.LocalName == "DataTemplate");
        var buttons = itemTemplate.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToList();
        var copyButtons = buttons.Where(button =>
                ((string?)button.Attribute("Command"))?.Contains(
                    "CopyMessageCommand",
                    StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(2, copyButtons.Count);
        var reload = Assert.Single(buttons, button =>
            ((string?)button.Attribute("Command"))?.Contains(
                "RegenerateMessageCommand",
                StringComparison.Ordinal) == true);
        var edit = Assert.Single(buttons, button =>
            ((string?)button.Attribute("Command"))?.Contains(
                "EditMessageCommand",
                StringComparison.Ordinal) == true);

        Assert.All(copyButtons, copy => Assert.Null(copy.Attribute("Content")));
        Assert.Null(reload.Attribute("Content"));
        Assert.All(copyButtons, copy => Assert.NotNull(copy.Attribute("ToolTip")));
        Assert.NotNull(reload.Attribute("ToolTip"));
        Assert.NotNull(edit.Attribute("ToolTip"));
        Assert.All(copyButtons, copy => Assert.Contains(
            "CanCopy",
            (string?)copy.Attribute("Visibility") ?? string.Empty));
        Assert.Contains(
            "CanReload",
            (string?)reload.Attribute("Visibility") ?? string.Empty);
        Assert.All(copyButtons, copy => Assert.Contains(
            copy.Descendants(),
            child => child.Name.LocalName == "TextBlock" &&
                     child.Attributes().Any(attribute =>
                         attribute.Name.LocalName.EndsWith("Glyph", StringComparison.Ordinal) &&
                         attribute.Value.Contains(
                             "CopyIconName", StringComparison.Ordinal))));
        Assert.Contains(
            reload.Descendants(),
            child => child.Name.LocalName == "TextBlock" &&
                     child.Attributes().Any(attribute =>
                         attribute.Name.LocalName.EndsWith("Glyph", StringComparison.Ordinal) &&
                         attribute.Value == "Refresh"));

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var userActionRow = itemTemplate.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "UserActionRow");
        Assert.Single(copyButtons, copy => copy.Ancestors().Contains(userActionRow));
        Assert.Single(copyButtons, copy => !copy.Ancestors().Contains(userActionRow));
        Assert.Contains(userActionRow.Descendants(), element =>
            element.Name.LocalName == "Condition" &&
            ((string?)element.Attribute("Binding"))?.Contains(
                "IsMouseOver", StringComparison.Ordinal) == true);
        Assert.Contains(userActionRow.Descendants(), element =>
            element.Name.LocalName == "Condition" &&
            ((string?)element.Attribute("Binding"))?.Contains(
                "IsKeyboardFocusWithin", StringComparison.Ordinal) == true);
    }

    private static FrameworkElement CreatePresenter(
        object document,
        bool sizeToContent = false,
        double availableWidth = 760,
        IReadOnlyDictionary<string, object>? resources = null)
    {
        var presenterType = Type.GetType(
            "Nexus.App.AIWorkspace.Rendering.MessageContentPresenter, Nexus.App",
            throwOnError: false);
        if (presenterType is null)
        {
            throw new XunitException(
                "MessageContentPresenter is missing; RED until the approved renderer is implemented.");
        }

        if (Activator.CreateInstance(presenterType, nonPublic: true) is not FrameworkElement presenter)
        {
            throw new XunitException("MessageContentPresenter is not a FrameworkElement.");
        }

        var documentProperty = presenterType.GetProperty(
            "Document",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (documentProperty is null)
        {
            throw new XunitException("MessageContentPresenter.Document is missing.");
        }

        if (resources != null)
        {
            foreach (var resource in resources)
            {
                presenter.Resources[resource.Key] = resource.Value;
            }
        }

        var sizeProperty = presenterType.GetProperty(
            "SizeToContent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sizeToContent)
        {
            Assert.NotNull(sizeProperty);
            sizeProperty!.SetValue(presenter, true);
        }

        documentProperty.SetValue(presenter, document);
        presenter.Measure(new Size(availableWidth, double.PositiveInfinity));
        presenter.Arrange(new Rect(
            0,
            0,
            presenter.DesiredSize.Width,
            Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();
        return presenter;
    }

    private static object Parse(string source)
    {
        var parserType = Type.GetType(
            "Nexus.App.AIWorkspace.Markdown.NexusMessageParser, Nexus.App",
            throwOnError: false);
        if (parserType is null)
        {
            throw new XunitException("NexusMessageParser is missing.");
        }

        var parser = Activator.CreateInstance(parserType, nonPublic: true)
            ?? throw new XunitException("NexusMessageParser could not be constructed.");
        var parse = parserType.GetMethod(
            "Parse",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null)
            ?? throw new XunitException("Expected Parse(string, bool).");
        return parse.Invoke(parser, new object[] { source, true })
            ?? throw new XunitException("Parser returned null.");
    }

    private static void AssertSelectableContent(FrameworkElement presenter)
    {
        var selectable = VisualDescendants(presenter)
            .OfType<TextBoxBase>()
            .ToList();
        Assert.NotEmpty(selectable);
        Assert.All(selectable, control =>
        {
            Assert.True(control.IsReadOnly);
            Assert.True(control.IsInactiveSelectionHighlightEnabled);
            Assert.Equal(new Thickness(0), control.BorderThickness);
            Assert.Equal(ScrollBarVisibility.Disabled, control.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, control.HorizontalScrollBarVisibility);
        });

        var richText = selectable.OfType<RichTextBox>().FirstOrDefault();
        if (richText != null)
        {
            var start = richText.Document.ContentStart.GetNextInsertionPosition(
                LogicalDirection.Forward);
            Assert.NotNull(start);
            var end = start!.GetPositionAtOffset(4, LogicalDirection.Forward);
            Assert.NotNull(end);
            richText.Selection.Select(start, end!);
            Assert.NotEmpty(richText.Selection.Text);
            return;
        }

        var textBox = Assert.IsType<TextBox>(selectable[0]);
        textBox.Select(0, Math.Min(4, textBox.Text.Length));
        Assert.NotEmpty(textBox.SelectedText);
    }

    [Fact]
    public void CodeBlock_TypographyAndPadding_MatchesHarnessSpecification()
    {
        RunSta(() =>
        {
            var presenter = CreatePresenter(Parse("```csharp\nConsole.WriteLine(\"Hello\");\n```"));
            var box = (RichTextBox)((ContentControl)presenter).Content;
            var table = Assert.IsType<Table>(box.Document.Blocks.FirstBlock);

            // Header row: language padding 10,6,4,6; copy padding 0,6,12,6; font size 11
            var headerRow = table.RowGroups[0].Rows[0];
            var languageCell = headerRow.Cells[0];
            var copyCell = headerRow.Cells[1];
            Assert.Equal(new Thickness(10, 6, 4, 6), languageCell.Padding);
            Assert.Equal(new Thickness(0, 6, 12, 6), copyCell.Padding);
            var langPara = Assert.IsType<Paragraph>(languageCell.Blocks.FirstBlock);
            Assert.Equal(11.0, langPara.FontSize);

            // Content row: padding 14,8,14,12
            var contentRow = table.RowGroups[0].Rows[1];
            var contentCell = contentRow.Cells[0];
            Assert.Equal(new Thickness(14, 8, 14, 12), contentCell.Padding);
            Assert.Equal(2, contentCell.ColumnSpan);
        });
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static XDocument LoadConversationView()
    {
        var viewPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Nexus.App", "Views", "ConversationView.xaml"));
        return XDocument.Load(viewPath);
    }

    private static void RunSta(Action action)
    {
        WpfAppHost.Run(action);
    }
}
