using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Nexus.App.Services;

public sealed record PdfOfficeToolResult(IReadOnlyList<string> Files, IReadOnlyList<string> Warnings);

public interface IPdfOfficeToolService
{
    bool IsAvailable { get; }
    Task<PdfOfficeToolResult> ConvertAsync(string inputPdf, string outputDirectory, string format,
        bool multipleSheets, CancellationToken token);
    Task<PdfOfficeToolResult> ConvertAsync(string inputPdf, string outputDirectory, string format,
        bool multipleSheets, CancellationToken token, string ocrMode);
}

// Keep the engine's Skia2 runtime separate from the application's existing Skia4 page renderer.
public sealed class PdfOfficeToolService(Func<string> engineDll) : IPdfOfficeToolService
{
    public bool IsAvailable => File.Exists(engineDll());

    public Task<PdfOfficeToolResult> ConvertAsync(string inputPdf, string outputDirectory, string format,
        bool multipleSheets, CancellationToken token)
        => ConvertAsync(inputPdf, outputDirectory, format, multipleSheets, token, "auto");

    public async Task<PdfOfficeToolResult> ConvertAsync(string inputPdf, string outputDirectory, string format,
        bool multipleSheets, CancellationToken token, string ocrMode)
    {
        if (format is not ("docx" or "xlsx" or "pptx" or "images")) throw new ArgumentException("Format tidak didukung.", nameof(format));
        token.ThrowIfCancellationRequested();
        string executable = Path.GetFullPath(engineDll());
        if (!File.Exists(executable)) throw new FileNotFoundException("Modul Convert from PDF belum tersedia di folder Engine.", executable);
        if (!File.Exists(inputPdf)) throw new FileNotFoundException("PDF sumber tidak ditemukan.", inputPdf);
        string destination = Path.GetFullPath(outputDirectory);
        string stem = Path.GetFileNameWithoutExtension(inputPdf);
        // Stage on the destination volume so publishing a completed file is an atomic rename.
        string ownedRoot = destination;
        string work = Path.Combine(ownedRoot, ".nexus-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string staged = Path.Combine(work, format == "images" ? "images" : "output." + format);
            string dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            bool hasProgramFilesDotnet = File.Exists(dotnet);
            var start = new ProcessStartInfo(hasProgramFilesDotnet ? dotnet : "dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (hasProgramFilesDotnet && Path.GetDirectoryName(dotnet) is { Length: > 0 } hostRoot)
            {
                start.Environment["DOTNET_ROOT"] = hostRoot;
            }
            foreach (var argument in new[] { executable, format, Path.GetFullPath(inputPdf), staged, "--control-stdin" })
                start.ArgumentList.Add(argument);
            if (format == "xlsx" && !multipleSheets) start.ArgumentList.Add("--single-sheet");
            if (ocrMode is "none" or "no-ocr" or "Tanpa OCR" or "No OCR") start.ArgumentList.Add("--no-ocr");
            else if (ocrMode is "force" or "force-ocr" or "Paksa OCR" or "OCR") start.ArgumentList.Add("--force-ocr");
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException("Engine konversi tidak dapat dijalankan.");
            using var cancel = token.Register(() =>
            {
                try { process.StandardInput.WriteLine("cancel"); process.StandardInput.Flush(); }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            });
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await stdout, error = await stderr;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode == 130) throw new OperationCanceledException(token);
            if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(error) ? "Konversi gagal." : error.Trim());
            using var json = JsonDocument.Parse(output);
            var result = json.RootElement;
            if (!result.GetProperty("IsSuccess").GetBoolean()) throw new IOException("Engine tidak menghasilkan konversi yang berhasil.");
            var warnings = result.GetProperty("Warnings").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
            var files = result.GetProperty("CreatedFiles").EnumerateArray().Select(x => Path.GetFullPath(x.GetString()!)).ToArray();
            string stagedDirectory = format == "images" ? staged : work;
            foreach (string file in files)
                if (!string.Equals(Path.GetDirectoryName(file), stagedDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetExtension(file), format == "images" ? ".png" : "." + format, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                    throw new IOException("Engine mengembalikan lokasi hasil yang tidak valid.");
            if (format != "images" && (files.Length != 1 || !string.Equals(files[0], staged, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("File Office hasil konversi tidak lengkap.");
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destination);
            var committed = new List<string>();
            foreach (string file in files)
            {
                string name = format == "images" ? stem + "_" + Path.GetFileName(file) : stem + "." + format;
                string target = Path.Combine(destination, name);
                File.Move(file, target, overwrite: true);
                committed.Add(target);
            }
            return new(committed.AsReadOnly(), Array.AsReadOnly(warnings));
        }
        finally
        {
            if (Path.GetFullPath(work).StartsWith(ownedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(work))
                Directory.Delete(work, recursive: true);
        }
    }
}
