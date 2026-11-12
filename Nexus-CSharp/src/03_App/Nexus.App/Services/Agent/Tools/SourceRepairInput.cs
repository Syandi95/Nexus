using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.AI.EditingLite.Services;

namespace Nexus.App.Services.Agent.Tools;

internal sealed record AdvisoryRepairExclusion(string Slot,int ItemCount,int WireBytes,string Digest,string Disposition="ExcludedFromAuthority");

// Only failed-file edits survive validation. Echo collections and raw JSON do not.
internal sealed class ValidatedSourceRepairInput
{
    internal Guid RunId { get; }
    internal Guid APlanId { get; }
    internal string APlanDigest { get; }
    internal string BaselineDigest { get; }
    internal string FailedTarget { get; }
    internal int FailedOrdinal { get; }
    internal ImmutableArray<SourceTextEdit> FailedEdits { get; }
    internal ImmutableArray<AdvisoryRepairExclusion> AdvisoryExclusions { get; }
    internal ValidatedSourceRepairInput(SourceAttemptFact first,int failed,ImmutableArray<SourceTextEdit> edits,ImmutableArray<AdvisoryRepairExclusion> excluded)
    {
        RunId=first.RunId;APlanId=first.PlanId;APlanDigest=first.PlanDigest;BaselineDigest=first.BaselineDigest;
        FailedTarget=first.Plan.Entries[failed].Target;FailedOrdinal=failed;FailedEdits=edits;AdvisoryExclusions=excluded;
    }
}

internal static class SourceRepairInputValidator
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static bool TryValidate(string raw,SourceAttemptFact first,int failed,out ValidatedSourceRepairInput? input,out string reason)
    {
        input=null;reason="InvalidRepairArguments";
        try
        {
            if(raw is null || Utf8.GetByteCount(raw)>ToolArgumentValidator.MaxArgumentBytes || failed<0 || failed>=first.Plan.Entries.Length) return false;
            using var doc=JsonDocument.Parse(raw,new JsonDocumentOptions { MaxDepth=4 });var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object) return false;
            var seen=new HashSet<string>(StringComparer.Ordinal);var edits=ImmutableArray<SourceTextEdit>.Empty;
            var excluded=ImmutableArray.CreateBuilder<AdvisoryRepairExclusion>();var total=0;var bytes=0;
            foreach(var slot in root.EnumerateObject())
            {
                if(!seen.Add(slot.Name)) { reason="DuplicateRepairSlot";return false; }
                if(slot.Name.Length!=1 || slot.Name[0]<'a' || slot.Name[0]>='a'+first.Plan.Entries.Length) { reason="UnknownRepairSlotOrField";return false; }
                var authoritative=slot.Name[0]-'a'==failed;var value=slot.Value;
                if(value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()>2 || authoritative && value.GetArrayLength()==0) return false;
                total+=value.GetArrayLength();if(total>3) return false;
                var proposed=ImmutableArray.CreateBuilder<SourceTextEdit>();
                foreach(var item in value.EnumerateArray())
                {
                    if(item.ValueKind!=JsonValueKind.Object) return false;
                    var keys=item.EnumerateObject().Select(p=>p.Name).ToArray();
                    if(keys.Length!=2 || keys.Distinct(StringComparer.Ordinal).Count()!=2 || keys.Any(k=>k is not ("old" or "new")) ||
                        item.GetProperty("old").ValueKind!=JsonValueKind.String || item.GetProperty("new").ValueKind!=JsonValueKind.String) return false;
                    var old=item.GetProperty("old").GetString()!;var next=item.GetProperty("new").GetString()!;
                    if(old.Length is <1 or >256 || next.Length is <1 or >256 || old.Contains('\0') || next.Contains('\0')) return false;
                    bytes+=Utf8.GetByteCount(old)+Utf8.GetByteCount(next);if(bytes>1024) return false;
                    if(authoritative)
                    {
                        var edit=new SourceTextEdit(old,next);if(!ExactSourceEdits.ValidLiteral(edit) || proposed.Contains(edit)) return false;
                        proposed.Add(edit);
                    }
                }
                if(authoritative) edits=proposed.ToImmutable();
                else excluded.Add(new(slot.Name,value.GetArrayLength(),Utf8.GetByteCount(value.GetRawText()),
                    Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(value.GetRawText()))).ToLowerInvariant()));
            }
            if(edits.IsDefaultOrEmpty) { reason="MissingFailedSourceEdits";return false; }
            _=ExactSourceEdits.Resolve(first.Plan.Entries[failed].Before.Content,edits);
            input=new(first,failed,edits,excluded.ToImmutable());reason="ValidHostBoundRepair";return true;
        }
        catch(Exception e) when(e is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException) { return false; }
    }
}
