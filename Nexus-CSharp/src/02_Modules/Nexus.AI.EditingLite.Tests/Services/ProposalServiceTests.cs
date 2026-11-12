using System;
using Nexus.AI.EditingLite.Exceptions;
using Nexus.AI.EditingLite.Services;
using Xunit;

namespace Nexus.AI.EditingLite.Tests.Services;

public class ProposalServiceTests
{
    private readonly ProposalService _service;

    public ProposalServiceTests()
    {
        _service = new ProposalService();
    }

    [Fact]
    public void CountLiteralMatches_ReturnsCorrectCount()
    {
        var content = "abc def abc ghi";
        var count = _service.CountLiteralMatches(content, "abc");
        Assert.Equal(2, count);
    }

    [Fact]
    public void CreateProposal_Success()
    {
        var content = "var x = 1;";
        var proposal = _service.CreateProposal(content, "1", "2");
        Assert.Equal("var x = 2;", proposal);
    }

    [Fact]
    public void CreateProposal_ThrowsOldTextRequired()
    {
        var ex = Assert.Throws<EditingException>(() => _service.CreateProposal("abc", "", "def"));
        Assert.Equal(EditingFailureCode.OldTextRequired, ex.FailureCode);
    }

    [Fact]
    public void CreateProposal_ThrowsNoChange()
    {
        var ex = Assert.Throws<EditingException>(() => _service.CreateProposal("abc def", "def", "def"));
        Assert.Equal(EditingFailureCode.NoChange, ex.FailureCode);
    }

    [Fact]
    public void CreateProposal_ThrowsOldTextNotFound()
    {
        var ex = Assert.Throws<EditingException>(() => _service.CreateProposal("abc", "def", "ghi"));
        Assert.Equal(EditingFailureCode.OldTextNotFound, ex.FailureCode);
    }

    [Fact]
    public void CreateProposal_ThrowsOldTextNotUnique()
    {
        var ex = Assert.Throws<EditingException>(() => _service.CreateProposal("abc abc", "abc", "def"));
        Assert.Equal(EditingFailureCode.OldTextNotUnique, ex.FailureCode);
    }
}
