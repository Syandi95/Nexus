using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Exceptions;

namespace Nexus.AI.EditingLite.Services;

public class EditSession
{
    public required string Id { get; init; }
    public required string TargetPath { get; init; }
    public required string ProposedContent { get; init; }
    public required Encoding Encoding { get; init; }
}

public interface IFileWriterService
{
    Task<string> WriteTempFileAsync(EditSession session);
    void AtomicReplace(string tempPath, string targetPath);
}

public partial class FileWriterService : IFileWriterService
{
    public async Task<string> WriteTempFileAsync(EditSession session)
    {
        try
        {
            var dir = Path.GetDirectoryName(session.TargetPath) ?? string.Empty;
            var tempName = $".nexus-edit-{session.Id}.tmp";
            var tempPath = Path.Combine(dir, tempName);

            await File.WriteAllTextAsync(tempPath, session.ProposedContent, session.Encoding);

            return tempPath;
        }
        catch (Exception ex) when (ex is not EditingException)
        {
            throw new EditingException(EditingFailureCode.TempFileCreationFailed, "Failed to create temp file.", ex);
        }
    }

    public void AtomicReplace(string tempPath, string targetPath)
    {
        try
        {
            var backupPath = targetPath + $".{Guid.NewGuid():N}.bak";
            File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
            
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            throw new EditingException(EditingFailureCode.AtomicReplaceFailed, "Failed to atomic replace file.", ex);
        }
    }
}
