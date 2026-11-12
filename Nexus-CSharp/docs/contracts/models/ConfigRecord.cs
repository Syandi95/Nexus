using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nexus.Contracts.Models
{
    public record ConfigRecord(
        [property: JsonPropertyName("theme")] string Theme

        // candidate_eval: Bidang "accentColor" tidak pernah dipanggil/digunakan di kode PowerShell (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("accentColor")] string? AccentColor,

        // candidate_eval: Bidang "activeTab" tidak pernah dipanggil/digunakan di kode PowerShell (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("activeTab")] string? ActiveTab,

        // candidate_eval: Bidang "aiProvider" tidak pernah dipanggil/digunakan di kode PowerShell (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("aiProvider")] string? AiProvider,

        // candidate_eval: Bidang "aiModel" tidak pernah dipanggil/digunakan sebagai flat properti di kode PowerShell, hanya ada ai.lastModel (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("aiModel")] string? AiModel,

        // candidate_eval: Bidang "windowWidth" tidak pernah dipanggil/digunakan di kode PowerShell (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("windowWidth")] int? WindowWidth,

        // candidate_eval: Bidang "windowHeight" tidak pernah dipanggil/digunakan di kode PowerShell (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("windowHeight")] int? WindowHeight,

        // candidate_eval: Bidang "recentFiles" tidak pernah dipanggil/digunakan di kode PowerShell (fitur masa depan, DTO diabaikan).
        // [property: JsonPropertyName("recentFiles")] IReadOnlyList<string>? RecentFiles
    );
}
