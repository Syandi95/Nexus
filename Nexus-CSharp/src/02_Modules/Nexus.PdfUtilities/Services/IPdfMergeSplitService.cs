using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.PdfUtilities;

public interface IPdfMergeSplitService
{
    Task MergePdfAsync(IEnumerable<string> inputPaths, string outputPath, CancellationToken cancellationToken);
    Task SplitPdfAsync(string inputPath, string outputDir, CancellationToken cancellationToken);
}
