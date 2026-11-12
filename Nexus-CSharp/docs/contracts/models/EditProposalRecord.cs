namespace Nexus.Contracts.Models;

public record DiffRecord(
    bool HasChanges,
    string DiffText,
    int RemovedLines,
    int AddedLines,
    bool Truncated
);

public record SessionRecord(
    string SessionId
);

public record EditProposalRecord(
    int MessageIndex,
    string Status,
    SessionRecord Session,
    string ProjectRoot,
    string TargetPath,
    string OriginalContent,
    string OriginalHash,
    string OldText,
    string NewText,
    string ProposedContent,
    DiffRecord Diff
);
