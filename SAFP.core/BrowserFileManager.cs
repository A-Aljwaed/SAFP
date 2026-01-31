using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices; // For RuntimeInformation
using System.Text;
using System.Text.Json; // For BrowserBackupData serialization
using System.Threading.Tasks;
using System.Buffers.Text; // For Base64 - Not strictly needed with Convert
using System.Security.Cryptography; // Potentially needed if we were doing crypto here
using System.ComponentModel; // For Win32Exception

// Namespace updated to SAFP.Core
namespace SAFP.Core
{
    /// <summary>
    /// Handles finding, backing up, and restoring browser credential storage FILES.
    /// WARNING: This operates on the browser's raw data files. It does NOT decrypt
    /// the passwords within them. Requires browsers to be CLOSED during operation.
    /// </summary>
    public class BrowserFileManager
    {
        #region Windows API P/Invoke for Forced Deletion
        
        // Windows API constants and structures for file handle management
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_DELETE_ON_CLOSE = 0x04000000;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(
            string lpExistingFileName,
            string? lpNewFileName,
            uint dwFlags);
        
        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;
        
        #endregion
        
        private readonly PasswordManagerLogic _logic; // Uses the core logic for encryption/decryption
        private const string BrowserDataFile = "browser_vault.safp"; // Backup filename using new extension
        private readonly string _backupFilePath; // Full path to the backup file

        public BrowserFileManager()
        {
            // Determine path for browser backup file (e.g., in AppData)
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string safpDataPath = Path.Combine(appDataPath, "SAFP"); // App-specific folder
            _backupFilePath = Path.Combine(safpDataPath, BrowserDataFile);

            // Initialize logic with the specific backup file path
            _logic = new PasswordManagerLogic(_backupFilePath);
        }

        // --- Path Finding Logic ---
        // (GetFirefoxProfileDirs, GetChromiumBaseDirs, FindBrowserFiles methods remain the same as before)
        #region Path Finding Methods
        private List<string> GetFirefoxProfileDirs()
        {
            var profileDirs = new List<string>();
            string basePath;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Firefox");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mozilla", "firefox");
                }
                else { return profileDirs; } // Unsupported OS

                string profilesIniPath = Path.Combine(basePath, "profiles.ini");
                if (!File.Exists(profilesIniPath)) return profileDirs;

                // Basic ini parsing (consider a dedicated library for robustness)
                var lines = File.ReadAllLines(profilesIniPath);
                string? currentPath = null;
                bool isRelative = false;
                string? currentSection = null;

                foreach (var line in lines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#") && !l.StartsWith(";")))
                {
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        // Process previous profile block before starting new one
                        if (currentSection != null && currentPath != null && currentSection.StartsWith("Profile"))
                        {
                            string fullPath = isRelative ? Path.Combine(basePath, currentPath) : currentPath;
                            if (Directory.Exists(fullPath)) profileDirs.Add(fullPath);
                        }
                        // Start new section
                        currentSection = line.Substring(1, line.Length - 2);
                        currentPath = null;
                        isRelative = false;
                    }
                    else if (currentSection != null && line.Contains('='))
                    {
                        var parts = line.Split('=', 2);
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        if (key.Equals("Path", StringComparison.OrdinalIgnoreCase)) currentPath = value;
                        else if (key.Equals("IsRelative", StringComparison.OrdinalIgnoreCase)) isRelative = value == "1";
                    }
                }
                // Process the last profile block
                if (currentSection != null && currentPath != null && currentSection.StartsWith("Profile"))
                {
                    string fullPath = isRelative ? Path.Combine(basePath, currentPath) : currentPath;
                    if (Directory.Exists(fullPath)) profileDirs.Add(fullPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Firefox profiles.ini: {ex.Message}");
                // Log error appropriately
            }
            return profileDirs;
        }

