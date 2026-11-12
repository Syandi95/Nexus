using System.Threading.Tasks;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Services
{
    public interface IFileManifestService
    {
        Task SaveManifestAsync(FileManifest manifest, string path);
        Task<FileManifest> LoadManifestAsync(string path);
    }
}
