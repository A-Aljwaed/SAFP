using SAFP.Core; // For PasswordEntry, PasswordManagerLogic
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SAFP.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Store master password securely in memory only while unlocked
        public string? MasterPassword { get; set; } = null;

        // Define vault file path (consider making this configurable)
        public string VaultFilePath { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SAFP", // App-specific folder
            "vault.safp"); // Use new extension

        private PasswordManagerLogic? _tempLogic; // Temporary logic instance for startup checks

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            _tempLogic = new PasswordManagerLogic(VaultFilePath);
            bool vaultExists = File.Exists(VaultFilePath);
            bool proceedToMain = false;

            // --- Login / Initial Setup Flow ---
            while (!proceedToMain) // Loop until login succeeds or app exits
            {
                var loginWindow = new LoginWindow(VaultFilePath, isInitialSetup: !vaultExists);
                bool? loginResult = loginWindow.ShowDialog(); // Show as modal dialog

                if (loginResult == true)
                {
                    // Login/Setup was successful, MasterPassword is set by LoginWindow via App instance
                    if (MasterPassword != null)
                    {
                        proceedToMain = true; // Allow loop to exit and show main window
                    }
                    else
                    {
                         // Should not happen if LoginWindow logic is correct
                         MessageBox.Show("Login succeeded but master password was not set. Exiting.", "Internal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                         Current.Shutdown();
                         return;
                    }
                    vaultExists = true; // Vault now definitely exists (or was just created)
                }
                else
                {
                    // Login/Setup failed or was cancelled
                    // Use !vaultExists to determine if it was initial setup phase
                    bool wasInitialSetup = !vaultExists;
                    MessageBox.Show( wasInitialSetup ? "Setup cancelled. Application will exit." : "Login failed or cancelled. Application will exit.",
                                     "Exiting",
                                     MessageBoxButton.OK,
                                     wasInitialSetup ? MessageBoxImage.Warning : MessageBoxImage.Stop); // Use correct image based on phase
                    Current.Shutdown(); // Exit application
                    return;
                }
            }


            // --- Load Data and Show Main Window ---
            if (proceedToMain && !string.IsNullOrEmpty(MasterPassword))
            {
                 try
                 {
                     // Load initial data using the successful password
                     // Ensure _tempLogic is not null here (it should be assigned above)
                     if (_tempLogic == null) {
                         throw new InvalidOperationException("Logic instance was unexpectedly null.");
                     }
                     var initialData = await _tempLogic.LoadDataAsync<Dictionary<string, PasswordEntry>>(MasterPassword);
                     if (initialData == null) {
                          // Handle potential null case from loading, though LoadDataAsync should throw or return empty
                          initialData = new Dictionary<string, PasswordEntry>();
                          MessageBox.Show("Warning: Vault data loaded as null, starting with empty vault.", "Load Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                     }

                     var mainWindow = new MainWindow(MasterPassword, initialData);
                     this.MainWindow = mainWindow; // Set the main window
                     mainWindow.Show();
                 }
                 catch (Exception ex) // Catch errors during final load
                 {
                     MessageBox.Show($"Failed to load vault data after login: {ex.Message}\nApplication will exit.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                     Current.Shutdown();
                 }
            }
             // else: Should have already exited if proceedToMain is false or MasterPassword is null

            _tempLogic = null; // Release temporary logic instance
        }
    }
}
