using Nexus.Contracts;

namespace Nexus.Engine.ImageSubsystem.Pipelines;

internal sealed class PipelineJobScope : IJobScope
{
    private readonly CancellationTokenSource _cancellation;

    public PipelineJobScope(CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    }

    public CancellationToken CancellationToken => _cancellation.Token;

    public ValueTask DisposeAsync()
    {
        _cancellation.Dispose();
        return ValueTask.CompletedTask;
    }
}
