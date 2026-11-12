using System.Collections.Immutable;
using System.Text;

namespace Nexus.AI.EditingLite.Services;

public sealed record SourceTextEdit(string OldText,string NewText);
public sealed record ResolvedSourceTextEdit(int Ordinal,int Start,int Length,string OldText,string NewText);
public sealed record ResolvedSourceEdits(ImmutableArray<ResolvedSourceTextEdit> Edits,string DesiredContent);
public sealed class SourceEditValidationException(string reasonCode) : ArgumentException(reasonCode)
{
    public string ReasonCode { get; }=reasonCode;
}

// Original-coordinate changes (Roslyn TextChange semantics), not patch execution.
public static class ExactSourceEdits
{
    public const int MaxEditsPerFile=2;
    public const int MaxEditsPerPlan=3;
    private static readonly UTF8Encoding Utf8=new(false,true);
    public static int LiteralBytes(IEnumerable<SourceTextEdit> edits)=>edits.Sum(e=>Utf8.GetByteCount(e.OldText)+Utf8.GetByteCount(e.NewText));
    public static int ChangedLines(IEnumerable<ResolvedSourceTextEdit> edits)=>edits.Sum(e=>Lines(e.OldText)+Lines(e.NewText));
    private static int Lines(string value)=>value.Split(new[] { "\r\n","\r","\n" },StringSplitOptions.None).Length;
    public static bool ValidLiteral(SourceTextEdit? edit)
    {
        if(edit is null || string.IsNullOrEmpty(edit.OldText) || string.IsNullOrEmpty(edit.NewText) || edit.OldText==edit.NewText ||
            edit.OldText.Length>256 || edit.NewText.Length>256 || edit.OldText.Contains('\0') || edit.NewText.Contains('\0')) return false;
        try { _=Utf8.GetByteCount(edit.OldText);_=Utf8.GetByteCount(edit.NewText);return true; }
        catch(EncoderFallbackException) { return false; }
    }
    public static bool SameIntent(IEnumerable<SourceTextEdit> first,IEnumerable<SourceTextEdit> second)=>
        first.OrderBy(e=>e.OldText,StringComparer.Ordinal).ThenBy(e=>e.NewText,StringComparer.Ordinal)
            .SequenceEqual(second.OrderBy(e=>e.OldText,StringComparer.Ordinal).ThenBy(e=>e.NewText,StringComparer.Ordinal));
    public static ResolvedSourceEdits Resolve(string original,IEnumerable<SourceTextEdit> requested,int maximumLiteralBytes=1024)
    {
        if(Utf8.GetByteCount(original)>4096) throw new SourceEditValidationException("SourceSizeLimit");
        var edits=requested.ToImmutableArray();
        if(edits.Length is <1 or >MaxEditsPerFile) throw new SourceEditValidationException("EditCountLimit");
        if(edits.Any(e=>!ValidLiteral(e))) throw new SourceEditValidationException("InvalidLiteral");
        if(edits.Distinct().Count()!=edits.Length) throw new SourceEditValidationException("DuplicateEdit");
        if(LiteralBytes(edits)>maximumLiteralBytes) throw new SourceEditValidationException("LiteralByteLimit");
        var ranges=new List<ResolvedSourceTextEdit>();
        foreach(var edit in edits)
        {
            var start=original.IndexOf(edit.OldText,StringComparison.Ordinal);
            if(start<0) throw new SourceEditValidationException("NoMatch");
            // Advance by one UTF16 position, not literal length: overlapping starts count.
            if(original.IndexOf(edit.OldText,start+1,StringComparison.Ordinal)>=0) throw new SourceEditValidationException("MultipleMatches");
            var end=start+edit.OldText.Length;
            if(SplitsSurrogate(original,start) || SplitsSurrogate(original,end)) throw new SourceEditValidationException("EncodingBoundary");
            ranges.Add(new(0,start,edit.OldText.Length,edit.OldText,edit.NewText));
        }
        var ordered=ranges.OrderBy(e=>e.Start).ThenBy(e=>e.Length).ThenBy(e=>e.OldText,StringComparer.Ordinal).ThenBy(e=>e.NewText,StringComparer.Ordinal).ToArray();
        var output=new StringBuilder();var cursor=0;
        for(var i=0;i<ordered.Length;i++)
        {
            var edit=ordered[i];
            if(edit.Start<cursor) throw new SourceEditValidationException("OverlappingEdits");
            ordered[i]=edit with { Ordinal=i };
            output.Append(original,cursor,edit.Start-cursor);output.Append(edit.NewText);cursor=edit.Start+edit.Length;
        }
        output.Append(original,cursor,original.Length-cursor);
        var desired=output.ToString();
        if(desired==original) throw new SourceEditValidationException("NoChange");
        if(Utf8.GetByteCount(desired)>4096) throw new SourceEditValidationException("DesiredSizeLimit");
        return new(ordered.ToImmutableArray(),desired);
    }
    private static bool SplitsSurrogate(string text,int boundary)=>boundary>0 && boundary<text.Length && char.IsHighSurrogate(text[boundary-1]) && char.IsLowSurrogate(text[boundary]);
}
