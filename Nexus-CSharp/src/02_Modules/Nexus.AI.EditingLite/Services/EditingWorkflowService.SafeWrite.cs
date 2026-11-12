using System.Security.Cryptography;
using System.Text;

namespace Nexus.AI.EditingLite.Services;

public partial class EditingWorkflowService
{
    internal Action<SafeWriteStage,string>? SafeWriteProbe { get; set; }
    internal bool FailSafeVerification { get; set; }
    public Task<SafeWriteProposal> PrepareSourceWriteAsync(ApprovedTextWorkspace workspace,string target,string expectedHash,
        IEnumerable<SourceTextEdit> edits,Guid runId,string callId,SafeWriteLimits limits,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!workspace.IsSourceScope || !limits.IsValid || runId==Guid.Empty || expectedHash is null || expectedHash.Length!=64 || expectedHash.Any(c=>!Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid source proposal.");
        workspace.ResolveTarget(target,limits.MaxTargetFileBytes);
        using var lease=new SafePathLease(workspace.Root!,target);using var current=lease.OpenTarget();
        var before=ReadUtf8(current,lease.TargetPath,limits.MaxTargetFileBytes);
        if(before.Hash!=expectedHash.ToLowerInvariant()) throw new SafeWriteConflictException();
        var resolved=ExactSourceEdits.Resolve(before.Content,edits,limits.MaxWriteInputBytes);
        _syntaxValidator.Validate(lease.TargetPath,resolved.DesiredContent);
        var bytes=Encode(before.Encoding,resolved.DesiredContent);
        if(bytes.Length>limits.MaxTargetFileBytes) throw new ArgumentException("Desired size denied.");
        var diff=ExactSourceDiff(before.Content,resolved.DesiredContent,resolved.Edits);
        var first=resolved.Edits[0];
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new SafeWriteProposal(Guid.NewGuid(),runId,callId,target,before.Hash,Hash(bytes),Hash(Encoding.UTF8.GetBytes(diff)),diff,
            DateTimeOffset.UtcNow,workspace,before,resolved.DesiredContent,limits,first.OldText,first.NewText) { ResolvedEdits=resolved.Edits });
    }
    public async Task<SafeWriteProposal> PrepareSafeWriteAsync(ApprovedTextWorkspace workspace,string target,string expectedHash,
        string oldText,string newText,Guid runId,string callId,SafeWriteLimits limits,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(workspace.IsSourceScope) return await PrepareSourceWriteAsync(workspace,target,expectedHash,[new SourceTextEdit(oldText,newText)],runId,callId,limits,token);
        if(!limits.IsValid || runId==Guid.Empty || expectedHash.Length!=64 || expectedHash.Any(c=>!Uri.IsHexDigit(c)) ||
            string.IsNullOrEmpty(oldText) || oldText.Length>256 || newText.Length>256 || newText.Contains('\0') || oldText.Contains('\0') ||
            Encoding.UTF8.GetByteCount(oldText)+Encoding.UTF8.GetByteCount(newText)>limits.MaxWriteInputBytes) throw new ArgumentException("Invalid bounded patch.");
        workspace.ResolveTarget(target,limits.MaxTargetFileBytes);
        using var lease=new SafePathLease(workspace.Root!,target);
        using var current=lease.OpenTarget();
        var before=ReadUtf8(current,lease.TargetPath,limits.MaxTargetFileBytes);
        if(before.Hash!=expectedHash.ToLowerInvariant()) throw new SafeWriteConflictException();
        var oldCount=_proposalService.CountLiteralMatches(before.Content,oldText);
        var newCount=_proposalService.CountLiteralMatches(before.Content,newText);
        var oldPosition=before.Content.IndexOf(oldText,StringComparison.Ordinal);
        var newPosition=before.Content.IndexOf(newText,StringComparison.Ordinal);
        if(workspace.IsSourceScope && oldCount!=1) throw new ArgumentException("Source replacement must match exactly once.");
        var alreadyDesired=newCount==1 && (oldCount==0 || oldCount==1 && oldPosition>=newPosition && oldPosition+oldText.Length<=newPosition+newText.Length);
        var desired=oldText==newText || !workspace.IsSourceScope && alreadyDesired ? before.Content : _proposalService.CreateProposal(before.Content,oldText,newText);
        _syntaxValidator.Validate(lease.TargetPath,desired);
        var bytes=Encode(before.Encoding,desired);
        if(bytes.Length>limits.MaxTargetFileBytes) throw new ArgumentException("Desired size denied.");
        var diff=ExactDiff(before.Content,desired,oldText,newText);
        token.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        return new(Guid.NewGuid(),runId,callId,target,before.Hash,Hash(bytes),Hash(Encoding.UTF8.GetBytes(diff)),diff,DateTimeOffset.UtcNow,workspace,before,desired,limits,oldText,newText);
    }

