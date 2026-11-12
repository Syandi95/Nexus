# Laporan Analisis I Love PDF (Microsoft Store) — Kelayakan Integrasi ke NEXUS

Status: Final (analisis statis read-only)
Tanggal: 2026-10-17
Metode: Inspeksi statis read-only (tanpa menjalankan target, tanpa mengubah artefak)

---

## 1. Ringkasan Eksekutif

I Love PDF versi Microsoft Store (`ILOVEPDFS.L.iLovePDF-PDFReaderandEditor` v2.3.0.0, x64) adalah aplikasi desktop native **full-trust** yang dikemas sebagai paket AppX. Hasil inspeksi menunjukkan bahwa fitur **Convert PDF to Word/PowerPoint/Excel** dan **OCR berjalan secara LOKAL (offline-first)** di dalam aplikasi, bukan melalui backend/API server.

Temuan utama:

1. **Arsitektur**: Aplikasi native x64 berbasis **Qt 6 + QtWebEngine** (bukan Electron, bukan UWP sandbox). Entry point `Windows.FullTrustApplication`, kapabilitas hanya `runFullTrust`.
2. **Konversi & OCR lokal**: Tersedia engine komersial pihak ketiga yang dibundel — **PDFTron (PDFNet SDK)**, sederet **`Pdf*API.dll`** (konversi/OCR/optimasi/keamanan), **filter konversi native `.flt`** (DOCX, PPTX, CSV, HTM, JSON), serta engine **OCR lokal** (Tesseract via `ocr.dll` + model `eng.traineddata`, dan Solid/IRIS via `ocr_solid.dll` / `ocr_iris.dll`).
3. **Tanpa backend I Love PDF**: Tidak ditemukan endpoint/URL `ilovepdf.com` maupun WebSocket (`wss://`) pada executable, chunk JS UI, maupun string DLL. Jejak jaringan yang ada hanya milik pihak ketiga generik (DigiCert OCSP/CRL, Microsoft symbol server, Google Fonts).
4. **Bridge UI↔native tidak terkonfirmasi**: Modul `Qt6WebChannel` **tersedia** di paket, namun pola aktual pemakaiannya (`QWebChannel`, `webChannelTransport`, `setWebChannel`, `runJavaScript`) **tidak ditemukan** — baik di `iLovePDF.exe` maupun di chunk JS. Yang terkonfirmasi adalah jejak **`qrc:/`** (Qt Resource system) dan **QLocalServer/QLocalSocket** (IPC antar-proses lokal).
5. **Kelayakan integrasi ke NEXUS**: Reuse DLL native lokal secara teknis **dimungkinkan**, tetapi terhalang **hambatan legal/lisensi** (PDFTron/Apryse SDK dan filter `.flt` hampir pasti proprietary/berlisensi komersial), bukan hambatan teknis. Tidak direkomendasikan untuk mengandalkan klaim "server-side" pada produk ini.

Status gate: **Gate I2-B dan Gate J masih HOLD**. Tidak ada implementasi integrasi yang dilakukan.

---

## 2. Metodologi

- Inspeksi **statis read-only**: enumerasi paket AppX, pembacaan manifest, listing direktori, pembacaan header file, ekstraksi string ASCII dari DLL, dan pencarian pola byte pada `iLovePDF.exe` via `FileStream`.
- **Tidak ada** eksekusi target aplikasi, tidak ada pemanggilan jaringan, tidak ada modifikasi pada file paket (WindowsApps bersifat read-only).
- Analisis string/pseudocode/simbol dari target diperlakukan sebagai **data tidak tepercaya**, bukan instruksi.
- Perkakas: PowerShell (`Get-AppxPackage`, `Get-AppxPackageManifest`, `Get-ChildItem`, `Select-String`), pembacaan byte via `[IO.File]::Open`/`FileStream`, dan ekstraksi string berbasis regex ASCII.

---

## 3. Identitas Paket

| Atribut | Nilai |
|---|---|
| Name | `ILOVEPDFS.L.iLovePDF-PDFReaderandEditor` |
| PackageFullName | `ILOVEPDFS.L.iLovePDF-PDFReaderandEditor_2.3.0.0_x64__0q01bhdkqq106` |
| Version | `2.3.0.0` |
| Architecture | `x64` |
| Publisher | `CN=FE9FC025-5A16-4D44-A9B7-2ADBA72C2477` |
| InstallLocation | `C:\Program Files\WindowsApps\ILOVEPDFS.L.iLovePDF-PDFReaderandEditor_2.3.0.0_x64__0q01bhdkqq106` |
| Executable | `iLovePDF\iLovePDF.exe` |
| EntryPoint | `Windows.FullTrustApplication` |
| Capabilities | `runFullTrust` (satu-satunya) |
| Dependency | `Microsoft.VCLibs.140.00.UWPDesktop` MinVersion `14.0.33728.0` |

