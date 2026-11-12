using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Nexus.AI.EditingLite.Services;

namespace Nexus.App.Services;

public static class ProjectReadLimits
{
    public const int Depth=6,Directories=128,Entries=4096,SourceFiles=256,MetadataFiles=32,FileBytes=256*1024,GrantBytes=8*1024*1024;
    public const int SearchFiles=32,QuestionBytes=2*1024*1024,ContextFiles=6,ContextLines=160,ContextBytes=24*1024,ContextTokens=3072;
    public static readonly TimeSpan Lifetime=TimeSpan.FromMinutes(10),SnapshotLifetime=TimeSpan.FromSeconds(60),OperationTime=TimeSpan.FromSeconds(10);
}
public sealed record ProjectSourceFile(string Id,string Path,ReadPathIdentity Identity);
public sealed record ProjectMetadataFile(string Path,ReadPathIdentity Identity,string Hash);
public sealed record ProjectReferenceFact(string From,string Path,bool InRoot);
public sealed record ProjectExactSource(string Target,string Hash,string Text,bool Utf8Bom,ReadPathIdentity Identity);
public sealed record ProjectReadCatalog(ImmutableArray<ProjectSourceFile> Sources,ImmutableArray<ProjectMetadataFile> Metadata,ImmutableArray<ProjectReferenceFact> References,ImmutableArray<string> Limitations,string Digest);

// Opaque host-created capability. Never persisted or supplied to a model/tool.
public sealed class ProjectReadGrant : IDisposable
{
    private readonly CancellationTokenSource cancellation=new();
    private int consumedBytes;
    private int disposed;
    internal ReadOnlyPathScope Scope {get;}
    public string Root=>Scope.Root;
    public string Session {get;}
    public DateTimeOffset ExpiresAt {get;}
    public ProjectReadCatalog Catalog {get;internal set;}=new([],[],[],[],"");
    [JsonIgnore] public CancellationToken Cancellation {get;}
    public bool IsActive=>!Cancellation.IsCancellationRequested && DateTimeOffset.UtcNow<ExpiresAt;
    internal ProjectReadGrant(ReadOnlyPathScope scope,string session)
    {Scope=scope;Session=session;ExpiresAt=DateTimeOffset.UtcNow+ProjectReadLimits.Lifetime;Cancellation=cancellation.Token;Cancellation.Register(scope.Dispose);cancellation.CancelAfter(ProjectReadLimits.Lifetime);}
    internal void Check(string session)
    {if(!IsActive || session!=Session)throw new InvalidOperationException("Izin baca sudah dicabut, kedaluwarsa, atau berbeda sesi.");Scope.Validate();}
    internal void Consume(int bytes)
    {if(Interlocked.Add(ref consumedBytes,bytes)>ProjectReadLimits.GrantBytes)throw new InvalidOperationException("Batas byte izin baca habis.");}
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;cancellation.Cancel();cancellation.Dispose();Scope.Dispose();}
}
public interface IProjectReadContextWorkflow : IDisposable
{
    ProjectReadGrant? Current {get;}
    Task<ProjectReadGrant> GrantAsync(string root,string session,CancellationToken token);
    void Revoke();
    Task<ProjectContextPacket> PrepareAsync(ProjectReadGrant grant,string session,string question,IReadOnlyList<string> fileIds,int tokenBudget,CancellationToken token);
    Task<ProjectDispatchLease> ValidateForDispatchAsync(ProjectContextPacket packet,string session,CancellationToken token);
    Task<ImmutableArray<ProjectExactSource>> ReadExactForEditAsync(ProjectReadGrant grant,string session,IReadOnlyList<string> targets,CancellationToken token)
        =>throw new InvalidOperationException("Exact edit snapshots unavailable.");
}
public sealed record ProjectContextFragment(string Path,int FirstLine,int LastLine,string Hash,string Encoding,ReadPathIdentity Identity,DateTimeOffset CapturedAt,bool Truncated,[property:JsonIgnore]string Text);
public sealed class ProjectContextPacket
{
    internal ProjectReadGrant Grant {get;}
    public ImmutableArray<ProjectContextFragment> Fragments {get;}
    public DateTimeOffset PreparedAt {get;}
    [JsonIgnore] public string Payload {get;}
    public ImmutableArray<string> Notices {get;}
    internal ProjectContextPacket(ProjectReadGrant grant,IEnumerable<ProjectContextFragment> fragments,string payload,IEnumerable<string> notices)
    {Grant=grant;Fragments=fragments.ToImmutableArray();Payload=payload;Notices=notices.ToImmutableArray();PreparedAt=DateTimeOffset.UtcNow;}
    public string Provenance=>string.Join("\n",Fragments.Select(f=>$"{f.Path}:L{f.FirstLine}-L{f.LastLine} · {f.Hash} · {f.Encoding}"));
    public bool CitationsValid(string answer)
    {
        var matches=System.Text.RegularExpressions.Regex.Matches(answer,@"\[([^\]\r\n]+):L(\d+)(?:-L?(\d+))?\]",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        return matches.Count>0 && matches.Cast<System.Text.RegularExpressions.Match>().All(m=>int.TryParse(m.Groups[2].Value,out var first) && int.TryParse(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[2].Value,out var last) && first<=last && Fragments.Any(f=>f.Path==m.Groups[1].Value && first>=f.FirstLine && last<=f.LastLine));
    }
}
public sealed class ProjectDispatchLease : IDisposable
{
    private readonly List<IDisposable> handles=[];
    internal void Add(IDisposable handle)=>handles.Add(handle);
    public void Dispose(){foreach(var handle in handles)handle.Dispose();handles.Clear();}
}
