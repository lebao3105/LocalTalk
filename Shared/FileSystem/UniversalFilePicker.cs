using Shared.Platform;
using Shared.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.FileSystem
{
    /// <summary>
    /// Universal file picker with cross-platform support and security validation
    /// </summary>
    public class UniversalFilePicker
    {
        private static UniversalFilePicker _instance;
        private readonly IPlatformAbstraction _platform;
        private readonly FileSystemSecurityAnalyzer _securityAnalyzer;
        private readonly FilePickerConfiguration _config;

        public static UniversalFilePicker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new UniversalFilePicker();
                }
                return _instance;
            }
        }

        public event EventHandler<FilePickerEventArgs> FileSelected;
        public event EventHandler<FilePickerEventArgs> FilesSelected;
        public event EventHandler<FilePickerErrorEventArgs> ErrorOccurred;

        private UniversalFilePicker()
        {
            _platform = PlatformFactory.Current;
            _securityAnalyzer = FileSystemSecurityAnalyzer.Instance;
            _config = new FilePickerConfiguration();
        }

        /// <summary>
        /// Picks a single file with security validation
        /// </summary>
        public async Task<FilePickerResult> PickSingleFileAsync(FilePickerOptions options = null)
        {
            var result = new FilePickerResult
            {
                Success = false,
                PickerType = FilePickerType.SingleFile,
                Timestamp = DateTime.Now
            };

            try
            {
                options = options ?? new FilePickerOptions();

                // Check if file picker is supported on this platform
                if (!PlatformFactory.Features.SupportsFilePickers)
                {
                    result.ErrorMessage = "File picker not supported on this platform";
                    OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, Options = options });
                    return result;
                }

                // Get platform file picker
                var filePicker = _platform.GetFilePicker();

                // Configure file type filters
                if (options.FileTypeFilters?.Any() == true)
                {
                    filePicker.SetFileTypeFilter(options.FileTypeFilters.ToArray());
                }

                // Pick the file
                var selectedFile = await filePicker.PickSingleFileAsync();

                if (selectedFile != null)
                {
                    // Perform security analysis
                    var securityResult = await _securityAnalyzer.AnalyzePathAsync(selectedFile.Path);

                    if (securityResult.SecurityLevel >= SecurityLevel.Critical)
                    {
                        result.ErrorMessage = $"File rejected for security reasons: {string.Join(", ", securityResult.SecurityThreats)}";
                        OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, SecurityResult = securityResult });
                        return result;
                    }

                    // Create file info with security analysis
                    var fileInfo = new SecureFileInfo
                    {
                        File = selectedFile,
                        SecurityAnalysis = securityResult,
                        PickedAt = DateTime.Now
                    };

                    result.Success = true;
                    result.SelectedFile = fileInfo;
                    result.SecurityLevel = securityResult.SecurityLevel;

                    // Raise event
                    OnFileSelected(new FilePickerEventArgs { FileInfo = fileInfo, Options = options });
                }
                else
                {
                    result.ErrorMessage = "No file selected";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error picking file: {ex.Message}";
                OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, Exception = ex, Options = options });
            }

            return result;
        }

        /// <summary>
        /// Picks multiple files with security validation
        /// </summary>
        public async Task<FilePickerResult> PickMultipleFilesAsync(FilePickerOptions options = null)
        {
            var result = new FilePickerResult
            {
                Success = false,
                PickerType = FilePickerType.MultipleFiles,
                Timestamp = DateTime.Now
            };

            try
            {
                options = options ?? new FilePickerOptions();

                // Check if file picker is supported on this platform
                if (!PlatformFactory.Features.SupportsFilePickers)
                {
                    result.ErrorMessage = "File picker not supported on this platform";
                    OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, Options = options });
                    return result;
                }

                // Get platform file picker
                var filePicker = _platform.GetFilePicker();

                // Configure file type filters
                if (options.FileTypeFilters?.Any() == true)
                {
                    filePicker.SetFileTypeFilter(options.FileTypeFilters.ToArray());
                }

                // Pick the files
                var selectedFiles = await filePicker.PickMultipleFilesAsync();

                if (selectedFiles?.Any() == true)
                {
                    var secureFiles = new List<SecureFileInfo>();
                    var rejectedFiles = new List<string>();
                    var maxSecurityLevel = SecurityLevel.Low;

                    // Analyze each selected file
                    foreach (var file in selectedFiles)
                    {
                        var securityResult = await _securityAnalyzer.AnalyzePathAsync(file.Path);

                        if (securityResult.SecurityLevel >= SecurityLevel.Critical)
                        {
                            rejectedFiles.Add($"{file.Name}: {string.Join(", ", securityResult.SecurityThreats)}");
                            continue;
                        }

                        // Track highest security level
                        if (securityResult.SecurityLevel > maxSecurityLevel)
                        {
                            maxSecurityLevel = securityResult.SecurityLevel;
                        }

                        var fileInfo = new SecureFileInfo
                        {
                            File = file,
                            SecurityAnalysis = securityResult,
                            PickedAt = DateTime.Now
                        };

                        secureFiles.Add(fileInfo);
                    }

                    if (secureFiles.Any())
                    {
                        result.Success = true;
                        result.SelectedFiles = secureFiles;
                        result.SecurityLevel = maxSecurityLevel;
                        result.RejectedFiles = rejectedFiles;

                        // Raise event
                        OnFilesSelected(new FilePickerEventArgs { FileInfos = secureFiles, Options = options });
                    }
                    else
                    {
                        result.ErrorMessage = "All selected files were rejected for security reasons";
                        result.RejectedFiles = rejectedFiles;
                    }
                }
                else
                {
                    result.ErrorMessage = "No files selected";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error picking files: {ex.Message}";
                OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, Exception = ex, Options = options });
            }

            return result;
        }

        /// <summary>
        /// Picks a folder with security validation
        /// </summary>
        public async Task<FilePickerResult> PickFolderAsync(FilePickerOptions options = null)
        {
            var result = new FilePickerResult
            {
                Success = false,
                PickerType = FilePickerType.Folder,
                Timestamp = DateTime.Now
            };

            try
            {
                options = options ?? new FilePickerOptions();

                // Check if folder picker is supported on this platform
                if (!PlatformFactory.Features.SupportsFilePickers)
                {
                    result.ErrorMessage = "Folder picker not supported on this platform";
                    OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, Options = options });
                    return result;
                }

                // Get platform file picker
                var filePicker = _platform.GetFilePicker();

                // Pick the folder
                var selectedFolder = await filePicker.PickFolderAsync();

                if (selectedFolder != null)
                {
                    // Perform security analysis on folder path
                    var securityResult = await _securityAnalyzer.AnalyzePathAsync(selectedFolder.Path);

                    if (securityResult.SecurityLevel >= SecurityLevel.Critical)
                    {
                        result.ErrorMessage = $"Folder rejected for security reasons: {string.Join(", ", securityResult.SecurityThreats)}";
                        OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, SecurityResult = securityResult });
                        return result;
                    }

                    // Create folder info with security analysis
                    var folderInfo = new SecureFolderInfo
                    {
                        Folder = selectedFolder,
                        SecurityAnalysis = securityResult,
                        PickedAt = DateTime.Now
                    };

                    result.Success = true;
                    result.SelectedFolder = folderInfo;
                    result.SecurityLevel = securityResult.SecurityLevel;
                }
                else
                {
                    result.ErrorMessage = "No folder selected";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error picking folder: {ex.Message}";
                OnError(new FilePickerErrorEventArgs { Message = result.ErrorMessage, Exception = ex, Options = options });
            }

            return result;
        }

        /// <summary>
        /// Gets available file type filters for the current platform
        /// </summary>
        public List<FileTypeFilter> GetAvailableFileTypeFilters()
        {
            var filters = new List<FileTypeFilter>();

            // Common file type filters
            filters.Add(new FileTypeFilter
            {
                Name = "All Files",
                Extensions = new[] { "*" },
                Description = "All file types"
            });

            filters.Add(new FileTypeFilter
            {
                Name = "Images",
                Extensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" },
                Description = "Image files"
            });

            filters.Add(new FileTypeFilter
            {
                Name = "Documents",
                Extensions = new[] { ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" },
                Description = "Document files"
            });

            filters.Add(new FileTypeFilter
            {
                Name = "Videos",
                Extensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" },
                Description = "Video files"
            });

            filters.Add(new FileTypeFilter
            {
                Name = "Audio",
                Extensions = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma" },
                Description = "Audio files"
            });

            filters.Add(new FileTypeFilter
            {
                Name = "Archives",
                Extensions = new[] { ".zip", ".rar", ".7z", ".tar", ".gz" },
                Description = "Archive files"
            });

            return filters;
        }

        /// <summary>
        /// Validates file selection against security policies
        /// </summary>
        /// <param name="files">The files to validate</param>
        /// <returns>Validation result indicating if files are safe</returns>
        /// <exception cref="ArgumentNullException">Thrown when files is null</exception>
        public async Task<ValidationResult> ValidateFileSelectionAsync(IEnumerable<IStorageFile> files)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files), "Files collection cannot be null");

            var result = new ValidationResult { IsValid = true };
            var filesList = files.ToList();

            if (filesList.Count == 0)
            {
                result.Warnings.Add("No files provided for validation");
                return result;
            }

            try
            {
                foreach (var file in filesList)
                {
                    if (file == null)
                    {
                        result.IsValid = false;
                        result.Errors.Add("File collection contains null file reference");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(file.Path))
                    {
                        result.IsValid = false;
                        result.Errors.Add("File has empty or null path");
                        continue;
                    }

                    var securityResult = await _securityAnalyzer.AnalyzePathAsync(file.Path);

                    if (securityResult.SecurityLevel >= SecurityLevel.Critical)
                    {
                        result.IsValid = false;
                        result.Errors.AddRange(securityResult.SecurityThreats);
                    }
                    else if (securityResult.SecurityLevel >= SecurityLevel.High)
                    {
                        result.Warnings.AddRange(securityResult.SecurityThreats);
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Validation failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Raises the FileSelected event
        /// </summary>
        private void OnFileSelected(FilePickerEventArgs args)
        {
            FileSelected?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the FilesSelected event
        /// </summary>
        private void OnFilesSelected(FilePickerEventArgs args)
        {
            FilesSelected?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ErrorOccurred event
        /// </summary>
        private void OnError(FilePickerErrorEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"File Picker Error: {args.Message}");
            ErrorOccurred?.Invoke(this, args);
        }
    }

    /// <summary>
    /// File picker configuration
    /// </summary>
    public class FilePickerConfiguration
    {
        public bool EnableSecurityValidation { get; set; } = true;
        public SecurityLevel MaxAllowedSecurityLevel { get; set; } = SecurityLevel.High;
        public int MaxFileCount { get; set; } = 100;
        public long MaxFileSize { get; set; } = 1024L * 1024L * 1024L; // 1GB
        public bool AllowSystemFiles { get; set; } = false;
        public bool AllowExecutableFiles { get; set; } = false;
    }

    /// <summary>
    /// File picker options
    /// </summary>
    public class FilePickerOptions
    {
        public List<string> FileTypeFilters { get; set; } = new List<string>();
        public string SuggestedStartLocation { get; set; }
        public FilePickerViewMode ViewMode { get; set; } = FilePickerViewMode.List;
        public bool AllowMultipleSelection { get; set; } = false;
        public string Title { get; set; }
    }

    /// <summary>
    /// File picker view modes
    /// </summary>
    public enum FilePickerViewMode
    {
        List,
        Thumbnail,
        Details
    }

    /// <summary>
    /// File picker types
    /// </summary>
    public enum FilePickerType
    {
        SingleFile,
        MultipleFiles,
        Folder
    }

    /// <summary>
    /// File picker result
    /// </summary>
    public class FilePickerResult
    {
        public bool Success { get; set; }
        public FilePickerType PickerType { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; }
        public SecurityLevel SecurityLevel { get; set; }
        public SecureFileInfo SelectedFile { get; set; }
        public List<SecureFileInfo> SelectedFiles { get; set; } = new List<SecureFileInfo>();
        public SecureFolderInfo SelectedFolder { get; set; }
        public List<string> RejectedFiles { get; set; } = new List<string>();
    }

    /// <summary>
    /// Secure file information with security analysis
    /// </summary>
    public class SecureFileInfo
    {
        public IStorageFile File { get; set; }
        public FileSystemAnalysisResult SecurityAnalysis { get; set; }
        public DateTime PickedAt { get; set; }
        public bool IsSecure => SecurityAnalysis?.SecurityLevel < SecurityLevel.High;
        public string SecuritySummary => SecurityAnalysis != null ?
            $"Security Level: {SecurityAnalysis.SecurityLevel}, Threats: {SecurityAnalysis.SecurityThreats.Count}" :
            "No security analysis";
    }

    /// <summary>
    /// Secure folder information with security analysis
    /// </summary>
    public class SecureFolderInfo
    {
        public IStorageFolder Folder { get; set; }
        public FileSystemAnalysisResult SecurityAnalysis { get; set; }
        public DateTime PickedAt { get; set; }
        public bool IsSecure => SecurityAnalysis?.SecurityLevel < SecurityLevel.High;
    }

    /// <summary>
    /// File type filter
    /// </summary>
    public class FileTypeFilter
    {
        public string Name { get; set; }
        public string[] Extensions { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// File picker event arguments
    /// </summary>
    public class FilePickerEventArgs : EventArgs
    {
        public SecureFileInfo FileInfo { get; set; }
        public List<SecureFileInfo> FileInfos { get; set; }
        public SecureFolderInfo FolderInfo { get; set; }
        public FilePickerOptions Options { get; set; }
    }

    /// <summary>
    /// File picker error event arguments
    /// </summary>
    public class FilePickerErrorEventArgs : EventArgs
    {
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public FilePickerOptions Options { get; set; }
        public FileSystemAnalysisResult SecurityResult { get; set; }
    }

    /// <summary>
    /// Validation result for file operations
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