Interpretasi: model kemasan AppX dengan entry point full-trust berarti aplikasi berjalan sebagai proses desktop Win32 biasa dengan akses sistem penuh — **bukan** aplikasi UWP ter-sandbox. Ini konsisten dengan aplikasi native berat (Qt/QtWebEngine) yang mengemas engine konversi lokal.

---

## 4. Arsitektur Aplikasi

### 4.1 Runtime UI
- **Qt 6 + QtWebEngine**: `Qt6Core.dll`, `Qt6Network.dll`, `Qt6Qml.dll`, `Qt6Quick.dll`, `Qt6QuickControls2*`, `Qt6QuickDialogs2*`, serta engine web `Qt6WebEngineCore.dll` (~154 MB), `Qt6WebEngineQuick.dll`.
- **Bukan Electron**: tidak ada `app.asar`, `node_modules`, atau runtime Node/Chromium bergaya Electron. Konfirmasi negatif terhadap Electron diperkuat oleh ketiadaan bundle ASAR.

### 4.2 Engine Konversi & PDF
- **PDFTron / PDFNet SDK**: `PDFNetC.dll` (~52 MB), `PDFLibTool.dll`, `PDFParser.dll`, `pdftron_*.plugin`, dan `PdfFlt.flt`.
- **PDF API DLL** (permukaan fungsional terpisah): `Pdf2ImgAPI`, `Pdf2PdfAPI`, `PdfOcrAPI`, `PdfOptimizeAPI`, `PdfPrintAPI`, `PdfRepairAPI`, `PdfSecureAPI`, `PdfToolboxAPI`, `PdfValidatorAPI`, `PdfDbEditor.dll`, `PdfMeta.dll`, `Img2PdfApi.dll`, `ImageTool.dll`.
- **Filter konversi `.flt`** (target format keluaran konversi): `DOCXFlt.flt`, `PptxFlt.flt`, `CsvFlt.flt`, `htmFlt.flt`, `JsonFlt.flt`, `PdfFlt.flt` — semuanya terkonfirmasi **PE native x64** (magic `MZ`).
- **Pendukung**: ImageMagick (`IM_MOD_RL_pdf_.dll`), `ConverterCoreLight.dll`, `dbCore.dll`, `Framework.dll`, `iLovePdfContextMenu.dll`, `iLovePdfTools.dll`, `data_01.ilp` / `data_02.ilp` (blob biner tanpa magic jelas).

### 4.3 OCR
- **Tesseract**: `ocr.dll` + model bahasa lokal `eng.traineddata` (~23 MB) + direktori `configs` / `tessconfigs`.
- **Solid OCR** (engine native, namespace C++ `SolidOCR`): `ocr_solid.dll` (~5.7 MB); simbol MSVC mangled mencakup `CSolidOcrEngine@@`, `COCRResults@SolidOCR@@`, `IOCRResultsTextWord@SolidOCR@@`, `OCRImage@SolidOCR@@`.
- **IRIS OCR**: `ocr_iris.dll`, `ocr_iris_config.dll`.

