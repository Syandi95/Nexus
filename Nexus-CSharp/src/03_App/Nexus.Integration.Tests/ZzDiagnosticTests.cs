using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexus.App.Services;
using Xunit;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class ZzDiagnosticTests
{

    [Fact]
    public void CodeBlockLayout_IsTight()
    {
        RunSta(() =>
        {
            EnsureAppResources();

            double plain1 = MeasureExtent("Teks satu baris.");
            double code0 = MeasureExtent("```\n```");
            double code1 = MeasureExtent("```\nSatuBaris\n```");
            double code2 = MeasureExtent("```\nWrite-Output 'Nexus'\nVar = 1\n```");
            double code4 = MeasureExtent("```\nA\nB\nC\nD\n```");
            double perLine = (code4 - code2) / 2;

            Console.WriteLine(
                $"plain1={plain1:0.###} code0={code0:0.###} code1={code1:0.###} " +
                $"code2={code2:0.###} code4={code4:0.###} perLine={perLine:0.###}");

            Assert.True(Math.Abs(plain1 - 19.5) < 0.5, $"plain1={plain1:0.###}");
            Assert.True(code1 < 85, $"1-line code block too tall: {code1:0.###}");
            Assert.True(code0 < 85, $"empty code block too tall: {code0:0.###}");
            Assert.True(perLine is > 11 and < 20, $"per code line height off: {perLine:0.###}");

            var table = RichTextBoxOf("```\nSatuBaris\n```")
                .Document.Blocks.OfType<Table>().Single();

            var columns = table.Columns.Select(c => c.Width.GridUnitType).ToList();
            Assert.All(columns, unit => Assert.Equal(GridUnitType.Auto, unit));

            var header = table.RowGroups[0].Rows[0];
            var language = header.Cells[0].Blocks.OfType<Paragraph>().Single();
            var languageRun = language.Inlines.OfType<Run>().First();
            var top = languageRun.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
            var bottom = languageRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom;
            var headerBand = bottom - top;
            Console.WriteLine($"header glyph band = {headerBand:0.###}");
            Assert.True(headerBand < 25, $"header text wrapped into lines: band={headerBand:0.###}");

            var copyParagraph = header.Cells[1].Blocks.OfType<Paragraph>().Single();
            var copyLink = copyParagraph.Inlines.OfType<Hyperlink>().Single();
            var copyGlyph = copyLink.Inlines.OfType<Run>().First();
            Assert.Equal(NexusIcon.GetIconGlyph("Copy"), copyGlyph.Text);
        });
    }

    private static RichTextBox RichTextBoxOf(string source) =>
        FindRichTextBox(CreatePresenter(Parse(source)));

    private static double MeasureExtent(string source)
    {
        var rich = RichTextBoxOf(source);
        return rich.ExtentHeight;
    }

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
        presenter.Measure(new Size(760, double.PositiveInfinity));
        presenter.Arrange(new Rect(
            0, 0, presenter.DesiredSize.Width, Math.Max(1, presenter.DesiredSize.Height)));
        presenter.UpdateLayout();
        return presenter;
    }

    private static RichTextBox FindRichTextBox(FrameworkElement root)
    {
        return Descendants(root).OfType<RichTextBox>().First();
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