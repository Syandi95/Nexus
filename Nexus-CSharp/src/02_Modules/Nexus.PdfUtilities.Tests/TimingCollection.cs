using Xunit;

// PDFium/JIT/disk-cache timing must not contend with other tests in this process.
[CollectionDefinition("PDF timing", DisableParallelization = true)]
public sealed class TimingCollection { }
