namespace Nexus.AI.EditingLite.Exceptions;

public enum EditingFailureCode
{
    // Snapshot
    BinaryFileRejected = 1,
    PathOutsideProjectRoot = 2,
    FileTooLarge = 3,
    FileNotFound = 4,

    // Proposal
    OldTextRequired = 5,
    NoChange = 6,
    OldTextNotFound = 7,
    OldTextNotUnique = 8,

    // Syntax
    SyntaxValidationFailed = 9,
    UnsupportedFileType = 10,

    // FileWriter & Workflow
    BackupFailed = 11,
    RestoreFailed = 12,
    ApplyEditFailed = 13,
    FinalValidationFailed = 14,
    TempFileCreationFailed = 15,
    AtomicReplaceFailed = 16,
    UnauthorizedAccess = 17,
    PathTooLong = 18,
    UnknownError = 19
}

public class EditingException : System.Exception
{
    public EditingFailureCode FailureCode { get; }

    public EditingException(EditingFailureCode failureCode, string message) : base(message)
    {
        FailureCode = failureCode;
    }

    public EditingException(EditingFailureCode failureCode, string message, System.Exception inner) : base(message, inner)
    {
        FailureCode = failureCode;
    }
}
