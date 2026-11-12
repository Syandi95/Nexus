using System.IO;
using System.Reflection;
using System.Windows.Media;
using Nexus.Foundation.Config;

namespace Nexus.Engine.ColorManagement;

public class CmykProfileManager : ICmykProfileManager
{
    private const string BUILTIN_PROFILE = "JapanColor2011Coated.icc";
    private const string PROFILES_SUBDIR = "profiles";

    private readonly IConfigService _configService;
    private readonly string _storePath;
    private readonly List<CmykProfileInfo> _builtIn = new();
    private readonly List<CmykProfileInfo> _custom = new();
    private bool _isCmykAvailable;

    public bool IsCmykAvailable => _isCmykAvailable;
    public string? ProfileStorePath => _storePath;

    public CmykProfileManager(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _storePath = Path.Combine(baseDir, "nexus_data", PROFILES_SUBDIR);
        try { Directory.CreateDirectory(_storePath); } catch { _isCmykAvailable = false; return; }

        InitializeBuiltInProfiles();
        ScanCustomProfiles();
    }

    private void InitializeBuiltInProfiles()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(BUILTIN_PROFILE));

            if (resourceName == null)
            {
                _isCmykAvailable = false;
                return;
            }

            var destPath = Path.Combine(_storePath, BUILTIN_PROFILE);
            if (!File.Exists(destPath))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) { _isCmykAvailable = false; return; }
                using var file = File.Create(destPath);
                stream.CopyTo(file);
            }

            var info = ValidateProfileInternal(destPath, isBuiltIn: true);
            if (info != null)
            {
                _builtIn.Add(info);
                _isCmykAvailable = true;
            }
            else
            {
                _isCmykAvailable = false;
            }
        }
        catch
        {
            _isCmykAvailable = false;
        }
    }

    private void ScanCustomProfiles()
    {
        _custom.Clear();
        if (!Directory.Exists(_storePath)) return;

        foreach (var file in Directory.GetFiles(_storePath, "*.icc"))
        {
            if (Path.GetFileName(file).Equals(BUILTIN_PROFILE, StringComparison.OrdinalIgnoreCase))
                continue;
            var info = ValidateProfileInternal(file);
            if (info != null) _custom.Add(info);
        }

        foreach (var file in Directory.GetFiles(_storePath, "*.icm"))
        {
            var info = ValidateProfileInternal(file);
            if (info != null) _custom.Add(info);
        }
    }

    public IReadOnlyList<CmykProfileInfo> GetBuiltInProfiles() => _builtIn.AsReadOnly();
    public IReadOnlyList<CmykProfileInfo> GetCustomProfiles() => _custom.AsReadOnly();
    public IReadOnlyList<CmykProfileInfo> GetAllProfiles() => _builtIn.Concat(_custom).ToList().AsReadOnly();

    public CmykProfileInfo? ValidateProfile(string filePath)
    {
        return ValidateProfileInternal(filePath);
    }

    private static CmykProfileInfo? ValidateProfileInternal(string filePath, bool isBuiltIn = false)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            if (!IsValidCmykIccProfile(filePath)) return null;

            var ctx = new ColorContext(new Uri(filePath));
            using var stream = ctx.OpenProfileStream();
            if (stream.Length == 0) return null;

            return new CmykProfileInfo
            {
                Id = Path.GetFileName(filePath),
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                IsBuiltIn = isBuiltIn
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidCmykIccProfile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 128) return false;

            var header = new byte[40];
            if (fs.Read(header, 0, 40) < 40) return false;

            var magic = System.Text.Encoding.ASCII.GetString(header, 36, 4);
            if (magic != "acsp") return false;

            var colorSpace = System.Text.Encoding.ASCII.GetString(header, 16, 4);
            return colorSpace == "CMYK";
        }
        catch
        {
            return false;
        }
    }

    public void SetDefaultProfile(string profileId)
    {
        var allProfiles = GetAllProfiles();
        var match = allProfiles.FirstOrDefault(p => p.Id == profileId);
        if (match == null)
            throw new ArgumentException($"Profile with ID '{profileId}' not found.", nameof(profileId));

        var config = _configService.GetConfig();
        config.DefaultCmykProfilePath = match.FilePath;
        _configService.SaveConfig(config);
    }

    public CmykProfileInfo? GetDefaultProfile()
    {
        var config = _configService.GetConfig();

        if (!string.IsNullOrEmpty(config.DefaultCmykProfilePath) && File.Exists(config.DefaultCmykProfilePath))
        {
            var info = ValidateProfileInternal(config.DefaultCmykProfilePath);
            if (info != null) return info;
        }

        return _builtIn.FirstOrDefault();
    }
}
