using System;
using System.Collections.Generic;
using System.Text;

namespace Nexus.AI.EditingLite.Services;

public interface IDiffGeneratorService
{
    string CreateDiff(string orig, string proposed, int contextLines = 3);
}

public class DiffGeneratorService : IDiffGeneratorService
{
    public string CreateDiff(string orig, string proposed, int contextLines = 3)
    {
        var origLines = orig.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var proposedLines = proposed.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        int start = 0;
        while (start < origLines.Length && start < proposedLines.Length && origLines[start] == proposedLines[start])
        {
            start++;
        }

        if (start == origLines.Length && start == proposedLines.Length)
            return string.Empty;

        int endOrig = origLines.Length - 1;
        int endProposed = proposedLines.Length - 1;

        while (endOrig >= start && endProposed >= start && origLines[endOrig] == proposedLines[endProposed])
        {
            endOrig--;
            endProposed--;
        }

        var sb = new StringBuilder();
        int contextStart = Math.Max(0, start - contextLines);
        int contextEndOrig = Math.Min(origLines.Length - 1, endOrig + contextLines);
        int contextEndProposed = Math.Min(proposedLines.Length - 1, endProposed + contextLines);

        for (int i = contextStart; i < start; i++)
        {
            sb.AppendLine("  " + origLines[i]);
        }

        for (int i = start; i <= endOrig; i++)
        {
            sb.AppendLine("- " + origLines[i]);
        }

        for (int i = start; i <= endProposed; i++)
        {
            sb.AppendLine("+ " + proposedLines[i]);
        }

        for (int i = endOrig + 1; i <= contextEndOrig; i++)
        {
            sb.AppendLine("  " + origLines[i]);
        }

        return sb.ToString().TrimEnd();
    }
}
