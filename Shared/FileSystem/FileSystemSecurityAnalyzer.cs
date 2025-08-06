using Shared.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.FileSystem
{
    /// <summary>
    /// Analyzes file system permissions and security restrictions
    /// </summary>
    public class FileSystemSecurityAnalyzer
    {
        // File size constants
        private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100MB
        private const long MaxDirectorySizeBytes = 1024 * 1024 * 1024; // 1GB
        private const int MaxPathLength = 260; // Windows MAX_PATH
        private const int MaxFilenameLength = 255; // Most filesystems

        private static FileSystemSecurityAnalyzer _instance;
        private readonly IPlatformAbstraction _platform;
        private readonly Dictionary<string, FileSystemCapability> _capabilities;
        private readonly HashSet<string> _restrictedPaths;
        private readonly HashSet<string> _allowedExtensions;

        /// <summary>
        /// Gets the singleton instance of the FileSystemSecurityAnalyzer
        /// </summary>
        public static FileSystemSecurityAnalyzer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FileSystemSecurityAnalyzer();
                }
                return _instance;
            }
        }

        private FileSystemSecurityAnalyzer()
        {
            _platform = PlatformFactory.Current;
            _capabilities = new Dictionary<string, FileSystemCapability>();
            _restrictedPaths = new HashSet<string>();
            _allowedExtensions = new HashSet<string>();

            InitializeCapabilities();
            InitializeRestrictedPaths();
            InitializeAllowedExtensions();
        }

        /// <summary>
        /// Initializes platform-specific file system capabilities
        /// </summary>
        private void InitializeCapabilities()
        {
#if WINDOWS_UWP
            _capabilities["read_documents"] = new FileSystemCapability
            {
                Name = "Documents Library",
                IsAvailable = true,
                RequiresPermission = true,
                PermissionType = "documentsLibrary",
                SecurityLevel = SecurityLevel.Medium
            };

            _capabilities["read_pictures"] = new FileSystemCapability
            {
                Name = "Pictures Library",
                IsAvailable = true,
                RequiresPermission = true,
                PermissionType = "picturesLibrary",
                SecurityLevel = SecurityLevel.Medium
            };

            _capabilities["read_videos"] = new FileSystemCapability
            {
                Name = "Videos Library",
                IsAvailable = true,
                RequiresPermission = true,
                PermissionType = "videosLibrary",
                SecurityLevel = SecurityLevel.Medium
            };

            _capabilities["read_removable_storage"] = new FileSystemCapability
            {
                Name = "Removable Storage",
                IsAvailable = true,
                RequiresPermission = true,
                PermissionType = "removableStorage",
                SecurityLevel = SecurityLevel.High
            };

            _capabilities["write_downloads"] = new FileSystemCapability
            {
                Name = "Downloads Folder",
                IsAvailable = true,
                RequiresPermission = false,
                SecurityLevel = SecurityLevel.Low
            };
#elif WINDOWS_PHONE
            _capabilities["isolated_storage"] = new FileSystemCapability
            {
                Name = "Isolated Storage",
                IsAvailable = true,
                RequiresPermission = false,
                SecurityLevel = SecurityLevel.Low
            };

            _capabilities["media_library"] = new FileSystemCapability
            {
                Name = "Media Library",
                IsAvailable = true,
                RequiresPermission = true,
                PermissionType = "ID_CAP_MEDIALIB_PHOTO",
                SecurityLevel = SecurityLevel.Medium
            };
#endif
        }

        /// <summary>
        /// Initializes restricted file system paths
        /// </summary>
        private void InitializeRestrictedPaths()
        {
#if WINDOWS_UWP
            _restrictedPaths.Add("C:\\Windows");
            _restrictedPaths.Add("C:\\Program Files");
            _restrictedPaths.Add("C:\\Program Files (x86)");
            _restrictedPaths.Add("C:\\System Volume Information");
            _restrictedPaths.Add("C:\\$Recycle.Bin");
#elif WINDOWS_PHONE
            _restrictedPaths.Add("/Windows");
            _restrictedPaths.Add("/Program Files");
            _restrictedPaths.Add("/System");
#endif
        }

        /// <summary>
        /// Initializes allowed file extensions for security
        /// </summary>
        private void InitializeAllowedExtensions()
        {
            // Document files
            var documentExtensions = new[] {
                ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                ".ppt", ".pptx", ".rtf", ".odt", ".ods", ".odp"
            };

            // Image files
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg" };

            // Video files
            var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" };

            // Audio files
            var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" };

            // Archive files
            var archiveExtensions = new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" };

            foreach (var ext in documentExtensions
                .Concat(imageExtensions)
                .Concat(videoExtensions)
                .Concat(audioExtensions)
                .Concat(archiveExtensions))
            {
                _allowedExtensions.Add(ext.ToLowerInvariant());
            }
        }

        /// <summary>
        /// Analyzes file system access for a specific path
        /// </summary>
        /// <param name="path">The file system path to analyze</param>
        /// <returns>Analysis result containing security information</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null</exception>
        /// <exception cref="ArgumentException">Thrown when path is empty or contains invalid characters</exception>
        public async Task<FileSystemAnalysisResult> AnalyzePathAsync(string path)
        {
            // Input validation
            if (path == null)
                throw new ArgumentNullException(nameof(path), "Path cannot be null");

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty or whitespace", nameof(path));

            if (path.Length > 32767) // Windows MAX_PATH extended limit
                throw new ArgumentException("Path is too long", nameof(path));

            // Check for invalid path characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
                throw new ArgumentException("Path contains invalid characters", nameof(path));

            var result = new FileSystemAnalysisResult
            {
                Path = path,
                IsAnalyzed = true,
                Timestamp = DateTime.Now
            };

            try
            {
                // Normalize path
                var normalizedPath = NormalizePath(path);
                result.NormalizedPath = normalizedPath;

                // Check if path is restricted
                result.IsRestricted = IsPathRestricted(normalizedPath);
                if (result.IsRestricted)
                {
                    result.Restrictions.Add("Path is in restricted system directory");
                    result.SecurityLevel = SecurityLevel.Critical;
                }

                // Check path traversal attempts
                if (ContainsPathTraversal(path))
                {
                    result.SecurityThreats.Add("Path traversal attempt detected");
                    result.SecurityLevel = SecurityLevel.Critical;
                }

                // Analyze file extension if it's a file path
                if (Path.HasExtension(normalizedPath))
                {
                    var extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
                    result.FileExtension = extension;
                    result.IsAllowedExtension = _allowedExtensions.Contains(extension);

                    if (!result.IsAllowedExtension)
                    {
                        result.SecurityThreats.Add($"Potentially dangerous file extension: {extension}");
                        result.SecurityLevel = SecurityLevel.High;
                    }
                }

                // Check for suspicious file names
                var fileName = Path.GetFileName(normalizedPath);
                if (IsSuspiciousFileName(fileName))
                {
                    result.SecurityThreats.Add("Suspicious file name detected");
                    result.SecurityLevel = SecurityLevel.High;
                }

                // Analyze required capabilities
                result.RequiredCapabilities = GetRequiredCapabilities(normalizedPath);

                // Check if path exists and get additional info
                await AnalyzeExistingPath(normalizedPath, result);

            }
            catch (Exception ex)
            {
                result.Errors.Add($"Analysis error: {ex.Message}");
                result.SecurityLevel = SecurityLevel.Medium;
            }

            return result;
        }

        /// <summary>
        /// Normalizes a file path for security analysis
        /// </summary>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            try
            {
                // Remove dangerous characters and sequences
                var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                                   .Replace('\\', Path.DirectorySeparatorChar);

                // Remove multiple consecutive separators
                var doubleSeparator = Path.DirectorySeparatorChar.ToString() +
                    Path.DirectorySeparatorChar.ToString();
                while (normalized.Contains(doubleSeparator))
                {
                    normalized = normalized.Replace(doubleSeparator, Path.DirectorySeparatorChar.ToString());
                }

                // Remove leading/trailing separators
                normalized = normalized.Trim(Path.DirectorySeparatorChar);

                return normalized;
            }
            catch
            {
                return path; // Return original if normalization fails
            }
        }

        /// <summary>
        /// Checks if a path is in restricted system directories
        /// </summary>
        private bool IsPathRestricted(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var upperPath = path.ToUpperInvariant();

            return _restrictedPaths.Any(restrictedPath =>
                upperPath.StartsWith(restrictedPath.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks for path traversal attempts
        /// </summary>
        private bool ContainsPathTraversal(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var dangerousPatterns = new[]
            {
                "..", "..\\", "../", "....//", "....\\\\",
                "%2e%2e", "%2e%2e%2f", "%2e%2e%5c",
                "%252e%252e", "%252e%252e%252f", "%252e%252e%255c"
            };

            return dangerousPatterns.Any(pattern =>
                path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if a file name is suspicious
        /// </summary>
        private bool IsSuspiciousFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            var suspiciousPatterns = new[]
            {
                "autorun.inf", "desktop.ini", "thumbs.db", ".htaccess",
                "web.config", "app.config", "machine.config"
            };

            var suspiciousExtensions = new[]
            {
                ".exe", ".bat", ".cmd", ".com", ".scr", ".pif", ".vbs", ".js",
                ".jar", ".msi", ".dll", ".sys", ".drv"
            };

            var lowerFileName = fileName.ToLowerInvariant();

            return suspiciousPatterns.Any(pattern => lowerFileName.Contains(pattern)) ||
                   suspiciousExtensions.Any(ext => lowerFileName.EndsWith(ext));
        }

        /// <summary>
        /// Gets required capabilities for accessing a path
        /// </summary>
        private List<string> GetRequiredCapabilities(string path)
        {
            var capabilities = new List<string>();

            if (string.IsNullOrEmpty(path))
                return capabilities;

            var lowerPath = path.ToLowerInvariant();

#if WINDOWS_UWP
            if (lowerPath.Contains("documents"))
                capabilities.Add("documentsLibrary");
            if (lowerPath.Contains("pictures") || lowerPath.Contains("photos"))
                capabilities.Add("picturesLibrary");
            if (lowerPath.Contains("videos") || lowerPath.Contains("movies"))
                capabilities.Add("videosLibrary");
            if (lowerPath.Contains("music"))
                capabilities.Add("musicLibrary");
            if (lowerPath.Contains("removable"))
                capabilities.Add("removableStorage");
#elif WINDOWS_PHONE
            if (lowerPath.Contains("media") || lowerPath.Contains("pictures") || lowerPath.Contains("photos"))
                capabilities.Add("ID_CAP_MEDIALIB_PHOTO");
#endif

            return capabilities;
        }

        /// <summary>
        /// Analyzes an existing path for additional security information
        /// </summary>
        private async Task AnalyzeExistingPath(string path, FileSystemAnalysisResult result)
        {
            try
            {
                var storageManager = _platform.GetStorageManager();

                // This is a simplified check - in a real implementation,
                // you would check if the path exists and get its properties
                result.PathExists = !string.IsNullOrEmpty(path);

                if (result.PathExists)
                {
                    // Check available space
                    var availableSpace = await storageManager.GetAvailableSpaceAsync();
                    result.AvailableSpace = availableSpace;

                    // Additional security checks could be performed here
                    // such as checking file attributes, permissions, etc.
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Path analysis error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets file system capabilities for the current platform
        /// </summary>
        public Dictionary<string, FileSystemCapability> GetCapabilities()
        {
            return new Dictionary<string, FileSystemCapability>(_capabilities);
        }

        /// <summary>
        /// Checks if a specific capability is available
        /// </summary>
        /// <param name="capabilityName">Name of the capability to check</param>
        /// <returns>True if the capability is available, false otherwise</returns>
        public bool IsCapabilityAvailable(string capabilityName)
        {
            return _capabilities.ContainsKey(capabilityName) && _capabilities[capabilityName].IsAvailable;
        }

        /// <summary>
        /// Gets security recommendations for file operations
        /// </summary>
        /// <returns>List of security recommendations for the current platform</returns>
        public List<SecurityRecommendation> GetSecurityRecommendations()
        {
            var recommendations = new List<SecurityRecommendation>();

#if WINDOWS_UWP
            recommendations.Add(new SecurityRecommendation
            {
                Type = RecommendationType.Permission,
                Title = "Use Appropriate Capabilities",
                Description = "Declare only the minimum required capabilities in Package.appxmanifest",
                Priority = RecommendationPriority.High
            });

            recommendations.Add(new SecurityRecommendation
            {
                Type = RecommendationType.Storage,
                Title = "Prefer Application Data",
                Description = "Store application-specific data in ApplicationData folders when possible",
                Priority = RecommendationPriority.Medium
            });
#elif WINDOWS_PHONE
            recommendations.Add(new SecurityRecommendation
            {
                Type = RecommendationType.Storage,
                Title = "Use Isolated Storage",
                Description = "Store files in isolated storage to maintain security boundaries",
                Priority = RecommendationPriority.High
            });
#endif

            recommendations.Add(new SecurityRecommendation
            {
                Type = RecommendationType.Validation,
                Title = "Validate File Extensions",
                Description = "Always validate file extensions and content types before processing",
                Priority = RecommendationPriority.Critical
            });

            return recommendations;
        }

        #region Enhanced Security Validation Methods

        /// <summary>
        /// Validates if a path format is secure and properly formatted
        /// </summary>
        private bool IsValidPathFormat(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Check path length (Windows MAX_PATH is 260, but we use extended limit)
            if (path.Length > 32767)
                return false;

            // Check for null bytes and other dangerous characters
            if (path.Contains('\0') || path.Contains('\r') || path.Contains('\n'))
                return false;

            // Check for invalid path characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
                return false;

            // Check for reserved names (Windows)
            var fileName = Path.GetFileNameWithoutExtension(path);
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                                      "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2",
                                      "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

            if (reservedNames.Contains(fileName?.ToUpperInvariant()))
                return false;

            return true;
        }

        /// <summary>
        /// Enhanced check for secure path (prevents directory traversal)
        /// </summary>
        private bool IsSecurePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Check for obvious directory traversal patterns
            if (path.Contains("..") || path.Contains("\\..\\") || path.Contains("/../"))
                return false;

            // Check for URL-encoded traversal attempts
            var urlDecodedPath = Uri.UnescapeDataString(path);
            if (urlDecodedPath.Contains("..") || urlDecodedPath != path)
                return false;

            // Check for double-encoded traversal attempts
            try
            {
                var doubleDecoded = Uri.UnescapeDataString(urlDecodedPath);
                if (doubleDecoded.Contains("..") || doubleDecoded != urlDecodedPath)
                    return false;
            }
            catch
            {
                return false; // Invalid encoding
            }

            // Check for UNC paths that could be dangerous
            if (path.StartsWith("\\\\") && !IsAllowedUNCPath(path))
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a UNC path is allowed
        /// </summary>
        private bool IsAllowedUNCPath(string path)
        {
            // For security, we generally don't allow UNC paths unless explicitly configured
            // This can be customized based on application requirements
            return false;
        }

        /// <summary>
        /// Performs additional security analysis for files
        /// </summary>
        private void PerformFileSecurityAnalysis(string path, FileSystemAnalysisResult result)
        {
            try
            {
                var fileInfo = new FileInfo(path);

                // Check file size for potential issues
                if (fileInfo.Length > 1024 * 1024 * 1024) // 1GB
                {
                    result.SecurityThreats.Add("File size exceeds safe limits for processing");
                    result.SecurityLevel = Math.Max(result.SecurityLevel, SecurityLevel.Medium);
                }

                // Check for hidden or system files
                if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden) ||
                    fileInfo.Attributes.HasFlag(FileAttributes.System))
                {
                    result.SecurityThreats.Add("File has hidden or system attributes");
                    result.SecurityLevel = Math.Max(result.SecurityLevel, SecurityLevel.Medium);
                }

                // Check file extension against dangerous types
                var extension = fileInfo.Extension.ToLowerInvariant();
                var dangerousExtensions = new[] { ".exe", ".bat", ".cmd", ".com", ".scr", ".pif",
                                                ".vbs", ".js", ".jar", ".ps1", ".msi" };

                if (dangerousExtensions.Contains(extension))
                {
                    result.SecurityThreats.Add($"Potentially dangerous executable file type: {extension}");
                    result.SecurityLevel = Math.Max(result.SecurityLevel, SecurityLevel.High);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error during file security analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs additional security analysis for directories
        /// </summary>
        private void PerformDirectorySecurityAnalysis(string path, FileSystemAnalysisResult result)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);

                // Check for system directories
                if (dirInfo.Attributes.HasFlag(FileAttributes.System))
                {
                    result.SecurityThreats.Add("Directory is marked as system directory");
                    result.SecurityLevel = Math.Max(result.SecurityLevel, SecurityLevel.High);
                }

                // Check if directory is in sensitive system paths
                var systemPaths = new[] {
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                };

                foreach (var systemPath in systemPaths)
                {
                    if (!string.IsNullOrEmpty(systemPath) &&
                        path.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SecurityThreats.Add("Directory is in sensitive system path");
                        result.SecurityLevel = Math.Max(result.SecurityLevel, SecurityLevel.High);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error during directory security analysis: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// File system capability information
    /// </summary>
    public class FileSystemCapability
    {
        public string Name { get; set; }
        public bool IsAvailable { get; set; }
        public bool RequiresPermission { get; set; }
        public string PermissionType { get; set; }
        public SecurityLevel SecurityLevel { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// File system analysis result
    /// </summary>
    public class FileSystemAnalysisResult
    {
        public string Path { get; set; }
        public string NormalizedPath { get; set; }
        public bool IsAnalyzed { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsRestricted { get; set; }
        public bool PathExists { get; set; }
        public string FileExtension { get; set; }
        public bool IsAllowedExtension { get; set; }
        public SecurityLevel SecurityLevel { get; set; } = SecurityLevel.Low;
        public List<string> SecurityThreats { get; set; } = new List<string>();
        public List<string> Restrictions { get; set; } = new List<string>();
        public List<string> RequiredCapabilities { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public long AvailableSpace { get; set; }
    }

    /// <summary>
    /// Security levels for file system operations
    /// </summary>
    public enum SecurityLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Security recommendation
    /// </summary>
    public class SecurityRecommendation
    {
        public RecommendationType Type { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public RecommendationPriority Priority { get; set; }
    }

    /// <summary>
    /// Recommendation types
    /// </summary>
    public enum RecommendationType
    {
        Permission,
        Storage,
        Validation,
        Security,
        Performance
    }

    /// <summary>
    /// Recommendation priorities
    /// </summary>
    public enum RecommendationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }
}
