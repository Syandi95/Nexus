# PDF Tools Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign PDF Tools UI to match Image Tools layout, add multi-file support with page-1 preview, while preserving all existing logic (Custom DPI, conversion service, icon system).

**Architecture:** Single-page WPF view with 3-row Grid (header/content/footer). Multi-file managed via `ObservableCollection<PdfFileItem>` in ViewModel. Preview rendered via `Docnet.Core` page-0 thumbnail. All icons via `NexusIcon.Glyph` attached property. Conversion iterates per file through untouched `PdfConversionService`.

**Tech Stack:** WPF (.NET 8), Docnet.Core (PDFium), SkiaSharp, Segoe Fluent Icons, MVVM with RelayCommand

## Global Constraints

- Do NOT modify `PdfConversionService.cs`
- Do NOT modify `NexusIcon.cs`
- Do NOT modify `IConfigService.cs` or `AppConfigRecord.cs`
- Do NOT hardcode Unicode glyphs or emoji in XAML or C#
- All icons must use `services:NexusIcon.Glyph="KeyName"` with existing catalog keys only: `Add`, `Delete`, `Folder`, `PDFPage`, `Settings`, `Check`, `Sync`, `Error`
- Style must reuse Image Tools shared styles: `RemoveBackgroundSettingsTextBoxStyle`, `RemoveBackgroundModernButtonStyle`, `RemoveBackgroundPrimaryActionStyle`, `RemoveBackgroundToolbarButtonStyle`, `RemoveBackgroundToolbarShadow`
- Custom DPI logic (`SelectDpiCommand`, `ApplyCustomDpiCommand`, `IsCustomDpiVisible`, `CustomDpiText`, `CustomDpiButtonText`, `TxtCustomDpi_KeyDown`) must be preserved as-is
- `Docnet.Core` page rendering code pattern from `PdfConversionService.cs:71-81` must be followed for preview
- No parallel processing — files convert sequentially in a loop

---

### Task 1: IPdfEngineService + PdfEngineService — Page Preview

**Files:**
- Modify: `src/02_Modules/Nexus.PdfUtilities/Services/IPdfEngineService.cs`
- Modify: `src/02_Modules/Nexus.PdfUtilities/Services/PdfEngineService.cs`
- Test: build solution to verify

**Interfaces:**
- Consumes: `Docnet.Core` (DocLib, PageDimensions, PageReader, RenderFlags) — already referenced
- Produces: `byte[]?` — raw BGRA pixel bytes for page-1 thumbnail

- [ ] **Step 1: Add `GetPageImage` to interface**

In `IPdfEngineService.cs`, add method signature:

```csharp
byte[]? GetPageImage(string pdfPath, int maxWidth = 200);
```

- [ ] **Step 2: Implement `GetPageImage` in PdfEngineService**

In `PdfEngineService.cs`, add:

```csharp
using SkiaSharp;
using System;
using System.Runtime.InteropServices;

public byte[]? GetPageImage(string pdfPath, int maxWidth = 200)
{
    try
    {
        using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1.0));
        int pageCount = docReader.GetPageCount();
        if (pageCount == 0) return null;

        using var pageReader = docReader.GetPageReader(0);
        var rawBytes = pageReader.GetImage(RenderFlags.RenderAnnotations);
        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();

        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(rawBytes, GCHandleType.Pinned);
        try
        {
            bitmap.InstallPixels(bitmap.Info, handle.AddrOfPinnedObject(), bitmap.RowBytes, delegate { handle.Free(); });

            double scale = Math.Min(1.0, (double)maxWidth / width);
            int newWidth = (int)(width * scale);
            int newHeight = (int)(height * scale);

            using var resized = bitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.Medium);
            if (resized == null) return null;

            return resized.Bytes;
        }
        catch
        {
            if (handle.IsAllocated) handle.Free();
            throw;
        }
    }
    catch
    {
        return null;
    }
}
```

- [ ] **Step 3: Build solution to verify**

```bash
dotnet build src/02_Modules/Nexus.PdfUtilities/Nexus.PdfUtilities.csproj
```

Expected: Build succeeded.

---

### Task 2: PdfUtilitiesViewModel — Multi-file + Format/DPI Simplification

**Files:**
- Modify: `src/03_App/Nexus.App/ViewModels/PdfUtilitiesViewModel.cs`

**Interfaces:**
- Consumes: `IPdfEngineService.GetPageImage()`, `IPdfConversionService.ConvertAsync()`
- Produces: `ObservableCollection<PdfFileItem> PdfFiles`, `PdfFileItem` model class, simplified Format/DPI properties

