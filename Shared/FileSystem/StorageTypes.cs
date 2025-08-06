using System;
using System.Collections.Generic;
using System.IO;

namespace Shared.FileSystem
{
    /// <summary>
    /// Storage configuration
    /// </summary>
    public class StorageConfiguration
    {
        public double WarningThreshold { get; set; } = 80.0; // 80%
        public double CriticalThreshold { get; set; } = 95.0; // 95%
        public double HealthyThreshold { get; set; } = 60.0; // 60%
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(6);
        public TimeSpan TempFileAge { get; set; } = TimeSpan.FromDays(7);
        public TimeSpan CacheFileAge { get; set; } = TimeSpan.FromDays(30);
        public TimeSpan LogFileAge { get; set; } = TimeSpan.FromDays(90);
        public bool EnableAutomaticCleanup { get; set; } = true;
        public Dictionary<string, long> PathQuotas { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>
    /// Storage analysis result
    /// </summary>
    public class StorageAnalysis
    {
        public string Path { get; set; }
        public long TotalSpace { get; set; }
        public long FreeSpace { get; set; }
        public long UsedSpace { get; set; }
        public double UsagePercentage { get; set; }
        public long DirectorySize { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public List<FileInfo> LargestFiles { get; set; } = new List<FileInfo>();
        public List<FileTypeInfo> FileTypeBreakdown { get; set; } = new List<FileTypeInfo>();
        public StorageStatus Status { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public string ErrorMessage { get; set; }

        public string FormattedTotalSpace => FormatBytes(TotalSpace);
        public string FormattedFreeSpace => FormatBytes(FreeSpace);
        public string FormattedUsedSpace => FormatBytes(UsedSpace);
        public string FormattedDirectorySize => FormatBytes(DirectorySize);

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Directory analysis result
    /// </summary>
    internal class DirectoryAnalysis
    {
        public long TotalSize { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public List<FileInfo> LargestFiles { get; set; } = new List<FileInfo>();
        public List<FileTypeInfo> FileTypeBreakdown { get; set; } = new List<FileTypeInfo>();
    }

    /// <summary>
    /// File type information
    /// </summary>
    public class FileTypeInfo
    {
        public string Extension { get; set; }
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string FormattedSize => FormatBytes(TotalSize);

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Space check result
    /// </summary>
    public class SpaceCheckResult
    {
        public string Path { get; set; }
        public long RequiredBytes { get; set; }
        public long AvailableBytes { get; set; }
        public bool HasSufficientSpace { get; set; }
        public long ShortfallBytes { get; set; }
        public bool CanBeFreedByCleanup { get; set; }
        public DateTime CheckedAt { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Cleanup options
    /// </summary>
    public class CleanupOptions
    {
        public bool CleanTemporaryFiles { get; set; } = true;
        public bool CleanCacheFiles { get; set; } = true;
        public bool CleanLogFiles { get; set; } = true;
        public bool CleanDuplicateFiles { get; set; } = false;
        public bool CleanLargeOldFiles { get; set; } = false;
        public long LargeFileThreshold { get; set; } = 100 * 1024 * 1024; // 100MB
        public TimeSpan OldFileThreshold { get; set; } = TimeSpan.FromDays(365); // 1 year
    }

    /// <summary>
    /// Cleanup result
    /// </summary>
    public class CleanupResult
    {
        public bool Success { get; set; }
        public string Path { get; set; }
        public CleanupOptions Options { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public long InitialFreeSpace { get; set; }
        public long FinalFreeSpace { get; set; }
        public long SpaceFreed { get; set; }
        public int FilesDeleted { get; set; }
        public List<CleanedFileInfo> CleanedFiles { get; set; } = new List<CleanedFileInfo>();
        public string ErrorMessage { get; set; }
        public string FormattedSpaceFreed => FormatBytes(SpaceFreed);

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Cleaned file information
    /// </summary>
    public class CleanedFileInfo
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public CleanupType Type { get; set; }
        public DateTime DeletedAt { get; set; }
        public string FormattedSize => FormatBytes(Size);

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Storage statistics
    /// </summary>
    public class StorageStatistics
    {
        public List<DriveStatistics> DriveStatistics { get; set; } = new List<DriveStatistics>();
        public long TotalSpace { get; set; }
        public long TotalFreeSpace { get; set; }
        public long TotalUsedSpace { get; set; }
        public double OverallUsagePercentage { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Drive statistics
    /// </summary>
    public class DriveStatistics
    {
        public string Name { get; set; }
        public string DriveType { get; set; }
        public long TotalSize { get; set; }
        public long FreeSpace { get; set; }
        public long UsedSpace { get; set; }
        public double UsagePercentage { get; set; }
        public string FormattedTotalSize => FormatBytes(TotalSize);
        public string FormattedFreeSpace => FormatBytes(FreeSpace);
        public string FormattedUsedSpace => FormatBytes(UsedSpace);

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Storage status enumeration
    /// </summary>
    public enum StorageStatus
    {
        Excellent,
        Healthy,
        Warning,
        Critical
    }

    /// <summary>
    /// Cleanup type enumeration
    /// </summary>
    public enum CleanupType
    {
        TemporaryFile,
        CacheFile,
        LogFile,
        DuplicateFile,
        LargeOldFile,
        Other
    }

    /// <summary>
    /// Storage quota event arguments
    /// </summary>
    public class StorageQuotaEventArgs : EventArgs
    {
        public string Path { get; set; }
        public long QuotaBytes { get; set; }
        public long UsedBytes { get; set; }
        public long ExcessBytes { get; set; }
    }

    /// <summary>
    /// Storage cleanup event arguments
    /// </summary>
    public class StorageCleanupEventArgs : EventArgs
    {
        public string Path { get; set; }
        public long SpaceFreed { get; set; }
        public int FilesDeleted { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Low storage event arguments
    /// </summary>
    public class LowStorageEventArgs : EventArgs
    {
        public string DriveName { get; set; }
        public double UsagePercentage { get; set; }
        public long FreeSpace { get; set; }
        public long TotalSpace { get; set; }
    }
}
