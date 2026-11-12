using System.IO.Compression;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;

namespace Nexus.AI.EditingLite.Services;

// Adapt MSBuild's public typed replay source; never evaluate a project, load a
// task/plugin, or parse console/binary record formats ourselves.
internal sealed record CompilerBuildEvent(string Kind,string Context,string? Project,string? Task,string? TaskFile,string? Assembly,
    string? Code,string? File,int Line,int Column,string? Message,bool? Succeeded,DateTime Timestamp=default);
internal static class CompilerBuildLedger
{
    internal const int CompressedLimit=1024*1024,ExpandedLimit=8*1024*1024,EventLimit=20000;
    internal static IReadOnlyList<CompilerBuildEvent> Read(string sdkDirectory,string path,CancellationToken token)
        =>ReadBytes(sdkDirectory,File.ReadAllBytes(path),token);
    internal static IReadOnlyList<CompilerBuildEvent> ReadBytes(string sdkDirectory,byte[] bytes,CancellationToken token)
    {
        if(!System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(sdkDirectory,"Microsoft.Build.dll")).ProductVersion!.StartsWith("17.11.48",StringComparison.Ordinal))
            throw new NotSupportedException("Pinned MSBuild reader required.");
        using var file=new MemoryStream(bytes,false);
        if(file.Length is <=0 or >CompressedLimit) throw new IOException("Compiler ledger size denied.");
        using var expanded=new MemoryStream(); using(var gzip=new GZipStream(file,CompressionMode.Decompress,true))
        {
            var buffer=new byte[8192]; int count;
            while((count=gzip.Read(buffer))>0) { token.ThrowIfCancellationRequested(); if(expanded.Length+count>ExpandedLimit) throw new IOException("Compiler ledger expansion denied."); expanded.Write(buffer,0,count); }
        }
        if(bytes.Length<8 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length-4))!=expanded.Length)
            throw new IOException("Incomplete compiler ledger container.");
        expanded.Position=0;
        var load=new PinnedSdkContext(sdkDirectory);
        try
        {
            var assembly=load.LoadFromAssemblyPath(Path.Combine(sdkDirectory,"Microsoft.Build.dll"));
            var type=assembly.GetType("Microsoft.Build.Logging.BinaryLogReplayEventSource",true)!;
            var replay=Activator.CreateInstance(type)!;
            var method=type.GetMethod("Replay",[typeof(BinaryReader),typeof(bool),typeof(CancellationToken)]) ?? throw new NotSupportedException("Pinned replay API missing.");
            var raised=type.GetEvent("AnyEventRaised") ?? throw new NotSupportedException("Pinned typed events missing.");
            var signature=raised.EventHandlerType!.GetMethod("Invoke")!.GetParameters();
            var sender=Expression.Parameter(signature[0].ParameterType,"sender"); var args=Expression.Parameter(signature[1].ParameterType,"args");
            var events=new List<CompilerBuildEvent>(); var count=0;
            Action<object> collect=value=>
            {
                token.ThrowIfCancellationRequested(); if(++count>EventLimit) throw new IOException("Compiler ledger event budget exceeded.");
                var kind=value.GetType().Name;
                if(kind is not ("BuildStartedEventArgs" or "BuildFinishedEventArgs" or "ProjectStartedEventArgs" or "ProjectFinishedEventArgs" or
                    "TaskStartedEventArgs" or "TaskFinishedEventArgs" or "BuildErrorEventArgs" or "BuildWarningEventArgs" or "TaskCommandLineEventArgs")) return;
                string? Text(string name,int limit=2048)
                { var text=value.GetType().GetProperty(name)?.GetValue(value)?.ToString(); if(text?.Length>limit) throw new IOException("Compiler event field exceeded."); return text; }
                var context=value.GetType().GetProperty("BuildEventContext")!.GetValue(value);
                if(context is null && kind is not ("BuildStartedEventArgs" or "BuildFinishedEventArgs")) throw new IOException("Missing task/project event context.");
                var key=context is null ? "build" : string.Join(":",new[] { "NodeId","ProjectContextId","TargetId","TaskId" }.Select(n=>context.GetType().GetProperty(n)!.GetValue(context)));
                bool? success=value.GetType().GetProperty("Succeeded")?.GetValue(value) is bool b ? b : null;
                events.Add(new(kind,key,Text("ProjectFile"),Text("TaskName"),Text("TaskFile"),Text("TaskAssemblyLocation"),Text("Code",32),Text("File"),
                    value.GetType().GetProperty("LineNumber")?.GetValue(value) is int line ? line : 0,
                    value.GetType().GetProperty("ColumnNumber")?.GetValue(value) is int column ? column : 0,
                    kind=="TaskCommandLineEventArgs" ? Text("CommandLine",65536) : kind is "BuildErrorEventArgs" or "BuildWarningEventArgs" ? Text("Message") : null,success,
                    value.GetType().GetProperty("Timestamp")!.GetValue(value) is DateTime timestamp ? timestamp.ToUniversalTime() : default));
            };
            var callback=Expression.Lambda(raised.EventHandlerType,Expression.Call(Expression.Constant(collect),typeof(Action<object>).GetMethod("Invoke")!,Expression.Convert(args,typeof(object))),sender,args).Compile();
            raised.AddEventHandler(replay,callback);
            try { using var reader=new BinaryReader(expanded,System.Text.Encoding.UTF8,true); method.Invoke(replay,[reader,false,token]); }
            catch(TargetInvocationException error) { throw new IOException("Pinned structured replay failed.",error.InnerException); }
            finally { raised.RemoveEventHandler(replay,callback); }
            return events;
        }
        finally { load.Unload(); }
    }
    private sealed class PinnedSdkContext(string directory) : AssemblyLoadContext("Nexus pinned compiler ledger",isCollectible:true)
    {
        protected override Assembly? Load(AssemblyName name)=>name.Name is "Microsoft.Build.Framework" or "Microsoft.NET.StringTools"
            ? LoadFromAssemblyPath(Path.Combine(directory,name.Name+".dll")) : null;
    }
}