- [ ] **Step 1: Add `PdfFileItem` model class**

Add at the top of the file (or as nested class inside ViewModel):

```csharp
using System.Windows.Media.Imaging;

public class PdfFileItem : ViewModelBase
{
    private string _path = string.Empty;
    private string _name = string.Empty;
    private string _sizeText = string.Empty;
    private string _pageCountText = string.Empty;
    private string _dateText = string.Empty;
    private string _statusText = string.Empty;
    private Brush _statusForeground = System.Windows.Media.Brushes.Gray;
    private BitmapSource? _previewImage;
    private bool _hasPreviewFailed;

    public string Path { get => _path; set => SetProperty(ref _path, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string SizeText { get => _sizeText; set => SetProperty(ref _sizeText, value); }
    public string PageCountText { get => _pageCountText; set => SetProperty(ref _pageCountText, value); }
    public string DateText { get => _dateText; set => SetProperty(ref _dateText, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public Brush StatusForeground { get => _statusForeground; set => SetProperty(ref _statusForeground, value); }
    public BitmapSource? PreviewImage { get => _previewImage; set => SetProperty(ref _previewImage, value); }
    public bool HasPreviewFailed { get => _hasPreviewFailed; set => SetProperty(ref _hasPreviewFailed, value); }
}
```

- [ ] **Step 2: Replace single-file fields with collection**

In `PdfUtilitiesViewModel`, replace:

```csharp
private string? _selectedPdfPath;
private bool _hasSelectedFile;
private string _fileName = string.Empty;
private string _fileSizeText = string.Empty;
private string _pageCountText = string.Empty;
private string _fileDateText = string.Empty;
private string _fileStatusText = string.Empty;
private Brush _fileStatusForeground = System.Windows.Media.Brushes.Gray;
```

With:

```csharp
private readonly ObservableCollection<PdfFileItem> _pdfFiles = new();
public ReadOnlyObservableCollection<PdfFileItem> PdfFiles { get; }
private int _totalPages;
private string _combinedStatusText = string.Empty;

public int TotalPages { get => _totalPages; set => SetProperty(ref _totalPages, value); }
public string CombinedStatusText { get => _combinedStatusText; set => SetProperty(ref _combinedStatusText, value); }
public bool HasFiles => _pdfFiles.Count > 0;
public bool CanConvert => HasFiles && !IsConverting;
```

Add to constructor:

```csharp
PdfFiles = new ReadOnlyObservableCollection<PdfFileItem>(_pdfFiles);
```

