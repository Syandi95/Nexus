using System.Collections.Generic;

namespace Nexus.Contracts.Models;

public record MessageRecord(
    string Content,
    string Role
);

public record LastSessionRecord(
    List<MessageRecord> Messages,
    string Timestamp,
    string ActiveModel
);
