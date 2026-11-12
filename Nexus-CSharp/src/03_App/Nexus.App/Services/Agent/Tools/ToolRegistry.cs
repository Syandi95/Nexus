using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Nexus.App.Services.Agent.Tools;

public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, Entry> _entries;
    public ToolRegistry(IEnumerable<INexusTool> explicitTools)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var tool in explicitTools)
        {
            var descriptor = tool.Descriptor;
            if (!IsValidName(descriptor.Name) || string.IsNullOrWhiteSpace(descriptor.Description) || descriptor.Description.Length > 512 ||
                !Enum.IsDefined(descriptor.Risk) || descriptor.ReadOnly != (descriptor.Risk == ToolRisk.ReadOnly) ||
                descriptor.Timeout <= TimeSpan.Zero || descriptor.Timeout > TimeSpan.FromSeconds(descriptor.Risk is ToolRisk.SafeWrite or ToolRisk.SourcePlan ? 120 : 5) || descriptor.MaxResultSize is < 256 or > 16384 || descriptor.Risk==ToolRisk.Dangerous)
                throw new ArgumentException("Invalid tool descriptor.");
            if(tool is ApplySourcePlanTool && descriptor.Name==ApplySourcePlanTool.ToolName && descriptor.Risk==ToolRisk.SourcePlan)
                SourcePlanArgumentValidator.ValidateSchema(descriptor.ArgumentSchema);
            else ToolArgumentValidator.ValidateSchema(descriptor.ArgumentSchema);
            var snapshot = descriptor with { ArgumentSchema = descriptor.ArgumentSchema.Clone() };
            if (!entries.TryAdd(snapshot.Name, new Entry(snapshot, tool))) throw new ArgumentException("Duplicate tool name.");
        }
        _entries = new ReadOnlyDictionary<string, Entry>(entries);
        Descriptors = Array.AsReadOnly(entries.Values.Select(e => e.Descriptor).ToArray());
    }
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }
    internal bool TryGet(string exactName, out Entry? entry)
    {
        entry = null;
        return IsValidName(exactName) && _entries.TryGetValue(exactName, out entry);
    }
    internal sealed record Entry(ToolDescriptor Descriptor, INexusTool Tool);
    internal static bool IsValidName(string? name) => name is not null && Regex.IsMatch(name, "\\A[a-z][a-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant);
}
