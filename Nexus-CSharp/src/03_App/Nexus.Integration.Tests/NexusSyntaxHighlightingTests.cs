using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.App.AIWorkspace.Rendering;
using Nexus.App.Services;
using Xunit;

namespace Nexus.Integration.Tests;

public class NexusSyntaxHighlightingTests
{
    private const string ArtifactPath = @"C:\Users\Syand\.gemini\antigravity-ide\brain\875b40b1-0201-4ce4-8bae-60168d83f4df";

    [Fact]
    public void SyntaxColorizer_CSharpCode_GeneratesDistinctKeywordStringCommentAndNumberRuns()
    {
        WpfAppHost.Run(() =>
        {
            var paragraph = new Paragraph();
            const string code = "using System;\npublic class Calculator\n{\n    private int _val = 42; // accumulator\n    public string Name => \"Calc\";\n}";
            NexusSyntaxColorizer.PopulateInlines(paragraph, "csharp", code);

            var runs = paragraph.Inlines.OfType<Run>().ToList();
            Assert.NotEmpty(runs);

            // Reconstructed text must equal original exactly
            var fullText = string.Concat(runs.Select(r => r.Text));
            Assert.Equal(code, fullText);

            // Check for keyword runs (public, class, int, string, using)
            var keywordRuns = runs.Where(r => IsBrushReference(r, "NexusSyntaxKeywordBrush")).ToList();
            Assert.NotEmpty(keywordRuns);
            Assert.Contains(keywordRuns, r => r.Text.Trim() == "public" || r.Text.Trim() == "class" || r.Text.Trim() == "using");

            // Check for string run ("Calc")
            var stringRuns = runs.Where(r => IsBrushReference(r, "NexusSyntaxStringBrush")).ToList();
            Assert.NotEmpty(stringRuns);
            Assert.Contains(stringRuns, r => r.Text.Contains("\"Calc\""));

            // Check for comment run (// accumulator)
            var commentRuns = runs.Where(r => IsBrushReference(r, "NexusSyntaxCommentBrush")).ToList();
            Assert.NotEmpty(commentRuns);
            Assert.Contains(commentRuns, r => r.Text.Contains("// accumulator"));

            // Check for number run (42)
            var numberRuns = runs.Where(r => IsBrushReference(r, "NexusSyntaxNumberBrush")).ToList();
            Assert.NotEmpty(numberRuns);
            Assert.Contains(numberRuns, r => r.Text.Trim() == "42");
        });
    }

    [Fact]
    public void SyntaxColorizer_JsonCode_GeneratesKeysAndValues()
    {
        WpfAppHost.Run(() =>
        {
            var paragraph = new Paragraph();
            const string json = "{\n  \"service\": \"Nexus\",\n  \"retries\": 3,\n  \"enabled\": true\n}";
            NexusSyntaxColorizer.PopulateInlines(paragraph, "json", json);

            var runs = paragraph.Inlines.OfType<Run>().ToList();
            Assert.NotEmpty(runs);
            Assert.Equal(json, string.Concat(runs.Select(r => r.Text)));

            // String values
            Assert.Contains(runs, r => IsBrushReference(r, "NexusSyntaxStringBrush") && r.Text.Contains("\"Nexus\""));

            // Numbers
            Assert.Contains(runs, r => IsBrushReference(r, "NexusSyntaxNumberBrush") && r.Text.Trim() == "3");

            // Const / boolean
            Assert.Contains(runs, r => IsBrushReference(r, "NexusSyntaxKeywordBrush") && r.Text.Trim() == "true");
        });
    }

    [Fact]
    public void SyntaxColorizer_UnknownLanguage_FallsBackToPlainTextWithoutThrowing()
    {
        WpfAppHost.Run(() =>
        {
            var paragraph = new Paragraph();
            const string snippet = "SOME_CUSTOM_DIRECTIVE = 123";
            NexusSyntaxColorizer.PopulateInlines(paragraph, "unknown_custom_lang_xyz", snippet);

            var runs = paragraph.Inlines.OfType<Run>().ToList();
            var run = Assert.Single(runs);
            Assert.Equal(snippet, run.Text);
            Assert.True(IsBrushReference(run, "NexusSyntaxPlainTextBrush"));
        });
    }

