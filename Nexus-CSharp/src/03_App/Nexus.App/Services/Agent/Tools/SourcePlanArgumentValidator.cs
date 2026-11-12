using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexus.AI.EditingLite.Services;

namespace Nexus.App.Services.Agent.Tools;

// A single explicit source-plan dialect. Generic tool schemas remain scalar-only.
internal static class SourcePlanArgumentValidator
{
    internal static JsonElement Schema { get; }=CreateSchema();
    private static JsonElement CreateSchema()
    {
        var item=new { type="object",properties=new { old=new { type="string",minLength=1,maxLength=256 },@new=new { type="string",minLength=1,maxLength=256 } },required=new[] { "old","new" },additionalProperties=false };
        var array=new { type="array",minItems=1,maxItems=2,items=item };
        return JsonSerializer.SerializeToElement(new { type="object",properties=new { a=array,b=array,c=array },required=new[] { "a","b" },additionalProperties=false });
    }
    internal static void ValidateSchema(JsonElement schema)
    {
        if(!JsonNode.DeepEquals(JsonNode.Parse(schema.GetRawText()),JsonNode.Parse(Schema.GetRawText()))) throw new ArgumentException("Exact source-plan schema required.");
    }
    internal static bool TryValidate(string raw,out JsonElement arguments)
    {
        arguments=default;
        if(raw is null || Encoding.UTF8.GetByteCount(raw)>ToolArgumentValidator.MaxArgumentBytes) return false;
        try
        {
            using var doc=JsonDocument.Parse(raw,new JsonDocumentOptions { MaxDepth=4 });
            var root=doc.RootElement;if(root.ValueKind!=JsonValueKind.Object) return false;
            var slots=root.EnumerateObject().ToArray();
            if(slots.Length is <2 or >3 || slots.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=slots.Length || slots.Any(s=>s.Name is not ("a" or "b" or "c")) ||
                !root.TryGetProperty("a",out _) || !root.TryGetProperty("b",out _)) return false;
            var total=0;var literalBytes=0;
            foreach(var slot in slots)
            {
                if(slot.Value.ValueKind!=JsonValueKind.Array || slot.Value.GetArrayLength() is <1 or >2) return false;
                total+=slot.Value.GetArrayLength();if(total>3) return false;
                var edits=new List<SourceTextEdit>();
                foreach(var item in slot.Value.EnumerateArray())
                {
                    if(item.ValueKind!=JsonValueKind.Object) return false;
                    var keys=item.EnumerateObject().ToArray();
                    if(keys.Length!=2 || keys.Select(k=>k.Name).Distinct(StringComparer.Ordinal).Count()!=2 || keys.Any(k=>k.Name is not ("old" or "new")) ||
                        item.GetProperty("old").ValueKind!=JsonValueKind.String || item.GetProperty("new").ValueKind!=JsonValueKind.String) return false;
                    var edit=new SourceTextEdit(item.GetProperty("old").GetString()!,item.GetProperty("new").GetString()!);
                    if(!ExactSourceEdits.ValidLiteral(edit) || edits.Contains(edit)) return false;
                    edits.Add(edit);
                }
                literalBytes+=ExactSourceEdits.LiteralBytes(edits);if(literalBytes>1024) return false;
            }
            arguments=root.Clone();return true;
        }
        catch(Exception e) when(e is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException) { return false; }
    }
    internal static SourceTextEdit[] Read(JsonElement slot)=>slot.EnumerateArray().Select(e=>new SourceTextEdit(e.GetProperty("old").GetString()!,e.GetProperty("new").GetString()!)).ToArray();
}
