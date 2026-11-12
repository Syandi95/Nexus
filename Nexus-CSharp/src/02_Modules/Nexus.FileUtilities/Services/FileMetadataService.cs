using System;

namespace Nexus.FileUtilities.Services
{
    public class FileMetadataService : IFileMetadataService
    {
        public string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00} {1}", len, sizes[order]);
        }

        public string GetAgeText(DateTime lastWriteTime)
        {
            var diff = DateTime.Now - lastWriteTime;
            if (diff.TotalDays < 1) return "Hari ini";
            if (diff.TotalDays < 365) return $"{(int)diff.TotalDays} hari";
            return $"{(int)(diff.TotalDays / 365)} th";
        }
    }
}
