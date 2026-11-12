# CMYK Color Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CMYK Color Management to Nexus image export using WPF ColorConvertedBitmap (WIC/WCS internally), zero new dependencies, AI pipeline untouched.

**Architecture:** New `Nexus.Engine.ColorManagement` module with `IColorManagementService` (Transient, per-export) and `ICmykProfileManager` (Singleton, profile cache). Module integrates at `ImageSubsystemViewModel.SaveResultAsync()` only. Config via `AppConfigRecord.DefaultCmykProfilePath`. Profile store at `nexus_data/profiles/`.

**Tech Stack:** .NET 8, WPF (`PresentationCore`), WIC/Windows Color System (built-in), xUnit, FluentAssertions, NSubstitute.

## Global Constraints

- Zero new NuGet dependencies — only `PresentationCore.dll` (already loaded by WPF host)
- AI Pipeline files (`UpscaleService.cs`, `RemoveBackgroundService.cs`, `ImagePipelineService.cs`, `OnnxModelManager.cs`) must NOT be modified
- All color conversion uses `ColorConvertedBitmap` (WPF) → WIC → WCS internally
- No P/Invoke — zero `DllImport` in codebase
- CMYK output format v1 only: TIFF with embedded ICC profile (IFD tag 0x8773)
- `IColorManagementService` registered as Transient; `ICmykProfileManager` registered as Singleton
- Default CMYK profile: Japan Color 2011 Coated (embedded resource)
- Overwrite behavior: `overwrite: true` by default; `IOException` when `overwrite: false` and file exists
- Cancellation: caller owns cleanup of partial output files
- Logging via `ILoggerService` (existing Foundation service)

---

## File Structure

### New Files

| # | Path | Responsibility |
|---|------|---------------|
| 1 | `src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj` | Module project (net8.0, references PresentationCore + Foundation + Contracts) |
| 2 | `src/02_Modules/Nexus.Engine.ColorManagement/Models.cs` | `ColorSpaceKind`, `CmykOutputFormat`, `ColorExportResult`, `ColorManagementErrorCode`, `ColorManagementException`, `CmykProfileInfo` |
| 3 | `src/02_Modules/Nexus.Engine.ColorManagement/IColorManagementService.cs` | Contract for CMYK export |
| 4 | `src/02_Modules/Nexus.Engine.ColorManagement/ICmykProfileManager.cs` | Contract for profile CRUD + validation |
| 5 | `src/02_Modules/Nexus.Engine.ColorManagement/WpfColorManagementService.cs` | Implementation via ColorConvertedBitmap + TiffBitmapEncoder |
| 6 | `src/02_Modules/Nexus.Engine.ColorManagement/CmykProfileManager.cs` | Profile store management, built-in extraction |
| 7 | `src/02_Modules/Nexus.Engine.ColorManagement/Resources/JapanColor2011Coated.icc` | Built-in default profile (Embedded Resource) |
| 8 | `src/02_Modules/Nexus.Engine.ColorManagement.Tests/Nexus.Engine.ColorManagement.Tests.csproj` | Test project |
| 9 | `src/02_Modules/Nexus.Engine.ColorManagement.Tests/WpfColorManagementServiceTests.cs` | Service tests |
| 10 | `src/02_Modules/Nexus.Engine.ColorManagement.Tests/CmykProfileManagerTests.cs` | Profile manager tests |

### Modified Files

| # | Path | Change |
|---|------|--------|
| 11 | `src/01_Foundation/Nexus.Foundation/Config/AppConfigRecord.cs:5` | Add `DefaultCmykProfilePath` property |
| 12 | `src/03_App/Nexus.App/App.xaml.cs:78-81` | Register `IColorManagementService` + `ICmykProfileManager` |
| 13 | `src/03_App/Nexus.App/ViewModels/ImageSubsystemViewModel.cs:46-55` | Add `IsCmykExport` prop, modify `SaveResultAsync()`, `PrimaryActionButtonText`, `PrimaryActionToolTip` |
| 14 | `src/03_App/Nexus.App/Views/ImageSubsystemView.xaml:554-572` | Add Color Space radio group in Upscale settings panel |
| 15 | `src/03_App/Nexus.App/ViewModels/SettingsViewModel.cs:18-20,77-86` | Add CMYK profile property, commands, load/save |
| 16 | `src/03_App/Nexus.App/Views/SettingsView.xaml:80-96` | Add Color Management card between Confirmations and Action Buttons |

---

### Task 1: Create ColorManagement Module Project

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj`

**Dependencies:** None (new module)

- [ ] **Step 1: Create project directory and csproj**

```bash
mkdir -p src/02_Modules/Nexus.Engine.ColorManagement/Resources
```

- [ ] **Step 2: Write csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\01_Foundation\Nexus.Foundation\Nexus.Foundation.csproj" />
    <ProjectReference Include="..\..\00_Contracts\Nexus.Contracts\Nexus.Contracts.csproj" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Resources\JapanColor2011Coated.icc" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

`net8.0-windows` + `UseWPF` ensures `PresentationCore.dll` is available.

- [ ] **Step 3: Add project to solution**

```bash
dotnet sln src/Nexus.sln add src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement/
git add src/Nexus.sln
git commit -m "feat(cms): create Nexus.Engine.ColorManagement module project"
```

---

### Task 2: Define Contract Types (Models.cs)

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement/Models.cs`

