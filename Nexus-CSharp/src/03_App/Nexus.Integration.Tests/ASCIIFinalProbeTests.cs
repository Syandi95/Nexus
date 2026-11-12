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

// ============================================================================
// FINAL PROBE (Langkah 2).  Observational only - NO behaviour assertions and
// NO production changes.
//
// Goal: find the FIRST FAILURE POINT *after* the proven-correct FlowDocument
// geometry.  It follows the REAL UI path:
//
//   RAW Content
//     -> NexusMessageParser.Parse                  -> CodeBlock.Content
//     -> MessageContentPresenter(Document, IsUser) -> FlowDocument
//     -> Run.Text
//     -> TextRange extraction (the only post-document extraction helper in
//        the app, used by presenter MeasureText when SizeToContent=IsUser)
//     -> GetCharacterRect display geometry ('|' '+' '-' X)
//
// Every boundary records: text, line count, per-line length, whitespace,
// X of '|' and '+', font + size, and the presenter measure result.  The first
// boundary whose geometry diverges is the FAILURE POINT.
// ============================================================================
[Collection(WpfApplicationCollection.Name)]
public class ASCIIFinalProbeTests
{
    private static string OutputPath =>
        Path.Combine(Path.GetTempPath(), "opencode", "ascii-final-probe.txt");

    [Fact]
    public void FinalProbe_WalkRealExtractionPath_CollectsBoundaryEvidence()
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

