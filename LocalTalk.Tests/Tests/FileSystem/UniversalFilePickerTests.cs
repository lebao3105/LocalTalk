using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalTalk.Tests.TestFramework;
using Shared.FileSystem;
using Shared.Platform;

namespace LocalTalk.Tests.Tests.FileSystem
{
    [TestClass]
    public class UniversalFilePickerTests
    {
        private MockPlatform _mockPlatform;
        private MockFilePicker _mockFilePicker;
        private MockSecurityAnalyzer _mockSecurityAnalyzer;
        private UniversalFilePicker _filePicker;

        [TestInitialize]
        public void Setup()
        {
            _mockPlatform = new MockPlatform();
            _mockFilePicker = new MockFilePicker();
            _mockSecurityAnalyzer = new MockSecurityAnalyzer();
            
            // Setup mock platform to return our mock file picker
            _mockPlatform.SetFilePicker(_mockFilePicker);
            
            // Reset singleton and inject mock dependencies
            ResetFilePickerSingleton();
            _filePicker = UniversalFilePicker.Instance;
        }

        [TestCleanup]
        public void Cleanup()
        {
            ResetFilePickerSingleton();
        }

        #region Single File Picker Tests

        [TestMethod]
        public async Task PickSingleFileAsync_WithValidFile_ReturnsSuccessResult()
        {
            // Arrange
            var mockFile = new MockStorageFile("test.txt", "C:\\test.txt", 1024);
            _mockFilePicker.SetSingleFileResult(mockFile);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Low, new List<string>());

            // Act
            var result = await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(FilePickerType.SingleFile, result.PickerType);
            Assert.IsNotNull(result.SelectedFile);
            Assert.AreEqual("test.txt", result.SelectedFile.File.Name);
            Assert.AreEqual(SecurityLevel.Low, result.SecurityLevel);
            Assert.IsNull(result.ErrorMessage);
        }

        [TestMethod]
        public async Task PickSingleFileAsync_WithHighSecurityThreat_RejectsFile()
        {
            // Arrange
            var mockFile = new MockStorageFile("malware.exe", "C:\\malware.exe", 1024);
            _mockFilePicker.SetSingleFileResult(mockFile);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Critical, new List<string> { "Executable file", "Suspicious extension" });