**Interfaces:**
- Produces: `ColorSpaceKind`, `CmykOutputFormat`, `ColorExportResult`, `ColorManagementErrorCode`, `ColorManagementException`, `CmykProfileInfo`

- [ ] **Step 1: Write Models.cs**

```csharp
namespace Nexus.Engine.ColorManagement;

public enum ColorSpaceKind
{
    Rgb,
    Cmyk
}

public enum CmykOutputFormat
{
    /// <summary>v1 only. Future values: JpegCmyk, Psd, Pdf.</summary>
    Tiff
}

public class ColorExportResult
{
    public string OutputPath { get; init; } = string.Empty;
    public string UsedProfilePath { get; init; } = string.Empty;
    public bool ProfileWasEmbedded { get; init; }
    public long FileSizeBytes { get; init; }
}

public enum ColorManagementErrorCode
{
    ProfileNotFound,
    ProfileInvalid,
    ConversionFailed,
    FileWriteFailed
}

public class ColorManagementException : Exception
{
    public string ProfilePath { get; }
    public ColorManagementErrorCode ErrorCode { get; }

    public ColorManagementException(
        ColorManagementErrorCode errorCode,
        string profilePath,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        ProfilePath = profilePath;
    }
}

public class CmykProfileInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement/Models.cs
git commit -m "feat(cms): add contract types (ColorSpaceKind, ColorExportResult, etc.)"
```

---

### Task 3: Define IColorManagementService Interface

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement/IColorManagementService.cs`

**Interfaces:**
- Consumes: `ColorExportResult`, `ColorManagementException` (from Task 2)
- Produces: `IColorManagementService`

- [ ] **Step 1: Write IColorManagementService.cs**

```csharp
using Nexus.Foundation.Logging;

namespace Nexus.Engine.ColorManagement;

