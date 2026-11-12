using System.IO;
using Xunit;
using Nexus.Foundation.Utils;

namespace Nexus.Foundation.Tests
{
    public class FileSystemServiceTests
    {
        [Fact]
        public void FileExists_ReturnsTrueForExistingFile()
        {
            var service = new FileSystemService();
            var path = Path.GetTempFileName();
            
            Assert.True(service.FileExists(path));
            
            File.Delete(path);
            Assert.False(service.FileExists(path));
        }

        [Fact]
        public void CreateDirectory_CreatesDir()
        {
            var service = new FileSystemService();
            var path = Path.Combine(Path.GetTempPath(), "NexusTestDir");
            if (Directory.Exists(path)) Directory.Delete(path);
            
            service.CreateDirectory(path);
            
            Assert.True(Directory.Exists(path));
            Directory.Delete(path);
        }

        [Fact]
        public void SafePathCombine_CombinesProperly()
        {
            var service = new FileSystemService();
            var result = service.SafePathCombine("folder", "subfolder", "file.txt");
            
            var expected = Path.Combine("folder", "subfolder", "file.txt");
            Assert.Equal(expected, result);
        }
    }
}
