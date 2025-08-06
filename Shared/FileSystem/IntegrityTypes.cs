using System;
using System.Collections.Generic;

namespace Shared.FileSystem
{
    /// <summary>
    /// Integrity configuration
    /// </summary>
    public class IntegrityConfiguration
    {
        public TimeSpan VerificationInterval { get; set; } = TimeSpan.FromHours(24);
        public int MaxConcurrentVerifications { get; set; } = Environment.ProcessorCount;
        public int MaxFilesPerVerificationCycle { get; set; } = 100;
        public bool EnableAutomaticBackup { get; set; } = true;
        public bool EnablePeriodicVerification { get; set; } = true;
        public HashAlgorithmType DefaultHashAlgorithm { get; set; } = HashAlgorithmType.SHA256;
    }

    /// <summary>
    /// File integrity information
    /// </summary>
    internal class FileIntegrityInfo
    {
        public string FilePath { get; set; }
        public string Hash { get; set; }
        public uint Checksum { get; set; }
        public HashAlgorithmType Algorithm { get; set; }
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastVerified { get; set; }
        public int VerificationCount { get; set; }
    }

    /// <summary>
    /// Integrity calculation result
    /// </summary>
    public class IntegrityResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string Hash { get; set; }
        public uint Checksum { get; set; }
        public HashAlgorithmType Algorithm { get; set; }
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime CalculatedAt { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Verification result
    /// </summary>
    public class VerificationResult
    {
        public string FilePath { get; set; }
        public VerificationStatus Status { get; set; }
        public string Message { get; set; }
        public string ExpectedHash { get; set; }
        public string ActualHash { get; set; }
        public long ExpectedSize { get; set; }
        public long ActualSize { get; set; }
        public DateTime ExpectedModified { get; set; }
        public DateTime ActualModified { get; set; }
        public DateTime VerifiedAt { get; set; }
    }

    /// <summary>
    /// Repair options
    /// </summary>
    public class RepairOptions
    {
        public bool UseBackup { get; set; } = true;
        public bool AttemptRecovery { get; set; } = false;
        public bool CreateNewBackup { get; set; } = true;
        public RepairMethod PreferredMethod { get; set; } = RepairMethod.BackupRestore;
    }

    /// <summary>
    /// Repair result
    /// </summary>
    public class RepairResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public RepairMethod RepairMethod { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Integrity statistics
    /// </summary>
    public class IntegrityStatistics
    {
        public int TotalFiles { get; set; }
        public int TotalVerifications { get; set; }
        public int RecentlyVerified { get; set; }
        public int NewFiles { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// Hash algorithm types
    /// </summary>
    public enum HashAlgorithmType
    {
        MD5,
        SHA1,
        SHA256,
        SHA512
    }

    /// <summary>
    /// Verification status
    /// </summary>
    public enum VerificationStatus
    {
        Valid,
        HashMismatch,
        SizeChanged,
        ModificationTimeChanged,
        FileNotFound,
        NoBaseline,
        Error
    }

    /// <summary>
    /// Corruption types
    /// </summary>
    public enum CorruptionType
    {
        ContentChange,
        SizeChange,
        PermissionChange,
        MetadataChange,
        Unknown
    }

    /// <summary>
    /// Repair methods
    /// </summary>
    public enum RepairMethod
    {
        BackupRestore,
        PartialRecovery,
        JournalRecovery,
        RedundantDataReconstruction,
        ManualIntervention
    }

    /// <summary>
    /// File corruption event arguments
    /// </summary>
    public class FileCorruptionEventArgs : EventArgs
    {
        public string FilePath { get; set; }
        public CorruptionType CorruptionType { get; set; }
        public DateTime DetectedAt { get; set; }
        public string Details { get; set; }
        public string ExpectedHash { get; set; }
        public string ActualHash { get; set; }
    }

    /// <summary>
    /// Integrity verification event arguments
    /// </summary>
    public class IntegrityVerificationEventArgs : EventArgs
    {
        public string FilePath { get; set; }
        public VerificationStatus Status { get; set; }
        public DateTime VerifiedAt { get; set; }
    }

    /// <summary>
    /// Integrity repair event arguments
    /// </summary>
    public class IntegrityRepairEventArgs : EventArgs
    {
        public string FilePath { get; set; }
        public RepairMethod RepairMethod { get; set; }
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
