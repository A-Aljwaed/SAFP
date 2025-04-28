using SAFP.Core; // For PasswordEntry, PasswordManagerLogic
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

            // 2. Initialize Logic
            try { _logic = new PasswordManagerLogic(VaultFilePath); }
            catch (Exception ex) { ShowFatalError($"Failed to initialize core logic: {ex.Message}", true); return; }

            // 3. Check Vault Existence
            bool vaultExists = File.Exists(VaultFilePath);
            Debug.WriteLine($"[App] Vault file exists check: {vaultExists}");
            bool isInitialSetup = !vaultExists;

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
        protected override void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine($"[App] Application exiting with code: {e.ApplicationExitCode}");
            base.OnExit(e);
        }
    }
}
