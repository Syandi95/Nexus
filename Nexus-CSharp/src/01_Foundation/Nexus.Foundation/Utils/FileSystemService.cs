using System.IO;
using System.Linq;

namespace Nexus.Foundation.Utils
{
    public class FileSystemService : IFileSystemService
    {
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public string SafePathCombine(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return string.Empty;
            }
            
            // Basic SafePathCombine: just uses Path.Combine, but you might want to add validation
            // to ensure it doesn't traverse up unexpectedly (e.g., checking for "..") if that's a security concern.
            return Path.Combine(paths);
        }
    }
}
