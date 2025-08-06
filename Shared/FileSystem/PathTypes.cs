using System;
using System.Collections.Generic;

namespace Shared.FileSystem
{
    /// <summary>
    /// Path configuration
    /// </summary>
    public class PathConfiguration
    {
        public bool EnableSecurityValidation { get; set; } = true;
        public bool EnableLengthValidation { get; set; } = true;
        public bool EnableCharacterValidation { get; set; } = true;
        public bool EnableReservedNameValidation { get; set; } = true;
        public string DefaultReplacementChar { get; set; } = "_";
    }

    /// <summary>
    /// Platform-specific path rules
    /// </summary>
    internal class PathRules
    {
        public char PathSeparator { get; set; }
        public char? AltPathSeparator { get; set; }
        public int MaxPathLength { get; set; }
        public int MaxComponentLength { get; set; }
        public char[] IllegalCharacters { get; set; }
        public string[] ReservedNames { get; set; }
        public bool CaseSensitive { get; set; }
    }

    /// <summary>
    /// Path normalization result
    /// </summary>
    public class PathNormalizationResult
    {
        public bool Success { get; set; }
        public string OriginalPath { get; set; }
        public string NormalizedPath { get; set; }
        public PlatformType TargetPlatform { get; set; }
        public List<char> RemovedCharacters { get; set; } = new List<char>();
        public List<string> ReservedNameConflicts { get; set; } = new List<string>();
        public bool WasTruncated { get; set; }
        public string TruncationReason { get; set; }
        public List<string> SecurityIssues { get; set; } = new List<string>();
        public SecurityLevel SecurityLevel { get; set; }
        public bool IsSecure { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    /// <summary>
    /// Path validation result
    /// </summary>
    public class PathValidationResult
    {
        public bool IsValid { get; set; }
        public string Path { get; set; }
        public PlatformType Platform { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public List<string> SecurityIssues { get; set; } = new List<string>();
        public SecurityLevel SecurityLevel { get; set; }
        public DateTime ValidatedAt { get; set; }
    }

    /// <summary>
    /// Platform types
    /// </summary>
    public enum PlatformType
    {
        Current,
        Windows,
        Unix,
        MacOS
    }

    /// <summary>
    /// Security levels
    /// </summary>
    public enum SecurityLevel
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Illegal character removal result
    /// </summary>
    internal class IllegalCharacterResult
    {
        public string CleanedPath { get; set; }
        public List<char> RemovedCharacters { get; set; }
    }

    /// <summary>
    /// Reserved name handling result
    /// </summary>
    internal class ReservedNameResult
    {
        public string ModifiedPath { get; set; }
        public List<string> Conflicts { get; set; }
    }

    /// <summary>
    /// Path length validation result
    /// </summary>
    internal class PathLengthResult
    {
        public bool IsValid { get; set; }
        public int ActualLength { get; set; }
        public int MaxLength { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Path security validation result
    /// </summary>
    internal class PathSecurityResult
    {
        public List<string> Issues { get; set; }
        public SecurityLevel Level { get; set; }
    }
}
