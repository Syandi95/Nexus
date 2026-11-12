using System;
using System.Threading;

namespace Nexus.Contracts;

/// <summary>
/// Kontrak scope minimal untuk job pendek yang dapat dibatalkan dan dibersihkan secara asinkron.
/// </summary>
public interface IJobScope : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }
}
