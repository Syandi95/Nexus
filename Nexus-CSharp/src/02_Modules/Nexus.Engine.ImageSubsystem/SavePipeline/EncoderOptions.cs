namespace Nexus.Engine.ImageSubsystem.SavePipeline;

public sealed record EncoderOptions
{
    public int Quality { get; init; } = 95;
}
