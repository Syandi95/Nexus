using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Exceptions;
using Nexus.AI.EditingLite.Services;
using Xunit;

namespace Nexus.AI.EditingLite.Tests.Services;

public class EditingWorkflowServiceTests : IDisposable
{
    private readonly EditingWorkflowService _service;
    private readonly string _testRoot;

    public EditingWorkflowServiceTests()
    {
        var snapshotService = new FileSnapshotService();
        var proposalService = new ProposalService();
        var syntaxValidator = new SyntaxValidatorService();
        var fileWriter = new FileWriterService();

        _service = new EditingWorkflowService(snapshotService, proposalService, syntaxValidator, fileWriter);
        
        _testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Fact]
    public async Task ApplyEditAsync_Success()
    {
        var filePath = "test.txt";
        var absolutePath = Path.Combine(_testRoot, filePath);
        await File.WriteAllTextAsync(absolutePath, "original");

        await _service.ApplyEditAsync(_testRoot, filePath, "original", "replaced");

        var newContent = await File.ReadAllTextAsync(absolutePath);
        Assert.Equal("replaced", newContent);
    }

    [Fact]
    public async Task ApplyEditAsync_SyntaxValidationFailed_RollsBack()
    {
        var filePath = "test.json";
        var absolutePath = Path.Combine(_testRoot, filePath);
        await File.WriteAllTextAsync(absolutePath, "{\"a\":1}");

        var ex = await Assert.ThrowsAsync<EditingException>(() => 
            _service.ApplyEditAsync(_testRoot, filePath, "1", "invalid"));

        Assert.Equal(EditingFailureCode.SyntaxValidationFailed, ex.FailureCode);

        // Check if rolled back
        var newContent = await File.ReadAllTextAsync(absolutePath);
        Assert.Equal("{\"a\":1}", newContent);
    }

    [Fact]
    public async Task ApplyEditAsync_OldTextNotFound_Throws()
    {
        var filePath = "test.txt";
        var absolutePath = Path.Combine(_testRoot, filePath);
        await File.WriteAllTextAsync(absolutePath, "original");

        var ex = await Assert.ThrowsAsync<EditingException>(() => 
            _service.ApplyEditAsync(_testRoot, filePath, "missing", "replaced"));

        Assert.Equal(EditingFailureCode.OldTextNotFound, ex.FailureCode);
    }

    [Fact]
    public async Task ApplyEditAsync_NoChange_ThrowsFinalValidationFailed()
    {
        var filePath = "test.txt";
        var absolutePath = Path.Combine(_testRoot, filePath);
        await File.WriteAllTextAsync(absolutePath, "original");

        // "NoChange" is caught by ProposalService first
        var ex = await Assert.ThrowsAsync<EditingException>(() => 
            _service.ApplyEditAsync(_testRoot, filePath, "original", "original"));

        Assert.Equal(EditingFailureCode.NoChange, ex.FailureCode);
    }
}