Also remove (or keep but don't use) the old single-file properties since they're referenced by the old XAML. Better approach: keep them as computed from the collection for backward compatibility, or just remove them and update XAML bindings.

Actually, since we're rewriting the XAML entirely, we can remove these old properties and replace with collection-based ones. Remove these properties:
- `SelectedPdfPath`, `HasSelectedFile`, `FileName`, `FileSizeText`, `PageCountText`, `FileDateText`, `FileStatusText`, `FileStatusForeground`

Add new properties:

```csharp
public string FileCountText => $"{_pdfFiles.Count} file{(_pdfFiles.Count != 1 ? "s" : "")}";
```

- [ ] **Step 3: Rewrite `LoadPdfFile` → `AddFiles`**

Replace `LoadPdfFile(string path)` with:

```csharp
public void AddFiles(IEnumerable<string> paths)
{
    foreach (var path in paths)
    {
        if (!File.Exists(path)) continue;
        var ext = Path.GetExtension(path);
        if (!ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) continue;

        try
        {
            var fi = new FileInfo(path);
            int pageCount = _pdfEngineService.GetPageCount(path);

            var item = new PdfFileItem
            {
                Path = path,
                Name = fi.Name,
                SizeText = FormatFileSize(fi.Length),
                PageCountText = $"{pageCount} halaman",
                DateText = fi.LastWriteTime.ToString("dd/MM/yyyy"),
                StatusText = "Siap",
                StatusForeground = (Brush)new BrushConverter().ConvertFromString("#10B981")
            };

            // Load preview thumbnail
            LoadPreview(item);

            _pdfFiles.Add(item);
            TotalPages += pageCount;
        }
        catch (Exception ex)
        {
            var item = new PdfFileItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                StatusText = $"Error: {ex.Message}",
                StatusForeground = System.Windows.Media.Brushes.Red,
                HasPreviewFailed = true
            };
            _pdfFiles.Add(item);
        }
    }

    UpdateCombinedStatus();
    OnPropertyChanged(nameof(HasFiles));
    OnPropertyChanged(nameof(CanConvert));
    OnPropertyChanged(nameof(FileCountText));

    if (string.IsNullOrWhiteSpace(OutputDirPath) && _pdfFiles.Count > 0)
    {
        var dir = Path.GetDirectoryName(_pdfFiles[0].Path) ?? string.Empty;
        OutputDirPath = Path.Combine(dir, "PDF_Output");
    }
}

private void LoadPreview(PdfFileItem item)
{
    try
    {
        var bytes = _pdfEngineService.GetPageImage(item.Path, 200);
        if (bytes != null)
        {
            // Convert byte[] to BitmapSource
            // We need width/height from the bytes — we need to store them or use SKBitmap
            // Actually, let's simplify: use SkiaSharp to decode
            using var bitmap = SKBitmap.Decode(bytes.AsSpan());
            if (bitmap != null)
            {
                using var skImage = SKImage.FromBitmap(bitmap);
                using var data = skImage.Encode(SKEncodedImageFormat.Png, 80);
                var ms = new System.IO.MemoryStream(data.ToArray());
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                item.PreviewImage = bmp;
                item.HasPreviewFailed = false;
                return;
            }
        }
    }
    catch { }

    item.HasPreviewFailed = true;
    item.PreviewImage = null;
}
```

- [ ] **Step 4: Rewrite `SelectFile` for multi-select**

```csharp
private void SelectFile()
{
    var dialog = new OpenFileDialog
    {
        Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
        Title = "Pilih File PDF",
        Multiselect = true
    };

    if (dialog.ShowDialog() == true)
    {
        AddFiles(dialog.FileNames);
    }
}
```

- [ ] **Step 5: Rewrite `RemoveFile` to accept parameter**

```csharp
public void RemoveFile(PdfFileItem item)
{
    if (item == null) return;
    TotalPages -= int.TryParse(item.PageCountText.Split(' ')[0], out var pc) ? pc : 0;
    _pdfFiles.Remove(item);
    UpdateCombinedStatus();
    OnPropertyChanged(nameof(HasFiles));
    OnPropertyChanged(nameof(CanConvert));
    OnPropertyChanged(nameof(FileCountText));

    if (_pdfFiles.Count == 0)
    {
        HasConverted = false;
        IsConverting = false;
        ProgressValue = 0;
        ProgressPctText = "0%";
        StatusText = "Belum ada proses konversi";
    }
}
```

Update `RemoveFileCommand`:
```csharp
RemoveFileCommand = new RelayCommand<PdfFileItem>(RemoveFile);
```

- [ ] **Step 6: Simplify Format properties**

Remove all visual toggle properties. Replace with:

```csharp
private int _selectedFormatIndex;
public int SelectedFormatIndex
{
    get => _selectedFormatIndex;
    set => SetProperty(ref _selectedFormatIndex, value);
}

public string SelectedFormat => SelectedFormatIndex == 0 ? "Jpg" : "Png";

// Remove: IsFormatJpg, IsFormatPng, FormatJpgBackground, FormatJpgForeground, FormatPngBackground, FormatPngForeground
// Remove: SelectFormatJpgCommand, SelectFormatPngCommand
```

- [ ] **Step 7: Simplify DPI properties — PRESERVE custom logic, remove visual helpers**

Keep:
- `SelectedDpi` (int)
- `CustomDpiText` (string)
- `IsCustomDpiVisible` (bool)
- `CustomDpiButtonText` (string)
- `SelectDpiCommand` (RelayCommand<int>)
- `ApplyCustomDpiCommand` (RelayCommand)
- `SelectDpi(int dpi)` method
- `ApplyCustomDpi()` method

Remove:
- `IsDpi72`, `IsDpi144`, `IsDpi216`, `IsDpi300`, `IsDpi600`, `IsDpiCustom`
- `Dpi72Background`, `Dpi72Foreground`, `Dpi144Background`, `Dpi144Foreground`, `Dpi216Background`, `Dpi216Foreground`, `Dpi300Background`, `Dpi300Foreground`, `Dpi600Background`, `Dpi600Foreground`, `DpiCustomBackground`, `DpiCustomForeground`

Add new DPI property for ComboBox:

```csharp
private int _selectedDpiIndex = 1; // default 144
public int SelectedDpiIndex
{
    get => _selectedDpiIndex;
    set
    {
        if (SetProperty(ref _selectedDpiIndex, value))
        {
            // Map ComboBox index to DPI value
            int[] dpiValues = [72, 144, 216, 300, 600, -1];
            if (value >= 0 && value < dpiValues.Length)
            {
                SelectDpi(dpiValues[value]);
            }
        }
    }
}
```

- [ ] **Step 8: Rewrite `ConvertPdfAsync` for multi-file iteration**

```csharp
private async Task ConvertPdfAsync()
{
    if (_pdfFiles.Count == 0) return;

    if (string.IsNullOrWhiteSpace(OutputDirPath))
    {
        var dir = Path.GetDirectoryName(_pdfFiles[0].Path) ?? string.Empty;
        OutputDirPath = Path.Combine(dir, "PDF_Output");
    }

    CanConvert = false;
    IsConverting = true;
    HasConverted = false;
    ProgressValue = 0;
    ProgressPctText = "0%";
    StatusText = "Memulai konversi...";

    int dpiToUse = SelectedDpi == -1 ? (int.TryParse(CustomDpiText, out var cDpi) ? cDpi : 300) : SelectedDpi;
    _conversionCts = new CancellationTokenSource();

    try
    {
        int totalAcrossAll = _pdfFiles.Sum(f =>
        {
            var parts = f.PageCountText.Split(' ');
            return int.TryParse(parts[0], out var pc) ? pc : 0;
        });
        int completedAcrossAll = 0;

        foreach (var file in _pdfFiles)
        {
            var fileProgress = new Progress<PdfConversionProgressReport>(report =>
            {
                double filePercent = report.Percent / 100.0;
                double overall = (completedAcrossAll + filePercent) / totalAcrossAll * 100;
                ProgressValue = overall;
                ProgressPctText = $"{Math.Min(100, (int)overall)}%";
                StatusText = $"{file.Name}: {report.PhaseText}";
            });

            await _pdfConversionService.ConvertAsync(
                file.Path,
                OutputDirPath,
                SelectedFormat,
                dpiToUse,
                QualityValue,
                NamingIndex,
                fileProgress,
                _conversionCts.Token);

            var pageParts = file.PageCountText.Split(' ');
            if (int.TryParse(pageParts[0], out var pCount))
                completedAcrossAll += pCount;

            file.StatusText = "Selesai";
            file.StatusForeground = (Brush)new BrushConverter().ConvertFromString("#10B981");
        }

        ProgressValue = 100;
        ProgressPctText = "100%";
        StatusText = "Selesai! Semua file tersimpan.";
        HasConverted = true;
    }
    catch (OperationCanceledException)
    {
        StatusText = "Konversi dibatalkan.";
    }
    catch (Exception ex)
    {
        StatusText = $"Error: {ex.Message}";
    }
    finally
    {
        IsConverting = true;
        CanConvert = _pdfFiles.Count > 0;
    }
}
```

- [ ] **Step 9: Add `UpdateCombinedStatus` helper**

```csharp
private void UpdateCombinedStatus()
{
    int totalFiles = _pdfFiles.Count;
    int readyCount = _pdfFiles.Count(f => f.StatusText == "Siap");
    int errorCount = _pdfFiles.Count(f => f.StatusText.StartsWith("Error"));

    if (totalFiles == 0)
        CombinedStatusText = "Belum ada file dipilih";
    else if (errorCount > 0)
        CombinedStatusText = $"{totalFiles} file, {errorCount} error";
    else
        CombinedStatusText = $"{totalFiles} file siap dikonversi";

    OnPropertyChanged(nameof(CombinedStatusText));
}
```

- [ ] **Step 10: Build to verify**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj
```

Expected: Build succeeded.

---

### Task 3: PdfUtilitiesView.xaml — Complete UI Redesign

**Files:**
- Modify: `src/03_App/Nexus.App/Views/PdfUtilitiesView.xaml`

**Interfaces:**
- Consumes: All ViewModel properties from Task 2, `NexusIcon` glyphs, shared Image Tools styles

- [ ] **Step 1: Replace entire XAML with new 3-row Grid layout**

```xml
<UserControl x:Class="Nexus.App.Views.PdfUtilitiesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:services="clr-namespace:Nexus.App.Services"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" 
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008" 
             mc:Ignorable="d" 
             d:DesignHeight="800" d:DesignWidth="1100" Foreground="White"
             AllowDrop="True" Drop="View_Drop" DragOver="View_DragOver">

    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- HEADER -->
        <StackPanel Grid.Row="0" Margin="28,20,28,12">
            <TextBlock Text="PDF Tools" FontSize="{StaticResource NexusFontDisplay}" FontWeight="Bold" Foreground="{DynamicResource TextPrimaryBrush}"/>
            <TextBlock Text="Konversi halaman PDF ke format gambar" FontSize="{StaticResource NexusFontBody}" Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,4,0,0"/>
        </StackPanel>

        <!-- MAIN CONTENT -->
        <Grid Grid.Row="1" Margin="28,0,28,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" MinWidth="440"/>
                <ColumnDefinition Width="18"/>
                <ColumnDefinition Width="310"/>
            </Grid.ColumnDefinitions>

            <!-- LEFT: Drop Area / Multi-file Cards -->
            <Border Grid.Column="0"
                    Background="{DynamicResource SurfacePrimaryBrush}"
                    BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1"
                    CornerRadius="14" Padding="16">

                <!-- Drop Zone (shown when no files) -->
                <Border x:Name="DropAreaBorder"
                        Visibility="{Binding HasFiles, Converter={StaticResource BooleanToVisibilityConverter}, ConverterParameter=Inverse}"
                        CornerRadius="12" Padding="30"
                        BorderThickness="2" BorderBrush="{DynamicResource BorderSubtleBrush}"
                        BorderStyle="None"
                        AllowDrop="True"
                        Drop="DropArea_Drop"
                        DragEnter="DropArea_DragEnter"
                        DragOver="DropArea_DragOver"
                        Cursor="Hand">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="*"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>
                        <StackPanel Grid.Row="0" VerticalAlignment="Center" HorizontalAlignment="Center">
                            <TextBlock services:NexusIcon.Glyph="PDFPage" FontSize="64"
                                       Foreground="{DynamicResource TextSecondaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,0,0,20"/>
                            <TextBlock Text="Seret &amp; Letakkan file PDF di sini"
                                       FontSize="{StaticResource NexusFontTitle}" FontWeight="SemiBold"
                                       Foreground="{DynamicResource TextPrimaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,0,0,8"/>
                            <TextBlock Text="atau klik tombol di bawah untuk memilih file"
                                       FontSize="{StaticResource NexusFontBody}"
                                       Foreground="{DynamicResource TextSecondaryBrush}"
                                       HorizontalAlignment="Center"/>
                        </StackPanel>
                        <Button Grid.Row="1" Style="{StaticResource RemoveBackgroundModernButtonStyle}"
                                Command="{Binding SelectFileCommand}"
                                Width="160" HorizontalAlignment="Center" Margin="0,20,0,10" Cursor="Hand">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock services:NexusIcon.Glyph="Add" FontSize="14" Margin="0,0,8,0" VerticalAlignment="Center"/>
                                <TextBlock Text="Pilih File" VerticalAlignment="Center"/>
                            </StackPanel>
                        </Button>
                    </Grid>
                </Border>

                <!-- Multi-file Cards (shown when files exist) -->
                <ScrollViewer Visibility="{Binding HasFiles, Converter={StaticResource BooleanToVisibilityConverter}}"
                              VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <WrapPanel>
                        <ItemsControl ItemsSource="{Binding PdfFiles}" BorderThickness="0" Background="Transparent">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <WrapPanel Orientation="Horizontal"/>
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Width="180" Margin="0,0,16,16"
                                            Background="{DynamicResource SurfacePrimaryBrush}"
                                            BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1"
                                            CornerRadius="14" Padding="0">
                                        <Grid>
                                            <Grid.RowDefinitions>
                                                <RowDefinition Height="130"/>
                                                <RowDefinition Height="Auto"/>
                                            </Grid.RowDefinitions>

                                            <!-- Preview area -->
                                            <Border Grid.Row="0" CornerRadius="14,14,0,0" ClipToBounds="True"
                                                    Background="{DynamicResource SurfaceSecondaryBrush}">
                                                <!-- Preview image if available -->
                                                <Image Source="{Binding PreviewImage}" Stretch="Uniform"
                                                       Visibility="{Binding HasPreviewFailed, Converter={StaticResource BooleanToVisibilityConverter}, ConverterParameter=Inverse}"/>
                                                <!-- Fallback icon -->
                                                <TextBlock services:NexusIcon.Glyph="PDFPage" FontSize="40"
                                                           Foreground="{DynamicResource TextSecondaryBrush}"
                                                           HorizontalAlignment="Center" VerticalAlignment="Center"
                                                           Visibility="{Binding HasPreviewFailed, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                                            </Border>

                                            <!-- Delete button overlay -->
                                            <Border CornerRadius="8" Width="36" Height="36"
                                                    Background="{DynamicResource NexusBrushCardIconBg}"
                                                    HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8"
                                                    Panel.ZIndex="10">
                                                <Button Command="{Binding DataContext.RemoveFileCommand, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type UserControl}}}"
                                                        CommandParameter="{Binding}"
                                                        Background="Transparent" BorderThickness="0" Cursor="Hand"
                                                        ToolTip="Hapus file">
                                                    <TextBlock services:NexusIcon.Glyph="Delete" FontSize="14"
                                                               Foreground="{DynamicResource NexusBrushStatusDanger}"
                                                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                </Button>
                                            </Border>

                                            <!-- File info -->
                                            <StackPanel Grid.Row="1" Margin="12,10,12,12">
                                                <TextBlock Text="{Binding Name}" FontSize="13" FontWeight="SemiBold"
                                                           Foreground="{DynamicResource TextPrimaryBrush}"
                                                           TextTrimming="CharacterEllipsis" Margin="0,0,0,4"/>
                                                <TextBlock FontSize="11" Foreground="{DynamicResource TextSecondaryBrush}"
                                                           TextTrimming="CharacterEllipsis">
                                                    <Run Text="{Binding SizeText}"/><Run Text=" · "/><Run Text="{Binding PageCountText}"/>
                                                </TextBlock>
                                                <TextBlock Text="{Binding StatusText}" FontSize="11" FontWeight="SemiBold"
                                                           Foreground="{Binding StatusForeground}" Margin="0,4,0,0"/>
                                            </StackPanel>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <!-- Add PDF button -->
                        <Button ToolTip="Tambah PDF"
                                Command="{Binding SelectFileCommand}"
                                Width="48" Height="48" Margin="0,0,0,16"
                                Style="{StaticResource RemoveBackgroundToolbarButtonStyle}"
                                VerticalAlignment="Top">
                            <TextBlock services:NexusIcon.Glyph="Add" FontSize="20"
                                       Foreground="{DynamicResource AccentPrimaryBrush}"/>
                        </Button>
                    </WrapPanel>
                </ScrollViewer>
            </Border>

            <!-- RIGHT: Settings Panel -->
            <Border Grid.Column="2"
                    Background="{DynamicResource SurfacePrimaryBrush}"
                    BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1"
                    CornerRadius="18" Padding="20"
                    Effect="{StaticResource RemoveBackgroundToolbarShadow}">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <!-- Settings Header -->
                        <StackPanel Orientation="Horizontal" Margin="0,0,0,20">
                            <Border Width="34" Height="34" CornerRadius="11"
                                    Background="{DynamicResource SurfaceSecondaryBrush}">
                                <TextBlock services:NexusIcon.Glyph="Settings" FontSize="15"
                                           Foreground="{DynamicResource AccentPrimaryBrush}"
                                           HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Border>
                            <TextBlock Text="Pengaturan" FontSize="17" FontWeight="SemiBold"
                                       Foreground="{DynamicResource TextPrimaryBrush}"
                                       VerticalAlignment="Center" Margin="11,0,0,0"/>
                        </StackPanel>

                        <!-- Format Output -->
                        <TextBlock Text="Format Output" Foreground="{DynamicResource TextSecondaryBrush}"/>
                        <ComboBox SelectedIndex="{Binding SelectedFormatIndex, Mode=TwoWay}"
                                  Margin="0,7,0,17"
                                  Style="{StaticResource NexusComboBoxStyle}">
                            <ComboBoxItem Content="JPEG"/>
                            <ComboBoxItem Content="PNG"/>
                        </ComboBox>

                        <!-- Resolusi -->
                        <TextBlock Text="Resolusi" Foreground="{DynamicResource TextSecondaryBrush}"/>
                        <ComboBox SelectedIndex="{Binding SelectedDpiIndex, Mode=TwoWay}"
                                  Margin="0,7,0,17"
                                  Style="{StaticResource NexusComboBoxStyle}">
                            <ComboBoxItem Content="72 DPI"/>
                            <ComboBoxItem Content="144 DPI"/>
                            <ComboBoxItem Content="216 DPI"/>
                            <ComboBoxItem Content="300 DPI"/>
                            <ComboBoxItem Content="600 DPI"/>
                            <ComboBoxItem Content="Kustom..."/>
                        </ComboBox>

                        <!-- Custom DPI TextBox (visible when "Kustom..." selected) -->
                        <TextBox Text="{Binding CustomDpiText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                 Style="{StaticResource RemoveBackgroundSettingsTextBoxStyle}"
                                 Visibility="{Binding IsCustomDpiVisible, Converter={StaticResource BooleanToVisibilityConverter}}"
                                 Margin="0,-10,0,17"
                                 KeyDown="TxtCustomDpi_KeyDown"/>

                        <!-- Kualitas JPEG -->
                        <Grid Margin="0,7,0,7">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="Kualitas JPEG"
                                       Foreground="{DynamicResource TextSecondaryBrush}"/>
                            <TextBlock Grid.Column="1" Text="{Binding QualityText}"
                                       FontWeight="Bold" Foreground="{DynamicResource AccentPrimaryBrush}"/>
                        </Grid>
                        <Border Margin="0,0,0,17" Padding="10,2" CornerRadius="11"
                                Background="{DynamicResource SurfaceSecondaryBrush}"
                                BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1">
                            <Slider Minimum="10" Maximum="100" Value="{Binding QualityValue, Mode=TwoWay}"
                                    TickFrequency="10" IsSnapToTickEnabled="True" IsMoveToPointEnabled="True"
                                    Style="{StaticResource NexusSliderStandard}" Height="24"/>
                        </Border>

                        <!-- Folder Output -->
                        <TextBlock Text="Folder Output" Foreground="{DynamicResource TextSecondaryBrush}"/>
                        <Grid Margin="0,7,0,17">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBox Text="{Binding OutputDirPath, Mode=TwoWay}" IsReadOnly="True"
                                     Style="{StaticResource RemoveBackgroundSettingsTextBoxStyle}"/>
                            <Button Grid.Column="1" Margin="8,0,0,0"
                                    Command="{Binding BrowseOutputDirCommand}"
                                    ToolTip="Pilih Folder Output"
                                    Style="{StaticResource RemoveBackgroundToolbarButtonStyle}">
                                <TextBlock services:NexusIcon.Glyph="Folder" FontSize="16"/>
                            </Button>
                        </Grid>

                        <!-- Penamaan File -->
                        <TextBlock Text="Penamaan File" Foreground="{DynamicResource TextSecondaryBrush}"/>
                        <ComboBox SelectedIndex="{Binding NamingIndex, Mode=TwoWay}"
                                  Margin="0,7,0,24"
                                  Style="{StaticResource NexusComboBoxStyle}">
                            <ComboBoxItem Content="Nama Asli + Nomor Halaman"/>
                            <ComboBoxItem Content="Nama Asli Saja"/>
                            <ComboBoxItem Content="Nomor Halaman Saja"/>
                            <ComboBoxItem Content="Prefix + Nama Asli + Nomor"/>
                        </ComboBox>

                        <!-- Convert Button -->
                        <Button Content="{Binding ConvertButtonText}"
                                Command="{Binding ConvertCommand}"
                                IsEnabled="{Binding CanConvert}"
                                Style="{StaticResource RemoveBackgroundPrimaryActionStyle}"
                                Height="44" FontSize="14" FontWeight="SemiBold" Cursor="Hand"/>
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Grid>

        <!-- FOOTER STATUS BAR -->
        <Border Grid.Row="2" Margin="28,0,28,24" Padding="18,14"
                Background="{DynamicResource SurfacePrimaryBrush}"
                BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1"
                CornerRadius="18"
                Effect="{StaticResource RemoveBackgroundToolbarShadow}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <!-- Status -->
                <Grid VerticalAlignment="Center">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Border Width="38" Height="38" CornerRadius="12"
                            Background="{DynamicResource SurfaceSecondaryBrush}" Margin="0,0,12,0">
                        <TextBlock services:NexusIcon.Glyph="Info" FontSize="16"
                                   Foreground="{DynamicResource AccentPrimaryBrush}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                        <TextBlock Text="{Binding StatusText}" FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextPrimaryBrush}"/>
                        <TextBlock Text="{Binding CombinedStatusText}"
                                   Margin="0,3,0,0" FontSize="11"
                                   Foreground="{DynamicResource TextSecondaryBrush}"
                                   TextTrimming="CharacterEllipsis"/>
                    </StackPanel>
                </Grid>

                <!-- Progress Bar -->
                <ProgressBar Grid.Column="1" Margin="18,0" VerticalAlignment="Center"
                             Value="{Binding ProgressValue}" Minimum="0" Maximum="100">
                    <ProgressBar.Style>
                        <Style TargetType="ProgressBar" BasedOn="{StaticResource RemoveBackgroundProgressStyle}">
                            <Setter Property="Visibility" Value="Collapsed"/>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsConverting}" Value="True">
                                    <Setter Property="Visibility" Value="Visible"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </ProgressBar.Style>
                </ProgressBar>

                <!-- Action Buttons -->
                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Content="Buka Folder" Margin="0,0,10,0"
                            Command="{Binding OpenOutputDirCommand}"
                            ToolTip="Buka folder hasil"
                            Style="{StaticResource RemoveBackgroundModernButtonStyle}"/>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

