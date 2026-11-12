using System.Text.Json;
using Nexus.PdfUtilities.OfficeConversion;

if (args.Length < 3 || args[0] is not ("docx" or "xlsx" or "pptx" or "images") ||
    args.Skip(3).Any(a => a != "--control-stdin" && a != "--no-ocr" && a != "--force-ocr" && (a != "--single-sheet" || args[0] != "xlsx")))
{
    Console.Error.WriteLine("Usage: NexusConverterEngine <docx|xlsx|pptx|images> <input.pdf> <output-file-or-images-directory> [--single-sheet (xlsx)] [--no-ocr] [--force-ocr] [--control-stdin]");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
if (args.Contains("--control-stdin"))
    _ = Task.Run(async () =>
    {
        try
        {
            while (await Console.In.ReadLineAsync() is string command)
                if (command == "cancel") { cancellation.Cancel(); return; }
            cancellation.Cancel(); // The owning application disconnected.
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { cancellation.Cancel(); }
    });
try
{
    var input = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args[2]);
    if (!File.Exists(input)) throw new FileNotFoundException("Input PDF was not found.", input);
    if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Input and output must be different paths.");
    if (args[0] != "images" && !string.Equals(Path.GetExtension(output), "." + args[0], StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Output extension must match the requested format.");
    INexusHighFidelityConverter engine = new NexusHighFidelityConverter();
    var options = new NexusConversionOptions();
    if (args.Contains("--no-ocr"))
    {
        options.EnableScannedPageOcr = false;
        options.EnableCurveTextOcr = false;
    }
    else if (args.Contains("--force-ocr"))
    {
        options.EnableScannedPageOcr = true;
        options.EnableCurveTextOcr = true;
    }
    var result = args[0] switch
    {
        "docx" => await engine.ConvertToDocxAsync(input, output, options: options, ct: cancellation.Token),
        "xlsx" => await engine.ConvertToXlsxAsync(input, output, multipleSheets: !args.Contains("--single-sheet"), options: options, ct: cancellation.Token),
        "pptx" => await engine.ConvertToPptxAsync(input, output, options: options, ct: cancellation.Token),
        _ => await engine.ExtractImagesAsync(input, output, ct: cancellation.Token)
    };
    Console.WriteLine(JsonSerializer.Serialize(result));
    return result.IsSuccess ? 0 : 1;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Conversion cancelled."); return 130; }
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
