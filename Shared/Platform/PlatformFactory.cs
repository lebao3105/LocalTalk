using System;

namespace Shared.Platform
{
    /// <summary>
    /// Factory for creating platform-specific implementations
    /// </summary>
    public static class PlatformFactory
    {
        private static IPlatformAbstraction _current;

        /// <summary>
        /// Gets the current platform abstraction instance
        /// </summary>
        public static IPlatformAbstraction Current
        {
            get
            {
                if (_current == null)
                {
                    _current = CreatePlatformAbstraction();
                }
                return _current;
            }
        }

        /// <summary>
        /// Sets a custom platform abstraction (for testing)
        /// </summary>
        /// <param name="platform">The platform abstraction to use</param>
        public static void SetPlatform(IPlatformAbstraction platform)
        {
            _current = platform;
        }

        /// <summary>
        /// Creates the appropriate platform abstraction based on compilation symbols
        /// </summary>
        /// <returns>Platform-specific implementation</returns>
        private static IPlatformAbstraction CreatePlatformAbstraction()
        {
#if WINDOWS_UWP
            return new UwpPlatform();
#elif WINDOWS_PHONE
            return new WindowsPhonePlatform();
#else
            throw new PlatformNotSupportedException("Current platform is not supported");
#endif
        }

        /// <summary>
        /// Feature flags for platform capabilities
        /// </summary>
        public static class Features
        {
            /// <summary>
            /// Whether the platform supports HTTP server functionality
            /// </summary>
            public static bool SupportsHttpServer
            {
                get
                {
#if WINDOWS_UWP
                    return true;
#elif WINDOWS_PHONE
                    return false; // Limited HTTP server support on WP8
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Whether the platform supports file pickers
            /// </summary>
            public static bool SupportsFilePickers
            {
                get
                {
#if WINDOWS_UWP
                    return true;
#elif WINDOWS_PHONE
                    return false; // Limited file picker support on WP8
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Whether the platform supports certificate generation
            /// </summary>
            public static bool SupportsCertificateGeneration
            {
                get
                {
#if WINDOWS_UWP
                    return false; // Would need additional implementation
#elif WINDOWS_PHONE
                    return false; // Not supported on WP8
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Whether the platform supports multicast UDP
            /// </summary>
            public static bool SupportsMulticastUdp
            {
                get
                {
#if WINDOWS_UWP
                    return true;
#elif WINDOWS_PHONE
                    return true;
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Whether the platform supports background tasks
            /// </summary>
            public static bool SupportsBackgroundTasks
            {
                get
                {
#if WINDOWS_UWP
                    return true;
#elif WINDOWS_PHONE
                    return true;
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Whether the platform supports advanced file system operations
            /// </summary>
            public static bool SupportsAdvancedFileSystem
            {
                get
                {
#if WINDOWS_UWP
                    return true;
#elif WINDOWS_PHONE
                    return false; // Limited to isolated storage
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Whether the platform supports resource-based localization
            /// </summary>
            public static bool SupportsResourceLocalization
            {
                get
                {
#if WINDOWS_UWP
                    return true;
#elif WINDOWS_PHONE
                    return false; // Would need custom implementation
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// Maximum file size supported for transfers (in bytes)
            /// </summary>
            public static long MaxFileSize
            {
                get
                {
#if WINDOWS_UWP
                    return long.MaxValue; // No practical limit
#elif WINDOWS_PHONE
                    return 1024L * 1024L * 100L; // 100MB limit for WP8
#else
                    return 1024L * 1024L * 10L; // 10MB default
#endif
                }
            }

            /// <summary>
            /// Maximum number of concurrent transfers
            /// </summary>
            public static int MaxConcurrentTransfers
            {
                get
                {
#if WINDOWS_UWP
                    return 10;
#elif WINDOWS_PHONE
                    return 3; // Limited resources on mobile
#else
                    return 5;
#endif
                }
            }

            /// <summary>
            /// Default chunk size for file transfers (in bytes)
            /// </summary>
            public static int DefaultChunkSize
            {
                get
                {
#if WINDOWS_UWP
                    return 1024 * 64; // 64KB
#elif WINDOWS_PHONE
                    return 1024 * 16; // 16KB for mobile
#else
                    return 1024 * 32; // 32KB default
#endif
                }
            }
        }
    }
}
