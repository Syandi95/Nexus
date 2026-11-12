namespace Nexus.Engine.ColorManagement;

public interface ICmykProfileManager
{
    IReadOnlyList<CmykProfileInfo> GetBuiltInProfiles();
    IReadOnlyList<CmykProfileInfo> GetCustomProfiles();
    IReadOnlyList<CmykProfileInfo> GetAllProfiles();
    CmykProfileInfo? ValidateProfile(string filePath);
    void SetDefaultProfile(string profileId);
    CmykProfileInfo? GetDefaultProfile();
    bool IsCmykAvailable { get; }
    string? ProfileStorePath { get; }
}
