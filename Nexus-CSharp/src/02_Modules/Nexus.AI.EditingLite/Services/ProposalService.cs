using System;
using Nexus.AI.EditingLite.Exceptions;

namespace Nexus.AI.EditingLite.Services;

public interface IProposalService
{
    int CountLiteralMatches(string content, string value);
    string CreateProposal(string content, string oldText, string newText);
}

public class ProposalService : IProposalService
{
    public int CountLiteralMatches(string content, string value)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(content))
            return 0;

        int count = 0;
        int i = 0;
        while ((i = content.IndexOf(value, i, StringComparison.Ordinal)) != -1)
        {
            i++;
            count++;
        }
        return count;
    }

    public string CreateProposal(string content, string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText))
        {
            throw new EditingException(EditingFailureCode.OldTextRequired, "Old text is required.");
        }

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            throw new EditingException(EditingFailureCode.NoChange, "New text is identical to old text.");
        }

        int matches = CountLiteralMatches(content, oldText);

        if (matches == 0)
        {
            throw new EditingException(EditingFailureCode.OldTextNotFound, "Old text was not found in the file.");
        }
        
        if (matches > 1)
        {
            throw new EditingException(EditingFailureCode.OldTextNotUnique, $"Old text matches {matches} times. Please provide more context to make the match unique.");
        }

        return content.Replace(oldText, newText ?? string.Empty, StringComparison.Ordinal);
    }
}