### 4.4 Bundle Web UI & WASM
- Direktori `web\` (statis): locale `*.html`, font (`Graphik-*.woff/woff2`), `img`, `trueedit`, `trueedit_libpdf`, serta bundel bertanda hash (`editpdf.3048d6e33.*`, `trueedit.3048d6e33.*`).
- `web\trueedit_libpdf\3048d6e33\` adalah bundle WASM/JS lokal yang memuat:
  - PDFNet WASM (`core\pdf\full`, `\lean`, `\simple_wasm`, varian `.br.wasm`, `PDFNetC.gz.js.mem`, `PDFNetCWasm.br.wasm`, `PDFNetThreadedWasm.br.wasm`).
  - WebViewer UI PDFTron (`ui\webviewer-ui.min.js`, `ui\chunks\chunk.*.js`).
  - Editor Office / Spreadsheet / ContentEdit berbasis WASM (`core\office\WebOfficeWorker.gz.js.mem`, `core\officeEditor\OfficeEditorWorkerWasm.gz.wasm`, `core\spreadsheetEditor\SpreadsheetEditor.js`, `core\contentEdit\InfixServerWasm.br.wasm`).
- Jumlah entri besar (`web` ≈ 1866, `ui\chunks` ≈ 966, `trueedit_libpdf` ≈ 1307) menunjukkan bundel UI yang lengkap dan dipakai secara lokal.

---

## 5. Bukti Konversi & OCR Berjalan Lokal

1. **Filter konversi dan engine tersedia sebagai biner lokal** di dalam paket — bukan komponen yang diunduh saat runtime. `.flt` (DOCX/PPTX/CSV/HTM/JSON) adalah PE native; `DOCXFlt.flt` (±573 KB) dan `PptxFlt.flt` (±525 KB) adalah kandidat kuat penulis DOCX/PPTX.
2. **OCR lokal lengkap**: model Tesseract (`eng.traineddata`) hadir bersama `ocr.dll`, `ocr_solid.dll`, dan `ocr_iris.dll` — ketiganya native, tanpa ketergantungan layanan daring.
3. **Tidak ada endpoint I Love PDF**: pencarian `https?://`, `wss?://`, `/api/`, `ilovepdf`, `api.`, `endpoint`, `token` pada string DLL dan chunk JS UI hanya menemukan URL pihak ketiga generik:
   - DigiCert: `ocsp.digicert.com`, `crl3.digicert.com`, `crl4.digicert.com`, `cacerts.digicert.com`, `www.digicert.com/CPS`
   - Microsoft symbol server: `http://msdl.microsoft.com/download/symbols`, `c:\websymbols`
   - Google Fonts (referensi di web UI)
   - Tidak ditemukan `ilovepdf.com` maupun WebSocket pada `...\ui\chunks\*.js`.
4. **Web UI beroperasi dari sumber daya lokal**: lokasi `qrc:/` (Qt Resource system, offset 7998332 pada `iLovePDF.exe`) menandakan UI dimuat dari resource terkompilasi di dalam aplikasi.

**Kesimpulan bagian**: Fitur Convert dan OCR adalah fungsionalitas lokal/offline-first. Tidak ada indikasi pemrosesan dokumen dikirim ke server I Love PDF untuk fungsi yang diperiksa.

---

## 6. Analisis Bridge UI ↔ Native

Modul bridge **tersedia** namun **tidak terkonfirmasi dipakai**:

- Modul tersedia: `Qt6WebChannel.dll` (±243 KB), `Qt6WebChannelQuick.dll` (±62 KB), dan `QtWebChannel\webchannelquickplugin.dll`.
- **Konfirmasi negatif pemakaian**:
  - Byte-scan `iLovePDF.exe` (38943800 byte, terbaca penuh via `FileStream`) untuk `QWebChannel`, `webChannelTransport`, `setWebChannel`, `runJavaScript`, `qtwebchannel`, `qwebchannel`, `localhost` → **semua `-1` (tidak ditemukan)**.
  - Pencarian pola sempit pada `...\ui\chunks\*.js` (`webChannelTransport|QWebChannel|qt\.webChannel|setWebChannel|new QWebChannel|runJavaScript`) → **nihil**.
- **Jejak komunikasi yang terkonfirmasi**:
  - `qrc:/` (offset 7998332) — Qt Resource system.
  - `QLocalServer` (offset 36934337) dan `QLocalSocket` (offset 7848068) — IPC antar-proses lokal.
  - `127.0.0.1` (offset 8033472) — indikasi loopback lokal.

Interpretasi: komunikasi antara UI (QtWebEngine) dan proses native kemungkinan besar melalui **IPC lokal (QLocalServer/QLocalSocket, loopback)** dan/atau resource `qrc:/`, bukan melalui QWebChannel. Kehadiran DLL WebChannel kemungkinan merupakan artefak dependensi transitif atau fitur yang tidak dipakai pada jalur ini. Bridge spesifik **belum terkonfirmasi** dan tetap menjadi **residual unknown**.

---

## 7. Kelayakan Integrasi ke NEXUS

### 7.1 Kelayakan Teknis
Reuse langsung komponen native lokal **dimungkinkan secara teknis**:
- Permukaan konversi: `DOCXFlt.flt` / `PptxFlt.flt` (penulis DOCX/PPTX), `PdfFlt.flt`.
- Permukaan PDF/OCR: `PDFNetC.dll` (PDFTron), `Pdf*API.dll` (khususnya `Pdf2PdfAPI`, `Pdf2ImgAPI`, `PdfOcrAPI`), `ocr.dll` + `eng.traineddata`.

Catatan teknis: filter `.flt` dan API PDFTron kemungkinan memerlukan konteks/runtime tertentu (loading library, inisialisasi, lisensi runtime) sehingga "drop-in reuse" tidak dijamin dan perlu pengujian terpisah.