        private List<string> GetChromiumBaseDirs()
        {
            var baseDirs = new List<string>();
            try
            {
                string appDataBase, configBase;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    appDataBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    baseDirs.Add(Path.Combine(appDataBase, "Google", "Chrome", "User Data"));
                    baseDirs.Add(Path.Combine(appDataBase, "Microsoft", "Edge", "User Data"));
                    baseDirs.Add(Path.Combine(appDataBase, "BraveSoftware", "Brave-Browser", "User Data"));
                    baseDirs.Add(Path.Combine(appDataBase, "Vivaldi", "User Data"));
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    appDataBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
                    baseDirs.Add(Path.Combine(appDataBase, "Google", "Chrome"));
                    baseDirs.Add(Path.Combine(appDataBase, "Microsoft Edge"));
                    baseDirs.Add(Path.Combine(appDataBase, "BraveSoftware", "Brave-Browser"));
                    baseDirs.Add(Path.Combine(appDataBase, "Vivaldi"));
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    configBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                    baseDirs.Add(Path.Combine(configBase, "google-chrome"));
                    baseDirs.Add(Path.Combine(configBase, "chromium"));
                    baseDirs.Add(Path.Combine(configBase, "microsoft-edge"));
                    baseDirs.Add(Path.Combine(configBase, "microsoft-edge-dev"));
                    baseDirs.Add(Path.Combine(configBase, "BraveSoftware", "Brave-Browser"));
                    baseDirs.Add(Path.Combine(configBase, "brave-browser"));
                    baseDirs.Add(Path.Combine(configBase, "vivaldi"));
                }
            }
            catch (Exception ex) {
                 Console.WriteLine($"Error getting base directories: {ex.Message}");
                 // Log error
            }
            // Return only existing directories
            return baseDirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).ToList();
        }


        private List<string> FindBrowserFiles()
        {
            var filesToBackup = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Case-insensitive paths

            // --- Firefox ---
            try {
                var firefoxProfileDirs = GetFirefoxProfileDirs();
                string[] firefoxFiles = { "logins.json", "key4.db" };
                foreach (var profileDir in firefoxProfileDirs)
                {
                    foreach (var ffFile in firefoxFiles)
                    {
                        string filePath = Path.Combine(profileDir, ffFile);
                        if (File.Exists(filePath))
                        {
                            filesToBackup.Add(Path.GetFullPath(filePath));
                        }
                    }
                }
            } catch (Exception ex) {
                 Console.WriteLine($"Error finding Firefox files: {ex.Message}");
                 // Log error
            }

            // --- Chromium-based ---
             try {
                var chromiumBaseDirs = GetChromiumBaseDirs();
                string[] chromiumFiles = { "Login Data", "Local State" };
                foreach (var baseDir in chromiumBaseDirs)
                {
                    string[] profilesToCheck = { "Default" }; // Start with Default
                    // Add named profiles (Profile 1, Profile 2...)
                    try {
                         profilesToCheck = profilesToCheck.Concat(Directory.EnumerateDirectories(baseDir, "Profile *").Select(Path.GetFileName)).ToArray()!;
                    } catch { /* Ignore if no named profiles or access denied */ }


                    foreach(var profileName in profilesToCheck.Where(p => !string.IsNullOrEmpty(p)))
                    {
                        string profilePath = Path.Combine(baseDir, profileName);
                        if (!Directory.Exists(profilePath)) continue;

                        foreach (var crFile in chromiumFiles)
                        {
                            string filePath = Path.Combine(profilePath, crFile);
                            if (File.Exists(filePath))
                            {
                                filesToBackup.Add(Path.GetFullPath(filePath));
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                 Console.WriteLine($"Error finding Chromium files: {ex.Message}");
                 // Log error
            }

            Console.WriteLine($"Found {filesToBackup.Count} potential browser files.");
            return filesToBackup.ToList();
        }
        #endregion

        // --- Secure File Management ---

        /// <summary>
        /// Attempts to forcibly delete a file that may be locked by another process.
        /// Uses multiple strategies including Windows API calls to close handles.
        /// </summary>
        private bool ForceDeleteLockedFile(string filePath)
        {
            if (!File.Exists(filePath))
                return true;

            // Only use Windows-specific APIs on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine($"Force deletion not supported on this platform for: {filePath}");
                return false;
            }

            Console.WriteLine($"Attempting forced deletion of locked file: {filePath}");

            try
            {
                // Strategy 1: Try to open with DELETE_ON_CLOSE flag
                // This marks the file for deletion when all handles are closed
                IntPtr handle = CreateFile(
                    filePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_DELETE_ON_CLOSE | FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);

                if (handle != INVALID_HANDLE_VALUE)
                {
                    Console.WriteLine($"Successfully opened file with DELETE_ON_CLOSE: {filePath}");
                    CloseHandle(handle);
                    
                    // Brief delay to allow OS to process the deletion
                    // Using Thread.Sleep here is intentional as this is a synchronous operation
                    // dealing with OS-level file handles, not async I/O
                    System.Threading.Thread.Sleep(100);
                    if (!File.Exists(filePath))
                    {
                        Console.WriteLine($"File successfully deleted: {filePath}");
                        return true;
                    }
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Could not open file with DELETE_ON_CLOSE (Error {error}): {filePath}");
                }

                // Strategy 2: Move file to temp location and mark for deletion on reboot
                string tempPath = Path.Combine(Path.GetTempPath(), $"safp_delete_{Guid.NewGuid()}.tmp");
                
                try
                {
                    File.Move(filePath, tempPath);
                    Console.WriteLine($"Moved locked file to temp location: {tempPath}");
                    
                    // Try to delete the moved file
                    try
                    {
                        File.Delete(tempPath);
                        Console.WriteLine($"Successfully deleted moved file: {tempPath}");
                        return true;
                    }
                    catch
                    {
                        // If still can't delete, mark for deletion on reboot
                        if (MoveFileEx(tempPath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                        {
                            Console.WriteLine($"Marked file for deletion on reboot: {tempPath}");
                            return true;
                        }
                    }
                }
                catch (Exception moveEx)
                {
                    Console.WriteLine($"Could not move file to temp location: {moveEx.Message}");
                }

                // Strategy 3: Mark original file for deletion on reboot
                if (MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                {
                    Console.WriteLine($"Marked file for deletion on reboot: {filePath}");
                    return true;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Could not mark file for deletion on reboot (Error {error}): {filePath}");
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ForceDeleteLockedFile: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Securely deletes a file by overwriting it multiple times before deletion.
        /// If the file is locked, attempts forced deletion using Windows APIs.
        /// </summary>
        private void SecureDeleteFile(string filePath, bool forceIfLocked = true)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileLength = fileInfo.Length;
                
                // Overwrite the file multiple times with random data
                using (var fileStream = File.OpenWrite(filePath))
                {
                    var random = new Random();
                    var buffer = new byte[4096];
                    
                    // Pass 1: Random data
                    fileStream.Position = 0;
                    for (long written = 0; written < fileLength; written += buffer.Length)
                    {
                        int bytesToWrite = (int)Math.Min(buffer.Length, fileLength - written);
                        random.NextBytes(buffer);
                        fileStream.Write(buffer, 0, bytesToWrite);
                    }
                    fileStream.Flush();
                    
                    // Pass 2: All zeros
                    fileStream.Position = 0;
                    Array.Clear(buffer, 0, buffer.Length);
                    for (long written = 0; written < fileLength; written += buffer.Length)
                    {
                        int bytesToWrite = (int)Math.Min(buffer.Length, fileLength - written);
                        fileStream.Write(buffer, 0, bytesToWrite);
                    }
                    fileStream.Flush();
                    
                    // Pass 3: All ones
                    fileStream.Position = 0;
                    Array.Fill(buffer, (byte)0xFF);
                    for (long written = 0; written < fileLength; written += buffer.Length)
                    {
                        int bytesToWrite = (int)Math.Min(buffer.Length, fileLength - written);
                        fileStream.Write(buffer, 0, bytesToWrite);
                    }
                    fileStream.Flush();
                }
                
                // Finally delete the file
                File.Delete(filePath);
                Console.WriteLine($"Securely deleted: {filePath}");
            }
            catch (IOException ioEx) when (forceIfLocked)
            {
                // Check for ERROR_SHARING_VIOLATION (0x20) which indicates file is in use
                const int ERROR_SHARING_VIOLATION = 32;
                const int ERROR_LOCK_VIOLATION = 33;
                int hResult = ioEx.HResult & 0xFFFF;
                
                if (hResult == ERROR_SHARING_VIOLATION || hResult == ERROR_LOCK_VIOLATION || 
                    ioEx.Message.Contains("being used by another process"))
                {
                    Console.WriteLine($"File is locked by another process: {filePath}");
                    Console.WriteLine("Attempting forced deletion (deletion is being enforced / löschung wird erzwungen)...");
                    
                    if (ForceDeleteLockedFile(filePath))
                    {
                        Console.WriteLine($"Successfully forced deletion of: {filePath}");
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not force delete {filePath}: File will be removed on next reboot or when unlocked.");
                    }
                }
                else
                {
                    // Re-throw if it's a different type of IOException
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not securely delete {filePath}: {ex.Message}");
                
                // If forced deletion is enabled and on Windows, try it
                if (forceIfLocked && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.WriteLine("Attempting forced deletion...");
                    if (ForceDeleteLockedFile(filePath))
                    {
                        Console.WriteLine($"Successfully forced deletion of: {filePath}");
                    }
                    else
                    {
                        // Try normal deletion as final fallback
                        try 
                        { 
                            File.Delete(filePath); 
                            Console.WriteLine($"Deleted using fallback method: {filePath}"); 
                        } 
                        catch 
                        { 
                            Console.WriteLine($"All deletion methods failed for: {filePath}"); 
                        }
                    }
                }
                else
                {
                    // Try normal deletion as fallback
                    try 
                    { 
                        File.Delete(filePath); 
                    } 
                    catch 
                    { 
                        /* Ignore if deletion fails */ 
                    }
                }
            }
        }

        /// <summary>
        /// Backs up browser files and securely deletes originals for protection.
        /// </summary>
        public async Task<(bool Success, List<string> Messages)> BackupAndSecureDeleteAsync(string masterPassword)
        {
            var (backupSuccess, backupMessages) = await BackupBrowserFilesAsync(masterPassword);
            
            if (!backupSuccess)
            {
                backupMessages.Add("Backup failed - browser files NOT deleted for safety.");
                return (false, backupMessages);
            }

            // Securely delete original files after successful backup
            var filesToDelete = FindBrowserFiles();
            int deletedCount = 0;
            
            foreach (var filePath in filesToDelete)
            {
                try
                {
                    SecureDeleteFile(filePath);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    backupMessages.Add($"Warning: Could not delete {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }
            
            backupMessages.Add($"Securely deleted {deletedCount} original browser files.");
            return (true, backupMessages);
        }

        // --- Backup and Restore Logic ---

        /// <summary>
        /// Backs up browser credential storage files to an encrypted archive.
        /// IMPORTANT: Assumes browsers are closed. Does not verify this.
        /// </summary>
        public async Task<(bool Success, List<string> Messages)> BackupBrowserFilesAsync(string masterPassword)
        {
            var messages = new List<string>();
            var filesToBackup = FindBrowserFiles();

            if (!filesToBackup.Any())
            {
                messages.Add("No supported browser credential files found.");
                return (false, messages);
            }

            var backupData = new BrowserBackupData();
            string tempDir = Path.Combine(Path.GetTempPath(), $"safp_backup_{Guid.NewGuid()}");
            bool overallSuccess = false;

            try
            {
                Directory.CreateDirectory(tempDir);
                int filesProcessed = 0;

                foreach (var sourcePathStr in filesToBackup)
                {
                    var sourcePath = new FileInfo(sourcePathStr);
                    if (!sourcePath.Exists)
                    {
                        messages.Add($"Warning: File not found during copy: {sourcePathStr}");
                        continue;
                    }

                    string key = sourcePath.FullName; // Use full path as the key
                    string tempFilePath = Path.Combine(tempDir, $"{Guid.NewGuid()}_{sourcePath.Name}");

                    try
                    {
                        Console.WriteLine($"Copying {sourcePath.FullName} to {tempFilePath}");
                        File.Copy(sourcePath.FullName, tempFilePath, true); // Copy to temp location first

                        byte[] content = await File.ReadAllBytesAsync(tempFilePath);
                        backupData.Files[key] = Convert.ToBase64String(content); // Store Base64 encoded
                        backupData.OriginalPaths[key] = sourcePath.FullName; // Store original path
                        filesProcessed++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        messages.Add($"Warning: Error copying/reading file '{sourcePath.Name}': {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"Warning: Unexpected error processing file '{sourcePath.Name}': {ex.Message}");
                    }
                    finally
                    {
                        // Clean up individual temp file immediately
                        if (File.Exists(tempFilePath))
                        {
                            try { File.Delete(tempFilePath); } catch { /* Ignore */ }
                        }
                    }
                }

                if (filesProcessed == 0)
                {
                    messages.Add("No files were successfully copied for backup.");
                    return (false, messages);
                }

                // Encrypt and save the collected data
                await _logic.SaveDataAsync(backupData, masterPassword);
                messages.Insert(0, $"Successfully backed up {filesProcessed} browser file(s)."); // Add success message at start
                overallSuccess = true;

            }
            catch (Exception ex) // Catch errors during overall process or saving
            {
                 messages.Add($"Backup failed: {ex.Message}");
                 // Consider logging the full exception
                 // throw new FileOperationException("Browser file backup failed.", ex); // Or just return messages
            }
            finally
            {
                // Clean up the main temp directory
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { /* Ignore cleanup error */ }
                }
            }

            return (overallSuccess, messages);
        }


        /// <summary>
        /// Restores browser credential storage files from the encrypted archive.
        /// IMPORTANT: Assumes browsers are closed. Does not verify this.
        /// </summary>
        public async Task<(bool Success, List<string> Messages)> RestoreBrowserFilesAsync(string masterPassword)
        {
            var messages = new List<string>();
            bool overallSuccess = false; // Track if at least one file is restored

            // *** CORRECTED LINE ***
            // Check existence using the full path stored in this class instance
            if (!File.Exists(_backupFilePath))
            {
                messages.Add($"Backup file not found: {_backupFilePath}");
                return (false, messages);
            }

            BrowserBackupData? backupData;
            try
            {
                // Load data using the logic instance (which knows the correct _backupFilePath)
                backupData = await _logic.LoadDataAsync<BrowserBackupData>(masterPassword);
            }
            catch (Exception ex) // Catches DecryptionException, FileOperationException etc.
            {
                messages.Add($"Failed to load or decrypt backup file: {ex.Message}");
                return (false, messages);
            }

            if (backupData == null || !backupData.Files.Any())
            {
                messages.Add("Backup data is empty or invalid.");
                return (false, messages);
            }

            // --- Restore Process ---
            int filesRestored = 0;
            foreach (var kvp in backupData.Files)
            {
                string originalPathKey = kvp.Key;
                string base64Content = kvp.Value;

                // Retrieve the original path (redundant if key IS the path, but good practice)
                if (!backupData.OriginalPaths.TryGetValue(originalPathKey, out string? targetPathStr) || string.IsNullOrEmpty(targetPathStr))
                {
                    messages.Add($"Warning: Missing original path for key '{originalPathKey}'. Skipping.");
                    continue;
                }

                var targetPath = new FileInfo(targetPathStr);
                string tempWritePath = string.Empty;

                try
                {
                    // Ensure target directory exists
                    targetPath.Directory?.Create(); // Create directory if it doesn't exist

                    // Decode content
                    byte[] content = Convert.FromBase64String(base64Content);

                    // Write to a temporary file first in the *same* directory for atomic replace
                    tempWritePath = Path.Combine(targetPath.DirectoryName ?? "", $"_restore_{Guid.NewGuid()}_{targetPath.Name}.tmp");

                    await File.WriteAllBytesAsync(tempWritePath, content);

                    // Atomically replace
                    Console.WriteLine($"Restoring {targetPath.FullName} from temp {tempWritePath}");
                    File.Move(tempWritePath, targetPath.FullName, overwrite: true);
                    tempWritePath = string.Empty; // Clear path so finally block doesn't try delete
                    filesRestored++;
                    overallSuccess = true; // Mark success if at least one file is done
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    messages.Add($"Warning: Error restoring file '{targetPath.Name}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    messages.Add($"Warning: Unexpected error restoring file '{targetPath.Name}': {ex.Message}");
                }
                finally
                {
                    // Clean up temp file if move failed or an error occurred before move
                    if (!string.IsNullOrEmpty(tempWritePath) && File.Exists(tempWritePath))
                    {
                        try { File.Delete(tempWritePath); } catch { /* Ignore */ }
                    }
                }
            }

            if (filesRestored > 0) {
                 messages.Insert(0, $"Successfully restored {filesRestored} browser file(s).");
            } else if (!messages.Any(m => m.StartsWith("Warning:"))) {
                // If no files restored and no specific warnings, add a general message
                messages.Add("No files needed restoration or restore failed for all files.");
            }


            return (overallSuccess, messages);
        }

        /// <summary>
        /// Checks if any browser credential files exist on the system.
        /// </summary>
        public bool DoBrowserFilesExist()
        {
            var files = FindBrowserFiles();
            return files.Any();
        }

        /// <summary>
        /// Checks if a backup file exists.
        /// </summary>
        public bool DoesBackupExist()
        {
            return File.Exists(_backupFilePath);
        }

        /// <summary>
        /// Securely deletes all browser credential files (for app shutdown).
        /// </summary>
        public async Task<(bool Success, List<string> Messages)> SecureDeleteAllBrowserFilesAsync()
        {
            var messages = new List<string>();
            var filesToDelete = FindBrowserFiles();
            
            if (!filesToDelete.Any())
            {
                messages.Add("No browser files found to delete.");
                return (true, messages);
            }

            int deletedCount = 0;
            foreach (var filePath in filesToDelete)
            {
                try
                {
                    SecureDeleteFile(filePath);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    messages.Add($"Warning: Could not delete {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }
            
            messages.Add($"Securely deleted {deletedCount} browser files for security.");
            return (deletedCount > 0, messages);
        }

        /// <summary>
        /// Determines if browser files should be restored at startup.
        /// This happens when we have a backup but no browser files currently exist.
        /// </summary>
        public bool ShouldRestoreAtStartup()
        {
            return DoesBackupExist() && !DoBrowserFilesExist();
        }
    }
}
