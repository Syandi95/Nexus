using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Exceptions;

namespace Nexus.AI.EditingLite.Services;

public class FileSnapshot
{
    public required string Content { get; init; }
    public required Encoding Encoding { get; init; }
    public required string Hash { get; init; }
    public required string AbsolutePath { get; init; }
}

public interface IFileSnapshotService
{
    string ComputeSha256Hash(string path);
    string ResolvePath(string root, string path);
    Encoding DetectEncoding(byte[] bytes);
    Task<FileSnapshot> ReadSnapshotAsync(string root, string path, int maxBytes = 1024 * 1024);
}

public class FileSnapshotService : IFileSnapshotService
{
    public string ComputeSha256Hash(string path)
    {
        if (!File.Exists(path))
            throw new EditingException(EditingFailureCode.FileNotFound, $"File not found: {path}");

        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public string ResolvePath(string root, string path)
    {
        return SafePath.ResolveRelative(root,path);
    }

    public Encoding DetectEncoding(byte[] bytes)
    {
        // Check for NUL byte
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0x00)
            {
                throw new EditingException(EditingFailureCode.BinaryFileRejected, "Binary file rejected.");
            }
        }

        // Simple BOM detection
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;

        return Encoding.UTF8; // Fallback
    }

    public async Task<FileSnapshot> ReadSnapshotAsync(string root, string path, int maxBytes = 1024 * 1024)
    {
        var resolvedPath = ResolvePath(root, path);

        if (!File.Exists(resolvedPath))
        {
            throw new EditingException(EditingFailureCode.FileNotFound, $"File not found: {resolvedPath}");
        }

        var fileInfo = new FileInfo(resolvedPath);
        if (fileInfo.Length > maxBytes)
        {
            throw new EditingException(EditingFailureCode.FileTooLarge, $"File exceeds maximum allowed size.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(resolvedPath);
        var encoding = DetectEncoding(bytes);
        var content = encoding.GetString(bytes);

        // Strip BOM if present since GetString might include it or might not depending on encoding, but let's assume standard behavior
        if (content.StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            content = content.Substring(1);
        }

        if(bytes.Length>maxBytes) throw new EditingException(EditingFailureCode.FileTooLarge,"File exceeds maximum allowed size.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return new FileSnapshot
        {
            Content = content,
            Encoding = encoding,
            Hash = hash,
            AbsolutePath = resolvedPath
        };
    }
}