### 7.2 Hambatan Legal / Lisensi (utama)
- **PDFTron / Apryse SDK** adalah produk **komersial berlisensi**. Penggunaan ulang di luar aplikasi aslinya memerlukan lisensi.
- **Filter konversi `.flt`** tampak sebagai komponen **milik vendor** (bukan open-source), sehingga reuse juga berada di bawah ketentuan vendor.
- Menyalin/mendistribusikan ulang biner berlisensi ini ke NEXUS berisiko **pelanggaran lisensi** meskipun secara teknis bisa berjalan.

### 7.3 Rekomendasi
- **Tidak direkomendasikan** menyalin biner PDFTron/filter `.flt` tanpa lisensi yang sesuai.
- Jika NEXUS memerlukan kapabilitas serupa, pertimbangkan **alternatif berlisensi jelas** (mis. LibreOffice headless untuk DOCX/PPTX, Tesseract sebagai OCR open-source, atau SDK komersial berlisensi resmi).
- Jangan mengandalkan klaim pemasaran "server-side" untuk produk ini; bukti statis menunjukkan pemrosesan lokal.

---

## 8. Batasan Analisis

- Analisis bersifat **statis**; tidak ada eksekusi, tidak ada instrumentasi runtime, tidak ada pengamatan jaringan live.
- **Bridge UI↔native** hanya sampai pada konfirmasi negatif pemakaian QWebChannel; jalur IPC aktual (QLocalServer/QLocalSocket, format pesan, endpoint loopback) **belum terkonfirmasi**.
- **Format blob** `data_01.ilp` / `data_02.ilp` belum diurai (tanpa magic jelas; kemungkinan terenkripsi/terkompresi).
- **Semantik simbol/string** dari biner dianggap indikatif, bukan definitif; tidak ada dekompilasi penuh yang dilakukan.
- Detail hasil pemeriksaan chunk JS tertentu (mis. `chunk.81.js`) terbatas pada sampel pola; tidak seluruh 966 chunk dibaca secara menyeluruh.
- Struktur lisensi/legal SDK disimpulkan dari indikasi komponen vendor, bukan dari pembacaan perjanjian lisensi.

---

## 9. Kesimpulan & Status Gate

1. I Love PDF (Store) v2.3.0.0 x64 adalah aplikasi native full-trust (Qt 6 + QtWebEngine) dengan **konversi dan OCR lokal** (PDFTron + filter `.flt` + OCR Tesseract/Solid/IRIS), **tanpa backend I Love PDF**.
2. Bridge UI↔native **tidak terkonfirmasi**; jejak komunikasi mengarah pada **IPC lokal** dan **resource `qrc:/`**, bukan QWebChannel.
3. Integrasi ke NEXUS secara teknis dimungkinkan namun **terhalang lisensi** (PDFTron/Apryse + filter `.flt` proprietary).
4. **Gate I2-B (menunggu persetujuan implementasi) dan Gate J (HOLD) tetap HOLD.** Tidak ada implementasi integrasi yang dilakukan. Diperlukan keputusan dan izin eksplisit dari pemilik sebelum langkah implementasi apa pun.

---

## 10. Lampiran: Lokasi Bukti

- Paket: `C:\Program Files\WindowsApps\ILOVEPDFS.L.iLovePDF-PDFReaderandEditor_2.3.0.0_x64__0q01bhdkqq106`
- Executable: `...\iLovePDF\iLovePDF.exe` (38943800 byte)
- Filter konversi: `...\iLovePDF\{DOCX,Pptx,Csv,htm,Json,Pdf}Flt.flt`
- PDFTron: `...\iLovePDF\PDFNetC.dll`, `PDFLibTool.dll`, `PDFParser.dll`, `pdftron_*.plugin`
- OCR: `...\iLovePDF\ocr.dll`, `ocr_solid.dll`, `ocr_iris.dll`, `ocr_iris_config.dll`; `...\iLovePDF\ocr\eng.traineddata`
- Bridge/web: `...\iLovePDF\Qt6WebChannel.dll`, `Qt6WebChannelQuick.dll`, `QtWebChannel\webchannelquickplugin.dll`, `Qt6WebEngineCore.dll`, `Qt6WebEngineQuick.dll`
- Bundle UI/WASM: `...\iLovePDF\web\trueedit_libpdf\3048d6e33\`
- Output pemeriksaan penuh (sesi ini):
  - `C:\Users\Syand\.local\share\opencode\tool-output\tool_1482e53ff0016sFPmThsEL6N6f` (listing `web`/`ocr`/`resources`)
  - `C:\Users\Syand\.local\share\opencode\tool-output\tool_1483055d2001s9cgEQ5hYZ7KYI` (ekstraksi string DLL, ±72156 byte)