Note: For the inverse BooleanToVisibilityConverter, we need to either create one or handle it differently. The simplest approach: since the `HasFiles` bool controls drop zone vs. cards, we can use a `DataTrigger` or property negation. Let's add a `HasNoFiles` computed property in the ViewModel:

```csharp
public bool HasNoFiles => _pdfFiles.Count == 0;
```

Then bind drop zone to `HasNoFiles` and cards area to `HasFiles`.

Similarly for `HasPreviewFailed` inverse visibility — we can add `PreviewAvailable` property:

```csharp
// In PdfFileItem:
public bool PreviewAvailable => !HasPreviewFailed;
```

- [ ] **Step 2: Add inverse visibility properties**

In ViewModel, add:
```csharp
public bool HasNoFiles => _pdfFiles.Count == 0;
```

Update `AddFiles` and `RemoveFile` to notify:
```csharp
OnPropertyChanged(nameof(HasNoFiles));
```

In `PdfFileItem`, add:
```csharp
public bool PreviewAvailable => !HasPreviewFailed;
```

And update `HasPreviewFailed` setter to also notify:
```csharp
public bool HasPreviewFailed
{
    get => _hasPreviewFailed;
    set
    {
        if (SetProperty(ref _hasPreviewFailed, value))
            OnPropertyChanged(nameof(PreviewAvailable));
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj
```

