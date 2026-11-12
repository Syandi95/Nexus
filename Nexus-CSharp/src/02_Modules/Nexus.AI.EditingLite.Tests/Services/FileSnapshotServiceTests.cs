using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Exceptions;
using Nexus.AI.EditingLite.Services;
using Xunit;

namespace Nexus.AI.EditingLite.Tests.Services;

public class FileSnapshotServiceTests : IDisposable
{
    private readonly FileSnapshotService _service;
    private readonly string _testRoot;

    public FileSnapshotServiceTests()
    {
        _service = new FileSnapshotService();
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
    public void ResolvePath_ValidPath_ReturnsAbsolutePath()
    {
        var result = _service.ResolvePath(_testRoot, "test.txt");
        Assert.Equal(Path.Combine(_testRoot, "test.txt"), result);
    }

    [Fact]
    public void ResolvePath_PathTraversal_ThrowsPathOutsideProjectRoot()
    {
        var ex = Assert.Throws<EditingException>(() => _service.ResolvePath(_testRoot, "../outside.txt"));
        Assert.Equal(EditingFailureCode.PathOutsideProjectRoot, ex.FailureCode);
    }

    [Fact]
    public void DetectEncoding_NulByte_ThrowsBinaryFileRejected()
    {
        var bytes = new byte[] { 0x41, 0x00, 0x42 }; // A, NUL, B
        var ex = Assert.Throws<EditingException>(() => _service.DetectEncoding(bytes));
        Assert.Equal(EditingFailureCode.BinaryFileRejected, ex.FailureCode);
    }

    [Fact]
    public void DetectEncoding_Utf8_ReturnsUtf8()
    {
        var bytes = new byte[] { 0x41, 0x42, 0x43 }; // A, B, C
        var encoding = _service.DetectEncoding(bytes);
        Assert.Equal(Encoding.UTF8, encoding);
    }

    [Fact]
    public async Task ReadSnapshotAsync_Success()
    {
        var filePath = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello World", Encoding.UTF8);

        var snapshot = await _service.ReadSnapshotAsync(_testRoot, "test.txt");

        Assert.Equal("Hello World", snapshot.Content);
        Assert.Equal(filePath, snapshot.AbsolutePath);
        Assert.NotNull(snapshot.Hash);
    }

    [Fact]
    public async Task ReadSnapshotAsync_FileNotFound_ThrowsFileNotFound()
    {
        var ex = await Assert.ThrowsAsync<EditingException>(() => _service.ReadSnapshotAsync(_testRoot, "missing.txt"));
        Assert.Equal(EditingFailureCode.FileNotFound, ex.FailureCode);
    }
}
