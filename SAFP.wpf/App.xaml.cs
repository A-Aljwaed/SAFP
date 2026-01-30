using SAFP.Core; // For PasswordEntry, PasswordManagerLogic, BrowserFileManager
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics; // For Debug.WriteLine
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading; // For DispatcherUnhandledExceptionEventArgs

namespace SAFP.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public string? MasterPassword { get; set; } = null;
        public string VaultFilePath { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SAFP", "vault.safp");

        private PasswordManagerLogic? _logic;
        private BrowserFileManager? _browserManager;

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // Add handler for unhandled exceptions on the UI thread
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Ensure ShutdownMode is set (can also be done in XAML)
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Debug.WriteLine($"[App] ======================================");
            Debug.WriteLine($"[App] Application_Startup started.");
            Debug.WriteLine($"[App] Vault path: {VaultFilePath}");

            // 1. Ensure Directory Exists
            string? vaultDir = Path.GetDirectoryName(VaultFilePath);
            if (!string.IsNullOrEmpty(vaultDir) && !Directory.Exists(vaultDir))
            {
                try { Directory.CreateDirectory(vaultDir); }
                catch (Exception ex) { ShowFatalError($"Failed to create application data directory: {ex.Message}", true); return; }
            }

            // 2. Initialize Logic and Browser Manager
            try 
            { 
                _logic = new PasswordManagerLogic(VaultFilePath); 
                _browserManager = new BrowserFileManager();
            }
            catch (Exception ex) { ShowFatalError($"Failed to initialize core logic: {ex.Message}", true); return; }

            // 3. Check Vault Existence
            bool vaultExists = File.Exists(VaultFilePath);
            Debug.WriteLine($"[App] Vault file exists check: {vaultExists}");
            bool isInitialSetup = !vaultExists;

            // 3.5. Handle automatic browser backup for first-time users
            bool shouldAutoBackupBrowsers = isInitialSetup && _browserManager.DoBrowserFilesExist();
            Debug.WriteLine($"[App] Should auto-backup browsers: {shouldAutoBackupBrowsers}");

            // 4. Show Login/Setup Window
            Dictionary<string, PasswordEntry>? initialData = null;
            bool loginOrSetupSuccess = false;

            try
            {
                Debug.WriteLine($"[App] Creating LoginWindow (isInitialSetup: {isInitialSetup})");
                var loginWindow = new LoginWindow(VaultFilePath, isInitialSetup: isInitialSetup);
                bool? dialogResult = loginWindow.ShowDialog();
                Debug.WriteLine($"[App] LoginWindow closed with DialogResult: {dialogResult}");

                if (dialogResult == true && !string.IsNullOrEmpty(MasterPassword))
                { loginOrSetupSuccess = true; Debug.WriteLine("[App] Login/Setup reported success by LoginWindow."); }
                else
                { Debug.WriteLine("[App] Login/Setup failed or cancelled by user. Shutting down."); Current.Shutdown(); return; }
            }
            catch (Exception ex) { ShowFatalError($"An error occurred showing the login window: {ex.Message}", true); return; }

            // 5. Load Data (if login was successful)
            if (loginOrSetupSuccess && !string.IsNullOrEmpty(MasterPassword))
            {
                try
                {
                    Debug.WriteLine("[App] Loading vault data...");
                    initialData = await _logic.LoadDataAsync<Dictionary<string, PasswordEntry>>(MasterPassword);
                    if (initialData == null) { initialData = new Dictionary<string, PasswordEntry>(); Debug.WriteLine("[App] WARNING: LoadDataAsync returned null. Using empty data."); }
                    Debug.WriteLine($"[App] Vault data loaded. Entry count: {initialData.Count}");

                    // Perform automatic browser backup and secure delete for first-time users
                    if (shouldAutoBackupBrowsers)
                    {
                        Debug.WriteLine("[App] Performing automatic browser backup for first-time user...");
                        var (backupSuccess, backupMessages) = await _browserManager.BackupAndSecureDeleteAsync(MasterPassword);
                        if (backupSuccess)
                        {
                            Debug.WriteLine("[App] Automatic browser backup completed successfully.");
                            MessageBox.Show("Welcome to SAFP!\n\n" +
                                          "Your browser passwords have been automatically backed up and secured. " +
                                          "The original files have been securely deleted to prevent unauthorized access.\n\n" +
                                          "Browser passwords will be restored when SAFP is running and " +
                                          "securely removed when you close the application.\n\n" +
                                          string.Join("\n", backupMessages), 
                                          "Browser Passwords Secured", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            Debug.WriteLine("[App] Automatic browser backup failed.");
                            MessageBox.Show("Warning: Could not automatically backup browser passwords:\n\n" +
                                          string.Join("\n", backupMessages), 
                                          "Browser Backup Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }

                    // Restore browser files when app starts (if needed)
                    if (_browserManager.ShouldRestoreAtStartup())
                    {
                        Debug.WriteLine("[App] Restoring browser files from backup...");
                        var (restoreSuccess, restoreMessages) = await _browserManager.RestoreBrowserFilesAsync(MasterPassword);
                        if (restoreSuccess)
                        {
                            Debug.WriteLine("[App] Browser files restored successfully.");
                        }
                        else
                        {
                            Debug.WriteLine($"[App] Browser file restore failed: {string.Join("; ", restoreMessages)}");
                            MessageBox.Show("Warning: Could not restore browser passwords:\n\n" +
                                          string.Join("\n", restoreMessages), 
                                          "Browser Restore Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else if (_browserManager.DoesBackupExist())
                    {
                        Debug.WriteLine("[App] Browser backup exists and browser files are present - no restore needed.");
                    }
                }
                catch (Exception ex) { ShowFatalError($"Failed to load vault data after login: {ex.Message}", true); return; }
            }

            // 6. Show Main Window (only if everything succeeded)
            if (loginOrSetupSuccess && initialData != null && !string.IsNullOrEmpty(MasterPassword))
            {
                try
                {
                    Debug.WriteLine("[App] Creating MainWindow instance...");
                    var mainWindow = new MainWindow(MasterPassword, initialData);
                    this.MainWindow = mainWindow;
                    Debug.WriteLine("[App] Showing MainWindow...");
                    mainWindow.Show();
                    Debug.WriteLine("[App] MainWindow shown successfully.");
                }
                catch (Exception ex) { ShowFatalError($"An unexpected error occurred creating or showing the main window: {ex.Message}", true); return; }
            }
            else { Debug.WriteLine("[App] ERROR: Reached end of startup unexpectedly."); ShowFatalError("Application startup failed due to an unexpected state after login/setup.", true); return; }

            Debug.WriteLine("[App] Application_Startup finished successfully.");
        }

        // Global Exception Handler for UI Thread
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[App] !!!!! DispatcherUnhandledException caught !!!!!");
            Debug.WriteLine($"[App] Exception: {e.Exception}");

            // Show a message to the user
            string errorMessage = $"An unhandled error occurred: {e.Exception.Message}";
            MessageBox.Show(errorMessage, "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Prevent default WPF crash handling
            e.Handled = true;

            // Optionally, decide whether to shut down or try to continue
            // For safety, shutting down is often best unless you can recover gracefully.
            // ShowFatalError("An unhandled error occurred on the UI thread.", true);
            // OR attempt to close gracefully:
            // Current.Shutdown();
        }


        // Helper to show fatal error and optionally shutdown
        private void ShowFatalError(string message, bool shutdown = false)
        {
            Debug.WriteLine($"[App] FATAL ERROR: {message}");
            MessageBox.Show($"{message}\n{(shutdown ? "Application will exit." : "")}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            if (shutdown)
            {
                if (Dispatcher.CheckAccess()) { Current.Shutdown(1); }
                else { Dispatcher.Invoke(() => Current.Shutdown(1)); }
            }
        }

        // Override OnExit or handle MainWindow closing differently if needed with OnExplicitShutdown
        protected override async void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine($"[App] Application exiting with code: {e.ApplicationExitCode}");
            
            // Backup and securely delete browser files when app closes
            if (_browserManager != null && !string.IsNullOrEmpty(MasterPassword))
            {
                try
                {
                    // First, backup browser files to ensure they're up-to-date
                    Debug.WriteLine("[App] Backing up browser files before exit...");
                    var (backupSuccess, backupMessages) = await _browserManager.BackupBrowserFilesAsync(MasterPassword);
                    Debug.WriteLine($"[App] Browser file backup completed. Success: {backupSuccess}");
                    if (backupSuccess)
                    {
                        Debug.WriteLine("[App] Browser backup successful. Messages: " + string.Join("; ", backupMessages));
                    }
                    else
                    {
                        Debug.WriteLine("[App] Browser backup had issues: " + string.Join("; ", backupMessages));
                    }
                    
                    // Then securely delete browser files for security
                    Debug.WriteLine("[App] Securely deleting browser files on exit...");
                    var (deleteSuccess, deleteMessages) = await _browserManager.SecureDeleteAllBrowserFilesAsync();
                    Debug.WriteLine($"[App] Browser file deletion completed. Success: {deleteSuccess}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error during browser file backup/cleanup: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("[App] Skipping browser backup/cleanup - manager or password not available");
            }
            
            base.OnExit(e);
        }
    }
}
