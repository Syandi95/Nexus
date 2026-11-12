using System;

namespace Nexus.FileUtilities.Services
{
    public interface IFileMetadataService
    {
        string FormatFileSize(long bytes);
        string GetAgeText(DateTime lastWriteTime);
    }
}
