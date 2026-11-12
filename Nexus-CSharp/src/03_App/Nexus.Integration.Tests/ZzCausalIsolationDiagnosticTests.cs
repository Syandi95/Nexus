using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using Xunit;
using Xunit.Sdk;

namespace Nexus.Integration.Tests;

public class ZzCausalIsolationDiagnosticTests
{
    // Preserve the original diagnostic inputs as regressions under the real WPF
    // resource host. The old probe intentionally lacked Application resources
    // and expected a crash; success must no longer depend on test execution order.
    [Fact]
    public void Renderer_EmptyDocument_DoesNotReintroduceMetricNullReference()
    {
        var document = Parse("", true);
        var blocks = EnumerableProperty(document, "Blocks");
        Assert.Empty(blocks);

        var presenterType = Type.GetType(
            "Nexus.App.AIWorkspace.Rendering.MessageContentPresenter, Nexus.App",
            throwOnError: false)!;
        var documentProperty = presenterType.GetProperty(
            "Document",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        RunSta(() =>
        {
            var presenter = Activator.CreateInstance(presenterType, nonPublic: true)!;
            var ex = Record.Exception(
                () => documentProperty.SetValue(presenter, document));
            Assert.Null(Unwrap(ex));
            Assert.Same(document, documentProperty.GetValue(presenter));
        });
    }

    private static Exception? Unwrap(Exception? ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    [Fact]
    public void Renderer_SevenOriginalRegressionInputs_DoNotReintroduceMetricNullReference()
    {
        var inputs = new[]
        {
            ("UserAndAssistantMessages",
                "Teks **tebal** dan `inline code`."),
            ("CodeBlockFenced",
                "```powershell\nWrite-Output 'Nexus'\n```"),
            ("CodeBlockCopy",
                "Sebelum blok\n\n```powershell\nWrite-Output 'Nexus'\n```\n\nSebelum blok (copy langkah sendiri)."),
            ("MessageSelection",
                "Awal pesan.\n\n- Item daftar\n\n> Kutipan\n\n```powershell\nWrite-Output 'Nexus'\n```\n\n" +
                "| Fitur | Status |\n| --- | --- |\n| Table | Aktif |\n\nAkhir pesan."),
            ("TableMarkdown",
                "| Fitur | Status |\n| --- | --- |\n| Table | Aktif |"),
            ("ExistingColors",
                "Paragraph **bold** *italic* `code`.\n\n- List\n\n> Quote\n\n```powershell\nWrite-Output 'Nexus'\n```\n\n" +
                "| Fitur | Status |\n| --- | --- |\n| Table | Aktif |"),
            ("UserBubbleShort",
                "Halo")
        };

        var presenterType = Type.GetType(
            "Nexus.App.AIWorkspace.Rendering.MessageContentPresenter, Nexus.App",
            throwOnError: false)!;
        var documentProperty = presenterType.GetProperty(
            "Document",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        foreach (var (name, source) in inputs)
        {
            var document = Parse(source, true);
            var blocks = EnumerableProperty(document, "Blocks");
            var blockTypes = string.Join(",", blocks.Select(b => b.GetType().Name));

            RunSta(() =>
            {
                var presenter = Activator.CreateInstance(presenterType, nonPublic: true)!;
                var ex = Record.Exception(
                    () => documentProperty.SetValue(presenter, document));
                Assert.Null(Unwrap(ex));
                Assert.Same(document, documentProperty.GetValue(presenter));
            });

            var marker = blockTypes.Contains("CodeBlock") ? "[CodeBlock built]" : "[no CodeBlock]";
            Console.WriteLine($"REGRESSION {name}: blocks=({blockTypes}) {marker} -> metric/render assignment succeeds");
        }
    }

    private static object Parse(string source, bool includeCodeHeuristic)
    {
        var parserType = Type.GetType(
            "Nexus.App.AIWorkspace.Markdown.NexusMessageParser, Nexus.App",
            throwOnError: false)!;
        var parser = Activator.CreateInstance(parserType, nonPublic: true)!;
        var parse = parserType.GetMethod(
            "Parse",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null)!;
        return parse.Invoke(parser, new object[] { source, includeCodeHeuristic })!;
    }

    private static System.Collections.Generic.List<object> EnumerableProperty(
        object instance,
        string propertyName)
    {
        var value = instance.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as IEnumerable;
        return value?.Cast<object>().ToList() ?? new System.Collections.Generic.List<object>();
    }

    private static void RunSta(Action action)
    {
        WpfAppHost.Run(action);
    }
}
