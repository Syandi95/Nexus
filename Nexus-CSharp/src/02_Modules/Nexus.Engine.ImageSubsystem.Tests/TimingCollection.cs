using Xunit;

// Keep the empirical CPU gate independent of ONNX inference in other collections.
[CollectionDefinition("CPU timing", DisableParallelization = true)]
public sealed class TimingCollection { }