                report.AppendLine("=====================================================");
                report.AppendLine("FINAL PROBE: EXTRACTION / DISPLAY PATH (production path)");
                report.AppendLine("=====================================================");
                Step_01_Raw(report, real);
                Step_02_Parser(report, real);
                foreach (var isUser in new[] { false, true })
                {
                    report.AppendLine();
                    report.AppendLine($"@@@@@@@@ PRESENTER  IsUser = {isUser} (SizeToContent) @@@@@@@@");
                    Step_03_Presenter(report, real, isUser);
                    Step_04_TextRangeExtraction(report, real, isUser);
                    Step_05_CharacterDisplayRects(report, real, isUser);
                }
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

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, report.ToString());
        }

        Assert.True(
            fatal == null,
            "Probe ran to completion. See ascii-final-probe.txt. Fatal: " +
            (fatal == null ? "none" : fatal.GetType().Name));
    }

    private sealed record Case(string Name, string Input);

    // ---------- STEP 1: RAW ----------
    private static void Step_01_Raw(StringBuilder sb, Case testCase)
    {
        sb.AppendLine();
        sb.AppendLine("---------- STEP 1: RAW INPUT ----------");
        AppendLines(sb, "raw", testCase.Input);
    }

    // ---------- STEP 2: PARSER ----------
    private static void Step_02_Parser(StringBuilder sb, Case testCase)
    {
        sb.AppendLine();
        sb.AppendLine("---------- STEP 2: PARSER -> CodeBlock ----------");
        var document = Parse(testCase.Input, includeCodeHeuristic: true);
        var blocks = Blocks(document);
        sb.AppendLine($"block count = {blocks.Count}");
        var code = blocks.FirstOrDefault(b => b.GetType().Name == "CodeBlock");
        if (code == null)
        {
            sb.AppendLine("CodeBlock NOT FOUND -> parser did not classify as code.");
            return;
        }

        var content = Property(code, "Content");
        sb.AppendLine($"language = '{Property(code, "Language")}'");
        sb.AppendLine($"content == raw : {content == testCase.Input}");
        AppendLines(sb, "codeblock", content);
    }

    // ---------- STEP 3: PRESENTER / FLOWDOC / RUN ----------
    private static void Step_03_Presenter(StringBuilder sb, Case testCase, bool isUser)
    {
        sb.AppendLine();
        sb.AppendLine("---------- STEP 3: PRESENTER -> FlowDocument -> Run ----------");
        var document = Parse(testCase.Input, true);
        var presenter = CreatePresenter(document, isUser);
        var constraint = new Size(isUser ? 460 : 900, double.PositiveInfinity);
        presenter.Measure(constraint);
        presenter.Arrange(new Rect(
            0, 0, presenter.DesiredSize.Width, Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();

        var box = Descendants(presenter).OfType<RichTextBox>().FirstOrDefault();
        if (box == null)
        {
            sb.AppendLine("  !!! no RichTextBox found");
            return;
        }

        var flow = box.Document;
        sb.AppendLine($"presenter Desired/Actual/Render = {presenter.DesiredSize.Width:0.###} / {presenter.ActualWidth:0.###} / {presenter.RenderSize.Width:0.###}");
        sb.AppendLine($"RichTextBox Desired/Actual/Render = {box.DesiredSize.Width:0.###} / {box.ActualWidth:0.###} / {box.RenderSize.Width:0.###}");
        sb.AppendLine($"RichTextBox Viewport          = {box.ViewportWidth:0.###} x {box.ViewportHeight:0.###}");
        sb.AppendLine($"RichTextBox Extent            = {box.ExtentWidth:0.###} x {box.ExtentHeight:0.###}   (Extent>Viewport => horizontal clip candidate)");
        sb.AppendLine($"FlowDocument ColumnWidth      = {DocNum(flow.ColumnWidth)}");
        sb.AppendLine($"FlowDocument PagePadding      = {flow.PagePadding}");

        var para = CodeParagraphOf(flow);
        if (para == null)
        {
            sb.AppendLine("  !!! code Paragraph not found");
            return;
        }

        var runs = para.Inlines.OfType<Run>().ToList();
        sb.AppendLine($"code Paragraph runs = {runs.Count}");
        foreach (var run in runs)
        {
            sb.AppendLine($"  Run: '{run.FontFamily.Source}' @ {run.FontSize} / {run.FontWeight} / {run.FontStyle}");
        }

        var combined = string.Concat(runs.Select(r => r.Text));
        sb.AppendLine($"Run.Text == raw : {combined == testCase.Input}");
        AppendLines(sb, "run.text", combined);
    }

    // ---------- STEP 4: EXTRACTION (TextRange) ----------
    private static void Step_04_TextRangeExtraction(StringBuilder sb, Case testCase, bool isUser)
    {
        sb.AppendLine();
        sb.AppendLine("---------- STEP 4: EXTRACTION via TextRange (only post-doc extraction helper in the app) ----------");
        var document = Parse(testCase.Input, true);
        var presenter = CreatePresenter(document, isUser);
        presenter.Measure(new Size(isUser ? 460 : 900, double.PositiveInfinity));
        presenter.Arrange(new Rect(
            0, 0, presenter.DesiredSize.Width, Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();

        var box = Descendants(presenter).OfType<RichTextBox>().FirstOrDefault();
        if (box == null)
        {
            sb.AppendLine("  !!! RichTextBox not found");
            return;
        }

        var extracted = new TextRange(
            box.Document.ContentStart,
            box.Document.ContentEnd).Text;
        sb.AppendLine($"raw == extracted   : {testCase.Input == extracted}");
        sb.AppendLine($"extracted (visible)= [{DebugDump(extracted)}]");

        var trimmed = extracted.TrimEnd();
        var pixelsPerDip = VisualTreeHelper.GetDpi(presenter).PixelsPerDip;
        var uiFont = box.FontFamily;
        var ft = new FormattedText(
            trimmed.Length == 0 ? " " : trimmed,
            CultureInfo.CurrentUICulture,
            presenter.FlowDirection,
            new Typeface(uiFont, box.FontStyle, box.FontWeight, box.FontStretch),
            box.FontSize,
            box.Foreground,
            pixelsPerDip);
        var uiMeasure = Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 24;
        sb.AppendLine($"measureWidth via UI font (as presenter SizeToContent uses) = {uiMeasure:0.###}px  font={uiFont.Source} @ {box.FontSize}");

        var codeFontMeasure = MeasureWidth(trimmed, box.FontSize, "Consolas");
        sb.AppendLine($"measureWidth via Consolas            = {codeFontMeasure:0.###}px");
        sb.AppendLine($"UI measure vs Consolas measure diff  = {uiMeasure - codeFontMeasure:+0.###;-0.###;0}px");

        sb.AppendLine("  per-line extraction vs raw :");
        var rawLines = testCase.Input.Split('\n');
        var extLines = extracted.TrimEnd('\n').Split('\n');
        for (var i = 0; i < Math.Max(rawLines.Length, extLines.Length); i++)
        {
            var r = i < rawLines.Length ? rawLines[i] : "<none>";
            var e = i < extLines.Length ? extLines[i] : "<none>";
            sb.AppendLine($"    {(r == e ? "SAME " : "DIFF ")}line[{i,2}] rawLen={r.Length,3} extLen={e.Length,3}");
            if (r != e)
            {
                sb.AppendLine($"       raw='{Escape(r)}'");
                sb.AppendLine($"       ext='{Escape(e)}'");
            }
        }
    }

    // ---------- STEP 5: CHARACTER DISPLAY RECTS ----------
    private static void Step_05_CharacterDisplayRects(StringBuilder sb, Case testCase, bool isUser)
    {
        sb.AppendLine();
        sb.AppendLine("---------- STEP 5: CHARACTER DISPLAY RECTS (real FlowDocument geometry per line) ----------");
        var document = Parse(testCase.Input, true);
        var presenter = CreatePresenter(document, isUser);
        presenter.Measure(new Size(isUser ? 460 : 900, double.PositiveInfinity));
        presenter.Arrange(new Rect(
            0, 0, presenter.DesiredSize.Width, Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();

        var box = Descendants(presenter).OfType<RichTextBox>().FirstOrDefault();
        if (box == null)
        {
            sb.AppendLine("  !!! RichTextBox not found");
            return;
        }

        var para = CodeParagraphOf(box.Document);
        if (para == null)
        {
            sb.AppendLine("  !!! code paragraph not found");
            return;
        }

        var codeText = string.Concat(para.Inlines.OfType<Run>().Select(r => r.Text));
        var run = para.Inlines.OfType<Run>().First();
        var dpi = VisualTreeHelper.GetDpi(presenter).PixelsPerDip;
        var typeface = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);

        sb.AppendLine($"code Text area font: '{run.FontFamily.Source}' @ {run.FontSize}, LineHeight={DocNum(para.LineHeight)}");
        sb.AppendLine($"codeText == raw : {codeText == testCase.Input}");

        var pipes = new List<(int Line, int Col, char Ch, double Left, char RawAtCol)>();
        CollectPipeRects(para, pipes);
        var rawLines = testCase.Input.Split('\n');

        var grouped = pipes
            .GroupBy(p => p.Line)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Col).ToList());

        for (var line = 0; line < rawLines.Length; line++)
        {
            if (!grouped.TryGetValue(line, out var linePipes))
            {
                sb.AppendLine($"  line[{line,2}] --no pipe rects collected--  rawLen={rawLines[line].Length}");
                continue;
            }

sb.AppendLine($"  line[{line,2}] rawLen={rawLines[line].Length,3} => {linePipes.Count} pipe/box glyph chars:");
            foreach (var p in linePipes)
            {
                sb.AppendLine($"      col={p.Col,3} '{p.Ch}' X={p.Left:0.###}px  rawCharAtCol='{p.RawAtCol}'  {(p.Ch == p.RawAtCol ? "" : "  <<< RAW/FLOW MISMATCH")}");
            }
            }

        sb.AppendLine();
        sb.AppendLine("  Line start X (first char rect) per line, for left-column alignment check:");
        var lineStarts = new List<(int line, double left)>();
        var pos = para.ContentStart;
        var currentLine = 0;
        var col = 0;
        var firstOfLine = 0d;
        var firstStarted = false;
        while (pos != null && pos.CompareTo(para.ContentEnd) < 0)
        {
            var ch = InternCharAt(pos);
            var rect = pos.GetCharacterRect(LogicalDirection.Forward);
            if (!firstStarted)
            {
                firstOfLine = rect.Left;
                firstStarted = true;
            }

            if (ch == '\n')
            {
                lineStarts.Add((currentLine, firstOfLine));
                currentLine++;
                col = 0;
                firstStarted = false;
            }
            else
            {
                col++;
            }

            pos = NextPos(pos);
        }

        lineStarts.Add((currentLine, firstOfLine));
        foreach (var (l, left) in lineStarts)
        {
            sb.AppendLine($"    line[{l}] first glyph X = {left:0.###}px");
        }
    }

    private static void CollectPipeRects(
        Paragraph para,
        List<(int Line, int Col, char Ch, double Left, char RawAtCol)> sink)
    {
        var pos = para.ContentStart;
        var line = 0;
        var col = 0;
        var raw = string.Concat(para.Inlines.OfType<Run>().Select(r => r.Text));
        var rawLines = raw.Split('\n');
        while (pos != null && pos.CompareTo(para.ContentEnd) < 0)
        {
            var ch = InternCharAt(pos);
            if (ch is '|' or '+' or '-')
            {
                var rect = pos.GetCharacterRect(LogicalDirection.Forward);
                var rawChar = col < rawLines[Math.Min(line, rawLines.Length - 1)].Length
                    ? rawLines[Math.Min(line, rawLines.Length - 1)][col]
                    : '?';
                sink.Add((line, col, ch, rect.Left, rawChar));
            }

            if (ch == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }

            pos = NextPos(pos);
        }
    }

    private static char InternCharAt(TextPointer pos)
    {
        var next = pos.GetNextInsertionPosition(LogicalDirection.Forward);
        if (next == null)
        {
            return '\0';
        }

        var text = new TextRange(pos, next).Text;
        return text.Length > 0 ? text[0] : '\0';
    }

    private static TextPointer NextPos(TextPointer pos) =>
        pos.GetNextInsertionPosition(LogicalDirection.Forward) ?? pos;

    // ---------- generic helpers ----------
    private static void AppendLines(StringBuilder sb, string label, string text)
    {
        var lines = text.Split('\n');
        sb.AppendLine($"  [{label}] total line: {lines.Length}");
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var pipeCount = line.Count(c => c is '|' or '+' or '-');
            sb.AppendLine($"    [{i,2}] len={line.Length,3} pipes={pipeCount} '{Escape(line)}'");
        }
    }

    private static double MeasureWidth(string text, double fontSize, string family)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(family),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            Brushes.Black,
            1.0);
        return ft.WidthIncludingTrailingWhitespace;
    }

    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return "<empty>";
        }

        return value.Replace(" ", "•").Replace("\t", "\\t").Replace("\n", "\\n");
    }

    private static string DebugDump(string value) =>
        value.Length == 0 ? "<empty>" : value.Replace("\r", "␍").Replace("\n", "␊");

    private static string DocNum(double value) =>
        double.IsPositiveInfinity(value) ? "Inf" :
        double.IsNaN(value) ? "NaN" : value.ToString("0.###");

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
        var list = new List<object>();
        if (Get(document, "Blocks") is IEnumerable items)
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

    private static FrameworkElement CreatePresenter(object document, bool isUser)
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
        presenterType!.GetProperty("SizeToContent")!.SetValue(presenter, isUser);
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

    private static string Fault(Exception ex)
    {
        var builder = new StringBuilder();
        for (var current = ex; current != null; current = current.InnerException)
        {
            builder.AppendLine(current.GetType().FullName + ": " + current.Message);
        }

        return builder.ToString();
    }

    private static void RunSta(Action action)
    {
        WpfAppHost.Run(action);
    }
}