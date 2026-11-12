using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Services;
using Xunit;

namespace Nexus.AI.EditingLite.Tests.Services;

public class FileWriterServiceTests : IDisposable
{
    private readonly FileWriterService _service;
    private readonly string _testRoot;

    public FileWriterServiceTests()
    {
        _service = new FileWriterService();
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
    public async Task WriteTempFileAsync_CreatesFile()
    {
        var targetPath = Path.Combine(_testRoot, "target.txt");
        var session = new EditSession
        {
            Id = "123",
            TargetPath = targetPath,
            ProposedContent = "temp content",
            Encoding = Encoding.UTF8
        };

        var tempPath = await _service.WriteTempFileAsync(session);

        Assert.True(File.Exists(tempPath));
        Assert.Equal("temp content", await File.ReadAllTextAsync(tempPath));
    }

    [Fact]
    public async Task AtomicReplace_ReplacesFile()
    {
        var targetPath = Path.Combine(_testRoot, "target.txt");
        await File.WriteAllTextAsync(targetPath, "original");

        var session = new EditSession
        {
            Id = "123",
            TargetPath = targetPath,
            ProposedContent = "replaced",
            Encoding = Encoding.UTF8
        };

        var tempPath = await _service.WriteTempFileAsync(session);

        _service.AtomicReplace(tempPath, targetPath);

        Assert.True(File.Exists(targetPath));
        Assert.Equal("replaced", await File.ReadAllTextAsync(targetPath));
        Assert.False(File.Exists(tempPath));
    }
}
