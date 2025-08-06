# LocalTalk

Transfer your files between your devices using the LocalSend protocol!

## Platform Support

* **Windows Phone 8.x** :white_check_mark: - Fully implemented with platform compatibility fixes
* **UWP 10** :white_check_mark: - Complete implementation with modern Windows 10+ support

## Core Capabilities

* **Device discovery** :white_check_mark: - Automatic network device discovery via multicast UDP and HTTP
* **HTTP(s) server** :white_check_mark: - Full LocalSend protocol implementation with route handling
* **File pickers** :white_check_mark: - Universal file picker with security validation and platform abstraction
* **File transfers** :white_check_mark: - Chunked transfer protocol with progress tracking and resumption
* **Cross-platform compatibility** :white_check_mark: - Shared codebase with platform-specific optimizations
* **Security validation** :white_check_mark: - File system security analysis and threat detection
* **Progress tracking** :white_check_mark: - Real-time transfer progress with ETA and speed calculations
* **Error handling** :white_check_mark: - Comprehensive error recovery and user feedback
* **Testing framework** :white_check_mark: - Complete unit and integration test coverage

## Features

### File Transfer Workflows
- **Send Files**: Complete workflow from file selection through device discovery to transfer completion
- **Receive Files**: Automatic device advertising and file acceptance with storage management
- **Device Discovery**: Network scanning and device pairing with connection status tracking
- **Progress Monitoring**: Real-time progress bars, transfer speeds, and estimated completion times

### Technical Implementation
- **LocalSend Protocol**: Full compatibility with LocalSend ecosystem for cross-platform transfers
- **Chunked Transfers**: Efficient large file handling with parallel chunk processing
- **Security**: File system analysis, threat detection, and secure transfer validation
- **Platform Abstraction**: Unified API across Windows Phone and UWP with platform-specific optimizations
- **Error Recovery**: Robust error handling with automatic retry and graceful degradation

## Building

### Requirements
- Visual Studio 2017 or newer
- UWP and .NET workloads
- Windows 10 SDK (for UWP projects)
- Windows Phone 8.1 SDK (for Windows Phone projects)

### Build Instructions
1. Clone the repository
2. Open `LocalTalk.sln` (for Windows Phone) or `LocalTalkUWP.sln` (for UWP)
3. Restore NuGet packages
4. Build using MSBuild or Visual Studio IDE

### Testing
Run the comprehensive test suite:
```powershell
# Build and run all tests
.\build-validation.ps1 -All -Configuration Release

# Run specific test categories
.\build-validation.ps1 -UnitTests -Configuration Debug

# Run code standards testing (LLVM-inspired)
.\tests\code-standards\CodeStandardsValidator.ps1 -Verbose

# Run complete testing framework
.\tests\MasterAssumptionValidator.ps1 -Verbose
```

## Platform Compatibility

### Windows Phone 8.x
- **Status**: :white_check_mark: Fully supported
- **Location**: [LocalTalk](LocalTalk/) project
- **Features**: Complete LocalSend protocol implementation with platform-specific UI adaptations
- **Resolved Issues**:
  - Fixed System namespace conflicts with conditional compilation
  - Implemented ListView compatibility layer for cross-platform builds
  - Added proper XAML resource handling for Windows Phone

### UWP (Universal Windows Platform)
- **Status**: :white_check_mark: Fully supported
- **Location**: [LocalTalkUWP](LocalTalkUWP/) project
- **Features**: Modern Windows 10+ implementation with enhanced security and performance
- **Capabilities**: Full file system access, background transfers, and system integration

## Architecture

The project uses a shared codebase architecture:
- **Shared**: Core business logic, protocols, and cross-platform components
- **Platform Projects**: Platform-specific UI and system integrations
- **Testing**: Comprehensive unit and integration test coverage

For detailed technical documentation, see [PLATFORM_COMPATIBILITY.md](PLATFORM_COMPATIBILITY.md) and [BUILD.md](BUILD.md).