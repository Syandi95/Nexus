using System.Collections.Generic;
using Xunit;

namespace Nexus.Contracts.Tests;

public sealed class ResolvedModelDescriptorTests
{
    [Fact]
    public void AdditionalInputs_WhenNull_RepresentsLegacySingleInputModel()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "u2net",
            ModelPath = @"C:\models\u2net.onnx",
            InputTensorName = "input",
            InputShape = new long[] { 1, 3, 320, 320 },
            OutputTensorName = "output",
            OutputShape = new long[] { 1, 1, 320, 320 },
            AdditionalInputs = null
        };

        Assert.Null(descriptor.AdditionalInputs);
        Assert.Equal("input", descriptor.InputTensorName);
        Assert.Equal(new long[] { 1, 3, 320, 320 }, descriptor.InputShape);
    }

    [Fact]
    public void AdditionalInputs_WhenPopulated_StoresMultiInputTensorsCorrectly()
    {
        var additional = new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["mask"] = new long[] { 1, 1, 512, 512 }
        };

        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "big-lama-fp32",
            ModelPath = @"C:\models\lama_fp32.onnx",
            InputTensorName = "image",
            InputShape = new long[] { 1, 3, 512, 512 },
            OutputTensorName = "output",
            OutputShape = new long[] { 1, 3, 512, 512 },
            AdditionalInputs = additional
        };

        Assert.NotNull(descriptor.AdditionalInputs);
        Assert.Single(descriptor.AdditionalInputs);
        Assert.True(descriptor.AdditionalInputs.ContainsKey("mask"));
        Assert.Equal(new long[] { 1, 1, 512, 512 }, descriptor.AdditionalInputs["mask"]);
    }
}
