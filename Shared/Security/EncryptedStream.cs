using System;
using System.IO;
using System.Security.Cryptography;

namespace Shared.Security
{
    /// <summary>
    /// Stream wrapper that provides transparent encryption/decryption for large file transfers
    /// </summary>
    public class EncryptedStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;
        private readonly bool _forWriting;
        private readonly AesGcm _aes;
        private readonly HMACSHA256 _hmac;
        private bool _disposed;

        // Cryptographic constants
        private const int IvSize = 16;              // AES IV size in bytes
        private const int TagSize = 16;             // AES-GCM tag size in bytes
        private const int LengthFieldSize = 4;      // Data length field size in bytes
        private const int HmacSize = 32;            // HMAC-SHA256 size in bytes
        private const int ChunkSize = 64 * 1024;    // Chunk size for streaming encryption (64KB)
        
        public EncryptedStream(Stream baseStream, byte[] encryptionKey, byte[] macKey, bool forWriting)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _encryptionKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
            _macKey = macKey ?? throw new ArgumentNullException(nameof(macKey));
            _forWriting = forWriting;
            
            _aes = new AesGcm(_encryptionKey);
            _hmac = new HMACSHA256(_macKey);
        }

        public override bool CanRead => !_forWriting && _baseStream.CanRead;
        public override bool CanSeek => false; // Encrypted streams don't support seeking
        public override bool CanWrite => _forWriting && _baseStream.CanWrite;
        public override long Length => throw new NotSupportedException("Length is not supported for encrypted streams");
        public override long Position 
        { 
            get => throw new NotSupportedException("Position is not supported for encrypted streams");
            set => throw new NotSupportedException("Position is not supported for encrypted streams");
        }

        public override void Flush()
        {
            _baseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_forWriting)
                throw new NotSupportedException("Cannot read from a write-only encrypted stream");

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            // Read encrypted chunk header (IV + tag + data length)
            var headerSize = IvSize + TagSize + LengthFieldSize;
            var header = new byte[headerSize];
            var headerBytesRead = _baseStream.Read(header, 0, header.Length);

            if (headerBytesRead == 0)
                return 0; // End of stream

            if (headerBytesRead < header.Length)
                throw new InvalidDataException("Incomplete encrypted chunk header");

            var iv = new byte[IvSize];
            var tag = new byte[TagSize];
            Array.Copy(header, 0, iv, 0, IvSize);
            Array.Copy(header, IvSize, tag, 0, TagSize);
            var encryptedDataLength = BitConverter.ToInt32(header, IvSize + TagSize);

            if (encryptedDataLength <= 0 || encryptedDataLength > ChunkSize)
                throw new InvalidDataException("Invalid encrypted chunk size");

            // Read encrypted data
            var encryptedData = new byte[encryptedDataLength];
            var encryptedBytesRead = _baseStream.Read(encryptedData, 0, encryptedDataLength);
            
            if (encryptedBytesRead < encryptedDataLength)
                throw new InvalidDataException("Incomplete encrypted chunk data");

            // Read HMAC
            var hmac = new byte[HmacSize];
            var hmacBytesRead = _baseStream.Read(hmac, 0, HmacSize);

            if (hmacBytesRead < HmacSize)
                throw new InvalidDataException("Incomplete HMAC");

            // Verify HMAC
            var expectedHmac = CalculateChunkHMAC(iv, tag, encryptedData);
            if (!ConstantTimeEquals(expectedHmac, hmac))
                throw new CryptographicException("HMAC verification failed - data may have been tampered with");

            // Decrypt data
            var decryptedData = new byte[encryptedDataLength];
            _aes.Decrypt(iv, encryptedData, tag, decryptedData);

            // Copy to output buffer
            var bytesToCopy = Math.Min(count, decryptedData.Length);
            Array.Copy(decryptedData, 0, buffer, offset, bytesToCopy);
            
            return bytesToCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seeking is not supported for encrypted streams");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength is not supported for encrypted streams");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_forWriting)
                throw new NotSupportedException("Cannot write to a read-only encrypted stream");

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            // Process data in chunks
            var remaining = count;
            var currentOffset = offset;

            while (remaining > 0)
            {
                var chunkSize = Math.Min(remaining, ChunkSize);
                var chunk = new byte[chunkSize];
                Array.Copy(buffer, currentOffset, chunk, 0, chunkSize);

                WriteEncryptedChunk(chunk);

                remaining -= chunkSize;
                currentOffset += chunkSize;
            }
        }

        /// <summary>
        /// Writes an encrypted chunk to the base stream
        /// </summary>
        private void WriteEncryptedChunk(byte[] data)
        {
            // Generate random IV
            var iv = new byte[IvSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            // Encrypt data
            var encryptedData = new byte[data.Length];
            var tag = new byte[TagSize];
            _aes.Encrypt(iv, data, encryptedData, tag);

            // Calculate HMAC
            var hmac = CalculateChunkHMAC(iv, tag, encryptedData);

            // Write chunk header (IV + tag + data length)
            _baseStream.Write(iv, 0, IvSize);
            _baseStream.Write(tag, 0, TagSize);
            _baseStream.Write(BitConverter.GetBytes(encryptedData.Length), 0, LengthFieldSize);

            // Write encrypted data
            _baseStream.Write(encryptedData, 0, encryptedData.Length);

            // Write HMAC
            _baseStream.Write(hmac, 0, HmacSize);
        }

        /// <summary>
        /// Calculates HMAC for a chunk
        /// </summary>
        private byte[] CalculateChunkHMAC(byte[] iv, byte[] tag, byte[] encryptedData)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(iv, 0, iv.Length);
                ms.Write(tag, 0, tag.Length);
                ms.Write(encryptedData, 0, encryptedData.Length);
                
                ms.Position = 0;
                return _hmac.ComputeHash(ms);
            }
        }

        /// <summary>
        /// Constant-time comparison to prevent timing attacks
        /// </summary>
        private bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            
            return result == 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _aes?.Dispose();
                _hmac?.Dispose();
                _baseStream?.Dispose();
                
                // Clear sensitive data
                if (_encryptionKey != null)
                {
                    Array.Clear(_encryptionKey, 0, _encryptionKey.Length);
                }
                if (_macKey != null)
                {
                    Array.Clear(_macKey, 0, _macKey.Length);
                }
                
                _disposed = true;
            }
            
            base.Dispose(disposing);
        }
    }
}
