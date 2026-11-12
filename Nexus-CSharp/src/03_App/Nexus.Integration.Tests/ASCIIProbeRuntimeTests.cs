using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Xunit;

namespace Nexus.Integration.Tests;

// OBSERVATIONAL PROBE (Langkah 1).
// No assertions on behavior. Collects runtime evidence to locate FIRST
// FAILURE POINT of the ASCII diagram pipeline. It does NOT fix anything.
[Collection(WpfApplicationCollection.Name)]
public class ASCIIProbeRuntimeTests
{

    [Fact]
    public void Probe_AsciiDiagramPipeline_CollectsRuntimeEvidence()
    {
        var report = new StringBuilder();
        Exception? fatal = null;
        try
        {
            EnsureAppResources();
            RunSta(() =>
            {
                var real = new Case(
                    "REAL_AI_MODEL_SELECTOR",
                    "+--------------------------------------------------------------+\n" +
                    "|                         AI MODEL SELECTOR                     |\n" +
                    "+--------------------------------------------------------------+\n" +
                    "| [ KARTU 1 ]        [ KARTU 2 ]        [ KARTU 3 ]\n" +
                    "| Deskripsi kartu pertama yang sangat panjang dan berada di baris isi\n" +
                    "| yang biasanya membutuhkan lebih banyak kolom daripada border kartu\n" +
                    "| [ PILIH ]           [ PILIH ]            [ PILIH ]\n" +
                    "+--------------------------------------------------------------+");
                var control = new Case(
                    "CONTROL_SMALL_BOX",
                    "+----------+\n" +
                    "|  NEXUS   |\n" +
                    "+----------+");

                CollectParser(report, real);
                CollectParser(report, control);

                foreach (var width in new[] { 1400d, 860d, 700d, 560d })
                {
                    CollectLayout(report, real, width);
                }

                CollectLayout(report, control, 700d);
            });
        }
        catch (Exception ex)
        {
            fatal = ex;
        }
        finally
        {
            if (fatal != null)
            {
                report.AppendLine("FATAL: " + Fault(fatal));
            }

            var dir = Path.Combine(Path.GetTempPath(), "opencode");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "ascii-probe-report.txt"),
                report.ToString());
        }

        Assert.True(
            fatal == null,
            "Probe ran to completion. See ascii-probe-report.txt for the evidence. Fatal: " +
            (fatal == null ? "none" : fatal.GetType().Name));
    }

    private sealed record Case(string Name, string Input);

    private static void CollectParser(StringBuilder sb, Case testCase)
    {
        sb.AppendLine();
        sb.AppendLine($"############ CASE PARSER: {testCase.Name} ############");
        var document = Parse(testCase.Input, includeCodeHeuristic: true);
        var blocks = Blocks(document);
        sb.AppendLine($"block count = {blocks.Count}");
        foreach (var block in blocks)
        {
            sb.AppendLine($"  block type = {block.GetType().Name}");
            if (block.GetType().Name != "CodeBlock")
            {
                continue;
            }

            var content = Property(block, "Content");
            var language = Property(block, "Language");
            var lines = content.Split('\n');
            sb.AppendLine($"    language = '{language}'");
            sb.AppendLine($"    logical line count = {lines.Length}");
            sb.AppendLine($"    content === normalized input : {content == testCase.Input}");
            sb.AppendLine($"    content === raw input        : {content == testCase.Input}");
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var first = line.Length >= 3 ? line.Substring(0, 3) : line;
                var last = line.Length >= 3 ? line.Substring(line.Length - 3) : line;
                sb.AppendLine($"    line[{i,2}] len={line.Length,3} first='{Escape(first)}' last='{Escape(last)}'");
            }

            sb.AppendLine($"    newline count in content = {content.Count(c => c == '\n')}");
        }
    }

    private static void CollectLayout(StringBuilder sb, Case testCase, double width)
    {
        sb.AppendLine();
        sb.AppendLine($"=========== LAYOUT @ Measure width {width} — CASE {testCase.Name} ===========");
        var document = Parse(testCase.Input, includeCodeHeuristic: true);
        var presenter = CreatePresenter(document);
        presenter.Measure(new Size(width, double.PositiveInfinity));
        presenter.Arrange(new Rect(
            0, 0, presenter.DesiredSize.Width, Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();

        var box = Descendants(presenter)
            .OfType<RichTextBox>()
            .FirstOrDefault();
        if (box == null)
        {
            sb.AppendLine("  !!! no RichTextBox found inside presenter visual tree");
            return;
        }

        var flow = box.Document;
        var para = CodeParagraphOf(flow);
        sb.AppendLine("--- width/constraint chain ---");
        sb.AppendLine($"  presenter  Width/Actual = {presenter.Width}/{presenter.ActualWidth:0.###}, Desired={presenter.DesiredSize.Width:0.###}");
        sb.AppendLine($"  RichTextBox Width/Actual = {box.Width}/{box.ActualWidth:0.###}");
        sb.AppendLine($"  RichTextBox Viewport = {box.ViewportWidth:0.###} x {box.ViewportHeight:0.###}");
        sb.AppendLine($"  FlowDocument.ColumnWidth = {DocNum(flow.ColumnWidth)}");
        sb.AppendLine($"  FlowDocument.PageWidth   = {DocNum(flow.PageWidth)}");
        sb.AppendLine($"  FlowDocument.PagePadding = {flow.PagePadding}");
        sb.AppendLine($"  RichTextBox.Padding      = {box.Padding}");
        sb.AppendLine($"  RichTextBox HWrap/Trim    = HScrollbar={box.HorizontalScrollBarVisibility}, VScroll={box.VerticalScrollBarVisibility}");

        if (para == null)
        {
            sb.AppendLine("  !!! code Paragraph NOT found");
            return;
        }
        var weird = false;
        if (para is null) weird = true;
        var codeParagraph = para ?? new Paragraph();
        _ = codeParagraph;
        sb.AppendLine("  --- code Paragraph properties ---");
        sb.AppendLine($"  LineHeight            = {codeParagraph.LineHeight} (NaN={double.IsNaN(codeParagraph.LineHeight)})");
        sb.AppendLine($"  LineStackingStrategy  = {codeParagraph.LineStackingStrategy}");
        sb.AppendLine($"  TextAlignment         = {codeParagraph.TextAlignment}");
        foreach (var run in codeParagraph.Inlines.OfType<Run>())
        {
            sb.AppendLine("  --- Run properties ---");
            sb.AppendLine($"    Text.Length       = {run.Text.Length}");
            sb.AppendLine($"    FontFamily.Source = '{run.FontFamily.Source}'");
            sb.AppendLine($"    FontSize          = {run.FontSize}");
            sb.AppendLine($"    FontWeight        = {run.FontWeight}");
            sb.AppendLine($"    FontStyle         = {run.FontStyle}");
            var typeface = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(presenter).PixelsPerDip;
            foreach (var ch in new[] { "|", "+", "-", "A", " " })
            {
                var ft = new FormattedText(
                    ch, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface,
                    run.FontSize, Brushes.Black, dpi);
                sb.AppendLine($"    glyph '{ch}' width = {ft.WidthIncludingTrailingWhitespace:0.###}px");
            }
        }

        var content = string.Concat(
            codeParagraph.Inlines.OfType<Run>().Select(r => r.Text));
        var logicalLines = content.Split('\n').Length;
        var visualLines = CountVisualLines(codeParagraph);
        sb.AppendLine("  --- wrap detection ---");
        sb.AppendLine($"  logical lines = {logicalLines}");
        sb.AppendLine($"  visual  lines = {visualLines}");
        sb.AppendLine($"  WRAP? visual > logical : {visualLines > logicalLines}");

        sb.AppendLine("  --- per logical-line width vs viewport (FormattedText, monospace font) ---");
        var dpi2 = VisualTreeHelper.GetDpi(presenter).PixelsPerDip;
        var typeface2 = new Typeface(
            codeParagraph.FontFamily,
            codeParagraph.FontStyle,
            codeParagraph.FontWeight,
            codeParagraph.FontStretch);
        var logicalRows = content.Split('\n');
        for (var i = 0; i < logicalRows.Length; i++)
        {
            var row = logicalRows[i];
            var ft = new FormattedText(
                row.Length == 0 ? " " : row,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface2,
                codeParagraph.FontSize,
                Brushes.Black,
                dpi2);
            var w = ft.WidthIncludingTrailingWhitespace;
            var clip = w > box.ViewportWidth ? "  <== EXCEEDS viewport (candidate wrapping/clipping)" : string.Empty;
            sb.AppendLine($"    logical[{i}] width={w:0.###} vs viewport {box.ViewportWidth:0.###}{clip}");
        }

        if (para == null && !weird) { } // keep compiler quiet
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

    // ---------- shared infra (mirrors ZzDiagnosticTests / AIWorkspaceRenderingTests) ----------

    private static object Parse(string source, bool includeCodeHeuristic)
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
        return parse!.Invoke(parser!, new object[] { source, includeCodeHeuristic })!;
    }

    private static IReadOnlyList<object> Blocks(object document)
    {
        var value = Get(document, "Blocks");
        var list = new List<object>();
        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static string Property(object target, string name) =>
        Get(target, name)?.ToString() ?? string.Empty;

    private static object? Get(object target, string name)
    {
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(target);
    }

    private static FrameworkElement CreatePresenter(object document)
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
        return presenter;
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

    private static string Escape(string value) =>
        value.Length == 0
            ? "<empty>"
            : value.Replace(" ", "•").Replace("\t", "\\t").Replace("\n", "\\n");

    private static string Fault(Exception ex)
    {
        var builder = new StringBuilder();
        for (var current = ex; current != null; current = current.InnerException)
        {
            builder.AppendLine(current.GetType().FullName + ": " + current.Message);
        }

        return builder.ToString();
    }

    private static string DocNum(double value) =>
        double.IsPositiveInfinity(value) ? "Inf" :
        double.IsNaN(value) ? "NaN" : value.ToString("0.###");

    // suppress unused warning
    private static string DocNum(object v) => v?.ToString() ?? "null";

    private static void EnsureAppResources()
    {
        WpfAppHost.Run(() => { });
    }

    private static void RunSta(Action action)
    {
        WpfAppHost.Run(action);
    }
}