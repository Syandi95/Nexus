# Convert from PDF — integrasi NEXUS

26 Oktober 2026. Modul terhubung ke PDF Tools melalui `IPdfOfficeToolService` / proses CLI terpisah. Runtime SkiaSharp engine 2.88.8 tetap terpisah dari renderer halaman aplikasi 4.150.1. Tidak ada dependency baru atau project reference lintas runtime.

## Lokasi dan penggunaan

- Source: `D:\NEXUS\Nexus-CSharp\src\02_Modules\Nexus.ConvertFromPdf`
- Proyek: `Nexus.ConvertFromPdf.csproj`; hasil: `bin\Release\net8.0-windows`.
- Paket yang dibaca aplikasi: `<EngineRootFolder>\Convert from PDF\NexusConverterEngine.dll`, default `D:\NEXUS\Engine\Convert from PDF`.
- Jalankan aplikasi melalui `D:\NEXUS\Jalankan_NEXUS.cmd`. Tutup dan buka kembali jika masih memakai versi lama.
- Backup pemasangan: `D:\NEXUS\Engine\NexusConverterEngine\evidence\2026-10-26-pdf-tools-integration\deployment-backup`.

## PDF Tools

PDF ke Word, Excel, dan PowerPoint mengonversi seluruh halaman. Excel menyediakan satu worksheet atau beberapa worksheet. OCR tambahan belum diaktifkan.

Pengaturan PDF to JPG mempunyai dua pilihan vertikal:
1. **Konversi halaman** (default): pengaturan dan renderer JPG/PNG Nexus yang sudah ada, termasuk DPI, kualitas dan pemilihan halaman.
2. **Extract Images**: ekstrak gambar tertanam dari semua halaman PDF ke **PNG** dengan nama `<nama PDF>_img_1.png` dst. Bukan hanya PDF dari PowerPoint. Tidak merender halaman dan tidak mengekstrak ilustrasi vektor sebagai bitmap. PDF tanpa gambar tertanam menampilkan keterangan tanpa menghasilkan gambar palsu.

Hasil dengan nama sama dapat ditimpa setelah konversi file selesai. Gunakan folder keluaran terpisah jika ingin menyimpan beberapa versi. Pembatalan engine bersifat kooperatif; pembacaan PDF tertentu dapat menunda respons.

## CLI dan pemeliharaan

`dotnet "D:\NEXUS\Engine\Convert from PDF\NexusConverterEngine.dll" <docx|xlsx|pptx|images> input.pdf output`

Output adalah file Office atau direktori gambar. Tambahkan `--single-sheet` untuk XLSX satu worksheet. Adapter menggunakan `--control-stdin` dan mengirim `cancel` untuk pembatalan. Exit0 berhasil,1 gagal,2 argumen salah,130 dibatalkan. Periksa JSON `Warnings` dan `CreatedFiles`, bukan exit code saja.

Saat memperbarui paket, backup dahulu; salin seluruh release termasuk dependencies dan `runtimes`, jangan hanya DLL. Jangan menyalin SkiaSharp engine ke folder aplikasi. Source engine lama, RegressionTests dan evidence tetap dipertahankan. Convert to PDF serta Edit PDF tidak diimplementasikan oleh perubahan ini.

## Bukti dan batas

Stage B: 123/123 tes engine, Release0 warning/0 error,12/12 ekspor Excel native; maksimum error batas tabel0.96pt, Y0.25001pt.77 artefak Batch1 dan17 XLSX single-grid GerbangA utuh;31 DOCX+2PPTX juga terjaga. Angka ini berasal dari run StageB yang tercatat, tidak dihitung sebagai run ulang integrasi UI.

Integrasi:9/9 tes baru dan43/43 tes PDF terkait, tanpa gagal/skip;9/9 pemeriksaan CLI empat format ditambah uji CLI single-sheet. Build aplikasi0error,29warning. VSTest gagal mengakses parent process sebelum menjalankan tes; pengujian dijalankan dengan executor xUnit langsung yang mempertahankan assertions, theories dan fixture WPF.

Kontrak cetak teruji memakai Windows/Excel600DPI. DPI/printer lain, fallback metrik Skia, seluruh tema/DPI UI, seluruh tipe gambar PDF dan keseluruhan aplikasi belum disertifikasi. Tidak ada klaim identik100% atau OCR menyeluruh.

Checkpoint: `D:\NEXUS\Engine\NexusConverterEngine\evidence\2026-10-26-pdf-tools-integration\CHECKPOINT.md`.

## Pembaruan 27 Oktober 2026 — gambar dan artwork DOCX

130/130 regresi baru dijalankan (termasuk tujuh sampel lama), Release 0 warning/0 error. Perbaikan: posisi gambar pada halaman artwork, kurva vektor tanpa teks, grid dengan tepi terputus, konflik gambar dengan tabel perkiraan, dan legenda warna vektor. Renderer PDF to JPG tidak berubah.

Maharani kembali 1 halaman dengan 17 bitmap dan 1.615 objek vektor. Kalender tetap 1 halaman dengan 2 gambar dan grid 33 kolom. Tulisan berupa kurva tetap objek vektor, bukan karakter yang dapat diketik. Font, clipping, transparansi dan urutan lapisan kompleks masih memiliki batas.

Checkpoint terbaru: `D:\NEXUS\Engine\NexusConverterEngine\evidence\2026-10-26-user-images\CHECKPOINT.md`. Folder bukti dimulai pada 26 Oktober; finalisasi 27 Oktober. Hasil pengguna disimpan terpisah dalam `accepted-v2\final-outputs`.

## WordArt editable — 27 Oktober 2026
Mode Editable kini memulihkan WordArt dari tag teks PDF yang mempunyai bukti geometri glyph. Maharani terverifikasi 52 WordArt native melalui edit/simpan/buka ulang di Microsoft Word, 1 halaman dan 17 gambar. Font dan efek dekoratif asli belum sepenuhnya pulih; VisualFidelity pada API mempertahankan kurva. 133/133 tes baru dijalankan, Release 0 warning/error. PDF to JPG tetap tidak berubah.
Checkpoint: `D:\NEXUS\Engine\NexusConverterEngine\evidence\2026-10-27-word-semantics\CHECKPOINT.md`.

## Alignment paragraf, tab, font dan daftar Word — 27 Oktober 2026

Engine PDF ke DOCX sekarang menyimpan alignment kiri/tengah/kanan dan justify sebagai paragraf native Word jika geometri sumber cukup jelas. Metadata label tanpa garis memakai tab stop, bukan tabel perkiraan. Numbering desimal dan huruf serta bullet berurutan menggunakan numbering Word native dengan indentasi gantung yang dihitung dari marker dan posisi teks. Font family dengan style suffix umum dikenali dari daftar family terpasang. Batch: 145/145 tes lulus; Release 0 warning/0 error; 77/77 artefak Batch 1 utuh; 10/10 halaman dari lima dokumen sampel mempertahankan isi dan jumlah halaman. PDF ke JPG tidak berubah.

Numbering bertingkat/romawi, daftar lintas halaman, seluruh font yang tidak tersedia di sistem, dan semua bentuk tabel borderless belum dicakup. Geometri hasil tetap bergantung pada kualitas struktur PDF.
Checkpoint: `D:\NEXUS\Engine\NexusConverterEngine\evidence\2026-10-27-paragraphs\CHECKPOINT.md`.
