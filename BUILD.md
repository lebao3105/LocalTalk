# LocalTalk Build Guide

This document provides comprehensive instructions for building LocalTalk across different platforms.

## Prerequisites

### For Windows Phone 8.x Development
- Visual Studio 2017 or later
- Windows Phone 8.1 SDK
- .NET Framework 4.5 or later
- Microsoft Phone Controls Toolkit

### For UWP Development
- Visual Studio 2017 or later with UWP workload
- Windows 10 SDK (version 10.0.17763.0 or later)
- .NET Standard 2.0 support

## Project Structure

```
LocalTalk/
├── LocalTalk/                 # Windows Phone 8.x project
│   ├── LocalTalk.csproj
│   ├── App.xaml
│   ├── MainPage.xaml
│   ├── Resources/
│   │   └── PlatformResources.xaml
│   └── Views/
│       ├── SendPage.xaml      # WP-specific (uses LongListSelector)
│       ├── ReceivePage.xaml   # WP-specific (uses LongListSelector)
│       ├── Settings.xaml
│       └── About.xaml
├── LocalTalkUWP/              # UWP project
│   ├── LocalTalkUWP.csproj
│   ├── App.xaml
│   ├── MainPage.xaml
│   └── Resources/
│       └── PlatformResources.xaml
├── Shared/                    # Shared code library
│   ├── Views/
│   │   ├── SendPage.xaml      # UWP-specific (uses ListView)
│   │   ├── ReceivePage.xaml   # UWP-specific (uses ListView)
│   │   ├── Device.xaml
│   │   └── TransferItem.xaml
│   ├── Platform/              # Platform abstractions
│   ├── Protocol/              # File transfer protocols
│   ├── FileSystem/            # File handling
│   └── Http/                  # HTTP server/client
└── build-validation.ps1      # Build validation script
```

## Platform-Specific Considerations

### Windows Phone 8.x
- Uses `LongListSelector` instead of `ListView`
- Uses `clr-namespace:` syntax for XAML namespaces
- Supports `System` namespace in XAML with `clr-namespace:System;assembly=mscorlib`
- Limited to .NET Framework APIs
- Uses Windows Phone-specific controls and styles

### UWP (Universal Windows Platform)
- Uses modern `ListView` control
- Uses `using:` syntax for XAML namespaces
- Supports `x:String` and other XAML primitives natively
- Uses .NET Standard 2.0 APIs
- Supports multiple CPU architectures (x86, x64, ARM)

## Building the Projects

### Using Visual Studio IDE

1. **Open Solution**
   ```
   Open LocalTalk.sln in Visual Studio
   ```

2. **Select Target Platform**
   - For Windows Phone: Select "LocalTalk" project
   - For UWP: Select "LocalTalkUWP" project

3. **Choose Configuration**
   - Debug (for development)
   - Release (for distribution)

4. **Build**
   - Build → Build Solution (Ctrl+Shift+B)

### Using Command Line (MSBuild)

1. **Windows Phone 8.x**
   ```powershell
   msbuild LocalTalk\LocalTalk.csproj /p:Configuration=Release /p:Platform="Any CPU"
   ```

2. **UWP (multiple platforms)**
   ```powershell
   # x86
   msbuild LocalTalkUWP\LocalTalkUWP.csproj /p:Configuration=Release /p:Platform=x86
   
   # x64
   msbuild LocalTalkUWP\LocalTalkUWP.csproj /p:Configuration=Release /p:Platform=x64
   
   # ARM
   msbuild LocalTalkUWP\LocalTalkUWP.csproj /p:Configuration=Release /p:Platform=ARM
   ```

### Using Build Validation Script

Run the PowerShell validation script to test all platforms:

```powershell
# Test all platforms
.\build-validation.ps1 -All

# Test specific platforms
.\build-validation.ps1 -WindowsPhone
.\build-validation.ps1 -UWP

# Clean build
.\build-validation.ps1 -All -Clean

# Release configuration
.\build-validation.ps1 -All -Configuration Release
```

## Troubleshooting Common Issues

### 1. ListView Compilation Errors
**Problem**: `ListView` not found in Windows Phone project
**Solution**: Windows Phone projects use platform-specific XAML files with `LongListSelector`

### 2. System Namespace Conflicts
**Problem**: `System` namespace breaks UWP builds
**Solution**: Platform-specific resource files handle namespace differences

### 3. Missing Dependencies
**Problem**: NuGet packages not restored
**Solution**: 
```powershell
nuget restore LocalTalk.sln
```

### 4. XAML Namespace Errors
**Problem**: `using:` syntax not recognized in Windows Phone
**Solution**: Windows Phone uses `clr-namespace:` syntax in platform-specific files

## Deployment

### Windows Phone 8.x
1. Connect Windows Phone device or start emulator
2. Set LocalTalk as startup project
3. Press F5 to deploy and debug

### UWP
1. Enable Developer Mode on target device
2. Set LocalTalkUWP as startup project
3. Select target device (Local Machine, Device, or Emulator)
4. Press F5 to deploy and debug

## Continuous Integration

For automated builds, use the build validation script in your CI pipeline:

```yaml
# Azure DevOps example
steps:
- task: PowerShell@2
  displayName: 'Validate Cross-Platform Build'
  inputs:
    filePath: 'build-validation.ps1'
    arguments: '-All -Configuration Release'
```

## Platform Feature Matrix

| Feature | Windows Phone 8.x | UWP | Notes |
|---------|-------------------|-----|-------|
| File Picker | ✅ | ✅ | Platform-specific implementations |
| File Transfer | ✅ | ✅ | Shared protocol implementation |
| Device Discovery | ✅ | ✅ | UDP multicast + HTTP |
| ListView | ❌ (LongListSelector) | ✅ | Platform-specific XAML |
| System Namespace | ✅ | ❌ | Platform-specific resources |
| Modern XAML | ❌ | ✅ | Different syntax requirements |

## Known Limitations

1. **Windows Phone 8.x**: Limited modern XAML features
2. **Cross-Platform**: Some UI components require platform-specific implementations
3. **Debugging**: Windows Phone debugging requires Visual Studio on Windows

## Support

For build issues:
1. Check this documentation
2. Run build validation script
3. Review platform-specific considerations
4. Check Visual Studio output window for detailed errors
