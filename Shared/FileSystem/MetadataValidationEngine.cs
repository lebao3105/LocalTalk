using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Shared.FileSystem
{
    /// <summary>
    /// Metadata validation engine to detect security threats in file metadata
    /// </summary>
    public class MetadataValidationEngine
    {
        private readonly List<IMetadataValidator> _validators;
        private readonly Dictionary<string, string> _suspiciousPatterns;

        public MetadataValidationEngine()
        {
            _validators = new List<IMetadataValidator>();
            _suspiciousPatterns = new Dictionary<string, string>();
            InitializeValidators();
            InitializeSuspiciousPatterns();
        }

        /// <summary>
        /// Validates metadata for security threats
        /// </summary>
        public async Task<MetadataValidationResult> ValidateMetadataAsync(MetadataExtractionResult extractionResult)
        {
            var result = new MetadataValidationResult
            {
                IsSecure = true,
                SecurityThreats = new List<string>(),
                ValidationTimestamp = DateTime.Now
            };

            try
            {
                // Run all validators
                foreach (var validator in _validators)
                {
                    var validationResult = await validator.ValidateAsync(extractionResult);
                    
                    if (!validationResult.IsValid)
                    {
                        result.IsSecure = false;
                        result.SecurityThreats.AddRange(validationResult.Threats);
                    }
                }

                // Check for suspicious patterns in metadata values
                var patternThreats = await CheckSuspiciousPatternsAsync(extractionResult);
                if (patternThreats.Any())
                {
                    result.IsSecure = false;
                    result.SecurityThreats.AddRange(patternThreats);
                }

                // Determine overall security level
                result.SecurityLevel = DetermineSecurityLevel(result.SecurityThreats);
            }
            catch (Exception ex)
            {
                result.IsSecure = false;
                result.SecurityThreats.Add($"Validation error: {ex.Message}");
                result.SecurityLevel = SecurityLevel.Critical;
                System.Diagnostics.Debug.WriteLine($"Metadata validation error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Checks for suspicious patterns in metadata
        /// </summary>
        private async Task<List<string>> CheckSuspiciousPatternsAsync(MetadataExtractionResult extractionResult)
        {
            var threats = new List<string>();

            await Task.Run(() =>
            {
                // Check basic metadata
                if (extractionResult.BasicMetadata != null)
                {
                    CheckStringForPatterns(extractionResult.BasicMetadata.FileName, "FileName", threats);
                    CheckStringForPatterns(extractionResult.BasicMetadata.ContentType, "ContentType", threats);
                }

                // Check format-specific metadata
                if (extractionResult.FormatSpecificMetadata != null)
                {
                    foreach (var kvp in extractionResult.FormatSpecificMetadata)
                    {
                        if (kvp.Value is string stringValue)
                        {
                            CheckStringForPatterns(stringValue, kvp.Key, threats);
                        }
                    }
                }
            });

            return threats;
        }

        /// <summary>
        /// Checks a string value for suspicious patterns
        /// </summary>
        private void CheckStringForPatterns(string value, string fieldName, List<string> threats)
        {
            if (string.IsNullOrEmpty(value))
                return;

            foreach (var pattern in _suspiciousPatterns)
            {
                try
                {
                    if (Regex.IsMatch(value, pattern.Key, RegexOptions.IgnoreCase))
                    {
                        threats.Add($"Suspicious pattern detected in {fieldName}: {pattern.Value}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking pattern {pattern.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Determines security level based on threats
        /// </summary>
        private SecurityLevel DetermineSecurityLevel(List<string> threats)
        {
            if (!threats.Any())
                return SecurityLevel.Low;

            var criticalKeywords = new[] { "script", "executable", "malware", "virus", "trojan", "exploit" };
            var highKeywords = new[] { "suspicious", "embedded", "macro", "javascript", "vbscript" };

            foreach (var threat in threats)
            {
                var lowerThreat = threat.ToLowerInvariant();
                
                if (criticalKeywords.Any(keyword => lowerThreat.Contains(keyword)))
                    return SecurityLevel.Critical;
                
                if (highKeywords.Any(keyword => lowerThreat.Contains(keyword)))
                    return SecurityLevel.High;
            }

            return SecurityLevel.Medium;
        }

        /// <summary>
        /// Initializes metadata validators
        /// </summary>
        private void InitializeValidators()
        {
            _validators.Add(new ExifSecurityValidator());
            _validators.Add(new DocumentSecurityValidator());
            _validators.Add(new ArchiveSecurityValidator());
            _validators.Add(new MediaSecurityValidator());
            _validators.Add(new GeneralMetadataSecurityValidator());
        }

        /// <summary>
        /// Initializes suspicious pattern dictionary
        /// </summary>
        private void InitializeSuspiciousPatterns()
        {
            // Script injection patterns
            _suspiciousPatterns[@"<script[^>]*>.*?</script>"] = "JavaScript code detected";
            _suspiciousPatterns[@"javascript:"] = "JavaScript URL detected";
            _suspiciousPatterns[@"vbscript:"] = "VBScript URL detected";
            _suspiciousPatterns[@"data:text/html"] = "HTML data URL detected";
            
            // Command injection patterns
            _suspiciousPatterns[@"cmd\.exe"] = "Command prompt reference detected";
            _suspiciousPatterns[@"powershell"] = "PowerShell reference detected";
            _suspiciousPatterns[@"bash|sh|/bin/"] = "Shell command detected";
            
            // File path traversal patterns
            _suspiciousPatterns[@"\.\.[\\/]"] = "Path traversal attempt detected";
            _suspiciousPatterns[@"[\\/]etc[\\/]passwd"] = "System file access attempt detected";
            _suspiciousPatterns[@"[\\/]windows[\\/]system32"] = "System directory access attempt detected";
            
            // Suspicious file extensions in metadata
            _suspiciousPatterns[@"\.(exe|bat|cmd|scr|pif|com|vbs|js|jar|app)"] = "Executable file reference detected";
            
            // Network/URL patterns that might be suspicious
            _suspiciousPatterns[@"https?://[^\s]+\.(exe|bat|scr|zip|rar)"] = "Suspicious download URL detected";
            
            // Encoding patterns that might hide malicious content
            _suspiciousPatterns[@"base64,"] = "Base64 encoded data detected";
            _suspiciousPatterns[@"%[0-9a-fA-F]{2}"] = "URL encoded data detected";
            
            // Suspicious metadata field lengths (potential buffer overflow attempts)
            _suspiciousPatterns[@".{1000,}"] = "Unusually long metadata field detected";
        }
    }

    /// <summary>
    /// Metadata validation result
    /// </summary>
    public class MetadataValidationResult
    {
        public bool IsSecure { get; set; }
        public List<string> SecurityThreats { get; set; } = new List<string>();
        public SecurityLevel SecurityLevel { get; set; }
        public DateTime ValidationTimestamp { get; set; }
    }

    /// <summary>
    /// Interface for metadata validators
    /// </summary>
    public interface IMetadataValidator
    {
        Task<ValidationResult> ValidateAsync(MetadataExtractionResult extractionResult);
        string ValidatorName { get; }
    }

    /// <summary>
    /// EXIF security validator
    /// </summary>
    public class ExifSecurityValidator : IMetadataValidator
    {
        public string ValidatorName => "EXIF Security Validator";

        public async Task<ValidationResult> ValidateAsync(MetadataExtractionResult extractionResult)
        {
            var result = new ValidationResult { IsValid = true };

            await Task.Run(() =>
            {
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("GPS_Latitude") == true ||
                    extractionResult.FormatSpecificMetadata?.ContainsKey("GPS_Longitude") == true)
                {
                    if (extractionResult.FormatSpecificMetadata["GPS_Latitude"] != null ||
                        extractionResult.FormatSpecificMetadata["GPS_Longitude"] != null)
                    {
                        result.IsValid = false;
                        result.Threats.Add("GPS location data found in EXIF metadata");
                    }
                }

                // Check for embedded thumbnails that might contain malicious data
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasEmbeddedThumbnails") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasEmbeddedThumbnails"] ?? false))
                {
                    result.Threats.Add("Embedded thumbnails detected in image metadata");
                }

                // Check for suspicious camera make/model strings
                var cameraMake = extractionResult.FormatSpecificMetadata?.GetValueOrDefault("CameraMake") as string;
                var cameraModel = extractionResult.FormatSpecificMetadata?.GetValueOrDefault("CameraModel") as string;

                if (!string.IsNullOrEmpty(cameraMake) && cameraMake.Length > 100)
                {
                    result.IsValid = false;
                    result.Threats.Add("Unusually long camera make string detected");
                }

                if (!string.IsNullOrEmpty(cameraModel) && cameraModel.Length > 100)
                {
                    result.IsValid = false;
                    result.Threats.Add("Unusually long camera model string detected");
                }
            });

            return result;
        }
    }

    /// <summary>
    /// Document security validator
    /// </summary>
    public class DocumentSecurityValidator : IMetadataValidator
    {
        public string ValidatorName => "Document Security Validator";

        public async Task<ValidationResult> ValidateAsync(MetadataExtractionResult extractionResult)
        {
            var result = new ValidationResult { IsValid = true };

            await Task.Run(() =>
            {
                // Check for macros
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasMacros") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasMacros"] ?? false))
                {
                    result.IsValid = false;
                    result.Threats.Add("Document contains macros");
                }

                // Check for embedded objects
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasEmbeddedObjects") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasEmbeddedObjects"] ?? false))
                {
                    result.IsValid = false;
                    result.Threats.Add("Document contains embedded objects");
                }

                // Check for hyperlinks
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasHyperlinks") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasHyperlinks"] ?? false))
                {
                    result.Threats.Add("Document contains hyperlinks");
                }

                // Check for password protection (might indicate suspicious content)
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("IsPasswordProtected") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["IsPasswordProtected"] ?? false))
                {
                    result.Threats.Add("Document is password protected");
                }
            });

            return result;
        }
    }

    /// <summary>
    /// Archive security validator
    /// </summary>
    public class ArchiveSecurityValidator : IMetadataValidator
    {
        public string ValidatorName => "Archive Security Validator";

        public async Task<ValidationResult> ValidateAsync(MetadataExtractionResult extractionResult)
        {
            var result = new ValidationResult { IsValid = true };

            await Task.Run(() =>
            {
                // Check for executable files in archive
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasExecutableFiles") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasExecutableFiles"] ?? false))
                {
                    result.IsValid = false;
                    result.Threats.Add("Archive contains executable files");
                }

                // Check for password protection
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("IsPasswordProtected") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["IsPasswordProtected"] ?? false))
                {
                    result.Threats.Add("Archive is password protected");
                }

                // Check for unusually high compression ratio (zip bomb indicator)
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("CompressionRatio") == true)
                {
                    var ratio = (double)(extractionResult.FormatSpecificMetadata["CompressionRatio"] ?? 0.0);
                    if (ratio > 100) // More than 100:1 compression ratio
                    {
                        result.IsValid = false;
                        result.Threats.Add("Unusually high compression ratio detected (potential zip bomb)");
                    }
                }
            });

            return result;
        }
    }

    /// <summary>
    /// Media security validator
    /// </summary>
    public class MediaSecurityValidator : IMetadataValidator
    {
        public string ValidatorName => "Media Security Validator";

        public async Task<ValidationResult> ValidateAsync(MetadataExtractionResult extractionResult)
        {
            var result = new ValidationResult { IsValid = true };

            await Task.Run(() =>
            {
                // Check for embedded lyrics or subtitles that might contain malicious content
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasLyrics") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasLyrics"] ?? false))
                {
                    result.Threats.Add("Media file contains embedded lyrics");
                }

                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasSubtitles") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasSubtitles"] ?? false))
                {
                    result.Threats.Add("Media file contains embedded subtitles");
                }

                // Check for album art in audio files
                if (extractionResult.FormatSpecificMetadata?.ContainsKey("HasAlbumArt") == true &&
                    (bool)(extractionResult.FormatSpecificMetadata["HasAlbumArt"] ?? false))
                {
                    result.Threats.Add("Audio file contains embedded album art");
                }
            });

            return result;
        }
    }

    /// <summary>
    /// General metadata security validator
    /// </summary>
    public class GeneralMetadataSecurityValidator : IMetadataValidator
    {
        public string ValidatorName => "General Metadata Security Validator";

        public async Task<ValidationResult> ValidateAsync(MetadataExtractionResult extractionResult)
        {
            var result = new ValidationResult { IsValid = true };

            await Task.Run(() =>
            {
                // Check file size vs. metadata consistency
                if (extractionResult.BasicMetadata != null)
                {
                    // Check for files with zero size but claiming to have content
                    if (extractionResult.BasicMetadata.FileSize == 0 && 
                        extractionResult.FormatSpecificMetadata?.Any() == true)
                    {
                        result.IsValid = false;
                        result.Threats.Add("Zero-byte file with metadata detected");
                    }

                    // Check for unusually long file names
                    if (extractionResult.BasicMetadata.FileName?.Length > 255)
                    {
                        result.IsValid = false;
                        result.Threats.Add("Unusually long filename detected");
                    }

                    // Check for suspicious file extensions
                    var extension = extractionResult.BasicMetadata.FileExtension?.ToLowerInvariant();
                    var suspiciousExtensions = new[] { ".exe", ".bat", ".cmd", ".scr", ".pif", ".com", ".vbs", ".js" };
                    
                    if (suspiciousExtensions.Contains(extension))
                    {
                        result.IsValid = false;
                        result.Threats.Add($"Suspicious file extension detected: {extension}");
                    }
                }
            });

            return result;
        }
    }

    /// <summary>
    /// Validation result for individual validators
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Threats { get; set; } = new List<string>();
    }
}
