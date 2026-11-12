# Output Format & Color Space Design

## Summary

Memisahkan Color Space (RGB/CMYK) dari Output Format (JPEG/PNG/TIFF) pada AI Upscale,
dengan JPEG sebagai format default untuk kedua color space.

---

## 1. Enum: `ImageOutputFormat`

Lokasi: `src/02_Modules/Nexus.Engine.ColorManagement/Models.cs`

Mengganti `CmykOutputFormat` (hanya berisi `Tiff`) menjadi:

```csharp
public enum ImageOutputFormat
{
    Jpeg,
    Png,
    Tiff
}
```

Tidak ada prefix `Cmyk` karena enum ini digunakan untuk RGB maupun CMYK.

---

## 2. Kombinasi Valid

| Color Space | JPEG | PNG | TIFF |
|-------------|:----:|:---:|:----:|
| RGB         |  ✅  |  ✅  |  ❌  |
| CMYK        |  ✅  |  ❌  |  ✅  |

- PNG tidak valid untuk CMYK karena standar PNG tidak mendukung
  citra CMYK secara interoperabel.
- TIFF tidak didukung untuk RGB karena tidak memberikan manfaat
  signifikan untuk target pengguna Nexus dan menjaga pipeline
  tetap sederhana.
- Format default untuk kedua Color Space: **JPEG**.

---

## 3. UI Behavior — ImageSubsystemView.xaml

### 3a. Color Space (tetap)

```
Ruang Warna
(•) RGB   ( ) CMYK
```

### 3b. Output Format (baru, setelah Color Space)

```
Format Output
[ JPEG ▼ ]
```

ComboBox berisi `AvailableOutputFormats` — koleksi dinamis
yang berubah saat Color Space berubah.

### 3c. Info Box CMYK

Tetap ditampilkan hanya saat CMYK aktif, dengan teks deskriptif
profil yang digunakan (tanpa menyebut format spesifik).

### 3d. Saat Color Space berubah

1. `AvailableOutputFormats` di-repopulate:
   - RGB → [JPEG, PNG]
   - CMYK → [JPEG, TIFF]
2. Jika `OutputFormat` yang sedang dipilih TIDAK ADA dalam
   `AvailableOutputFormats` yang baru, pindahkan ke JPEG.
   Contoh: user pilih PNG → pindah ke CMYK → PNG tidak valid →
   otomatis ke JPEG. Contoh lain: user pilih TIFF → pindah ke RGB →
   TIFF tidak valid → otomatis ke JPEG.
3. **Tidak ada perubahan pilihan diam-diam** kecuali format
   sebelumnya benar-benar tidak valid untuk color space baru.

---

## 4. Save Pipeline — `SaveResultAsync`

### 4a. Logika Umum

Tidak lagi menggunakan `IsCmykColorSpace` sebagai penentu format.
Sebaliknya, gunakan `OutputFormat` (enum `ImageOutputFormat`).

### 4b. RGB → JPEG

```text
Load PNG temp → Encode sebagai JPEG → Simpan .jpg
```

### 4c. RGB → PNG

```text
File.Copy(PNG temp → .png)  — sama seperti sekarang
```

### 4d. CMYK → JPEG

```text
RGB PNG temp → ExportToCmyk(format=Jpeg) → .jpg (dengan ICC tertanam)
```

### 4f. CMYK → TIFF

```text
RGB PNG temp → ExportToCmyk(format=Tiff) → .tif (dengan ICC tertanam)
```

### Pipeline CMYK Tunggal

`IColorManagementService.ExportToCmyk()` menerima parameter
`ImageOutputFormat format`:

```csharp
ColorExportResult ExportToCmyk(
    string rgbInputPath,
    string cmykOutputPath,
    string iccProfilePath,
    ImageOutputFormat format,
    bool overwrite = true,
    CancellationToken ct = default);
```

Pipeline:

```text
RGB PNG temp
      ↓
Konversi CMYK (ColorConvertedBitmap) — sama untuk semua format
      ↓
Encoder sesuai format:
  - JPEG → JpegBitmapEncoder dengan ICC metadata
  - TIFF → TiffBitmapEncoder dengan ICC metadata (spt sekarang)
      ↓
Output sesuai format yang dipilih
```

Satu pipeline, satu method, hanya encoder akhir yang berbeda.

### 4e. Naming Convention

```text
{baseName}_upscaled_{scale}x.jpg
{baseName}_upscaled_{scale}x.png
{baseName}_upscaled_{scale}x.tif
```

---

## 5. Remove Background

Tidak berubah. Remove Background tetap menggunakan PNG.

Format Output selector **hanya muncul untuk AI Upscale**.

---

## 6. Daftar File & Tanggung Jawab

| File | Perubahan |
|------|-----------|
| `Models.cs` | Ganti `CmykOutputFormat` → `ImageOutputFormat`, tambah `Jpeg` + `Png` |
| `ImageSubsystemViewModel.cs` | Tambah `OutputFormat`, `AvailableOutputFormats`, logic color space switch, ubah `SaveResultAsync` pakai format enum |
| `ImageSubsystemView.xaml` | Tambah ComboBox Format Output setelah Color Space, hapus teks format spesifik dari info box CMYK |
| `WpfColorManagementService.cs` / `IColorManagementService.cs` | Tambah parameter `ImageOutputFormat format` ke `ExportToCmyk()` |
| `ImageSubsystemViewModelTests.cs` | Tambah test untuk setiap kombinasi RGB/CMYK × JPEG/PNG/TIFF |

---

## 7. Skenario Pengujian

### 7a. UI Validation

1. RGB → `AvailableOutputFormats` = [JPEG, PNG] ✅
2. CMYK → `AvailableOutputFormats` = [JPEG, TIFF] ✅
3. Pilih PNG → Switch ke CMYK → OutputFormat otomatis JPEG ✅
4. Switch balik ke RGB → OutputFormat tetap JPEG (tidak kembali ke PNG) ✅

### 7b. Save Pipeline

1. RGB + JPEG → file `.jpg` tersimpan, valid ✅
2. RGB + PNG → file `.png` tersimpan, valid ✅
3. CMYK + JPEG → file `.jpg` tersimpan dengan ICC ✅
4. CMYK + TIFF → file `.tif` tersimpan dengan ICC ✅

### 7c. Edge Cases

1. Pilih TIFF → switch ke RGB → format pindah ke JPEG →
   switch balik ke CMYK → TIFF tetap tersedia ✅
2. Pilih PNG → switch ke CMYK → format pindah ke JPEG →
   switch balik ke RGB → tetap JPEG (tidak kembali ke PNG) ✅
3. Tidak ada profil CMYK terpilih → CMYK disabled / error message ✅
4. Cancel di tengah penyimpanan → state kembali ✅