Expected: Build succeeded.

---

### Task 4: PdfUtilitiesView.xaml.cs — Drag & Drop Multi-file

**Files:**
- Modify: `src/03_App/Nexus.App/Views/PdfUtilitiesView.xaml.cs`

- [ ] **Step 1: Update Drop handler for multi-file**

```csharp
private void DropArea_Drop(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null && files.Length > 0 && DataContext is PdfUtilitiesViewModel vm)
        {
            var pdfFiles = files.Where(f => f.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pdfFiles.Length > 0)
            {
                vm.AddFiles(pdfFiles);
            }
        }
    }
    e.Handled = true;
}
```

Also add View-level drop handler for the outer UserControl:

```csharp
private void View_Drop(object sender, DragEventArgs e)
{
    DropArea_Drop(sender, e);
}

private void View_DragOver(object sender, DragEventArgs e)
{
    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj
```

Expected: Build succeeded.

---

### Task 5: SharedStyles.xaml — Ensure Image Tools Styles Are Reusable

**Files:**
- Modify: `src/03_App/Nexus.App/Themes/SharedStyles.xaml` (if styles not already shared)

- [ ] **Step 1: Verify styles are accessible**

The styles `RemoveBackgroundSettingsTextBoxStyle`, `RemoveBackgroundModernButtonStyle`, `RemoveBackgroundPrimaryActionStyle`, `RemoveBackgroundToolbarButtonStyle`, `RemoveBackgroundToolbarShadow`, `RemoveBackgroundProgressStyle` are defined locally in `ImageSubsystemView.xaml`. If they are NOT in SharedStyles.xaml, they need to be moved.

