namespace Nexus.Foundation.Utils
{
    public interface IFileSystemService
    {
        bool FileExists(string path);
        void CreateDirectory(string path);
        string SafePathCombine(params string[] paths);
    }
}
