using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Documents;
using ColorCode;
using ColorCode.Common;
using ColorCode.Compilation;
using ColorCode.Parsing;

namespace Nexus.App.AIWorkspace.Rendering;

/// <summary>
/// Provides syntax highlighting for code blocks using ColorCode.Core and Nexus semantic tokens.
/// </summary>
public static class NexusSyntaxColorizer
{
    private static readonly Dictionary<string, CompiledLanguage> CompiledLanguages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ReaderWriterLockSlim CompileLock = new();
    private static readonly LanguageCompiler Compiler = new(CompiledLanguages, CompileLock);
    private static readonly LanguageRepository Repo = CreateRepository();
    private static readonly LanguageParser Parser = new(Compiler, Repo);

    private static LanguageRepository CreateRepository()
    {
        var repository = new LanguageRepository(new Dictionary<string, ILanguage>(StringComparer.OrdinalIgnoreCase));
        foreach (var lang in Languages.All)
        {
            repository.Load(lang);
        }
        return repository;
    }

    public static void PopulateInlines(Paragraph paragraph, string? languageId, string codeContent)
    {
        paragraph.Inlines.Clear();
        if (string.IsNullOrEmpty(codeContent))
        {
            return;
        }

        ILanguage? language = ResolveLanguage(languageId);
        if (language == null)
        {
            var fallbackRun = new Run(codeContent);
            fallbackRun.SetResourceReference(TextElement.ForegroundProperty, "NexusSyntaxPlainTextBrush");
            paragraph.Inlines.Add(fallbackRun);
            return;
        }

        try
        {
            Parser.Parse(codeContent, language, (chunkText, scopes) =>
            {
                if (string.IsNullOrEmpty(chunkText))
                {
                    return;
                }

                var run = new Run(chunkText);
                var brushKey = ResolveBrushKey(scopes);
                run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
                paragraph.Inlines.Add(run);
            });
        }
        catch
        {
            paragraph.Inlines.Clear();
            var fallbackRun = new Run(codeContent);
            fallbackRun.SetResourceReference(TextElement.ForegroundProperty, "NexusSyntaxPlainTextBrush");
            paragraph.Inlines.Add(fallbackRun);
        }
    }

    private static ILanguage? ResolveLanguage(string? languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
        {
            return null;
        }

        var normalized = languageId.Trim().ToLowerInvariant();
        var language = Languages.FindById(normalized);
        if (language != null)
        {
            return language;
        }

        return normalized switch
        {
            "csharp" or "cs" => Languages.CSharp,
            "cpp" or "c++" => Languages.Cpp,
            "js" or "javascript" => Languages.JavaScript,
            "ts" or "typescript" => Languages.Typescript,
            "py" or "python" => Languages.Python,
            "ps" or "ps1" or "powershell" => Languages.PowerShell,
            "html" or "htm" => Languages.Html,
            "xml" or "xaml" => Languages.Xml,
            "css" => Languages.Css,
            "sql" => Languages.Sql,
            "md" or "markdown" => Languages.Markdown,
            "json" => Languages.FindById("json"),
            _ => null
        };
    }

    private static string ResolveBrushKey(IList<Scope> scopes)
    {
        if (scopes == null || scopes.Count == 0)
        {
            return "NexusSyntaxPlainTextBrush";
        }

        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            var name = scopes[i].Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (name.Contains("Keyword", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Const", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("PowerShellCommand", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SqlSystemFunction", StringComparison.OrdinalIgnoreCase))
            {
                return "NexusSyntaxKeywordBrush";
            }

            if (name.Contains("String", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("AttributeValue", StringComparison.OrdinalIgnoreCase))
            {
                return "NexusSyntaxStringBrush";
            }

            if (name.Contains("Comment", StringComparison.OrdinalIgnoreCase))
            {
                return "NexusSyntaxCommentBrush";
            }

            if (name.Contains("Number", StringComparison.OrdinalIgnoreCase))
            {
                return "NexusSyntaxNumberBrush";
            }

            if (name.Contains("Type", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ClassName", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Constructor", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("NameSpace", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("PowerShellType", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("PowerShellVariable", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("HtmlElementName", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("XmlName", StringComparison.OrdinalIgnoreCase))
            {
                return "NexusSyntaxTypeBrush";
            }
        }

        return "NexusSyntaxPlainTextBrush";
    }
}
