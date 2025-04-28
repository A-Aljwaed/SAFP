using System;
using System.Collections.Generic; // Required by Zxcvbn types
using System.IO;
using System.Linq; // Required for Any()
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Zxcvbn; // Correct using for Zxcvbn-cs package

// Namespace corrected to SAFP.Core
namespace SAFP.Core
{
    /// <summary>
    /// Custom exception for decryption or loading failures.
    /// </summary>
    public class DecryptionException : Exception
    {
        public DecryptionException(string message) : base(message) { }
        public DecryptionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Custom exception for file read/write errors.
    /// </summary>
    public class FileOperationException : IOException
    {
        public FileOperationException(string message) : base(message) { }
        public FileOperationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Handles core business logic: encryption, data storage, password utilities.
    /// </summary>
    public class PasswordManagerLogic
    {
        private readonly string _dataFile; // Private field to store the path

        // Constants to define the encryption strength
        private const int KdfIterations = 390000; // PBKDF2 iterations
        private const int KeyLengthBytes = 32;    // 256 bits for AES key
        private const int NonceLengthBytes = 12;   // 96 bits for AES-GCM nonce
        private const int SaltLengthBytes = 16;    // 128 bits for salt
        private const int TagLengthBytes = 16;     // 128 bits for AES-GCM tag

        private static readonly HashAlgorithmName Pbkdf2HashAlgorithm = HashAlgorithmName.SHA256;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true, // Pretty print JSON
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Allow non-ASCII chars
        };

