using System;
using System.IO;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Exceptions;

namespace Nexus.AI.EditingLite.Services;

public interface IEditingWorkflowService
{
    Task ApplyEditAsync(string projectRoot, string filePath, string oldText, string newText);
}

public partial class EditingWorkflowService : IEditingWorkflowService
{
    private readonly IFileSnapshotService _snapshotService;
    private readonly IProposalService _proposalService;
    private readonly ISyntaxValidatorService _syntaxValidator;
    private readonly IFileWriterService _fileWriter;

    public EditingWorkflowService(
        IFileSnapshotService snapshotService,
        IProposalService proposalService,
        ISyntaxValidatorService syntaxValidator,
        IFileWriterService fileWriter)
    {
        _snapshotService = snapshotService;
        _proposalService = proposalService;
        _syntaxValidator = syntaxValidator;
        _fileWriter = fileWriter;
    }

    public async Task ApplyEditAsync(string projectRoot, string filePath, string oldText, string newText)
    {
        string sessionId = Guid.NewGuid().ToString("N");
        string resolvedPath = string.Empty;
        string? backupPath = null;
        string? tempPath = null;

        try
        {
            // 1. Snapshot
            var snapshot = await _snapshotService.ReadSnapshotAsync(projectRoot, filePath);
            resolvedPath = snapshot.AbsolutePath;

            // 2. Proposal
            var proposedContent = _proposalService.CreateProposal(snapshot.Content, oldText, newText);

            // 3. NewSession
            var session = new EditSession
            {
                Id = sessionId,
                TargetPath = resolvedPath,
                ProposedContent = proposedContent,
                Encoding = snapshot.Encoding
            };

            // 4. CreateBackup
            backupPath = CreateBackup(resolvedPath, sessionId);

            // 5. Write Temp
            tempPath = await _fileWriter.WriteTempFileAsync(session);

            // 6. Syntax Validation (on temp file to avoid modifying target yet)
            _syntaxValidator.Validate(resolvedPath, proposedContent);

            // 7. Apply Edit (Atomic Replace)
            _fileWriter.AtomicReplace(tempPath, resolvedPath);
            tempPath = null; // replaced

            // 8. Final Validation (read back)
            var newSnapshot = await _snapshotService.ReadSnapshotAsync(projectRoot, filePath);
            if (newSnapshot.Hash == snapshot.Hash)
            {
                throw new EditingException(EditingFailureCode.FinalValidationFailed, "File hash did not change after edit.");
            }

            // Cleanup backup
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            // Rollback automatically
            if (!string.IsNullOrEmpty(resolvedPath) && !string.IsNullOrEmpty(backupPath))
            {
                RestoreBackup(backupPath, resolvedPath);
            }

            // Cleanup temp
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            if (ex is EditingException)
            {
                throw;
            }

            throw new EditingException(EditingFailureCode.UnknownError, "An unknown error occurred during the editing workflow.", ex);
        }
    }

    private string CreateBackup(string targetPath, string sessionId)
    {
        try
        {
            var backupPath = targetPath + $".nexus-edit-{sessionId}.bak";
            File.Copy(targetPath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex)
        {
            throw new EditingException(EditingFailureCode.BackupFailed, "Failed to create backup.", ex);
        }
    }

    private void RestoreBackup(string backupPath, string targetPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, targetPath, overwrite: true);
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            throw new EditingException(EditingFailureCode.RestoreFailed, "Failed to restore backup.", ex);
        }
    }
}