Check if these styles exist in SharedStyles.xaml:

If they DON'T exist there, move them from ImageSubsystemView.xaml to SharedStyles.xaml so both views can reuse them.

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/03_App/Nexus.App/Nexus.App.csproj
```

Expected: Build succeeded.

---

### Task 6: Verification

**Files:**
- No file changes — build + test only

- [ ] **Step 1: Full solution build**

```bash
dotnet build src/Nexus.sln
```

Expected: Build succeeded with no errors.

- [ ] **Step 2: Visual acceptance checklist**

Verify:
- [ ] Drop zone shows when no files selected
- [ ] Multi-file selection works (both dialog and drag-drop)
- [ ] Each file shows as a card with preview thumbnail or PDFPage fallback
- [ ] Delete button on each card removes only that file
- [ ] Add PDF button opens file picker
- [ ] Settings panel has Image Tools styling (radius 18, width 310, spacing 0,7,0,17)
- [ ] Format Output ComboBox switches JPEG/PNG
- [ ] Resolusi ComboBox selects DPI values and "Kustom..." shows TextBox
- [ ] Custom DPI TextBox accepts Enter and validates (72-2400)
- [ ] Kualitas JPEG slider works
- [ ] Folder Output has icon button browse
- [ ] Penamaan File ComboBox works
- [ ] Convert button is in settings panel, styled as primary action
- [ ] Footer shows status + progress bar (during conversion) + Buka Folder
- [ ] All icons use NexusIcon.Glyph (no hardcoded glyphs)
- [ ] Conversion processes files sequentially with progress
- [ ] After conversion, status shows completion
- [ ] Buka Folder opens output directory