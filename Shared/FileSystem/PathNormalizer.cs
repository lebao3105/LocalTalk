using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// Cross-platform path handling and normalization with security validation
    /// </summary>
    public class PathNormalizer
    {
        private static PathNormalizer _instance;
        private readonly PathConfiguration _config;
        private readonly Dictionary<PlatformType, PathRules> _platformRules;

        public static PathNormalizer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PathNormalizer();
                }
                return _instance;
            }
        }

        private PathNormalizer()
        {
            _config = new PathConfiguration();
            _platformRules = new Dictionary<PlatformType, PathRules>();
            InitializePlatformRules();
        }

        /// <summary>
        /// Normalizes a path for cross-platform compatibility
        /// </summary>
        public PathNormalizationResult NormalizePath(string path, PlatformType targetPlatform = PlatformType.Current)
        {
            var result = new PathNormalizationResult
            {
                OriginalPath = path,
                TargetPlatform = targetPlatform,
                ProcessedAt = DateTime.Now
            };

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.Success = false;
                    result.ErrorMessage = "Path cannot be null or empty";
                    return result;
                }

                // Get platform rules
                var rules = GetPlatformRules(targetPlatform);
                
                // Step 1: Basic normalization
                var normalizedPath = path.Trim();
                
                // Step 2: Handle path separators
                normalizedPath = NormalizePathSeparators(normalizedPath, rules);
                
                // Step 3: Remove illegal characters
                var illegalCharResult = RemoveIllegalCharacters(normalizedPath, rules);
                normalizedPath = illegalCharResult.CleanedPath;
                result.RemovedCharacters = illegalCharResult.RemovedCharacters;
                
                // Step 4: Handle reserved names
                var reservedNameResult = HandleReservedNames(normalizedPath, rules);
                normalizedPath = reservedNameResult.ModifiedPath;
                result.ReservedNameConflicts = reservedNameResult.Conflicts;
                
                // Step 5: Validate path length
                var lengthResult = ValidatePathLength(normalizedPath, rules);
                if (!lengthResult.IsValid)
                {
                    normalizedPath = TruncatePath(normalizedPath, rules.MaxPathLength);
                    result.WasTruncated = true;
                    result.TruncationReason = lengthResult.Reason;
                }
                
                // Step 6: Security validation
                var securityResult = ValidatePathSecurity(normalizedPath);
                result.SecurityIssues = securityResult.Issues;
                result.SecurityLevel = securityResult.Level;
                
                // Step 7: Final cleanup
                normalizedPath = PerformFinalCleanup(normalizedPath, rules);
                
                result.NormalizedPath = normalizedPath;
                result.Success = true;
                result.IsSecure = securityResult.Level != SecurityLevel.High;
                
                System.Diagnostics.Debug.WriteLine($"Path normalized: '{path}' -> '{normalizedPath}'");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Path normalization error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Validates if a path is safe and compliant
        /// </summary>
        public PathValidationResult ValidatePath(string path, PlatformType platform = PlatformType.Current)
        {
            var result = new PathValidationResult
            {
                Path = path,
                Platform = platform,
                ValidatedAt = DateTime.Now
            };

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.IsValid = false;
                    result.Issues.Add("Path is null or empty");
                    return result;
                }

                var rules = GetPlatformRules(platform);
                
                // Check illegal characters
                var illegalChars = GetIllegalCharacters(path, rules);
                if (illegalChars.Any())
                {
                    result.Issues.Add($"Contains illegal characters: {string.Join(", ", illegalChars)}");
                }
                
                // Check reserved names
                var reservedNames = GetReservedNameConflicts(path, rules);
                if (reservedNames.Any())
                {
                    result.Issues.Add($"Contains reserved names: {string.Join(", ", reservedNames)}");
                }
                
                // Check path length
                if (path.Length > rules.MaxPathLength)
                {
                    result.Issues.Add($"Path too long: {path.Length} > {rules.MaxPathLength}");
                }
                
                // Check component length
                var components = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var longComponents = components.Where(c => c.Length > rules.MaxComponentLength).ToList();
                if (longComponents.Any())
                {
                    result.Issues.Add($"Components too long: {string.Join(", ", longComponents.Take(3))}");
                }
                
                // Security validation
                var securityResult = ValidatePathSecurity(path);
                result.SecurityIssues = securityResult.Issues;
                result.SecurityLevel = securityResult.Level;
                
                result.IsValid = !result.Issues.Any() && securityResult.Level != SecurityLevel.High;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Issues.Add($"Validation error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Converts a path between different platform formats
        /// </summary>
        public string ConvertPath(string path, PlatformType fromPlatform, PlatformType toPlatform)
        {
            if (fromPlatform == toPlatform)
                return path;

            var fromRules = GetPlatformRules(fromPlatform);
            var toRules = GetPlatformRules(toPlatform);

            // Convert path separators
            var convertedPath = path;
            if (fromRules.PathSeparator != toRules.PathSeparator)
            {
                convertedPath = convertedPath.Replace(fromRules.PathSeparator, toRules.PathSeparator);
            }

            // Handle drive letters (Windows specific)
            if (fromPlatform == PlatformType.Windows && toPlatform != PlatformType.Windows)
            {
                // Convert C:\path to /c/path (Unix-style)
                var driveMatch = Regex.Match(convertedPath, @"^([A-Za-z]):[\\/](.*)");
                if (driveMatch.Success)
                {
                    convertedPath = $"/{driveMatch.Groups[1].Value.ToLowerInvariant()}/{driveMatch.Groups[2].Value}";
                }
            }
            else if (fromPlatform != PlatformType.Windows && toPlatform == PlatformType.Windows)
            {
                // Convert /c/path to C:\path
                var unixDriveMatch = Regex.Match(convertedPath, @"^/([a-zA-Z])/(.*)");
                if (unixDriveMatch.Success)
                {
                    convertedPath = $"{unixDriveMatch.Groups[1].Value.ToUpperInvariant()}:\\{unixDriveMatch.Groups[2].Value}";
                }
            }

            return convertedPath;
        }

        /// <summary>
        /// Gets safe filename from potentially unsafe input
        /// </summary>
        public string GetSafeFileName(string fileName, PlatformType platform = PlatformType.Current)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "unnamed";

            var rules = GetPlatformRules(platform);
            var safeFileName = fileName;

            // Remove illegal characters
            foreach (var illegalChar in rules.IllegalCharacters)
            {
                safeFileName = safeFileName.Replace(illegalChar, '_');
            }

            // Handle reserved names
            if (rules.ReservedNames.Contains(safeFileName.ToUpperInvariant()))
            {
                safeFileName = $"_{safeFileName}";
            }

            // Truncate if too long
            if (safeFileName.Length > rules.MaxComponentLength)
            {
                var extension = Path.GetExtension(safeFileName);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeFileName);
                var maxNameLength = rules.MaxComponentLength - extension.Length;
                
                if (maxNameLength > 0)
                {
                    safeFileName = nameWithoutExtension.Substring(0, Math.Min(nameWithoutExtension.Length, maxNameLength)) + extension;
                }
                else
                {
                    safeFileName = safeFileName.Substring(0, rules.MaxComponentLength);
                }
            }

            // Ensure it's not empty after cleaning
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = "unnamed";
            }

            return safeFileName;
        }

        /// <summary>
        /// Combines path components safely
        /// </summary>
        public string CombinePaths(PlatformType platform, params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return string.Empty;

            var rules = GetPlatformRules(platform);
            var combinedPath = paths[0];

            for (int i = 1; i < paths.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(paths[i]))
                    continue;

                var nextPath = paths[i].TrimStart(rules.PathSeparator, rules.AltPathSeparator ?? rules.PathSeparator);
                
                if (!combinedPath.EndsWith(rules.PathSeparator.ToString()))
                {
                    combinedPath += rules.PathSeparator;
                }
                
                combinedPath += nextPath;
            }

            return combinedPath;
        }

        /// <summary>
        /// Initializes platform-specific path rules
        /// </summary>
        private void InitializePlatformRules()
        {
            // Windows rules
            _platformRules[PlatformType.Windows] = new PathRules
            {
                PathSeparator = '\\',
                AltPathSeparator = '/',
                MaxPathLength = 260,
                MaxComponentLength = 255,
                IllegalCharacters = new[] { '<', '>', ':', '"', '|', '?', '*' },
                ReservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
                CaseSensitive = false
            };

            // Unix/Linux rules
            _platformRules[PlatformType.Unix] = new PathRules
            {
                PathSeparator = '/',
                AltPathSeparator = null,
                MaxPathLength = 4096,
                MaxComponentLength = 255,
                IllegalCharacters = new[] { '\0' },
                ReservedNames = new string[0],
                CaseSensitive = true
            };

            // macOS rules (similar to Unix but with some differences)
            _platformRules[PlatformType.MacOS] = new PathRules
            {
                PathSeparator = '/',
                AltPathSeparator = null,
                MaxPathLength = 1024,
                MaxComponentLength = 255,
                IllegalCharacters = new[] { '\0', ':' },
                ReservedNames = new string[0],
                CaseSensitive = false
            };
        }

        /// <summary>
        /// Gets platform rules for the specified platform
        /// </summary>
        private PathRules GetPlatformRules(PlatformType platform)
        {
            if (platform == PlatformType.Current)
            {
                platform = PlatformFactory.CurrentPlatform;
            }

            return _platformRules.TryGetValue(platform, out var rules) ? rules : _platformRules[PlatformType.Windows];
        }

        /// <summary>
        /// Normalizes path separators
        /// </summary>
        private string NormalizePathSeparators(string path, PathRules rules)
        {
            var normalizedPath = path;
            
            // Replace alternative separators with primary separator
            if (rules.AltPathSeparator.HasValue)
            {
                normalizedPath = normalizedPath.Replace(rules.AltPathSeparator.Value, rules.PathSeparator);
            }
            
            // Remove duplicate separators
            var duplicateSeparators = new string(rules.PathSeparator, 2);
            while (normalizedPath.Contains(duplicateSeparators))
            {
                normalizedPath = normalizedPath.Replace(duplicateSeparators, rules.PathSeparator.ToString());
            }
            
            return normalizedPath;
        }

        /// <summary>
        /// Removes illegal characters from path
        /// </summary>
        private IllegalCharacterResult RemoveIllegalCharacters(string path, PathRules rules)
        {
            var result = new IllegalCharacterResult
            {
                CleanedPath = path,
                RemovedCharacters = new List<char>()
            };

            foreach (var illegalChar in rules.IllegalCharacters)
            {
                if (path.Contains(illegalChar))
                {
                    result.RemovedCharacters.Add(illegalChar);
                    result.CleanedPath = result.CleanedPath.Replace(illegalChar, '_');
                }
            }

            return result;
        }

        /// <summary>
        /// Handles reserved names
        /// </summary>
        private ReservedNameResult HandleReservedNames(string path, PathRules rules)
        {
            var result = new ReservedNameResult
            {
                ModifiedPath = path,
                Conflicts = new List<string>()
            };

            var components = path.Split(rules.PathSeparator);
            
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(component);
                
                if (rules.ReservedNames.Contains(nameWithoutExtension.ToUpperInvariant()))
                {
                    result.Conflicts.Add(component);
                    components[i] = $"_{component}";
                }
            }

            if (result.Conflicts.Any())
            {
                result.ModifiedPath = string.Join(rules.PathSeparator.ToString(), components);
            }

            return result;
        }

        /// <summary>
        /// Validates path length
        /// </summary>
        private PathLengthResult ValidatePathLength(string path, PathRules rules)
        {
            var result = new PathLengthResult
            {
                IsValid = path.Length <= rules.MaxPathLength,
                ActualLength = path.Length,
                MaxLength = rules.MaxPathLength
            };

            if (!result.IsValid)
            {
                result.Reason = $"Path length {result.ActualLength} exceeds maximum {result.MaxLength}";
            }

            return result;
        }

        /// <summary>
        /// Validates path security
        /// </summary>
        private PathSecurityResult ValidatePathSecurity(string path)
        {
            var result = new PathSecurityResult
            {
                Issues = new List<string>(),
                Level = SecurityLevel.Low
            };

            // Check for path traversal attempts
            if (path.Contains(".."))
            {
                result.Issues.Add("Contains path traversal sequences (..)");
                result.Level = SecurityLevel.High;
            }

            // Check for suspicious patterns
            var suspiciousPatterns = new[] { "~", "$", "%", "\\\\", "//" };
            foreach (var pattern in suspiciousPatterns)
            {
                if (path.Contains(pattern))
                {
                    result.Issues.Add($"Contains suspicious pattern: {pattern}");
                    result.Level = Math.Max(result.Level, SecurityLevel.Medium);
                }
            }

            // Check for very long components (potential buffer overflow)
            var components = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (components.Any(c => c.Length > 1000))
            {
                result.Issues.Add("Contains extremely long path components");
                result.Level = Math.Max(result.Level, SecurityLevel.Medium);
            }

            return result;
        }

        /// <summary>
        /// Truncates path to fit within length limits
        /// </summary>
        private string TruncatePath(string path, int maxLength)
        {
            if (path.Length <= maxLength)
                return path;

            // Try to preserve the file extension
            var extension = Path.GetExtension(path);
            var pathWithoutExtension = path.Substring(0, path.Length - extension.Length);
            
            var maxPathLength = maxLength - extension.Length;
            if (maxPathLength > 0)
            {
                return pathWithoutExtension.Substring(0, Math.Min(pathWithoutExtension.Length, maxPathLength)) + extension;
            }
            
            return path.Substring(0, maxLength);
        }

        /// <summary>
        /// Performs final cleanup on the path
        /// </summary>
        private string PerformFinalCleanup(string path, PathRules rules)
        {
            // Remove trailing separators (except for root paths)
            if (path.Length > 1 && path.EndsWith(rules.PathSeparator.ToString()))
            {
                path = path.TrimEnd(rules.PathSeparator);
            }

            // Remove leading/trailing whitespace from components
            var components = path.Split(rules.PathSeparator);
            for (int i = 0; i < components.Length; i++)
            {
                components[i] = components[i].Trim();
            }
            
            return string.Join(rules.PathSeparator.ToString(), components);
        }

        /// <summary>
        /// Gets illegal characters in a path
        /// </summary>
        private List<char> GetIllegalCharacters(string path, PathRules rules)
        {
            return path.Where(c => rules.IllegalCharacters.Contains(c)).Distinct().ToList();
        }

        /// <summary>
        /// Gets reserved name conflicts in a path
        /// </summary>
        private List<string> GetReservedNameConflicts(string path, PathRules rules)
        {
            var conflicts = new List<string>();
            var components = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            foreach (var component in components)
            {
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(component);
                if (rules.ReservedNames.Contains(nameWithoutExtension.ToUpperInvariant()))
                {
                    conflicts.Add(component);
                }
            }
            
            return conflicts;
        }
    }
}