    public async Task<SafeWriteResult> ApplyApprovedSafeWriteAsync(SafeWriteProposal p,WriteApprovalDecision approval,CancellationToken token,
        Action<SafeWriteStage,WriteOperationRecord>? observe=null,Action? authorizeCommit=null)
    {
        var record=new WriteOperationRecord(p.OperationId,p.RunId,p.ToolCallId,p.Target,p.BeforeHash,p.DesiredHash,Timestamp:p.CreatedAt,PolicyId:p.PolicyId);
        string? temp=null,backup=null;
        var writer=_fileWriter as FileWriterService ?? throw new InvalidOperationException("Certified writer required.");
        SafePathLease? lease=null;
        SafePathLease? backupLease=null;
        FileStream? backupGuard=null;
        void Stage(SafeWriteStage stage)
        { record=record with { Stage=stage }; observe?.Invoke(stage,record); SafeWriteProbe?.Invoke(stage,lease!.TargetPath); }
        SafeWriteResult Finish(SafeWriteOutcome outcome)=>new(outcome,record);
        try
        {
            token.ThrowIfCancellationRequested();
            if(!p.Limits.IsValid || !approval.Approved || !approval.Matches(p) || Encode(p.Before.Encoding,p.DesiredText).Length>p.Limits.MaxTargetFileBytes ||
                Hash(Encode(p.Before.Encoding,p.DesiredText))!=p.DesiredHash ||
                Hash(Encode(p.Before.Encoding,p.Before.Content))!=p.BeforeHash ||
                !ProposalContentMatches(p) || Hash(Encoding.UTF8.GetBytes(p.Diff))!=p.DiffHash)
                return Finish(SafeWriteOutcome.ApprovalDenied);
            record=record with { ApprovalId=approval.ApprovalId };
            p.Workspace.ResolveTarget(p.Target,p.Limits.MaxTargetFileBytes);
            lease=new SafePathLease(p.Workspace.Root!,p.Target);
            Stage(SafeWriteStage.Approved); token.ThrowIfCancellationRequested();
            using(var current=lease.OpenTarget())
            {
                record=record with { ActualHash=FileWriterService.Hash(current) };
                if(record.ActualHash!=p.BeforeHash) return Finish(SafeWriteOutcome.Conflict);
                Stage(SafeWriteStage.PreconditionChecked); token.ThrowIfCancellationRequested();
                if(p.NoOp)
                { record=record with { Verified=true,ActualHash=p.BeforeHash }; Stage(SafeWriteStage.Verified); return Finish(SafeWriteOutcome.NoOp); }
                backup=lease.TargetPath+".nexus-edit-"+p.OperationId.ToString("N")+".bak";
                lease.ValidateDirectories();
                await using(var destination=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous|FileOptions.WriteThrough))
                { current.Position=0; await current.CopyToAsync(destination,token); await destination.FlushAsync(token); destination.Flush(true); }
                if(_snapshotService.ComputeSha256Hash(backup)!=p.BeforeHash) return Finish(SafeWriteOutcome.BackupFailed);
                backupLease=new SafePathLease(p.Workspace.Root!,p.Target+".nexus-edit-"+p.OperationId.ToString("N")+".bak");
                backupGuard=backupLease.OpenTarget();
                record=record with { BackupRef=Path.GetFileName(backup) }; Stage(SafeWriteStage.BackupCreated);
            }
            token.ThrowIfCancellationRequested();
            lease.ValidateDirectories();
            temp=await writer.WriteTempFileAsync(new EditSession { Id=p.OperationId.ToString("N"),TargetPath=lease.TargetPath,ProposedContent=p.DesiredText,Encoding=p.Before.Encoding },token);
            if(_snapshotService.ComputeSha256Hash(temp)!=p.DesiredHash) return Finish(SafeWriteOutcome.TempWriteFailed);
            Stage(SafeWriteStage.TempWritten); token.ThrowIfCancellationRequested();
            Stage(SafeWriteStage.CommitStarted); token.ThrowIfCancellationRequested();
            lease.ValidateDirectories(); authorizeCommit?.Invoke();
            writer.AtomicReplaceChecked(temp,lease.TargetPath,p.BeforeHash,p.DesiredHash,token,()=>{ lease.ValidateDirectories(); authorizeCommit?.Invoke(); });
            temp=null; record=record with { CommitReached=true,ActualHash=null }; Stage(SafeWriteStage.Committed);
            // Commit is durable. Ignore run cancellation for finite verify/recovery;
            // caller still receives cancellation plus truthful operation facts.
            if(FailSafeVerification) throw new IOException("Synthetic read-back verification failure.");
            using(var target=lease.OpenTarget()) record=record with { ActualHash=FileWriterService.Hash(target) };
            if(record.ActualHash!=p.DesiredHash) return Recover();
            record=record with { Verified=true }; Stage(SafeWriteStage.Verified); return Finish(SafeWriteOutcome.Success);
        }
        catch(SafeWriteConflictException) { return Finish(SafeWriteOutcome.Conflict); }
        catch(OperationCanceledException) when(!record.CommitReached) { return Finish(SafeWriteOutcome.Cancelled); }
        catch(Exception)
        {
            if(record.CommitReached) return Recover();
            return Finish(record.Stage switch
            {
                SafeWriteStage.Prepared => SafeWriteOutcome.PathDenied,
                SafeWriteStage.Approved => SafeWriteOutcome.Conflict,
                SafeWriteStage.PreconditionChecked => SafeWriteOutcome.BackupFailed,
                SafeWriteStage.BackupCreated => SafeWriteOutcome.TempWriteFailed,
                _ => SafeWriteOutcome.CommitFailed
            });
        }
        finally
        {
            if(temp is not null && File.Exists(temp))
            { try { lease?.ValidateDirectories(); if(_snapshotService.ComputeSha256Hash(temp)==p.DesiredHash) File.Delete(temp); } catch { } }
            backupGuard?.Dispose(); backupLease?.Dispose();
            lease?.Dispose();
        }
        SafeWriteResult Recover()
        {
            var recovered=RecoverApprovedSafeWrite(p,record,approval,(stage,updated)=>
            { record=updated; observe?.Invoke(stage,updated); SafeWriteProbe?.Invoke(stage,lease!.TargetPath); });
            record=recovered.Operation; return recovered;
        }
    }

    // One existing algorithm reused by immediate verification failure and the
    // source-plan coordinator. Never model-callable, never blanket restore.
    internal SafeWriteResult RecoverApprovedSafeWrite(SafeWriteProposal p,WriteOperationRecord record,WriteApprovalDecision approval,
        Action<SafeWriteStage,WriteOperationRecord>? observe=null)
    {
        if(!approval.Approved || !approval.Matches(p) || !record.CommitReached || record.OperationId!=p.OperationId || record.RunId!=p.RunId ||
            record.Target!=p.Target || record.BeforeHash!=p.BeforeHash || record.DesiredHash!=p.DesiredHash || record.PolicyId!=p.PolicyId || record.ApprovalId!=approval.ApprovalId)
            return new(SafeWriteOutcome.ApprovalDenied,record);
        try
        {
            using var lease=new SafePathLease(p.Workspace.Root!,p.Target);
            record=record with { Stage=SafeWriteStage.Recovering }; observe?.Invoke(SafeWriteStage.Recovering,record);
            using(var target=lease.OpenTarget()) record=record with { ActualHash=FileWriterService.Hash(target) };
            var backup=lease.TargetPath+".nexus-edit-"+p.OperationId.ToString("N")+".bak";
            if(Path.GetFileName(backup)!=record.BackupRef || record.ActualHash!=p.DesiredHash || _snapshotService.ComputeSha256Hash(backup)!=p.BeforeHash)
                return new(SafeWriteOutcome.RecoveryConflict,record with { Recovery=SafeWriteRecovery.Conflict });
            using var backupLease=new SafePathLease(p.Workspace.Root!,p.Target+".nexus-edit-"+p.OperationId.ToString("N")+".bak");
            using var backupGuard=backupLease.OpenTarget();
            if(FileWriterService.Hash(backupGuard)!=p.BeforeHash) return new(SafeWriteOutcome.RecoveryConflict,record with { Recovery=SafeWriteRecovery.Conflict });
            var writer=(FileWriterService)_fileWriter;
            var restore=writer.WriteTempFileAsync(new EditSession { Id=Guid.NewGuid().ToString("N"),TargetPath=lease.TargetPath,ProposedContent=p.Before.Content,Encoding=p.Before.Encoding },CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                writer.AtomicReplaceChecked(restore,lease.TargetPath,p.DesiredHash,p.BeforeHash,CancellationToken.None,lease.ValidateDirectories);
                using var target=lease.OpenTarget(); var actual=FileWriterService.Hash(target);
                record=record with { ActualHash=actual,Recovery=actual==p.BeforeHash ? SafeWriteRecovery.Restored : SafeWriteRecovery.Conflict,Stage=SafeWriteStage.Recovered };
                if(actual!=p.BeforeHash) return new(SafeWriteOutcome.RecoveryConflict,record);
                observe?.Invoke(SafeWriteStage.Recovered,record); return new(SafeWriteOutcome.RecoverySucceeded,record);
            }
            finally { if(File.Exists(restore)) { lease.ValidateDirectories(); if(_snapshotService.ComputeSha256Hash(restore)==p.BeforeHash) File.Delete(restore); } }
        }
        catch(SafeWriteConflictException) { return new(SafeWriteOutcome.RecoveryConflict,record with { Recovery=SafeWriteRecovery.Conflict }); }
        catch { return new(SafeWriteOutcome.VerificationFailed,record with { Recovery=SafeWriteRecovery.Failed }); }
    }

    public FileSnapshot ReadScopedSource(ApprovedTextWorkspace workspace,string target,int maximum=4096)
    {
        if(!workspace.IsSourceScope) throw new ArgumentException("Source scope required.");
        workspace.ResolveTarget(target,maximum); using var lease=new SafePathLease(workspace.Root!,target); using var current=lease.OpenTarget();
        return ReadUtf8(current,lease.TargetPath,maximum);
    }

    private static FileSnapshot ReadUtf8(FileStream current,string path,int maximum)
    {
        if(current.Length>maximum) throw new ArgumentException("Target size denied.");
        var bytes=new byte[(int)current.Length]; current.ReadExactly(bytes);
        if(bytes.Contains((byte)0)) throw new ArgumentException("Binary target denied.");
        var bom=bytes.AsSpan().StartsWith(new byte[] { 0xef,0xbb,0xbf });
        var encoding=new UTF8Encoding(bom,true);
        return new FileSnapshot { AbsolutePath=path,Hash=Hash(bytes),Content=encoding.GetString(bytes,bom ? 3 : 0,bytes.Length-(bom ? 3 : 0)),Encoding=encoding };
    }
    private static byte[] Encode(Encoding encoding,string content)=>encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();
    internal static bool ProposalContentMatches(SafeWriteProposal p)
    {
        if(!p.Workspace.IsSourceScope) return ExactDiff(p.Before.Content,p.DesiredText,p.OldText,p.NewText)==p.Diff;
        try
        {
            var rebuilt=ExactSourceEdits.Resolve(p.Before.Content,p.ResolvedEdits.Select(e=>new SourceTextEdit(e.OldText,e.NewText)),p.Limits.MaxWriteInputBytes);
            return rebuilt.Edits.SequenceEqual(p.ResolvedEdits) && rebuilt.DesiredContent==p.DesiredText &&
                ExactSourceDiff(p.Before.Content,p.DesiredText,rebuilt.Edits)==p.Diff;
        }
        catch(ArgumentException) { return false; }
    }
    internal static string ExactSourceDiff(string before,string desired,IEnumerable<ResolvedSourceTextEdit> edits)=>
        "Exact original-snapshot edits (JSON escaped):\n"+System.Text.Json.JsonSerializer.Serialize(edits)+"\n"+new DiffGeneratorService().CreateDiff(before,desired);
    private static string ExactDiff(string before,string desired,string oldText,string newText)=>
        (before==desired ? "NoOp: no bytes will be changed.\n" : "")+"Exact literal (JSON escaped):\n- "+System.Text.Json.JsonSerializer.Serialize(oldText)+"\n+ "+System.Text.Json.JsonSerializer.Serialize(newText)+
        "\n"+new DiffGeneratorService().CreateDiff(before,desired);
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