    [Fact]
    public void MessageContentPresenter_CodeBlock_RendersSyntaxRunsAndSupportsThemeSwitching()
    {
        WpfAppHost.Run(() =>
        {
            const string markdown = "```csharp\npublic class Greeter\n{\n    // Say hello\n    public string Greet() => \"Hello Nexus!\";\n}\n```";
            var parsed = ParseMarkdown(markdown);
            var presenter = new MessageContentPresenter
            {
                Document = parsed
            };

            presenter.Measure(new Size(800, 600));
            presenter.Arrange(new Rect(0, 0, 800, 600));
            presenter.UpdateLayout();

            var box = Assert.IsType<RichTextBox>(presenter.Content);
            var table = Assert.IsType<Table>(box.Document.Blocks.FirstBlock);

            // Row 0 = header (language + copy)
            // Row 1 = code content
            var contentCell = table.RowGroups[0].Rows[1].Cells[0];
            var codePara = Assert.IsType<Paragraph>(contentCell.Blocks.FirstBlock);
            var runs = codePara.Inlines.OfType<Run>().ToList();
            Assert.True(runs.Count > 1, "Code content must contain multiple syntax-highlighted runs.");

            // Verify theme switching does not throw and preserves runs
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.HighContrast })
            {
                ThemeManager.ApplyTheme(mode);
                presenter.UpdateLayout();
                Assert.Same(table, box.Document.Blocks.FirstBlock);
                Assert.Equal(runs.Count, codePara.Inlines.OfType<Run>().Count());

                // Capture snapshot for verification evidence
                CaptureElement(presenter, $"c1_1_syntax_csharp_{mode}.png", mode);
            }

            // Restore dark theme
            ThemeManager.ApplyTheme(ThemeMode.Dark);
        });
    }

    [Fact]
    public void MessageContentPresenter_StreamingCodeBlock_PreservesTableAndUpdatesHighlighting()
    {
        WpfAppHost.Run(() =>
        {
            var initial = ParseMarkdown("```csharp\nint a = 1;");
            var presenter = new MessageContentPresenter { Document = initial };
            presenter.Measure(new Size(800, 600));
            presenter.Arrange(new Rect(0, 0, 800, 600));
            presenter.UpdateLayout();

            var box = Assert.IsType<RichTextBox>(presenter.Content);
            var table = Assert.IsType<Table>(box.Document.Blocks.FirstBlock);
            var copyCell = table.RowGroups[0].Rows[0].Cells[1];
            var copyHyperlink = Assert.IsType<Hyperlink>(((Paragraph)copyCell.Blocks.FirstBlock).Inlines.FirstInline);

            // Stream update
            const string updatedCode = "```csharp\nint a = 1;\nstring msg = \"Streaming...\";\n```";
            var updated = ParseMarkdown(updatedCode);
            presenter.Document = updated;
            presenter.UpdateLayout();

            // Structure must be preserved
            Assert.Same(table, box.Document.Blocks.FirstBlock);
            Assert.Same(copyHyperlink, ((Paragraph)copyCell.Blocks.FirstBlock).Inlines.FirstInline);
            Assert.Equal("int a = 1;\nstring msg = \"Streaming...\";", (string)copyHyperlink.Tag);

            var codePara = (Paragraph)table.RowGroups[0].Rows[1].Cells[0].Blocks.FirstBlock;
            var runs = codePara.Inlines.OfType<Run>().ToList();
            Assert.Contains(runs, r => IsBrushReference(r, "NexusSyntaxStringBrush") && r.Text.Contains("\"Streaming...\""));
        });
    }

    private static bool IsBrushReference(Run run, string expectedResourceKey)
    {
        var expression = run.ReadLocalValue(TextElement.ForegroundProperty);
        if (expression != null)
        {
            var keyProp = expression.GetType().GetProperty("ResourceKey");
            if (keyProp != null)
            {
                var key = keyProp.GetValue(expression)?.ToString();
                if (string.Equals(key, expectedResourceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var str = expression.ToString() ?? string.Empty;
            if (str.Contains(expectedResourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (Application.Current != null && Application.Current.Resources.Contains(expectedResourceKey))
        {
            var expectedBrush = Application.Current.Resources[expectedResourceKey];
            if (Equals(run.Foreground, expectedBrush))
            {
                return true;
            }
        }

        return false;
    }

    private static object ParseMarkdown(string markdown)
    {
        var parserType = Type.GetType("Nexus.App.AIWorkspace.Markdown.NexusMessageParser, Nexus.App", true)!;
        var parser = Activator.CreateInstance(parserType, nonPublic: true)!;
        var parseMethod = parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null)!;
        return parseMethod.Invoke(parser, new object[] { markdown, true })!;
    }

    private static void CaptureElement(FrameworkElement element, string filename, ThemeMode mode)
    {
        try
        {
            var width = Math.Max(100, (int)Math.Ceiling(element.ActualWidth > 0 ? element.ActualWidth : 700));
            var height = Math.Max(60, (int)Math.Ceiling(element.ActualHeight > 0 ? element.ActualHeight : 200));

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            var bgBrush = mode switch
            {
                ThemeMode.Light => new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                ThemeMode.Dark => new SolidColorBrush(Color.FromRgb(37, 37, 37)),
                ThemeMode.HighContrast => new SolidColorBrush(Colors.Black),
                _ => Brushes.Transparent
            };
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));
                var vb = new VisualBrush(element) { Stretch = Stretch.None };
                dc.DrawRectangle(vb, null, new Rect(0, 0, width, height));
            }
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var filePath = Path.Combine(ArtifactPath, filename);
            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }
        catch
        {
            // Best effort capture for test diagnostics
        }
    }
}
