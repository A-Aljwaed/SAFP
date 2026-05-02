using SAFP.Core; // For PasswordEntry, PasswordManagerLogic, BrowserFileManager
using System;
using System.Collections.Generic;
using System.Diagnostics; // For Debug.WriteLine
using System.Drawing; // For Icon (Windows Forms)
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // For NotifyIcon
using System.Windows.Threading; // For DispatcherUnhandledExceptionEventArgs
using Microsoft.Win32; // For SystemEvents

namespace SAFP.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public string? MasterPassword { get; set; } = null;
        public string VaultFilePath { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SAFP", "vault.safp");

        private PasswordManagerLogic? _logic;
        private BrowserFileManager? _browserManager;

        // Background / tray support
        private NotifyIcon? _trayIcon;
        private Icon? _trayIconResource; // Owns the stream-loaded icon; needs explicit disposal
        private DispatcherTimer? _periodicBackupTimer;
        private const int BackupIntervalMinutes = 30;

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
                            System.Windows.MessageBox.Show("Welcome to SAFP!\n\n" +
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
                            System.Windows.MessageBox.Show("Warning: Could not automatically backup browser passwords:\n\n" +
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
                            System.Windows.MessageBox.Show("Warning: Could not restore browser passwords:\n\n" +
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

            // 7. Initialize system tray icon and background services
            InitializeTrayIcon();
            StartPeriodicBackupTimer();
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            Debug.WriteLine("[App] Application_Startup finished successfully.");
        }

        // -------------------------------------------------------------------------
        // System Tray Icon
        // -------------------------------------------------------------------------

        private void InitializeTrayIcon()
        {
            // Try to load the application icon from the embedded resource.
            // Keep a reference in _trayIconResource so we can dispose it on exit.
            // If loading fails, fall back to a copy of the system shield icon
            // (we copy it so we own the handle and can safely dispose it later).
            try
            {
                var streamInfo = GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (streamInfo?.Stream != null)
                    _trayIconResource = new Icon(streamInfo.Stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Could not load tray icon: {ex.Message}");
            }

            if (_trayIconResource == null)
                _trayIconResource = new Icon(SystemIcons.Shield, SystemIcons.Shield.Size);

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("🔐 Open SAFP", null, (s, e) => Dispatcher.Invoke(ShowMainWindow));
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("💾 Backup Browser Passwords Now", null, async (s, e) => await TrayBackupNowAsync());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("❌ Exit SAFP", null, async (s, e) => await TrayExitAsync());

            _trayIcon = new NotifyIcon
            {
                Icon = _trayIconResource,
                Text = "SAFP - Password Manager",
                ContextMenuStrip = contextMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => Dispatcher.Invoke(ShowMainWindow);

            Debug.WriteLine("[App] System tray icon initialized.");
        }

        /// <summary>Shows or restores the main window from the tray.</summary>
        internal void ShowMainWindow()
        {
            if (MainWindow != null)
            {
                if (!MainWindow.IsVisible)
                    MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        }

        /// <summary>Shows a balloon notification from the tray icon.</summary>
        internal void ShowTrayBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _trayIcon?.ShowBalloonTip(3000, title, message, icon);
        }

        private async Task TrayBackupNowAsync()
        {
            if (_browserManager == null || string.IsNullOrEmpty(MasterPassword))
            {
                ShowTrayBalloon("SAFP", "Not logged in – please open SAFP first.", ToolTipIcon.Warning);
                return;
            }

            try
            {
                Debug.WriteLine("[App] Tray-initiated browser backup starting...");
                var (success, messages) = await _browserManager.BackupBrowserFilesAsync(MasterPassword);
                string msg = messages.FirstOrDefault() ?? (success ? "Backup successful." : "Backup failed.");
                Debug.WriteLine($"[App] Tray backup: {success}. {string.Join("; ", messages)}");
                ShowTrayBalloon("SAFP Backup", msg, success ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Tray backup error: {ex.Message}");
                ShowTrayBalloon("SAFP Backup Error", ex.Message, ToolTipIcon.Error);
            }
        }

        private async Task TrayExitAsync()
        {
            var result = System.Windows.MessageBox.Show(
                "Exit SAFP?\n\nBrowser password files will be backed up and secured before exit.",
                "Exit SAFP",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Allow the main window to close (don't minimize to tray)
            (MainWindow as MainWindow)?.AllowClose();

            // Perform backup + deletion, then shut down
            await PerformExitCleanupAsync(showLockedFileInfo: true);
            Current.Shutdown();
        }

        // -------------------------------------------------------------------------
        // Periodic Backup Timer
        // -------------------------------------------------------------------------

        private void StartPeriodicBackupTimer()
        {
            _periodicBackupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(BackupIntervalMinutes)
            };
            _periodicBackupTimer.Tick += async (s, e) => await PeriodicBackupTickAsync();
            _periodicBackupTimer.Start();
            Debug.WriteLine($"[App] Periodic backup timer started (interval: {BackupIntervalMinutes} min).");
        }

        private async Task PeriodicBackupTickAsync()
        {
            if (_browserManager == null || string.IsNullOrEmpty(MasterPassword))
                return;

            try
            {
                Debug.WriteLine("[App] Periodic browser backup starting...");
                var (success, messages) = await _browserManager.BackupBrowserFilesAsync(MasterPassword);
                Debug.WriteLine($"[App] Periodic backup: {success}. {string.Join("; ", messages)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Periodic backup error: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Session Ending (PC Shutdown / Logoff)
        // -------------------------------------------------------------------------

        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            Debug.WriteLine($"[App] Session ending. Reason: {e.Reason}. Performing emergency backup...");

            if (_browserManager == null || string.IsNullOrEmpty(MasterPassword))
                return;

            try
            {
                // Run on a background thread to avoid deadlocking the UI thread
                // (GetAwaiter().GetResult() on UI thread would block its own continuations).
                Task.Run(() => _browserManager!.BackupBrowserFilesAsync(MasterPassword!))
                    .GetAwaiter().GetResult();
                Debug.WriteLine("[App] Emergency backup on session end completed.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Emergency backup error on session end: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Shared Exit-Cleanup Helper
        // -------------------------------------------------------------------------

        /// <summary>
        /// Backs up browser files and (where possible) securely deletes originals.
        /// Clears <see cref="MasterPassword"/> afterwards to prevent duplicate runs.
        /// </summary>
        private async Task PerformExitCleanupAsync(bool showLockedFileInfo = false)
        {
            if (_browserManager == null || string.IsNullOrEmpty(MasterPassword))
                return;

            try
            {
                Debug.WriteLine("[App] PerformExitCleanupAsync: backing up browser files...");
                var (backupSuccess, backupMessages) = await _browserManager.BackupBrowserFilesAsync(MasterPassword);
                Debug.WriteLine($"[App] Exit backup: {backupSuccess}. {string.Join("; ", backupMessages)}");

                if (backupSuccess)
                {
                    // Allow reboot-scheduled deletion as fallback (browser may still be running)
                    var (deleteSuccess, deleteMessages, lockedFiles) =
                        await _browserManager.SecureDeleteAllBrowserFilesAsync(requireImmediateDeletion: false);
                    Debug.WriteLine($"[App] Exit deletion: {deleteSuccess}. {string.Join("; ", deleteMessages)}");

                    if (!deleteSuccess && lockedFiles.Any() && showLockedFileInfo)
                    {
                        var names = string.Join("\n", lockedFiles.Select(f => "• " + Path.GetFileName(f)));
                        System.Windows.MessageBox.Show(
                            $"The following browser files are currently in use by a running browser.\n" +
                            $"They will be securely removed the next time the computer restarts:\n\n{names}",
                            "Browser Files Locked – Will Be Removed on Restart",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    Debug.WriteLine("[App] Backup incomplete – skipping deletion to preserve original files.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during exit cleanup: {ex.Message}");
            }
            finally
            {
                // Clear sensitive data so OnExit does not run a duplicate backup
                MasterPassword = null;
            }
        }

        // -------------------------------------------------------------------------
        // Global Exception Handler
        // -------------------------------------------------------------------------

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[App] !!!!! DispatcherUnhandledException caught !!!!!");
            Debug.WriteLine($"[App] Exception: {e.Exception}");

            string errorMessage = $"An unhandled error occurred: {e.Exception.Message}";
            System.Windows.MessageBox.Show(errorMessage, "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true;
        }

        // Helper to show fatal error and optionally shutdown
        private void ShowFatalError(string message, bool shutdown = false)
        {
            Debug.WriteLine($"[App] FATAL ERROR: {message}");
            System.Windows.MessageBox.Show($"{message}\n{(shutdown ? "Application will exit." : "")}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            if (shutdown)
            {
                if (Dispatcher.CheckAccess()) { Current.Shutdown(1); }
                else { Dispatcher.Invoke(() => Current.Shutdown(1)); }
            }
        }

        // -------------------------------------------------------------------------
        // Application Exit
        // -------------------------------------------------------------------------

        protected override async void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine($"[App] OnExit called (code: {e.ApplicationExitCode}).");

            // Unsubscribe from OS events
            SystemEvents.SessionEnding -= SystemEvents_SessionEnding;

            // Stop the periodic backup timer
            _periodicBackupTimer?.Stop();

            // Final backup if the explicit exit path did not already clear MasterPassword
            if (_browserManager != null && !string.IsNullOrEmpty(MasterPassword))
            {
                try
                {
                    Debug.WriteLine("[App] OnExit: running fallback backup...");
                    var (backupSuccess, _) = await _browserManager.BackupBrowserFilesAsync(MasterPassword);
                    if (backupSuccess)
                        await _browserManager.SecureDeleteAllBrowserFilesAsync(requireImmediateDeletion: false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] OnExit backup error: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("[App] Skipping OnExit backup – already performed by explicit exit path.");
            }

            // Dispose tray icon and its associated icon resource
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _trayIconResource?.Dispose();
            _trayIconResource = null;

            base.OnExit(e);
        }
    }
}
