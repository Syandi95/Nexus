using System.Collections.Generic;

namespace Nexus.FileUtilities.Models
{
    public class FileManifest
    {
        public Dictionary<string, string> OriginalPaths { get; set; } = new();
    }
}
