using Nexus.AI.EditingLite.Exceptions;
using Nexus.AI.EditingLite.Services;
using Xunit;

namespace Nexus.AI.EditingLite.Tests.Services;

public class SyntaxValidatorServiceTests
{
    private readonly SyntaxValidatorService _service;

    public SyntaxValidatorServiceTests()
    {
        _service = new SyntaxValidatorService();
    }

    [Fact]
    public void Validate_ValidJson_Passes()
    {
        _service.Validate("test.json", "{\"key\":\"value\"}");
    }

    [Fact]
    public void Validate_InvalidJson_ThrowsSyntaxValidationFailed()
    {
        var ex = Assert.Throws<EditingException>(() => _service.Validate("test.json", "{key:value}"));
        Assert.Equal(EditingFailureCode.SyntaxValidationFailed, ex.FailureCode);
    }

    [Fact]
    public void Validate_ValidXml_Passes()
    {
        _service.Validate("test.xml", "<root><item/></root>");
    }

    [Fact]
    public void Validate_InvalidXml_ThrowsSyntaxValidationFailed()
    {
        var ex = Assert.Throws<EditingException>(() => _service.Validate("test.xml", "<root><item></root>"));
        Assert.Equal(EditingFailureCode.SyntaxValidationFailed, ex.FailureCode);
    }

    [Fact]
    public void Validate_ValidPowerShell_Passes()
    {
        _service.Validate("script.ps1", "Write-Host 'Hello'");
    }

    [Fact]
    public void Validate_InvalidPowerShell_ThrowsSyntaxValidationFailed()
    {
        var ex = Assert.Throws<EditingException>(() => _service.Validate("script.ps1", "Write-Host 'Hello"));
        Assert.Equal(EditingFailureCode.SyntaxValidationFailed, ex.FailureCode);
    }

    [Fact]
    public void Validate_UnsupportedExtension_DoesNothing()
    {
        _service.Validate("test.txt", "Some random text that isn't valid anywhere {<");
    }
}
