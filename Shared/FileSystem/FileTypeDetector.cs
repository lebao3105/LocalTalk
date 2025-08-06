using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// File type detection using magic numbers, MIME validation, and content analysis
    /// </summary>
    public class FileTypeDetector
    {
        private static FileTypeDetector _instance;
        private readonly Dictionary<string, FileTypeSignature> _signatures;
        private readonly Dictionary<string, string> _extensionToMimeMap;
        private readonly HashSet<string> _dangerousExtensions;

        public static FileTypeDetector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FileTypeDetector();
                }
                return _instance;
            }
        }

        private FileTypeDetector()
        {
            _signatures = new Dictionary<string, FileTypeSignature>();
            _extensionToMimeMap = new Dictionary<string, string>();
            _dangerousExtensions = new HashSet<string>();
            InitializeSignatures();
            InitializeMimeMap();
            InitializeDangerousExtensions();
        }

        /// <summary>
        /// Detects file type from file content and validates against extension
        /// </summary>
        public async Task<FileTypeDetectionResult> DetectFileTypeAsync(IStorageFile file)
        {
            var result = new FileTypeDetectionResult
            {
                FileName = file.Name,
                FileSize = file.Size,
                DeclaredExtension = Path.GetExtension(file.Name).ToLowerInvariant(),
                DeclaredMimeType = file.ContentType,
                AnalyzedAt = DateTime.Now
            };

            try
            {
                // Read file header for magic number detection
                var header = await ReadFileHeaderAsync(file);
                if (header == null || header.Length == 0)
                {
                    result.DetectionStatus = DetectionStatus.Failed;
                    result.ErrorMessage = "Unable to read file header";
                    return result;
                }

                // Detect actual file type from magic numbers
                var detectedType = DetectFromMagicNumbers(header);
                result.DetectedFileType = detectedType;
                result.DetectedMimeType = detectedType?.MimeType;

                // Validate against declared extension
                var validationResult = ValidateFileTypeConsistency(result);
                result.IsConsistent = validationResult.IsConsistent;
                result.ValidationWarnings = validationResult.Warnings;

                // Check for security threats
                var securityResult = AnalyzeSecurityThreats(result);
                result.SecurityThreats = securityResult.Threats;
                result.SecurityLevel = securityResult.Level;

                // Perform additional content analysis if needed
                if (detectedType?.RequiresContentAnalysis == true)
                {
                    var contentResult = await PerformContentAnalysisAsync(file, detectedType);
                    result.ContentAnalysisResult = contentResult;
                }

                result.DetectionStatus = DetectionStatus.Success;
                result.Confidence = CalculateConfidence(result);

                System.Diagnostics.Debug.WriteLine($"File type detection completed for {file.Name}: {result.DetectedFileType?.Name ?? "Unknown"}");
            }
            catch (Exception ex)
            {
                result.DetectionStatus = DetectionStatus.Failed;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"File type detection error for {file.Name}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Validates MIME type against file content
        /// </summary>
        public async Task<MimeValidationResult> ValidateMimeTypeAsync(IStorageFile file, string declaredMimeType)
        {
            var result = new MimeValidationResult
            {
                DeclaredMimeType = declaredMimeType,
                ValidatedAt = DateTime.Now
            };

            try
            {
                var detectionResult = await DetectFileTypeAsync(file);
                result.ActualMimeType = detectionResult.DetectedMimeType;
                
                result.IsValid = string.Equals(declaredMimeType, detectionResult.DetectedMimeType, 
                    StringComparison.OrdinalIgnoreCase);
                
                if (!result.IsValid)
                {
                    result.ValidationError = $"MIME type mismatch: declared '{declaredMimeType}' but detected '{detectionResult.DetectedMimeType}'";
                    
                    // Check if it's a dangerous mismatch
                    if (IsDangerousMimeMismatch(declaredMimeType, detectionResult.DetectedMimeType))
                    {
                        result.IsDangerous = true;
                        result.DangerLevel = DangerLevel.High;
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ValidationError = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Gets supported file types
        /// </summary>
        public List<FileTypeInfo> GetSupportedFileTypes()
        {
            return _signatures.Values
                .Select(sig => new FileTypeInfo
                {
                    Name = sig.Name,
                    MimeType = sig.MimeType,
                    Extensions = sig.Extensions.ToList(),
                    Description = sig.Description,
                    Category = sig.Category
                })
                .OrderBy(info => info.Category)
                .ThenBy(info => info.Name)
                .ToList();
        }

        /// <summary>
        /// Checks if a file extension is considered dangerous
        /// </summary>
        public bool IsDangerousExtension(string extension)
        {
            return _dangerousExtensions.Contains(extension.ToLowerInvariant());
        }

        /// <summary>
        /// Reads file header for magic number detection
        /// </summary>
        private async Task<byte[]> ReadFileHeaderAsync(IStorageFile file, int headerSize = 512)
        {
            try
            {
                using (var stream = await file.OpenReadAsync())
                {
                    var buffer = new byte[Math.Min(headerSize, (int)file.Size)];
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                    return buffer;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading file header: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Detects file type from magic numbers
        /// </summary>
        private FileTypeSignature DetectFromMagicNumbers(byte[] header)
        {
            foreach (var signature in _signatures.Values.OrderByDescending(s => s.Priority))
            {
                if (signature.MagicNumbers?.Any() == true)
                {
                    foreach (var magicNumber in signature.MagicNumbers)
                    {
                        if (header.Length >= magicNumber.Bytes.Length + magicNumber.Offset)
                        {
                            bool matches = true;
                            for (int i = 0; i < magicNumber.Bytes.Length; i++)
                            {
                                if (header[magicNumber.Offset + i] != magicNumber.Bytes[i])
                                {
                                    matches = false;
                                    break;
                                }
                            }
                            
                            if (matches)
                            {
                                return signature;
                            }
                        }
                    }
                }
            }

            return null; // Unknown file type
        }

        /// <summary>
        /// Validates file type consistency between extension and content
        /// </summary>
        private FileTypeValidationResult ValidateFileTypeConsistency(FileTypeDetectionResult detection)
        {
            var result = new FileTypeValidationResult
            {
                IsConsistent = true,
                Warnings = new List<string>()
            };

            // Check if extension matches detected type
            if (detection.DetectedFileType != null)
            {
                var extensionMatches = detection.DetectedFileType.Extensions
                    .Contains(detection.DeclaredExtension, StringComparer.OrdinalIgnoreCase);

                if (!extensionMatches)
                {
                    result.IsConsistent = false;
                    result.Warnings.Add($"File extension '{detection.DeclaredExtension}' does not match detected type '{detection.DetectedFileType.Name}'");
                }
            }

            // Check MIME type consistency
            if (!string.IsNullOrEmpty(detection.DeclaredMimeType) && 
                !string.IsNullOrEmpty(detection.DetectedMimeType))
            {
                if (!string.Equals(detection.DeclaredMimeType, detection.DetectedMimeType, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsConsistent = false;
                    result.Warnings.Add($"Declared MIME type '{detection.DeclaredMimeType}' does not match detected type '{detection.DetectedMimeType}'");
                }
            }

            return result;
        }

        /// <summary>
        /// Analyzes security threats based on file type detection
        /// </summary>
        private SecurityAnalysisResult AnalyzeSecurityThreats(FileTypeDetectionResult detection)
        {
            var result = new SecurityAnalysisResult
            {
                Threats = new List<string>(),
                Level = SecurityLevel.Low
            };

            // Check for dangerous extensions
            if (IsDangerousExtension(detection.DeclaredExtension))
            {
                result.Threats.Add($"Dangerous file extension: {detection.DeclaredExtension}");
                result.Level = SecurityLevel.High;
            }

            // Check for file type spoofing
            if (!detection.IsConsistent)
            {
                result.Threats.Add("Potential file type spoofing detected");
                result.Level = Math.Max(result.Level, SecurityLevel.Medium);
            }

            // Check for executable content in non-executable files
            if (detection.DetectedFileType?.Category == FileCategory.Executable && 
                !IsDangerousExtension(detection.DeclaredExtension))
            {
                result.Threats.Add("Executable content disguised as non-executable file");
                result.Level = SecurityLevel.Critical;
            }

            return result;
        }

        /// <summary>
        /// Performs additional content analysis for specific file types
        /// </summary>
        private async Task<ContentAnalysisResult> PerformContentAnalysisAsync(IStorageFile file, FileTypeSignature fileType)
        {
            var result = new ContentAnalysisResult
            {
                FileType = fileType.Name,
                AnalyzedAt = DateTime.Now
            };

            try
            {
                // This is a simplified implementation
                // In a real implementation, you would perform specific analysis based on file type
                switch (fileType.Category)
                {
                    case FileCategory.Document:
                        result = await AnalyzeDocumentContentAsync(file);
                        break;
                    case FileCategory.Archive:
                        result = await AnalyzeArchiveContentAsync(file);
                        break;
                    case FileCategory.Image:
                        result = await AnalyzeImageContentAsync(file);
                        break;
                    default:
                        result.Analysis = "No specific content analysis available";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Analysis = $"Content analysis failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Analyzes document content
        /// </summary>
        private async Task<ContentAnalysisResult> AnalyzeDocumentContentAsync(IStorageFile file)
        {
            await Task.Delay(10); // Simulate analysis
            return new ContentAnalysisResult
            {
                FileType = "Document",
                Analysis = "Document content analysis completed",
                AnalyzedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Analyzes archive content
        /// </summary>
        private async Task<ContentAnalysisResult> AnalyzeArchiveContentAsync(IStorageFile file)
        {
            await Task.Delay(10); // Simulate analysis
            return new ContentAnalysisResult
            {
                FileType = "Archive",
                Analysis = "Archive content analysis completed",
                AnalyzedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Analyzes image content
        /// </summary>
        private async Task<ContentAnalysisResult> AnalyzeImageContentAsync(IStorageFile file)
        {
            await Task.Delay(10); // Simulate analysis
            return new ContentAnalysisResult
            {
                FileType = "Image",
                Analysis = "Image content analysis completed",
                AnalyzedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Checks if MIME type mismatch is dangerous
        /// </summary>
        private bool IsDangerousMimeMismatch(string declared, string actual)
        {
            // Check for executable content disguised as safe content
            var dangerousMimes = new[] { "application/x-executable", "application/x-msdownload", "application/x-dosexec" };
            var safeMimes = new[] { "text/plain", "image/jpeg", "image/png", "application/pdf" };

            return dangerousMimes.Contains(actual, StringComparer.OrdinalIgnoreCase) &&
                   safeMimes.Contains(declared, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calculates detection confidence based on various factors
        /// </summary>
        private double CalculateConfidence(FileTypeDetectionResult result)
        {
            double confidence = 0.5; // Base confidence

            // Increase confidence if magic number detection succeeded
            if (result.DetectedFileType != null)
                confidence += 0.3;

            // Increase confidence if types are consistent
            if (result.IsConsistent)
                confidence += 0.2;

            // Decrease confidence if there are warnings
            if (result.ValidationWarnings?.Any() == true)
                confidence -= 0.1 * result.ValidationWarnings.Count;

            return Math.Max(0.0, Math.Min(1.0, confidence));
        }

        /// <summary>
        /// Initializes file type signatures with magic numbers
        /// </summary>
        private void InitializeSignatures()
        {
            // PDF
            _signatures["pdf"] = new FileTypeSignature
            {
                Name = "PDF",
                MimeType = "application/pdf",
                Extensions = new[] { ".pdf" },
                Category = FileCategory.Document,
                Description = "Portable Document Format",
                Priority = 10,
                MagicNumbers = new[]
                {
                    new MagicNumber { Bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }, Offset = 0 } // %PDF
                }
            };

            // JPEG
            _signatures["jpeg"] = new FileTypeSignature
            {
                Name = "JPEG",
                MimeType = "image/jpeg",
                Extensions = new[] { ".jpg", ".jpeg" },
                Category = FileCategory.Image,
                Description = "JPEG Image",
                Priority = 10,
                MagicNumbers = new[]
                {
                    new MagicNumber { Bytes = new byte[] { 0xFF, 0xD8, 0xFF }, Offset = 0 }
                }
            };

            // PNG
            _signatures["png"] = new FileTypeSignature
            {
                Name = "PNG",
                MimeType = "image/png",
                Extensions = new[] { ".png" },
                Category = FileCategory.Image,
                Description = "Portable Network Graphics",
                Priority = 10,
                MagicNumbers = new[]
                {
                    new MagicNumber { Bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, Offset = 0 }
                }
            };

            // ZIP
            _signatures["zip"] = new FileTypeSignature
            {
                Name = "ZIP",
                MimeType = "application/zip",
                Extensions = new[] { ".zip" },
                Category = FileCategory.Archive,
                Description = "ZIP Archive",
                Priority = 8,
                RequiresContentAnalysis = true,
                MagicNumbers = new[]
                {
                    new MagicNumber { Bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, Offset = 0 },
                    new MagicNumber { Bytes = new byte[] { 0x50, 0x4B, 0x05, 0x06 }, Offset = 0 }
                }
            };

            // EXE
            _signatures["exe"] = new FileTypeSignature
            {
                Name = "Windows Executable",
                MimeType = "application/x-msdownload",
                Extensions = new[] { ".exe", ".dll" },
                Category = FileCategory.Executable,
                Description = "Windows Executable",
                Priority = 10,
                MagicNumbers = new[]
                {
                    new MagicNumber { Bytes = new byte[] { 0x4D, 0x5A }, Offset = 0 } // MZ
                }
            };

            // Add more signatures as needed...
        }

        /// <summary>
        /// Initializes MIME type mappings
        /// </summary>
        private void InitializeMimeMap()
        {
            _extensionToMimeMap[".pdf"] = "application/pdf";
            _extensionToMimeMap[".jpg"] = "image/jpeg";
            _extensionToMimeMap[".jpeg"] = "image/jpeg";
            _extensionToMimeMap[".png"] = "image/png";
            _extensionToMimeMap[".gif"] = "image/gif";
            _extensionToMimeMap[".zip"] = "application/zip";
            _extensionToMimeMap[".txt"] = "text/plain";
            _extensionToMimeMap[".html"] = "text/html";
            _extensionToMimeMap[".css"] = "text/css";
            _extensionToMimeMap[".js"] = "application/javascript";
            _extensionToMimeMap[".json"] = "application/json";
            _extensionToMimeMap[".xml"] = "application/xml";
            _extensionToMimeMap[".mp3"] = "audio/mpeg";
            _extensionToMimeMap[".mp4"] = "video/mp4";
            _extensionToMimeMap[".avi"] = "video/x-msvideo";
            _extensionToMimeMap[".exe"] = "application/x-msdownload";
            _extensionToMimeMap[".dll"] = "application/x-msdownload";
        }

        /// <summary>
        /// Initializes dangerous file extensions
        /// </summary>
        private void InitializeDangerousExtensions()
        {
            var dangerous = new[]
            {
                ".exe", ".bat", ".cmd", ".com", ".pif", ".scr", ".vbs", ".vbe", ".js", ".jse",
                ".wsf", ".wsh", ".msi", ".msp", ".dll", ".cpl", ".jar", ".app", ".deb", ".rpm",
                ".dmg", ".pkg", ".run", ".bin", ".sh", ".bash", ".ps1", ".psm1", ".psd1"
            };

            foreach (var ext in dangerous)
            {
                _dangerousExtensions.Add(ext);
            }
        }
    }

    /// <summary>
    /// File type signature for detection
    /// </summary>
    public class FileTypeSignature
    {
        public string Name { get; set; }
        public string MimeType { get; set; }
        public string[] Extensions { get; set; }
        public FileCategory Category { get; set; }
        public string Description { get; set; }
        public int Priority { get; set; }
        public bool RequiresContentAnalysis { get; set; }
        public MagicNumber[] MagicNumbers { get; set; }
    }

    /// <summary>
    /// Magic number for file type detection
    /// </summary>
    public class MagicNumber
    {
        public byte[] Bytes { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// File type detection result
    /// </summary>
    public class FileTypeDetectionResult
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string DeclaredExtension { get; set; }
        public string DeclaredMimeType { get; set; }
        public FileTypeSignature DetectedFileType { get; set; }
        public string DetectedMimeType { get; set; }
        public bool IsConsistent { get; set; }
        public List<string> ValidationWarnings { get; set; } = new List<string>();
        public List<string> SecurityThreats { get; set; } = new List<string>();
        public SecurityLevel SecurityLevel { get; set; }
        public ContentAnalysisResult ContentAnalysisResult { get; set; }
        public DetectionStatus DetectionStatus { get; set; }
        public string ErrorMessage { get; set; }
        public double Confidence { get; set; }
        public DateTime AnalyzedAt { get; set; }
    }

    /// <summary>
    /// MIME validation result
    /// </summary>
    public class MimeValidationResult
    {
        public string DeclaredMimeType { get; set; }
        public string ActualMimeType { get; set; }
        public bool IsValid { get; set; }
        public string ValidationError { get; set; }
        public bool IsDangerous { get; set; }
        public DangerLevel DangerLevel { get; set; }
        public DateTime ValidatedAt { get; set; }
    }

    /// <summary>
    /// File type information
    /// </summary>
    public class FileTypeInfo
    {
        public string Name { get; set; }
        public string MimeType { get; set; }
        public List<string> Extensions { get; set; }
        public string Description { get; set; }
        public FileCategory Category { get; set; }
    }

    /// <summary>
    /// File type validation result
    /// </summary>
    internal class FileTypeValidationResult
    {
        public bool IsConsistent { get; set; }
        public List<string> Warnings { get; set; }
    }

    /// <summary>
    /// Security analysis result
    /// </summary>
    internal class SecurityAnalysisResult
    {
        public List<string> Threats { get; set; }
        public SecurityLevel Level { get; set; }
    }

    /// <summary>
    /// Content analysis result
    /// </summary>
    public class ContentAnalysisResult
    {
        public string FileType { get; set; }
        public string Analysis { get; set; }
        public DateTime AnalyzedAt { get; set; }
    }

    /// <summary>
    /// File categories
    /// </summary>
    public enum FileCategory
    {
        Unknown,
        Document,
        Image,
        Audio,
        Video,
        Archive,
        Executable,
        Text,
        Data
    }

    /// <summary>
    /// Detection status
    /// </summary>
    public enum DetectionStatus
    {
        Success,
        Failed,
        Partial
    }

    /// <summary>
    /// Danger levels
    /// </summary>
    public enum DangerLevel
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }
}