            // Act
            var result = await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.SelectedFile);
            Assert.IsTrue(result.ErrorMessage.Contains("security reasons"));
            Assert.IsTrue(result.ErrorMessage.Contains("Executable file"));
        }

        [TestMethod]
        public async Task PickSingleFileAsync_WithUnsupportedPlatform_ReturnsError()
        {
            // Arrange
            _mockPlatform.SetFilePickerSupport(false);

            // Act
            var result = await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("File picker not supported on this platform", result.ErrorMessage);
        }

        [TestMethod]
        public async Task PickSingleFileAsync_WithNoFileSelected_ReturnsError()
        {
            // Arrange
            _mockFilePicker.SetSingleFileResult(null);

            // Act
            var result = await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("No file selected", result.ErrorMessage);
        }

        [TestMethod]
        public async Task PickSingleFileAsync_WithFileTypeFilters_ConfiguresFilePicker()
        {
            // Arrange
            var options = new FilePickerOptions
            {
                FileTypeFilters = new List<string> { ".txt", ".pdf", ".docx" }
            };
            var mockFile = new MockStorageFile("test.txt", "C:\\test.txt", 1024);
            _mockFilePicker.SetSingleFileResult(mockFile);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Low, new List<string>());

            // Act
            var result = await _filePicker.PickSingleFileAsync(options);

            // Assert
            Assert.IsTrue(result.Success);
            CollectionAssert.AreEqual(options.FileTypeFilters.ToArray(), _mockFilePicker.AppliedFilters);
        }

        [TestMethod]
        public async Task PickSingleFileAsync_WithException_ReturnsErrorResult()
        {
            // Arrange
            _mockFilePicker.SetException(new InvalidOperationException("Test exception"));

            // Act
            var result = await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage.Contains("Test exception"));
        }

        #endregion

        #region Multiple Files Picker Tests

        [TestMethod]
        public async Task PickMultipleFilesAsync_WithValidFiles_ReturnsSuccessResult()
        {
            // Arrange
            var mockFiles = new List<MockStorageFile>
            {
                new MockStorageFile("file1.txt", "C:\\file1.txt", 1024),
                new MockStorageFile("file2.pdf", "C:\\file2.pdf", 2048)
            };
            _mockFilePicker.SetMultipleFilesResult(mockFiles.Cast<IStorageFile>());
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Low, new List<string>());

            // Act
            var result = await _filePicker.PickMultipleFilesAsync();

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(FilePickerType.MultipleFiles, result.PickerType);
            Assert.AreEqual(2, result.SelectedFiles.Count);
            Assert.AreEqual("file1.txt", result.SelectedFiles[0].File.Name);
            Assert.AreEqual("file2.pdf", result.SelectedFiles[1].File.Name);
        }

        [TestMethod]
        public async Task PickMultipleFilesAsync_WithMixedSecurityLevels_FiltersSecureFiles()
        {
            // Arrange
            var mockFiles = new List<MockStorageFile>
            {
                new MockStorageFile("safe.txt", "C:\\safe.txt", 1024),
                new MockStorageFile("malware.exe", "C:\\malware.exe", 1024),
                new MockStorageFile("document.pdf", "C:\\document.pdf", 2048)
            };
            _mockFilePicker.SetMultipleFilesResult(mockFiles.Cast<IStorageFile>());
            
            // Setup security analyzer to reject .exe files
            _mockSecurityAnalyzer.SetCustomAnalysis((path) =>
            {
                if (path.EndsWith(".exe"))
                    return new FileSystemAnalysisResult { SecurityLevel = SecurityLevel.Critical, SecurityThreats = new List<string> { "Executable file" } };
                return new FileSystemAnalysisResult { SecurityLevel = SecurityLevel.Low, SecurityThreats = new List<string>() };
            });

            // Act
            var result = await _filePicker.PickMultipleFilesAsync();

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.SelectedFiles.Count);
            Assert.AreEqual(1, result.RejectedFiles.Count);
            Assert.IsTrue(result.RejectedFiles[0].Contains("malware.exe"));
            Assert.AreEqual(SecurityLevel.Low, result.SecurityLevel);
        }

        [TestMethod]
        public async Task PickMultipleFilesAsync_WithAllFilesRejected_ReturnsError()
        {
            // Arrange
            var mockFiles = new List<MockStorageFile>
            {
                new MockStorageFile("malware1.exe", "C:\\malware1.exe", 1024),
                new MockStorageFile("malware2.exe", "C:\\malware2.exe", 1024)
            };
            _mockFilePicker.SetMultipleFilesResult(mockFiles.Cast<IStorageFile>());
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Critical, new List<string> { "Executable file" });

            // Act
            var result = await _filePicker.PickMultipleFilesAsync();

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("All selected files were rejected for security reasons", result.ErrorMessage);
            Assert.AreEqual(2, result.RejectedFiles.Count);
        }

        #endregion

        #region Folder Picker Tests

        [TestMethod]
        public async Task PickFolderAsync_WithValidFolder_ReturnsSuccessResult()
        {
            // Arrange
            var mockFolder = new MockStorageFolder("TestFolder", "C:\\TestFolder");
            _mockFilePicker.SetFolderResult(mockFolder);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Low, new List<string>());

            // Act
            var result = await _filePicker.PickFolderAsync();

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(FilePickerType.Folder, result.PickerType);
            Assert.IsNotNull(result.SelectedFolder);
            Assert.AreEqual("TestFolder", result.SelectedFolder.Folder.Name);
        }

        [TestMethod]
        public async Task PickFolderAsync_WithHighSecurityThreat_RejectsFolder()
        {
            // Arrange
            var mockFolder = new MockStorageFolder("SystemFolder", "C:\\Windows\\System32");
            _mockFilePicker.SetFolderResult(mockFolder);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Critical, new List<string> { "System directory" });

            // Act
            var result = await _filePicker.PickFolderAsync();

            // Assert
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage.Contains("security reasons"));
        }

        #endregion

        #region Event Tests

        [TestMethod]
        public async Task PickSingleFileAsync_WithValidFile_RaisesFileSelectedEvent()
        {
            // Arrange
            var mockFile = new MockStorageFile("test.txt", "C:\\test.txt", 1024);
            _mockFilePicker.SetSingleFileResult(mockFile);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Low, new List<string>());

            FilePickerEventArgs eventArgs = null;
            _filePicker.FileSelected += (sender, args) => eventArgs = args;

            // Act
            await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsNotNull(eventArgs);
            Assert.IsNotNull(eventArgs.FileInfo);
            Assert.AreEqual("test.txt", eventArgs.FileInfo.File.Name);
        }

        [TestMethod]
        public async Task PickSingleFileAsync_WithSecurityThreat_RaisesErrorEvent()
        {
            // Arrange
            var mockFile = new MockStorageFile("malware.exe", "C:\\malware.exe", 1024);
            _mockFilePicker.SetSingleFileResult(mockFile);
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Critical, new List<string> { "Executable file" });

            FilePickerErrorEventArgs errorArgs = null;
            _filePicker.ErrorOccurred += (sender, args) => errorArgs = args;

            // Act
            await _filePicker.PickSingleFileAsync();

            // Assert
            Assert.IsNotNull(errorArgs);
            Assert.IsTrue(errorArgs.Message.Contains("security reasons"));
            Assert.IsNotNull(errorArgs.SecurityResult);
        }

        #endregion

        #region Validation Tests

        [TestMethod]
        public async Task ValidateFileSelectionAsync_WithValidFiles_ReturnsValid()
        {
            // Arrange
            var files = new List<IStorageFile>
            {
                new MockStorageFile("file1.txt", "C:\\file1.txt", 1024),
                new MockStorageFile("file2.pdf", "C:\\file2.pdf", 2048)
            };
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Low, new List<string>());

            // Act
            var result = await _filePicker.ValidateFileSelectionAsync(files);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public async Task ValidateFileSelectionAsync_WithHighSecurityFiles_ReturnsWarnings()
        {
            // Arrange
            var files = new List<IStorageFile>
            {
                new MockStorageFile("suspicious.zip", "C:\\suspicious.zip", 1024)
            };
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.High, new List<string> { "Compressed archive" });

            // Act
            var result = await _filePicker.ValidateFileSelectionAsync(files);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(1, result.Warnings.Count);
            Assert.IsTrue(result.Warnings[0].Contains("Compressed archive"));
        }

        [TestMethod]
        public async Task ValidateFileSelectionAsync_WithCriticalSecurityFiles_ReturnsInvalid()
        {
            // Arrange
            var files = new List<IStorageFile>
            {
                new MockStorageFile("malware.exe", "C:\\malware.exe", 1024)
            };
            _mockSecurityAnalyzer.SetAnalysisResult(SecurityLevel.Critical, new List<string> { "Executable file" });

            // Act
            var result = await _filePicker.ValidateFileSelectionAsync(files);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(1, result.Errors.Count);
            Assert.IsTrue(result.Errors[0].Contains("Executable file"));
        }

        #endregion

        #region Helper Methods

        private void ResetFilePickerSingleton()
        {
            // Use reflection to reset the singleton instance for testing
            var field = typeof(UniversalFilePicker).GetField("_instance", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field?.SetValue(null, null);
        }

        #endregion
    }
}
