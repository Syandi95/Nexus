using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Services
{
    public class FileManifestService : IFileManifestService
    {
        public async Task SaveManifestAsync(FileManifest manifest, string path)
        {
            var json = JsonSerializer.Serialize(manifest);
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }

        public async Task<FileManifest> LoadManifestAsync(string path)
        {
            if (!File.Exists(path)) return new FileManifest();
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<FileManifest>(json) ?? new FileManifest();
        }
    }
}
