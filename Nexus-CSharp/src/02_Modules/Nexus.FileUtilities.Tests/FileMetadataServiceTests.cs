using System;
using Xunit;
using Nexus.FileUtilities.Services;

namespace Nexus.FileUtilities.Tests
{
    public class FileMetadataServiceTests
    {
        [Theory]
        [InlineData(1024, "1.00 KB")]
        [InlineData(1048576, "1.00 MB")]
        [InlineData(1073741824, "1.00 GB")]
        public void FormatFileSize_ShouldFormatCorrectly(long bytes, string expected)
        {
            var service = new FileMetadataService();
            Assert.Equal(expected, service.FormatFileSize(bytes));
        }

        [Fact]
        public void GetAgeText_ShouldReturnHariIni()
        {
            var service = new FileMetadataService();
            var date = DateTime.Now;
            Assert.Equal("Hari ini", service.GetAgeText(date));
        }
    }
}
