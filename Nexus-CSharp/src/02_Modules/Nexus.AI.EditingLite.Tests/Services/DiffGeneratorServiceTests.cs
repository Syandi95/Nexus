using Nexus.AI.EditingLite.Services;
using Xunit;

namespace Nexus.AI.EditingLite.Tests.Services;

public class DiffGeneratorServiceTests
{
    private readonly DiffGeneratorService _service;

    public DiffGeneratorServiceTests()
    {
        _service = new DiffGeneratorService();
    }

    [Fact]
    public void CreateDiff_GeneratesCorrectFormat()
    {
        var orig = "line1\nline2\nline3\nline4\nline5";
        var proposed = "line1\nline2\nchanged\nline4\nline5";

        var diff = _service.CreateDiff(orig, proposed, contextLines: 1);
        
        var expected = "  line2\n- line3\n+ changed\n  line4";
        // Normalize line endings for the test
        expected = expected.Replace("\n", System.Environment.NewLine);
        
        Assert.Equal(expected, diff);
    }

    [Fact]
    public void CreateDiff_NoChanges_ReturnsEmptyString()
    {
        var diff = _service.CreateDiff("abc", "abc");
        Assert.Equal(string.Empty, diff);
    }
}
