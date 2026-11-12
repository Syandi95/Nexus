using System;
using System.IO;
using System.Management.Automation.Language;
using System.Text.Json;
using System.Xml;
using Nexus.AI.EditingLite.Exceptions;

namespace Nexus.AI.EditingLite.Services;

public interface ISyntaxValidatorService
{
    void Validate(string path, string content);
}

public class SyntaxValidatorService : ISyntaxValidatorService
{
    public void Validate(string path, string content)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            switch (ext)
            {
                case ".ps1":
                case ".psm1":
                case ".psd1":
                    ValidatePowerShell(content);
                    break;
                case ".xml":
                case ".xaml":
                    ValidateXml(content);
                    break;
                case ".json":
                    ValidateJson(content);
                    break;
                default:
                    // Unsupported extension, just skip validation
                    break;
            }
        }
        catch (Exception ex) when (ex is not EditingException)
        {
            throw new EditingException(EditingFailureCode.SyntaxValidationFailed, $"Syntax validation failed for {ext} file.", ex);
        }
    }

    private void ValidatePowerShell(string content)
    {
        var ast = Parser.ParseInput(content, out var tokens, out var errors);
        if (errors != null && errors.Length > 0)
        {
            throw new EditingException(EditingFailureCode.SyntaxValidationFailed, $"PowerShell syntax errors detected. First error: {errors[0].Message}");
        }
    }

    private void ValidateXml(string content)
    {
        var doc = new XmlDocument();
        doc.LoadXml(content);
    }

    private void ValidateJson(string content)
    {
        using var doc = JsonDocument.Parse(content);
    }
}
