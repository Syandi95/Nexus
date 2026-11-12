# CMYK Color Management — Design Document

**Date:** 2026-07-25
**Status:** Approved
**Module:** `Nexus.Engine.ColorManagement`

---

## 1. Objective

Add CMYK Color Management to Nexus image export. AI pipeline remains RGB-only. CMYK conversion occurs exactly once during export, with zero impact on inference.

## 2. Architecture Decisions

### 2.1 Module Separation

```
Nexus.Engine.ColorManagement   ← NEW, independent module
Nexus.Engine.ImageSubsystem    ← UNCHANGED, does not know about ColorManagement
Nexus.App.ViewModels            ← single integration point at save time
Nexus.Foundation.Config        ← + DefaultCmykProfilePath property
```

### 2.2 Engine

WPF `ColorConvertedBitmap` → WIC (`IWICColorTransform`) → WCS/ICM 2.0 internally.

- Zero new NuGet dependencies
- Zero P/Invoke
- Uses `PresentationCore.dll` already loaded by WPF app
- Produces CMYK TIFF with embedded ICC profile (tag 0x8773)

### 2.3 Lifecycle

- `IColorManagementService`: **Transient** — created per CMYK export, disposed immediately after
- `ICmykProfileManager`: **Singleton** — caches profile list, shared across app

### 2.4 Output Format v1

- CMYK → TIFF (`.tif`)
- ICC profile embedded as IFD tag 34675 (InterColorProfile)
- Compression: LZW (balance of size/speed)

### 2.5 Default CMYK Profile

- **Japan Color 2011 Coated** (embedded resource, extracted on first init)
- Suitable for Indonesian offset printing market (Asian paper/ink characteristics)
- User can import custom `*.icc` / `*.icm` profiles via Settings

## 3. Contracts

### Namespace: `Nexus.Engine.ColorManagement`

```csharp
public enum ColorSpaceKind { Rgb, Cmyk }
public enum CmykOutputFormat
{
    /// <summary>Tagged Image File Format — v1 only. Primary format for print-ready CMYK.
    /// Future values may include JpegCmyk, Psd, Pdf.</summary>
    Tiff
}

public record ColorExportResult
{
    public string OutputPath { get; init; }
    public string UsedProfilePath { get; init; }
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
}
```

### Interface: `IColorManagementService`

```csharp
public interface IColorManagementService : IDisposable
{
    /// <summary>Convert RGB image to CMYK TIFF with embedded ICC profile.
    /// Behavior: creates or overwrites the output file. If the operation
    /// is cancelled mid-write, the output file may be incomplete and will
    /// NOT be deleted automatically — the caller owns cleanup.</summary>
    /// <param name="overwrite">If true (default), overwrites existing file.
    /// If false, throws IOException when file exists.</param>
    ColorExportResult ExportToCmyk(
        string rgbInputPath,
        string cmykOutputPath,
        string iccProfilePath,
        bool overwrite = true,
        CancellationToken ct = default);
}
```

### Interface: `ICmykProfileManager`

```csharp
public record CmykProfileInfo
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public string FilePath { get; init; }
    public bool IsBuiltIn { get; init; }
}

public interface ICmykProfileManager
{
    IReadOnlyList<CmykProfileInfo> GetBuiltInProfiles();
    IReadOnlyList<CmykProfileInfo> GetCustomProfiles();
    CmykProfileInfo? ValidateProfile(string filePath);
    CmykProfileInfo ImportProfile(string sourcePath);
    void SetDefaultProfile(string profileId);
    CmykProfileInfo? GetDefaultProfile();
}
```

## 4. Implementation: `WpfColorManagementService`

```
1. BitmapFrame.Create(new Uri(rgbInputPath))
2. ColorContext cmykContext = new ColorContext(new Uri(iccProfilePath))
3. ColorConvertedBitmap ccb = new ColorConvertedBitmap(
       source,
       new ColorContext(PixelFormats.Default),  // sRGB
       cmykContext,
       PixelFormats.Cmyk32)
4. TiffBitmapEncoder encoder = new TiffBitmapEncoder
   {
       Compression = TiffCompressOption.Lzw,
       ColorContexts = { cmykContext }       // → ICC tag 0x8773 embedded
   }
5. encoder.Frames.Add(BitmapFrame.Create(ccb))
6. encoder.Save(fileStream)
```

All WPF imaging objects implement `IDisposable`. Used in a `using` block and released immediately.

## 5. Integration Points

### 5.1 Config (`AppConfigRecord.cs`)

```csharp
public string DefaultCmykProfilePath { get; set; } = string.Empty;
// Empty = use built-in Japan Color 2011 Coated
```

### 5.2 Profile Store

```
nexus_data/profiles/
├── JapanColor2011Coated.icc    (built-in, embedded resource)
└── <custom>.icc                 (imported copies)
```

### 5.3 DI Registration (`App.xaml.cs`)