        public PasswordManagerLogic(string dataFile)
        {
            // Ensure the directory for the data file exists
            string? directory = Path.GetDirectoryName(dataFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _dataFile = dataFile ?? throw new ArgumentNullException(nameof(dataFile));
        }

        // Removed the unused public DataFile property

        /// <summary>
        /// Derives an AES key from the password using PBKDF2-HMAC-SHA256.
        /// </summary>
        private byte[] DeriveKey(string password, byte[] salt)
        {
            if (salt == null || salt.Length != SaltLengthBytes)
                throw new ArgumentException($"Salt must be {SaltLengthBytes} bytes long.", nameof(salt));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty.", nameof(password));

            // Use PBKDF2
            using var kdf = new Rfc2898DeriveBytes(password, salt, KdfIterations, Pbkdf2HashAlgorithm);
            return kdf.GetBytes(KeyLengthBytes);
        }

        /// <summary>
        /// Loads and decrypts data from the data file asynchronously.
        /// </summary>
        public async Task<T?> LoadDataAsync<T>(string masterPassword) where T : class, new()
        {
            if (!File.Exists(_dataFile))
                return new T(); // Return default/empty object if file doesn't exist

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(_dataFile);
                if (fileContent.Length < SaltLengthBytes + NonceLengthBytes + TagLengthBytes)
                    throw new DecryptionException("Data file is too short or corrupted.");

                // Using C# 8.0+ Range operators for slicing
                byte[] salt = fileContent[..SaltLengthBytes];
                byte[] nonce = fileContent[SaltLengthBytes..(SaltLengthBytes + NonceLengthBytes)];
                byte[] encryptedDataWithTag = fileContent[(SaltLengthBytes + NonceLengthBytes)..];

                // Ensure enough data remains for ciphertext + tag
                if (encryptedDataWithTag.Length < TagLengthBytes)
                     throw new DecryptionException("Data file is corrupted (missing tag).");

                byte[] key = DeriveKey(masterPassword, salt);
                byte[] ciphertext = encryptedDataWithTag[..^TagLengthBytes]; // Ciphertext is all bytes except the last TagLengthBytes
                byte[] tag = encryptedDataWithTag[^TagLengthBytes..]; // Tag is the last TagLengthBytes

                using var aesGcm = new AesGcm(key, TagLengthBytes); // Pass expected tag size
                byte[] plaintext = new byte[ciphertext.Length]; // Buffer for decrypted data
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext); // Decrypt in place

                string json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                    ?? throw new DecryptionException("Deserialized data is null."); // Or handle null differently
            }
            catch (IOException ex)
            {
                throw new FileOperationException($"Error reading data file: {_dataFile}", ex);
            }
            catch (CryptographicException ex) // Catches incorrect password (tag mismatch) or other crypto issues
            {
                throw new DecryptionException("Invalid master password or data tampered/corrupted.", ex);
            }
            catch (JsonException ex)
            {
                throw new DecryptionException("Decrypted data is invalid JSON.", ex);
            }
            catch (ArgumentException ex) // Can be thrown by DeriveKey or AesGcm constructor
            {
                 throw new DecryptionException($"Decryption setup failed: {ex.Message}", ex);
            }
            catch (Exception ex) // Catch-all for unexpected issues
            {
                // Log the full exception details here if possible
                throw new DecryptionException($"An unexpected error occurred during decryption: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Encrypts and saves data to the data file asynchronously using AES-GCM.
        /// </summary>
        public async Task SaveDataAsync<T>(T data, string masterPassword) where T : class
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(masterPassword))
                throw new ArgumentNullException(nameof(masterPassword), "Master password cannot be empty.");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
            byte[] key = DeriveKey(masterPassword, salt);

            try
            {
                string json = JsonSerializer.Serialize(data, _jsonOptions);
                byte[] plaintext = Encoding.UTF8.GetBytes(json);
                byte[] ciphertext = new byte[plaintext.Length]; // Buffer for ciphertext
                byte[] tag = new byte[TagLengthBytes]; // Buffer for authentication tag

                using var aesGcm = new AesGcm(key, TagLengthBytes); // Pass expected tag size
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag); // Encrypt and generate tag

                // Combine all parts: salt + nonce + ciphertext + tag
                byte[] output = new byte[salt.Length + nonce.Length + ciphertext.Length + tag.Length];
                Buffer.BlockCopy(salt, 0, output, 0, salt.Length);
                Buffer.BlockCopy(nonce, 0, output, salt.Length, nonce.Length);
                Buffer.BlockCopy(ciphertext, 0, output, salt.Length + nonce.Length, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, output, salt.Length + nonce.Length + ciphertext.Length, tag.Length);

                // Atomic Write
                string tempFilePath = Path.Combine(Path.GetDirectoryName(_dataFile) ?? "", Path.GetFileName(_dataFile) + ".tmp");
                await File.WriteAllBytesAsync(tempFilePath, output);
                File.Move(tempFilePath, _dataFile, overwrite: true); // Overwrite if exists
            }
            catch (IOException ex)
            {
                // Attempt cleanup if temp file exists after IO error during move/write
                string tempFilePath = Path.Combine(Path.GetDirectoryName(_dataFile) ?? "", Path.GetFileName(_dataFile) + ".tmp");
                if (File.Exists(tempFilePath)) { try { File.Delete(tempFilePath); } catch { /* Ignore cleanup error */ } }
                throw new FileOperationException($"Failed to write data file: {_dataFile}", ex);
            }
            catch (Exception ex) // Catch serialization, crypto, or other errors
            {
                 // Attempt cleanup if temp file exists after other errors
                string tempFilePath = Path.Combine(Path.GetDirectoryName(_dataFile) ?? "", Path.GetFileName(_dataFile) + ".tmp");
                if (File.Exists(tempFilePath)) { try { File.Delete(tempFilePath); } catch { /* Ignore cleanup error */ } }
                // Consider more specific exception wrapping if needed
                throw new FileOperationException($"An unexpected error occurred while saving: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generates a cryptographically secure password.
        /// </summary>
        public string GeneratePassword(int length = 16)
        {
            const string lowers = "abcdefghijklmnopqrstuvwxyz";
            const string uppers = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "0123456789";
            const string punctuation = "!@#$%^&*()_+-=[]{}|;:,./?"; // Consider if this set is too restrictive/broad
            const string allChars = lowers + uppers + digits + punctuation;

            if (length < 8) length = 8;

            StringBuilder password = new(length);
            int attempts = 0;
            bool hasLower, hasUpper, hasDigit, hasPunctuation;

            do
            {
                if (++attempts > 100) // Safety break
                    throw new Exception("Unable to generate a complex password after many attempts.");

                password.Clear();
                byte[] randomBytes = RandomNumberGenerator.GetBytes(length);
                for (int i = 0; i < length; i++)
                {
                    password.Append(allChars[randomBytes[i] % allChars.Length]); // Use modulo for index
                }

                string currentPassword = password.ToString();
                // Use Linq Any() for concise checks
                hasLower = currentPassword.Any(char.IsLower);
                hasUpper = currentPassword.Any(char.IsUpper);
                hasDigit = currentPassword.Any(char.IsDigit);
                hasPunctuation = currentPassword.Any(c => punctuation.Contains(c)); // Check against defined set

            } while (!hasLower || !hasUpper || !hasDigit || !hasPunctuation); // Ensure all types are present

            return password.ToString();
        }

        /// <summary>
        /// Generates a unique ID (GUID) for an entry.
        /// </summary>
        public string GenerateUuid() => Guid.NewGuid().ToString();

        /// <summary>
        /// Checks password strength using Zxcvbn-cs (NuGet package required).
        /// </summary>
        public Zxcvbn.Result CheckPasswordStrength(string password)
        {
            // Requires NuGet package: Zxcvbn-cs
            // Let the library handle empty or null strings directly.
            // It should return a Result object with Score = 0 in those cases.
            return Zxcvbn.Core.EvaluatePassword(password ?? ""); // Pass empty string if password is null
        }
    }
}
