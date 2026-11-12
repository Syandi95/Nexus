using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Xunit;

namespace Nexus.Integration.Tests;

// ============================================================================
// REGRESSION TEST (TDD RED -> GREEN).
//
// Reproduces the real UI condition that broke ASCII diagrams:
//   IsUser=true  ->  SizeToContent=true
//   CodeBlock containing a wide multi-column ASCII diagram.
//
// Behaviour under test:
//   The width MessageContentPresenter.MeasureDocumentWidth returns (which
//   becomes the RichTextBox viewport when SizeToContent=true) must be >= the
//   width the CodeBlock actually needs.  A smaller width forces the
//   long Consolas rows to wrap, breaking the box's border geometry.
//
// The "required width" is computed from the CodeBlock's REAL typography
// (its resolved FontFamily / FontSize) - never hardcoded, never ASCII-shaped.
// ============================================================================
[Collection(WpfApplicationCollection.Name)]
public class AsciiCodeBlockSizeToContentTests
{
    private const string BrokenRectangularDiagram =
        "+--------------------+\n" +
        "| Header             |\n" +
        "| AI Workspace Nexus |\n" +
        "| Status: Ready      |\n" +
        "+------------------+";

    private const string RepairedRectangularDiagram =
        "+--------------------+\n" +
        "| Header             |\n" +
        "| AI Workspace Nexus |\n" +
        "| Status: Ready      |\n" +
        "+--------------------+";

    private const string LongRectangularDiagram =
        "+--------------------------+\n" +
        "| Header                   |\n" +
        "| AI Workspace Nexus       |\n" +
        "| Status: Ready            |\n" +
        "| Sidebar                  |\n" +
        "| Home                     |\n" +
        "| AI Workspace             |\n" +
        "| Image Tools              |\n" +
        "| PDF Tools                |\n" +
        "| Content                  |\n" +
        "| Area Percakapan          |\n" +
        "| User                     |\n" +
        "| Pesan Pengguna           |\n" +
        "| AI                       |\n" +
        "| Pesan AI                 |\n" +
        "| Footer                   |\n" +
        "| Input Area               |\n" +
        "+--------------------------+";

    private const string RealDiagram =
        "+--------------------------------------------------------------+\n" +
        "|                         AI MODEL SELECTOR                     |\n" +
        "+--------------------------------------------------------------+\n" +
        "| [ KARTU 1 ]        [ KARTU 2 ]        [ KARTU 3 ]\n" +
        "| Deskripsi kartu pertama yang sangat panjang dan berada di baris isi\n" +
        "| yang biasanya membutuhkan lebih banyak kolom daripada border kartu\n" +
        "| [ PILIH ]           [ PILIH ]            [ PILIH ]\n" +
        "+--------------------------------------------------------------+";

    [Fact]
    public void SizeToContent_UserCodeBlockWidth_IsNotSmallerThanRequiredCodeWidth()
    {
        RunSta(() =>
        {
            EnsureAppResources();

            var presenter = CreateUserPresenter(Parse(RealDiagram));
            var box = Descendants(presenter).OfType<RichTextBox>().First();
            var flow = box.Document;
            var code = CodeParagraphOf(flow);
            Assert.NotNull(code);

            var codeText = string.Concat(code.Inlines.OfType<Run>().Select(r => r.Text));
            Assert.Equal(RealDiagram, codeText);

            var run = code.Inlines.OfType<Run>().First();
            var typeface = new Typeface(
                run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);
            var required = RequiredCodeWidth(codeText, run.FontSize, typeface);
            var measured = presenter.DesiredSize.Width;

            Assert.True(
                measured >= required,
                $"SizeToContent under-provisions the CodeBlock: " +
                $"measured={measured:0.###}px < required={required:0.###}px. " +
                $"Render font='{run.FontFamily.Source}' @ {run.FontSize}.");

            var logical = codeText.Split('\n').Length;
            var visual = CountVisualLines(code);
            Assert.True(
                visual == logical,
                $"Code block wrapped: visual={visual} != logical={logical}; " +
                $"viewport={box.ViewportWidth:0.###}px too narrow for required {required:0.###}px.");
        });
    }