```csharp
services.AddTransient<IColorManagementService, WpfColorManagementService>();
services.AddSingleton<ICmykProfileManager, CmykProfileManager>();
```

### 5.4 Save Flow (`ImageSubsystemViewModel.SaveResultAsync`)

```
IF ColorSpace == RGB:
    File.Copy(temp.png → destination.png)     ← existing path
ELSE:
    using var cms = sp.GetRequiredService<IColorManagementService>()
    cms.ExportToCmyk(tempPath, destination.tif, profilePath, ct)
    // ICC embedded automatically
```

## 6. UX

### 6.1 Image Tools Panel

- RadioButton: `RGB` (default) / `CMYK`
- Info box: `"TIFF (ICC: Japan Color 2011 Coated)"` (when CMYK selected)
- Primary button: `"Simpan PNG"` / `"Simpan TIFF (CMYK)"`

### 6.2 Settings Page — Color Management Card

- Dropdown: all profiles (built-in + custom)
- "Import ICC Profile..." button (filter: `*.icc;*.icm`)
- Profile details: name, type, path (read-only)

## 7. Files Unchanged

| File | Reason |
|------|--------|
| `UpscaleService.cs` | AI pipeline |
| `RemoveBackgroundService.cs` | AI pipeline |
| `ImagePipelineService.cs` | AI pipeline state machine |
| `OnnxModelManager.cs` | AI model management |
| `AppConfigRecord.cs` | Only 1 property added |
| `ConfigService.cs` | No change required (generic JSON) |
| `SettingsManagerService.cs` | New properties flow through existing pattern |

## 7. Cancellation Strategy

The `CancellationToken` in `ExportToCmyk()` is observed at these boundaries:

1. **Before transform creation** — if cancelled, no file is created
2. **Before encoder.Save()** — if cancelled, no file is written (temp path may be empty)
3. **During encoder.Save()** — WPF `TiffBitmapEncoder.Save()` does NOT natively observe CancellationToken. The entire export runs on a background thread via `Task.Run(...)`. If cancellation is requested during Save():
   - The `Task` will be cancelled on the next observation point
   - The output file may be partially written
   - **Cleanup responsibility**: the caller (`ImageSubsystemViewModel`) owns deleting incomplete output. `ColorManagementService` does not auto-delete on cancellation.
   - Rationale: WIC encoder state is unpredictable mid-write; attempting cleanup inside the service risks secondary exceptions.

## 8. Startup Validation

On first initialization, `CmykProfileManager` extracts built-in profiles from embedded resources to `nexus_data/profiles/`.

```csharp
// Pseudocode
try
{
    ExtractEmbeddedProfile("JapanColor2011Coated.icc");
    ValidateProfile(extractedPath);  // test-open via ColorContext(Uri)
}
catch
{
    // Built-in profile is corrupt or unreadable
    // → Set DefaultCmykProfilePath = string.Empty
    // → CMYK RadioButton is disabled in UI
    // → Settings shows warning: "Profile bawaan rusak. Impor ICC manual."
}
```

**Fallback chain:**
1. Try `DefaultCmykProfilePath` from config → if valid, use it
2. Else try built-in Japan Color → if valid, use it
3. Else set `IsCmykAvailable = false` → UI hides CMYK option
4. Settings page still allows importing a custom profile to restore functionality

## 9. Logging

Using Nexus's existing `ILoggerService`, the `WpfColorManagementService` logs each export:

| Field | Example |
|-------|---------|
| Event | `CMYK_EXPORT` |
| Profile | `JapanColor2011Coated.icc` |
| Input size | `8.2 MB (3840×2160)` |
| Output size | `24.7 MB (3840×2160)` |
| Duration | `312 ms` |
| Result | `Success` / `Failed: ProfileInvalid` |

Logged at `Info` level on success, `Error` level on failure. This is a single call per export — no per-pixel logging, no performance impact.

## 11. Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Corrupt ICC profile | `ICmykProfileManager.ValidateProfile()` test-opens via `ColorContext(Uri)` |
| CMYK gamut narrower than RGB | WCS handles via rendering intent (perceptual by default) |
| Large 8K TIFF slow to save | ~400-1200ms expected; progress bar already exists |
| User cannot open CMYK TIFF | Info text: "Format untuk cetak profesional" |
| Profile file deleted after selection | `ImportProfile()` copies to managed store |

## 12. Performance Estimates

| Resolution | Est. Time | Peak RAM (additional) |
|-----------|-----------|----------------------|
| 2K (2 MP) | 30-80 ms | ~8 MB |
| 4K (8 MP) | 100-300 ms | ~32 MB |
| 8K (33 MP) | 400-1200 ms | ~132 MB |

WIC processes scanline-by-scanline; no full buffer allocation for CMYK output.

## 13. Future Extensions

- CMYK JPEG support (if demand arises)
- Soft-proof preview in Image Tools (RGB simulation of CMYK output)
- Additional built-in profiles (ISO Coated v2, SWOP)
- Batch CMYK export from other modules