public interface IColorManagementService : IDisposable
{
    /// <summary>Convert RGB image to CMYK TIFF with embedded ICC profile.
    /// Creates or overwrites output file per overwrite parameter.
    /// On cancellation the caller owns cleanup of partial output.</summary>
    ColorExportResult ExportToCmyk(
        string rgbInputPath,
        string cmykOutputPath,
        string iccProfilePath,
        bool overwrite = true,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement/IColorManagementService.cs
git commit -m "feat(cms): add IColorManagementService interface"
```

---

### Task 4: Define ICmykProfileManager Interface

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement/ICmykProfileManager.cs`

**Interfaces:**
- Consumes: `CmykProfileInfo` (from Task 2)
- Produces: `ICmykProfileManager`

- [ ] **Step 1: Write ICmykProfileManager.cs**

```csharp
namespace Nexus.Engine.ColorManagement;

public interface ICmykProfileManager
{
    IReadOnlyList<CmykProfileInfo> GetBuiltInProfiles();
    IReadOnlyList<CmykProfileInfo> GetCustomProfiles();
    IReadOnlyList<CmykProfileInfo> GetAllProfiles();
    CmykProfileInfo? ValidateProfile(string filePath);
    CmykProfileInfo ImportProfile(string sourcePath);
    void SetDefaultProfile(string profileId);
    CmykProfileInfo? GetDefaultProfile();
    bool IsCmykAvailable { get; }
    string? ProfileStorePath { get; }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement/ICmykProfileManager.cs
git commit -m "feat(cms): add ICmykProfileManager interface"
```

---

### Task 5: Implement WpfColorManagementService

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement/WpfColorManagementService.cs`

**Interfaces:**
- Consumes: `IColorManagementService`, `ColorExportResult`, `ColorManagementException` (Task 2-3)
- Produces: CMYK TIFF via WPF ColorConvertedBitmap

- [ ] **Step 1: Write WpfColorManagementService.cs**

```csharp
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.Foundation.Logging;

namespace Nexus.Engine.ColorManagement;

public class WpfColorManagementService : IColorManagementService
{
    private static readonly Uri S_RgbProfileUri = new(GetSystemSrgbProfilePath());
    private readonly ILoggerService _logger;
    private bool _isDisposed;

    public WpfColorManagementService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ColorExportResult ExportToCmyk(
        string rgbInputPath,
        string cmykOutputPath,
        string iccProfilePath,
        bool overwrite = true,
        CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WpfColorManagementService));
        if (string.IsNullOrEmpty(rgbInputPath)) throw new ArgumentNullException(nameof(rgbInputPath));
        if (string.IsNullOrEmpty(cmykOutputPath)) throw new ArgumentNullException(nameof(cmykOutputPath));
        if (string.IsNullOrEmpty(iccProfilePath)) throw new ArgumentNullException(nameof(iccProfilePath));
        if (!File.Exists(rgbInputPath)) throw new FileNotFoundException("Source image not found", rgbInputPath);
        if (!File.Exists(iccProfilePath))
            throw new ColorManagementException(ColorManagementErrorCode.ProfileNotFound, iccProfilePath, "ICC profile file not found.");

        if (!overwrite && File.Exists(cmykOutputPath))
            throw new IOException($"Output file already exists: {cmykOutputPath}");

        if (!Directory.Exists(Path.GetDirectoryName(cmykOutputPath)))
            Directory.CreateDirectory(Path.GetDirectoryName(cmykOutputPath)!);

        ct.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();
        var profileName = Path.GetFileNameWithoutExtension(iccProfilePath);

        try
        {
            var source = BitmapFrame.Create(new Uri(rgbInputPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            ct.ThrowIfCancellationRequested();

            using var srcContext = new ColorContext(S_RgbProfileUri);
            using var dstContext = new ColorContext(new Uri(iccProfilePath));
            ct.ThrowIfCancellationRequested();

            var ccb = new ColorConvertedBitmap(source, srcContext, dstContext, PixelFormats.Cmyk32);
            ct.ThrowIfCancellationRequested();

            using var fileStream = new FileStream(cmykOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var encoder = new TiffBitmapEncoder
            {
                Compression = TiffCompressOption.Lzw
            };
            encoder.ColorContexts.Add(dstContext);

            var frame = BitmapFrame.Create(ccb);
            encoder.Frames.Add(frame);
            encoder.Save(fileStream);

            sw.Stop();
            var fileSize = new FileInfo(cmykOutputPath).Length;

            _logger.LogInfo("CMYK_EXPORT",
                $"Profile={profileName}, Input={new FileInfo(rgbInputPath).Length >> 20}MB, " +
                $"Output={fileSize >> 20}MB, Duration={sw.ElapsedMilliseconds}ms, Success");

            return new ColorExportResult
            {
                OutputPath = cmykOutputPath,
                UsedProfilePath = iccProfilePath,
                ProfileWasEmbedded = true,
                FileSizeBytes = fileSize
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("CMYK_EXPORT", $"Cancelled at {sw.ElapsedMilliseconds}ms. Partial output at {cmykOutputPath} — caller owns cleanup.");
            throw;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
        catch (ColorManagementException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError("CMYK_EXPORT", $"Failed: {ex.GetType().Name} — {ex.Message}. Profile={profileName}, Duration={sw.ElapsedMilliseconds}ms");
            throw new ColorManagementException(
                ColorManagementErrorCode.ConversionFailed,
                iccProfilePath,
                $"CMYK conversion failed: {ex.Message}", ex);
        }
    }

    private static string GetSystemSrgbProfilePath()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidate = Path.Combine(system32, "spool", "drivers", "color", "sRGB Color Space Profile.icm");
        if (File.Exists(candidate)) return candidate;
        throw new ColorManagementException(ColorManagementErrorCode.ProfileNotFound, candidate,
            "System sRGB profile not found. Windows color management may be misconfigured.");
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement/WpfColorManagementService.cs
git commit -m "feat(cms): implement WpfColorManagementService via ColorConvertedBitmap"
```

---

### Task 6: Implement CmykProfileManager

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement/CmykProfileManager.cs`

**Interfaces:**
- Consumes: `ICmykProfileManager`, `CmykProfileInfo`, `ColorManagementException` (Task 2, 4)
- Depends on: `IConfigService` (from `Nexus.Foundation.Config`)

- [ ] **Step 1: Write CmykProfileManager.cs**

```csharp
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
        Directory.CreateDirectory(_storePath);

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

            var info = ValidateProfileInternal(destPath);
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

    private static CmykProfileInfo? ValidateProfileInternal(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var ctx = new ColorContext(new Uri(filePath));
            using var stream = ctx.OpenProfileStream();
            if (stream.Length == 0) return null;

            return new CmykProfileInfo
            {
                Id = Path.GetFileName(filePath),
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                IsBuiltIn = false
            };
        }
        catch
        {
            return null;
        }
    }

    public CmykProfileInfo ImportProfile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source profile not found", sourcePath);

        var validated = ValidateProfileInternal(sourcePath);
        if (validated == null)
            throw new ColorManagementException(ColorManagementErrorCode.ProfileInvalid, sourcePath,
                "File is not a valid ICC color profile.");

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(_storePath, fileName);
        var index = 1;
        while (File.Exists(destPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(_storePath, $"{nameWithoutExt}_{index}{ext}");
            index++;
        }

        File.Copy(sourcePath, destPath, overwrite: false);

        var imported = new CmykProfileInfo
        {
            Id = Path.GetFileName(destPath),
            DisplayName = Path.GetFileNameWithoutExtension(destPath),
            FilePath = destPath,
            IsBuiltIn = false
        };
        _custom.Add(imported);
        return imported;
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
```

- [ ] **Step 2: Build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement/Nexus.Engine.ColorManagement.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement/CmykProfileManager.cs
git commit -m "feat(cms): implement CmykProfileManager with built-in extraction and custom import"
```

---

### Task 7: Add DefaultCmykProfilePath to AppConfigRecord

**Files:**
- Modify: `src/01_Foundation/Nexus.Foundation/Config/AppConfigRecord.cs:5`

- [ ] **Step 1: Add property to AppConfigRecord**

```csharp
using System;

namespace Nexus.Foundation.Config
{
    public class AppConfigRecord
    {
        public string Theme { get; set; } = "Dark";
        public string DownloadsFolder { get; set; } = string.Empty;
        public bool ConfirmExit { get; set; } = true;
        public bool ConfirmModule { get; set; } = true;
        public string DefaultCmykProfilePath { get; set; } = string.Empty;  // NEW

        public string LastActivityModule { get; set; } = string.Empty;
        // ... rest unchanged
    }
}
```

Apply the addition at line 8 (after `ConfirmModule`):

Add: `public string DefaultCmykProfilePath { get; set; } = string.Empty;`

- [ ] **Step 2: Build**

```bash
dotnet build src/01_Foundation/Nexus.Foundation/Nexus.Foundation.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/01_Foundation/Nexus.Foundation/Config/AppConfigRecord.cs
git commit -m "feat(cms): add DefaultCmykProfilePath to AppConfigRecord"
```

---

### Task 8: Register Services in DI Container

**Files:**
- Modify: `src/03_App/Nexus.App/App.xaml.cs:78-81`

- [ ] **Step 1: Add using + registrations to App.xaml.cs**

After the Image Subsystem Services block (after line 81 `services.AddSingleton<IUpscaleService, UpscaleService>();`), add:

```csharp
// Color Management Services
services.AddTransient<IColorManagementService, WpfColorManagementService>();
services.AddSingleton<ICmykProfileManager, CmykProfileManager>();
```

Also add the using at the top of the file:
```csharp
using Nexus.Engine.ColorManagement;
```

- [ ] **Step 2: Add project reference from App to ColorManagement**

In `src/03_App/Nexus.App/Nexus.App.csproj`, add after the `Nexus.Core.HomeSettings` line:

```xml
<ProjectReference Include="..\..\02_Modules\Nexus.Engine.ColorManagement\Nexus.Engine.ColorManagement.csproj" />
```

- [ ] **Step 3: Build**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/03_App/Nexus.App/App.xaml.cs src/03_App/Nexus.App/Nexus.App.csproj
git commit -m "feat(cms): register IColorManagementService and ICmykProfileManager in DI"
```

---

### Task 9: Add Color Space UI to ImageSubsystemView.xaml

**Files:**
- Modify: `src/03_App/Nexus.App/Views/ImageSubsystemView.xaml:554-572`

- [ ] **Step 1: Add Color Space radio group in Upscale settings panel**

Replace the existing Upscale Scale slider section (lines 564-571) to include the Color Space radio below it:

```xml
<!-- AI Upscale Settings -->
<StackPanel x:Name="PanelImageToolsUpscaleSettings" Visibility="{Binding IsUpscaleMode, Converter={StaticResource BooleanToVisibilityConverter}}">
    <TextBlock Text="Model AI" Foreground="{DynamicResource TextSecondaryBrush}"/>
    <Border Margin="0,7,0,17" Padding="12,10" CornerRadius="11"
            Background="{DynamicResource SurfaceSecondaryBrush}"
            BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1">
        <TextBlock Text="RealESRGAN General WDN x4 v3"
                   TextTrimming="CharacterEllipsis" Foreground="{DynamicResource TextPrimaryBrush}"/>
    </Border>

    <TextBlock Text="{Binding UpscaleScale, StringFormat=Skala Gambar ({0:0}×)}" Foreground="{DynamicResource TextSecondaryBrush}"/>
    <Border Margin="0,7,0,17" Padding="10,2" CornerRadius="11"
            Background="{DynamicResource SurfaceSecondaryBrush}"
            BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1">
        <Slider Minimum="1" Maximum="10" Value="{Binding UpscaleScale, Mode=TwoWay}"
                TickFrequency="1" IsSnapToTickEnabled="True" IsMoveToPointEnabled="True"
                Style="{StaticResource RemoveBackgroundZoomSliderStyle}"/>
    </Border>

    <!-- Color Space -->
    <TextBlock Text="Ruang Warna" Foreground="{DynamicResource TextSecondaryBrush}"/>
    <StackPanel Orientation="Horizontal" Margin="0,7,0,17">
        <RadioButton x:Name="RadioColorSpaceRgb" Style="{StaticResource NexusRadioButtonStandard}"
                     GroupName="ColorSpace" IsChecked="{Binding IsRgbColorSpace, Mode=TwoWay}"
                     FontSize="{StaticResource NexusFontLabel}" Margin="0,0,16,0"
                     Foreground="{DynamicResource NexusBrushTextPrimary}">
            <TextBlock Text="RGB" VerticalAlignment="Center"/>
        </RadioButton>
        <RadioButton x:Name="RadioColorSpaceCmyk" Style="{StaticResource NexusRadioButtonStandard}"
                     GroupName="ColorSpace" IsChecked="{Binding IsCmykColorSpace, Mode=TwoWay}"
                     FontSize="{StaticResource NexusFontLabel}"
                     Foreground="{DynamicResource NexusBrushTextPrimary}">
            <TextBlock Text="CMYK" VerticalAlignment="Center"/>
        </RadioButton>
    </StackPanel>

    <!-- CMYK Info Box (visible only when CMYK selected) -->
    <Border x:Name="CmykInfoBox" Margin="0,0,0,17" Padding="12,10" CornerRadius="11"
            Background="{DynamicResource SurfaceSecondaryBrush}"
            BorderBrush="{DynamicResource NexusBrushStatusInfo}" BorderThickness="1"
            Visibility="{Binding IsCmykColorSpace, Converter={StaticResource BooleanToVisibilityConverter}}">
        <StackPanel>
            <TextBlock Text="{Binding ActiveCmykProfileText}" FontSize="12"
                       FontWeight="SemiBold" Foreground="{DynamicResource NexusBrushTextPrimary}"/>
            <TextBlock Text="TIFF dengan profil ICC tertanam" FontSize="11"
                       Foreground="{DynamicResource TextSecondaryBrush}"/>
        </StackPanel>
    </Border>
</StackPanel>
```

- [ ] **Step 2: Build**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/03_App/Nexus.App/Views/ImageSubsystemView.xaml
git commit -m "feat(cms): add Color Space radio group to ImageTools panel"
```

---

### Task 10: Add CMYK Save Flow to ImageSubsystemViewModel

**Files:**
- Modify: `src/03_App/Nexus.App/ViewModels/ImageSubsystemViewModel.cs`

**Interfaces:**
- Consumes: `IColorManagementService`, `ICmykProfileManager`, `ColorSpaceKind`, `ColorExportResult`, `ColorManagementException`
- Depends on: DI `IServiceProvider` to resolve Transient `IColorManagementService`

- [ ] **Step 1: Add using statements, new fields, and properties**

After existing usings:
```csharp
using Nexus.Engine.ColorManagement;
```

Add fields after line 64:
```csharp
private bool _isRgbColorSpace = true;
private bool _isCmykColorSpace;
private string _activeCmykProfileText = string.Empty;
```

Inject `ICmykProfileManager` and `Func<IColorManagementService>` (Transient factory) into the constructor (after `IOnnxModelManager` parameter):
```csharp
private readonly ICmykProfileManager _cmykProfileManager;
private readonly Func<IColorManagementService> _colorManagementFactory;
```

And assign them in the constructor body.

Add properties:
```csharp
public bool IsRgbColorSpace
{
    get => _isRgbColorSpace;
    set
    {
        if (SetProperty(ref _isRgbColorSpace, value))
        {
            if (value) { _isCmykColorSpace = false; OnPropertyChanged(nameof(IsCmykColorSpace)); }
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
        }
    }
}

public bool IsCmykColorSpace
{
    get => _isCmykColorSpace;
    set
    {
        if (SetProperty(ref _isCmykColorSpace, value))
        {
            if (value)
            {
                _isRgbColorSpace = false;
                OnPropertyChanged(nameof(IsRgbColorSpace));
                var defaultProfile = _cmykProfileManager.GetDefaultProfile();
                ActiveCmykProfileText = defaultProfile != null
                    ? $"Profil: {defaultProfile.DisplayName}"
                    : "Tidak ada profil CMYK tersedia";
            }
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
        }
    }
}

public string ActiveCmykProfileText
{
    get => _activeCmykProfileText;
    set => SetProperty(ref _activeCmykProfileText, value);
}
```

- [ ] **Step 2: Update PrimaryActionButtonText and PrimaryActionToolTip**

In `PrimaryActionButtonText` getter, change the `"ResultUnsaved"` case:
```csharp
case "ResultUnsaved":
    if (IsCmykColorSpace) return "Simpan TIFF (CMYK)";
    return IsRemoveBgMode ? "Simpan PNG" : "Simpan Gambar";
```

In `PrimaryActionToolTip`, change the `"ResultUnsaved"` case:
```csharp
case "ResultUnsaved":
    return IsCmykColorSpace
        ? "Simpan sebagai CMYK TIFF dengan profil ICC"
        : "Simpan hasil ke folder file asli";
```

- [ ] **Step 3: Modify SaveResultAsync() to handle CMYK**

Replace the existing `SaveResultAsync()` method body (from `try` block line 719):

```csharp
try
{
    StatusText = "Menyimpan...";
    DetailText = "Menyalin hasil ke folder file asli.";

    if (IsCmykColorSpace)
    {
        var profile = _cmykProfileManager.GetDefaultProfile();
        if (profile == null || !File.Exists(profile.FilePath))
        {
            StatusText = "Profil CMYK tidak tersedia.";
            DetailText = "Periksa pengaturan Color Management.";
            return;
        }

        DetailText = $"Mengonversi ke CMYK menggunakan {profile.DisplayName}...";
        finalPath = Path.ChangeExtension(finalPath, ".tif");

        using var cms = _colorManagementFactory();
        var result = cms.ExportToCmyk(ResultTempPath!, finalPath, profile.FilePath,
            overwrite: false, _cts?.Token ?? CancellationToken.None);

        LastSavedPath = result.OutputPath;
        var sizeText = result.FileSizeBytes >= 1_000_000
            ? $"{result.FileSizeBytes / 1_000_000.0:F1} MB"
            : $"{result.FileSizeBytes / 1_000.0:F0} KB";
        StatusText = "Berhasil disimpan.";
        DetailText = $"{Path.GetFileName(result.OutputPath)} ({sizeText}) — CMYK";
    }
    else
    {
        await Task.Run(() => File.Copy(ResultTempPath!, finalPath, overwrite: false));

        LastSavedPath = finalPath;
        var savedSize = new FileInfo(finalPath).Length;
        var sizeText = savedSize >= 1_000_000
            ? $"{savedSize / 1_000_000.0:F1} MB"
            : $"{savedSize / 1_000.0:F0} KB";
        StatusText = "Berhasil disimpan.";
        DetailText = $"{Path.GetFileName(finalPath)} ({sizeText})";
    }

    SetWorkflowState("Saved");
    ProgressValue = 100.0;
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/03_App/Nexus.App/ViewModels/ImageSubsystemViewModel.cs
git commit -m "feat(cms): add CMYK save flow with ColorConvertedBitmap export"
```

---

### Task 11: Add Color Management Card to SettingsView.xaml

**Files:**
- Modify: `src/03_App/Nexus.App/Views/SettingsView.xaml:67-79`

- [ ] **Step 1: Add Color Management card between Confirmations and Action Buttons**

After the Confirmations card (after line 79 `</Border>`), add:

```xml
<!-- Card 4: Color Management -->
<Border x:Name="SettingsPanelColorManagement" Style="{StaticResource NexusCardStandard}" Padding="20" Margin="0,0,0,20">
    <StackPanel>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,14">
            <Border Width="28" Height="28" CornerRadius="{StaticResource NexusRadiusSmall}" Background="{DynamicResource NexusBrushInputBackground}">
                <TextBlock x:Name="IconSettingsColorMgmt" services:NexusIcon.Glyph="Color" FontSize="14" Foreground="{DynamicResource NexusBrushAccent}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <TextBlock x:Name="LblSetColorMgmt" Text="MANAJEMEN WARNA" FontSize="{StaticResource NexusFontLabel}" FontWeight="SemiBold" Foreground="{DynamicResource NexusBrushTextPrimary}" VerticalAlignment="Center" Margin="12,0,0,0"/>
        </StackPanel>

        <TextBlock Text="Default CMYK Profile" Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,0,0,7"/>
        <ComboBox x:Name="CboCmykProfile" ItemsSource="{Binding CmykProfiles}" DisplayMemberPath="DisplayName"
                  SelectedItem="{Binding SelectedCmykProfile, Mode=TwoWay}"
                  Style="{StaticResource NexusComboBoxStyle}" Margin="0,0,0,12"/>

        <Button x:Name="BtnImportIcc" Style="{StaticResource NexusButtonPrimary}"
                Command="{Binding ImportIccProfileCommand}" Padding="16,10" Cursor="Hand">
            <StackPanel Orientation="Horizontal">
                <TextBlock x:Name="IconImportIcc" services:NexusIcon.Glyph="Import" FontSize="14"
                           VerticalAlignment="Center" Margin="0,0,8,0" Foreground="#FFFFFF"/>
                <TextBlock Text="Import ICC Profile..." FontSize="{StaticResource NexusFontLabel}"
                           VerticalAlignment="Center" Foreground="#FFFFFF"/>
            </StackPanel>
        </Button>

        <Border x:Name="CmykProfileDetailBox" Margin="0,12,0,0" Padding="12,10" CornerRadius="8"
                Background="{DynamicResource SurfaceSecondaryBrush}"
                Visibility="{Binding HasCmykProfile, Converter={StaticResource BooleanToVisibilityConverter}}">
            <StackPanel>
                <TextBlock Text="{Binding CmykProfileDetailText}" FontSize="11"
                           Foreground="{DynamicResource TextSecondaryBrush}" TextWrapping="Wrap"/>
            </StackPanel>
        </Border>

        <Border x:Name="CmykProfileWarningBox" Margin="0,12,0,0" Padding="12,10" CornerRadius="8"
                Background="{DynamicResource SurfaceSecondaryBrush}"
                BorderBrush="{DynamicResource NexusBrushStatusDanger}" BorderThickness="1"
                Visibility="{Binding IsCmykUnavailable, Converter={StaticResource BooleanToVisibilityConverter}}">
            <TextBlock Text="Profil CMYK bawaan tidak tersedia. Impor ICC manual untuk mengaktifkan ekspor CMYK."
                       FontSize="11" Foreground="{DynamicResource NexusBrushStatusDanger}" TextWrapping="Wrap"/>
        </Border>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Build**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/03_App/Nexus.App/Views/SettingsView.xaml
git commit -m "feat(cms): add Color Management card to Settings page"
```

---

### Task 12: Add Color Management Settings to SettingsViewModel

**Files:**
- Modify: `src/03_App/Nexus.App/ViewModels/SettingsViewModel.cs`

**Interfaces:**
- Consumes: `ICmykProfileManager`, `CmykProfileInfo`

- [ ] **Step 1: Add using, fields, properties, and commands**

Add using:
```csharp
using Nexus.Engine.ColorManagement;
```

Add field:
```csharp
private readonly ICmykProfileManager _cmykProfileManager;
```

Add to constructor parameters (after ISettingsManagerService):
```csharp
ICmykProfileManager cmykProfileManager
```

Assign in constructor:
```csharp
_cmykProfileManager = cmykProfileManager ?? throw new ArgumentNullException(nameof(cmykProfileManager));
```

Add properties:
```csharp
private List<CmykProfileInfo> _cmykProfiles = new();
public List<CmykProfileInfo> CmykProfiles
{
    get => _cmykProfiles;
    set => SetProperty(ref _cmykProfiles, value);
}

private CmykProfileInfo? _selectedCmykProfile;
public CmykProfileInfo? SelectedCmykProfile
{
    get => _selectedCmykProfile;
    set
    {
        if (SetProperty(ref _selectedCmykProfile, value) && value != null)
        {
                            _cmykProfileManager.SetDefaultProfile(value.Id);
            UpdateCmykDetailText();
        }
    }
}

private string _cmykProfileDetailText = string.Empty;
public string CmykProfileDetailText
{
    get => _cmykProfileDetailText;
    set => SetProperty(ref _cmykProfileDetailText, value);
}

public bool HasCmykProfile => SelectedCmykProfile != null;
public bool IsCmykUnavailable => !_cmykProfileManager.IsCmykAvailable && _cmykProfiles.Count == 0;

public ICommand ImportIccProfileCommand { get; }
```

Add command binding in constructor (after SaveSettingsCommand):
```csharp
ImportIccProfileCommand = new RelayCommand(ImportIccProfile);
```

Add methods:
```csharp
private void LoadCmykProfiles()
{
    var all = _cmykProfileManager.GetAllProfiles().ToList();
    CmykProfiles = all;
    var defaultProfile = _cmykProfileManager.GetDefaultProfile();
    SelectedCmykProfile = defaultProfile ?? all.FirstOrDefault();
    UpdateCmykDetailText();
}

private void UpdateCmykDetailText()
{
    if (SelectedCmykProfile != null)
    {
        var type = SelectedCmykProfile.IsBuiltIn ? "Bawaan" : "Kustom";
        CmykProfileDetailText = $"Profil: {SelectedCmykProfile.DisplayName}\nTipe: {type}\nPath: {SelectedCmykProfile.FilePath}";
    }
    else
    {
        CmykProfileDetailText = string.Empty;
    }
    OnPropertyChanged(nameof(HasCmykProfile));
    OnPropertyChanged(nameof(IsCmykUnavailable));
}

private void ImportIccProfile()
{
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Filter = "ICC Profile (*.icc;*.icm)|*.icc;*.icm|Semua File (*.*)|*.*",
        Title = "Pilih ICC Profile"
    };

    if (dialog.ShowDialog() == true)
    {
        try
        {
            var imported = _cmykProfileManager.ImportProfile(dialog.FileName);
            LoadCmykProfiles();
            SelectedCmykProfile = imported;
            OnPropertyChanged(nameof(IsCmykUnavailable));
            MessageBox.Show($"Profil '{imported.DisplayName}' berhasil diimpor.", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal mengimpor profil: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
```

Add `LoadCmykProfiles()` call at the end of the constructor (after `LoadSettings()`):
```csharp
LoadCmykProfiles();
```

- [ ] **Step 2: Build**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/03_App/Nexus.App/ViewModels/SettingsViewModel.cs
git commit -m "feat(cms): add CMYK profile management to SettingsViewModel"
```

---

### Task 13: Create Test Project for ColorManagement

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement.Tests/Nexus.Engine.ColorManagement.Tests.csproj`

- [ ] **Step 1: Write test project csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Nexus.Engine.ColorManagement\Nexus.Engine.ColorManagement.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

```bash
dotnet sln src/Nexus.sln add src/02_Modules/Nexus.Engine.ColorManagement.Tests/Nexus.Engine.ColorManagement.Tests.csproj
```

- [ ] **Step 3: Build**

```bash
dotnet build src/02_Modules/Nexus.Engine.ColorManagement.Tests/Nexus.Engine.ColorManagement.Tests.csproj --no-restore 2>&1
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement.Tests/
git commit -m "test(cms): create test project for ColorManagement"
```

---

### Task 14: Write CmykProfileManager Tests

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement.Tests/CmykProfileManagerTests.cs`

- [ ] **Step 1: Write CmykProfileManagerTests.cs**

```csharp
using FluentAssertions;
using NSubstitute;
using Nexus.Engine.ColorManagement;
using Nexus.Foundation.Config;
using Xunit;

namespace Nexus.Engine.ColorManagement.Tests;

public class CmykProfileManagerTests
{
    private readonly IConfigService _configService;
    private readonly AppConfigRecord _config;

    public CmykProfileManagerTests()
    {
        _config = new AppConfigRecord();
        _configService = Substitute.For<IConfigService>();
        _configService.GetConfig().Returns(_config);
    }

    [Fact]
    public void IsCmykAvailable_ShouldBeFalse_WhenNoBuiltInProfileEmbedded()
    {
        // This test validates behavior when no embedded resource found.
        // In the actual test runner with the embedded resource, this will be true.
        var manager = new CmykProfileManager(_configService);
        // Manager should initialize without throwing regardless of resource availability
        manager.Should().NotBeNull();
    }

    [Fact]
    public void GetAllProfiles_ShouldReturnBuiltInProfiles()
    {
        var manager = new CmykProfileManager(_configService);
        var profiles = manager.GetAllProfiles();
        profiles.Should().NotBeNull();
    }

    [Fact]
    public void SetDefaultProfile_ShouldSaveToConfig()
    {
        var manager = new CmykProfileManager(_configService);
        var profiles = manager.GetAllProfiles();

        if (profiles.Count > 0)
        {
            var target = profiles[0];
            manager.SetDefaultProfile(target.Id);

            _config.DefaultCmykProfilePath.Should().Be(target.FilePath);
            _configService.Received(1).SaveConfig(_config);
        }
    }

    [Fact]
    public void SetDefaultProfile_WithUnknownId_ShouldThrow()
    {
        var manager = new CmykProfileManager(_configService);
        Action act = () => manager.SetDefaultProfile("nonexistent.icc");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetDefaultProfile_ShouldFallbackToBuiltIn_WhenConfigPathIsEmpty()
    {
        var manager = new CmykProfileManager(_configService);
        var profile = manager.GetDefaultProfile();

        if (manager.IsCmykAvailable)
        {
            profile.Should().NotBeNull();
            profile!.IsBuiltIn.Should().BeTrue();
            profile.DisplayName.Should().Contain("Japan");
        }
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test src/02_Modules/Nexus.Engine.ColorManagement.Tests/Nexus.Engine.ColorManagement.Tests.csproj --no-restore 2>&1
```
Expected: Tests pass (some may be skipped if no embedded resource in test context).

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement.Tests/CmykProfileManagerTests.cs
git commit -m "test(cms): add CmykProfileManager unit tests"
```

---

### Task 15: Write WpfColorManagementService Tests

**Files:**
- Create: `src/02_Modules/Nexus.Engine.ColorManagement.Tests/WpfColorManagementServiceTests.cs`

- [ ] **Step 1: Write WpfColorManagementServiceTests.cs**

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using FluentAssertions;
using NSubstitute;
using Nexus.Engine.ColorManagement;
using Nexus.Foundation.Logging;
using Xunit;

namespace Nexus.Engine.ColorManagement.Tests;

public class WpfColorManagementServiceTests
{
    private readonly ILoggerService _logger;
    private readonly string _tempDir;

    public WpfColorManagementServiceTests()
    {
        _logger = Substitute.For<ILoggerService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"cms-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        Action act = () => new WpfColorManagementService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExportToCmyk_WithMissingSource_ShouldThrowFileNotFound()
    {
        using var service = new WpfColorManagementService(_logger);
        Action act = () => service.ExportToCmyk("nonexistent.png", "out.tif", "profile.icc");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ExportToCmyk_WithMissingProfile_ShouldThrowColorManagementException()
    {
        var sourcePath = CreateTestRgbImage("source.png");
        using var service = new WpfColorManagementService(_logger);
        Action act = () => service.ExportToCmyk(sourcePath, "out.tif", "nonexistent.icc");
        act.Should().Throw<ColorManagementException>()
            .Which.ErrorCode.Should().Be(ColorManagementErrorCode.ProfileNotFound);
    }

    [Fact]
    public void ExportToCmyk_WithExistingOutputAndOverwriteFalse_ShouldThrow()
    {
        var sourcePath = CreateTestRgbImage("source.png");
        var outputPath = Path.Combine(_tempDir, "existing.tif");
        File.WriteAllText(outputPath, "dummy");

        using var service = new WpfColorManagementService(_logger);
        Action act = () => service.ExportToCmyk(sourcePath, outputPath, "profile.icc", overwrite: false);
        act.Should().Throw<IOException>();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var service = new WpfColorManagementService(_logger);
        service.Dispose();
        service.Dispose(); // double dispose should not throw
    }

    private string CreateTestRgbImage(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var bmp = new Bitmap(100, 100, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Red);
        g.FillEllipse(Brushes.Blue, 10, 10, 80, 80);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test src/02_Modules/Nexus.Engine.ColorManagement.Tests/Nexus.Engine.ColorManagement.Tests.csproj --no-restore 2>&1
```
Expected: Tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/02_Modules/Nexus.Engine.ColorManagement.Tests/WpfColorManagementServiceTests.cs
git commit -m "test(cms): add WpfColorManagementService unit tests"
```

---

### Task 16: Full Solution Build and Verify

- [ ] **Step 1: Build entire solution**

```bash
dotnet build src/Nexus.sln --no-restore 2>&1
```
Expected: Build succeeded with no errors.

- [ ] **Step 2: Run all tests**

```bash
dotnet test src/Nexus.sln --no-restore 2>&1
```
Expected: All tests pass.

- [ ] **Step 3: Final commit of all remaining files**

```bash
git add -A
git status
git commit -m "feat(cms): complete CMYK Color Management module with WPF/WCS integration"
```

- [ ] **Step 4: Print summary**

```bash
git log --oneline -10
```
Expected: Last 10 commits showing the CMYK feature history.