    [Fact]
    public void BrokenRectangularAscii_RendersNormalizedContentInOneVisualLinePerLogicalLine()
    {
        RunSta(() =>
        {
            EnsureAppResources();

            var presenter = CreateUserPresenter(Parse(BrokenRectangularDiagram));
            var box = Descendants(presenter).OfType<RichTextBox>().First();
            var code = CodeParagraphOf(box.Document);
            Assert.NotNull(code);

            var run = Assert.Single(code!.Inlines.OfType<Run>());
            Assert.Equal(RepairedRectangularDiagram, run.Text);
            Assert.Equal("Consolas", run.FontFamily.Source);

            Assert.Equal(
                RepairedRectangularDiagram.Split('\n').Length,
                CountVisualLines(code));
        });
    }

    [Fact]
    public void LongRectangularAscii_RendersEveryClosedBorderOnOneVisualLine()
    {
        RunSta(() =>
        {
            EnsureAppResources();

            var presenter = CreateUserPresenter(Parse(LongRectangularDiagram));
            var box = Descendants(presenter).OfType<RichTextBox>().First();
            var code = CodeParagraphOf(box.Document);
            Assert.NotNull(code);

            var run = Assert.Single(code!.Inlines.OfType<Run>());
            Assert.Equal(LongRectangularDiagram, run.Text);
            Assert.Equal("Consolas", run.FontFamily.Source);
            Assert.Equal(
                LongRectangularDiagram.Split('\n').Length,
                CountVisualLines(code));
        });
    }

    private static double RequiredCodeWidth(string text, double fontSize, Typeface typeface)
    {
        var required = 0d;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var ft = new FormattedText(
                line,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0);
            required = Math.Max(required, ft.WidthIncludingTrailingWhitespace);
        }

        return required;
    }

    // ---------- infra (mirrors existing probe/integration tests) ----------

    private static object Parse(string source)
    {
        var parserType = Type.GetType(
            "Nexus.App.AIWorkspace.Markdown.NexusMessageParser, Nexus.App",
            throwOnError: false);
        Assert.NotNull(parserType);
        var parser = Activator.CreateInstance(parserType!, nonPublic: true);
        var parse = parserType!.GetMethod(
            "Parse",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null);
        Assert.NotNull(parse);
        return parse!.Invoke(parser!, new object[] { source, true })!;
    }

    private static FrameworkElement CreateUserPresenter(object document)
    {
        var presenterType = Type.GetType(
            "Nexus.App.AIWorkspace.Rendering.MessageContentPresenter, Nexus.App",
            throwOnError: false);
        Assert.NotNull(presenterType);
        if (Activator.CreateInstance(presenterType!, nonPublic: true) is not FrameworkElement presenter)
        {
            throw new InvalidOperationException("Could not create presenter.");
        }

        presenterType!.GetProperty("Document")!.SetValue(presenter, document);
        presenterType!.GetProperty("SizeToContent")!.SetValue(presenter, true);
        presenter.Measure(new Size(900, double.PositiveInfinity));
        presenter.Arrange(new Rect(
            0, 0, presenter.DesiredSize.Width, Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();
        return presenter;
    }

    private static Paragraph? CodeParagraphOf(FlowDocument document)
    {
        Paragraph? best = null;
        var bestLen = 0;
        foreach (var table in document.Blocks.OfType<Table>())
        {
            foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var para in cell.Blocks.OfType<Paragraph>())
                    {
                        var len = para.Inlines.OfType<Run>().Sum(r => r.Text.Length);
                        if (len > bestLen)
                        {
                            bestLen = len;
                            best = para;
                        }
                    }
                }
            }
        }

        return best;
    }

    private static int CountVisualLines(Paragraph paragraph)
    {
        var count = 0;
        var position = paragraph.ContentStart;
        while (position != null)
        {
            count++;
            var next = position.GetLineStartPosition(1);
            if (next == null)
            {
                break;
            }

            if (next.CompareTo(paragraph.ContentEnd) > 0)
            {
                break;
            }

            position = next;
        }

        return count;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    private static void EnsureAppResources()
    {
        WpfAppHost.Run(() => { });
    }

    private static void RunSta(Action action)
    {
        WpfAppHost.Run(action);
    }
}
