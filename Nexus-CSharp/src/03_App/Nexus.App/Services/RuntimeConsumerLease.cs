using Nexus.AI.Llama.Models;

namespace Nexus.App.Services;

// Gate B Option A: a single additional inference consumer borrows the current
// workspace runtime. This is not a model owner, lifetime pin, or task scheduler.
internal sealed class RuntimeConsumerLease : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action<RuntimeConsumerLease> _release;
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal RuntimeConsumerLease(ModelCatalogItem model,int? pid,int runtimeIdentity,string endpoint,Action<RuntimeConsumerLease> release)
    {
        Model = model;
        Pid = pid;
        RuntimeIdentity = runtimeIdentity;
        Endpoint = endpoint;
        _release = release;
        CancellationToken = _cancellation.Token;
    }
    internal ModelCatalogItem Model { get; }
    internal int? Pid { get; }
    internal int RuntimeIdentity { get; }
    internal string Endpoint { get; }
    internal CancellationToken CancellationToken { get; }
    internal Task Completed => _completed.Task;
    internal void Cancel()
    {
        try { _cancellation.Cancel(); }
        catch(ObjectDisposedException) { /* operation already completed */ }
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        _release(this);
        _cancellation.Dispose();
        _completed.TrySetResult();
    }
}
