using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalTalk.Tests.TestFramework
{
    /// <summary>
    /// Common test helper utilities for LocalTalk testing
    /// </summary>
    public static class TestHelpers
    {
        /// <summary>
        /// Creates a temporary test file with specified content
        /// </summary>
        public static async Task<string> CreateTestFileAsync(string content = "Test file content", string fileName = null)
        {
            fileName = fileName ?? $"test_{Guid.NewGuid()}.txt";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            
            using (var writer = new StreamWriter(tempPath))
            {
                await writer.WriteAsync(content);
            }
            
            return tempPath;
        }

        /// <summary>
        /// Creates a temporary test file with binary content
        /// </summary>
        public static async Task<string> CreateTestBinaryFileAsync(byte[] content, string fileName = null)
        {
            fileName = fileName ?? $"test_{Guid.NewGuid()}.bin";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await stream.WriteAsync(content, 0, content.Length);
            }
            
            return tempPath;
        }

        /// <summary>
        /// Cleans up test files
        /// </summary>
        public static void CleanupTestFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail test cleanup
                System.Diagnostics.Debug.WriteLine($"Failed to cleanup test file {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Asserts that an async operation completes within the specified timeout
        /// </summary>
        public static async Task AssertCompletesWithinAsync(Task operation, TimeSpan timeout, string message = null)
        {
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(operation, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Assert.Fail(message ?? $"Operation did not complete within {timeout.TotalSeconds} seconds");
            }
            
            // Re-await the operation to propagate any exceptions
            await operation;
        }

        /// <summary>
        /// Asserts that an async operation throws a specific exception type
        /// </summary>
        public static async Task<T> AssertThrowsAsync<T>(Func<Task> operation, string message = null) where T : Exception
        {
            try
            {
                await operation();
                Assert.Fail(message ?? $"Expected exception of type {typeof(T).Name} was not thrown");
                return null; // Never reached
            }
            catch (T ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                Assert.Fail(message ?? $"Expected exception of type {typeof(T).Name}, but got {ex.GetType().Name}: {ex.Message}");
                return null; // Never reached
            }
        }

        /// <summary>
        /// Generates test data of specified size
        /// </summary>
        public static byte[] GenerateTestData(int sizeInBytes)
        {
            var data = new byte[sizeInBytes];
            var random = new Random(42); // Use fixed seed for reproducible tests
            random.NextBytes(data);
            return data;
        }

        /// <summary>
        /// Compares two byte arrays for equality
        /// </summary>
        public static bool ByteArraysEqual(byte[] array1, byte[] array2)
        {
            if (array1 == null && array2 == null) return true;
            if (array1 == null || array2 == null) return false;
            if (array1.Length != array2.Length) return false;
            
            for (int i = 0; i < array1.Length; i++)
            {
                if (array1[i] != array2[i]) return false;
            }
            
            return true;
        }

        /// <summary>
        /// Creates a mock IP address for testing
        /// </summary>
        public static string GetTestIPAddress()
        {
            return "192.168.1.100";
        }

        /// <summary>
        /// Creates a mock port for testing
        /// </summary>
        public static int GetTestPort()
        {
            return 53317; // LocalSend default port
        }

        /// <summary>
        /// Waits for a condition to become true within a timeout
        /// </summary>
        public static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, TimeSpan? pollInterval = null)
        {
            pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
            var endTime = DateTime.UtcNow.Add(timeout);
            
            while (DateTime.UtcNow < endTime)
            {
                if (condition())
                {
                    return true;
                }
                
                await Task.Delay(pollInterval.Value);
            }
            
            return false;
        }
    }
}
