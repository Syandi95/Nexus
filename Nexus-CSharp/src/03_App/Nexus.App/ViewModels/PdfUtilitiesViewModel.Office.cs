using System.IO;
using System.Windows.Input;
using Nexus.App.Controls.InfoBar;

namespace Nexus.App.ViewModels;

public partial class PdfUtilitiesViewModel
{
    private async Task ConvertOfficeToolAsync()
    {
        var sources=_pdfFiles.Where(x=>x.IsReady).Select(x=>(Item:x,Path:x.Path,Name:x.Name)).ToArray();
        string format=SelectedTool!.Id switch
        {
            PdfToolId.ToWord=>"docx",PdfToolId.ToExcel=>"xlsx",PdfToolId.ToPowerPoint=>"pptx",
            PdfToolId.ImageExport when ExtractEmbeddedImages=>"images",
            _=>throw new InvalidOperationException("Tool tidak didukung.")
        };
        bool multipleSheets=(SelectedToolSettings?.Find("layout") as PdfChoiceSetting)?.Value!="One Sheet";
        string directory;
        try
        {
            directory=Path.GetFullPath(string.IsNullOrWhiteSpace(OutputDirPath)
                ?Path.Combine(Path.GetDirectoryName(sources[0].Path)??"","PDF_Output"):OutputDirPath);
        }
        catch(Exception ex){ShowInfoBar("Folder tidak valid",ex.Message,InfoBarSeverity.Warning);return;}
        var results=sources.Select(x=>new PdfJobResult(x.Path,x.Name,directory)).ToArray();
        _jobResults.Clear();foreach(var result in results)_jobResults.Add(result);
        LastOutputDirectory=directory;HideInfoBar();CanConvert=false;IsConverting=true;HasConverted=false;
        ProgressValue=0;ProgressPctText="0%";IsProgressIndeterminate=true;IsStatusIconSpinning=true;StatusIconGlyph="Sync";
        _conversionCts=new();var token=_conversionCts.Token;
        bool ownsSlot=false;int failed=0;
        try
        {
            StatusText="Menunggu antrean konversi...";
            await ConversionSemaphore.WaitAsync(token);ownsSlot=true;
            for(int i=0;i<sources.Length;i++)
            {
                token.ThrowIfCancellationRequested();
                var source=sources[i];var result=results[i];
                source.Item.StatusText=result.Status="Memproses...";source.Item.StatusForeground=GrayBrush;
                StatusText=$"{source.Name}: {(format=="images"?"mengekstrak gambar tertanam":"mengonversi dokumen")}";
                try
                {
                    string? ocrChoice = (SelectedToolSettings?.Find("ocr") as PdfChoiceSetting)?.Value;
                    var converted = !string.IsNullOrWhiteSpace(ocrChoice) && ocrChoice != "Otomatis (Disarankan)"
                        ? await _officeTools!.ConvertAsync(source.Path, directory, format, multipleSheets, token, ocrChoice)
                        : await _officeTools!.ConvertAsync(source.Path, directory, format, multipleSheets, token);
                    result.Outputs=Array.AsReadOnly(converted.Files.Where(File.Exists).ToArray());
                    foreach(var earlier in results.Take(i))
                    {
                        var retained=earlier.Outputs.Except(result.Outputs,StringComparer.OrdinalIgnoreCase).ToArray();
                        if(retained.Length==earlier.Outputs.Count)continue;
                        earlier.Outputs=Array.AsReadOnly(retained);
                        earlier.Status+="; hasil dengan nama sama ditimpa file berikutnya";
                    }
                    token.ThrowIfCancellationRequested();
                    result.Status=result.HasOutputs?"Berhasil":"Tidak ada gambar tertanam yang berhasil diekstrak";
                    if(converted.Warnings.Count>0)result.Status+=" — "+string.Join("; ",converted.Warnings);
                    source.Item.StatusText=result.Status;source.Item.StatusForeground=result.HasOutputs?GreenBrush:GrayBrush;
                }
                catch(OperationCanceledException){source.Item.StatusText=result.Status="Dibatalkan";throw;}
                catch(Exception ex){failed++;source.Item.StatusText="Gagal";result.Status="Gagal: "+ex.Message;source.Item.StatusForeground=RedBrush;}
                ProgressValue=100.0*(i+1)/sources.Length;ProgressPctText=$"{(int)ProgressValue}%";
            }
            StatusText=failed>0?$"Selesai dengan {failed} file gagal. Periksa hasil per file.":
                results.All(x=>x.HasOutputs)?"Selesai! Semua hasil tersimpan.":"Selesai. Periksa hasil dan keterangan per file.";
            StatusIconGlyph=failed>0?"Error":"Check";
        }
        catch(OperationCanceledException)
        {
            StatusText="Konversi dibatalkan. Hasil yang sudah selesai tetap tersedia.";StatusIconGlyph="Info";
            foreach(var result in results.Where(x=>x.Status=="Menunggu"))result.Status="Belum diproses — pekerjaan dibatalkan";
        }
        finally
        {
            if(ownsSlot)ConversionSemaphore.Release();
            _conversionCts.Dispose();_conversionCts=null;
            HasConverted=results.Any(x=>x.HasOutputs);IsConverting=false;IsProgressIndeterminate=false;IsStatusIconSpinning=false;
            RefreshToolInput();CommandManager.InvalidateRequerySuggested();
        }
    }
}
