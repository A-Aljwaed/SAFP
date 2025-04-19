using SAFP.core; // Access core logic and models
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // For ObservableCollection
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input; // For ICommand
using System.ComponentModel;
using System.IO;
using SAFP.Core; // For ClosingEventArgs


namespace SAFP.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow(string masterPassword, Dictionary<string, PasswordEntry> initialData)
        {
            InitializeComponent();
            _viewModel = new MainViewModel(masterPassword, initialData);
            DataContext = _viewModel;

            // Handle lock request from ViewModel
            _viewModel.RequestLock += (sender, args) =>
            {
                // Show login window again
                 var loginWindow = new LoginWindow(((App)Application.Current).VaultFilePath);
                 Application.Current.MainWindow = loginWindow; // Set as main
                 loginWindow.Show();
                 this.Close(); // Close this main window
            };
        }

        // Handle window closing event to prompt user
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Ask ViewModel if exit should be allowed (it handles the lock prompt)
             if (!_viewModel.CanExitApplication())
             {
                 e.Cancel = true; // Prevent window from closing
             }
        }
    }

    // --- ViewModel for MainWindow ---
    public class MainViewModel : ViewModelBase
    {
        private readonly PasswordManagerLogic _vaultLogic;
        private readonly BrowserFileManager _browserManager;
        private string _masterPassword; // Store securely? For now, keep in memory while unlocked.
        private Dictionary<string, PasswordEntry> _passwordData; // The raw data

        private ObservableCollection<PasswordEntry> _passwordEntries;
        private PasswordEntry? _selectedEntry;
        private string _statusMessage = "Vault unlocked.";
        private string _vaultStatus = "Unlocked";
        private bool _isBusy = false; // For indicating long operations

        // Event to signal UI to lock
        public event EventHandler? RequestLock;

        // Commands
        public ICommand AddEntryCommand { get; }
        public ICommand EditEntryCommand { get; }
        public ICommand DeleteEntryCommand { get; }
        public ICommand CopyUsernameCommand { get; }
        public ICommand CopyPasswordCommand { get; }
        public ICommand LockVaultCommand { get; }
        public ICommand BackupBrowserFilesCommand { get; }
        public ICommand RestoreBrowserFilesCommand { get; }


        public MainViewModel(string masterPassword, Dictionary<string, PasswordEntry> initialData)
        {
            _masterPassword = masterPassword;
            _passwordData = initialData ?? new Dictionary<string, PasswordEntry>();

            // Determine path for main vault file (e.g., in AppData)
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string safpDataPath = Path.Combine(appDataPath, "SAFP"); // App-specific folder
            string vaultFilePath = Path.Combine(safpDataPath, "vault.safp"); // Use new extension

            _vaultLogic = new PasswordManagerLogic(vaultFilePath);
            _browserManager = new BrowserFileManager(); // Uses its own logic instance for browser_vault.safp

            _passwordEntries = new ObservableCollection<PasswordEntry>(
                 _passwordData.Select(kvp => { kvp.Value.Id = kvp.Key; return kvp.Value; }) // Assign ID for UI use
                            .OrderBy(p => p.Service, StringComparer.OrdinalIgnoreCase) // Initial sort
            );

            // Initialize Commands
            AddEntryCommand = new RelayCommand(AddEntry, CanExecuteSimpleCommand);
            EditEntryCommand = new RelayCommand<PasswordEntry>(EditEntry, CanExecuteOnSelectedEntry);
            DeleteEntryCommand = new RelayCommand<PasswordEntry>(async (entry) => await DeleteEntryAsync(entry), CanExecuteOnSelectedEntry);
            CopyUsernameCommand = new RelayCommand<PasswordEntry>(CopyUsername, CanExecuteOnSelectedEntry);
            CopyPasswordCommand = new RelayCommand<PasswordEntry>(CopyPassword, CanExecuteOnSelectedEntry);
            LockVaultCommand = new RelayCommand(LockVault, CanExecuteSimpleCommand);
            BackupBrowserFilesCommand = new RelayCommand(async (_) => await BackupBrowserFilesAsync(), CanExecuteSimpleCommand);
            RestoreBrowserFilesCommand = new RelayCommand(async (_) => await RestoreBrowserFilesAsync(), CanExecuteSimpleCommand);
        }

        // --- Properties for Data Binding ---

        public ObservableCollection<PasswordEntry> PasswordEntries
        {
            get => _passwordEntries;
            set => SetProperty(ref _passwordEntries, value);
        }

        public PasswordEntry? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    // Trigger CanExecuteChanged for commands that depend on selection
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
         public string VaultStatus
        {
            get => _vaultStatus;
            set => SetProperty(ref _vaultStatus, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if(SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested(); // Update command states
                }
            }
        }


        // --- Command Predicates (CanExecute logic) ---

        private bool CanExecuteSimpleCommand(object? parameter = null) => !IsBusy;
        private bool CanExecuteOnSelectedEntry(PasswordEntry? entry) => entry != null && !IsBusy;


        // --- Command Implementations ---

        private void AddEntry(object? parameter = null)
        {
            if (IsBusy) return;

            var newEntry = new PasswordEntry(); // Create empty entry
            var dialog = new EntryDialog(new EntryDialogViewModel(_vaultLogic, _masterPassword, newEntry, isNewEntry: true));

            if (dialog.ShowDialog() == true) // ShowDialog returns true if saved
            {
                 // ViewModel handled saving, just need to update UI collection
                 string newId = dialog.ViewModel.EntryId ?? ""; // Get ID from dialog VM
                 if (!string.IsNullOrEmpty(newId) && _passwordData.ContainsKey(newId)) // Check if actually added
                 {
                     newEntry.Id = newId; // Ensure ID is set for UI
                     PasswordEntries.Add(newEntry); // Add to observable collection
                     // Re-sort or insert in sorted order if needed
                     PasswordEntries = new ObservableCollection<PasswordEntry>(PasswordEntries.OrderBy(p => p.Service, StringComparer.OrdinalIgnoreCase));
                     SelectedEntry = newEntry; // Select the new entry
                     StatusMessage = $"Entry '{newEntry.Service}' added.";
                 } else {
                     StatusMessage = "Add operation cancelled or failed.";
                 }
            }
        }

        private void EditEntry(PasswordEntry? entry)
        {
            if (entry == null || IsBusy) return;

            // Important: Work on a copy for editing to allow cancellation
            var entryCopy = new PasswordEntry {
                 Id = entry.Id, // Keep track of original ID
                 Category = entry.Category,
                 Service = entry.Service,
                 Username = entry.Username,
                 Password = entry.Password, // Pass original password
                 Notes = entry.Notes
             };

            var dialog = new EntryDialog(new EntryDialogViewModel(_vaultLogic, _masterPassword, entryCopy, isNewEntry: false));

            if (dialog.ShowDialog() == true)
            {
                // Update the original entry in the dictionary and the observable collection
                if (entry.Id != null && _passwordData.ContainsKey(entry.Id))
                {
                    _passwordData[entry.Id] = entryCopy; // Update dictionary (ViewModel saved this)

                    // Update the item in the ObservableCollection
                    entry.Category = entryCopy.Category;
                    entry.Service = entryCopy.Service;
                    entry.Username = entryCopy.Username;
                    entry.Password = entryCopy.Password; // Update password if changed
                    entry.Notes = entryCopy.Notes;
                    // No need to replace item in ObservableCollection if PasswordEntry implements INotifyPropertyChanged correctly

                    StatusMessage = $"Entry '{entry.Service}' updated.";
                    // Re-sort if necessary (e.g., if Service name changed)
                    PasswordEntries = new ObservableCollection<PasswordEntry>(PasswordEntries.OrderBy(p => p.Service, StringComparer.OrdinalIgnoreCase));
                    SelectedEntry = entry; // Re-select
                } else {
                    StatusMessage = "Edit failed: Original entry not found.";
                }
            }
        }

        private async Task DeleteEntryAsync(PasswordEntry? entry)
        {
            if (entry == null || entry.Id == null || IsBusy) return;

            var result = MessageBox.Show($"Are you sure you want to delete the entry for '{entry.Service}'?",
                                         "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                IsBusy = true;
                StatusMessage = $"Deleting '{entry.Service}'...";
                try
                {
                    if (_passwordData.Remove(entry.Id)) // Remove from dictionary
                    {
                        await _vaultLogic.SaveDataAsync(_passwordData, _masterPassword); // Save changes
                        PasswordEntries.Remove(entry); // Remove from UI collection
                        StatusMessage = $"Entry '{entry.Service}' deleted.";
                        SelectedEntry = null;
                    }
                    else
                    {
                        StatusMessage = "Delete failed: Entry not found in data.";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error deleting entry: {ex.Message}";
                    MessageBox.Show($"Failed to delete entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Consider adding the entry back to _passwordData if save failed? More complex recovery.
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private void CopyUsername(PasswordEntry? entry)
        {
            if (entry == null || IsBusy) return;
            try
            {
                Clipboard.SetText(entry.Username ?? "");
                StatusMessage = $"Username for '{entry.Service}' copied to clipboard.";
                // Add clipboard clearing later if desired
            }
            catch (Exception ex)
            {
                 StatusMessage = $"Error copying username: {ex.Message}";
                 MessageBox.Show($"Could not copy username to clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyPassword(PasswordEntry? entry)
        {
             if (entry == null || IsBusy) return;
             try
             {
                 if (!string.IsNullOrEmpty(entry.Password))
                 {
                     Clipboard.SetText(entry.Password);
                     StatusMessage = $"Password for '{entry.Service}' copied to clipboard. Will clear soon."; // Update message
                     // TODO: Implement clipboard clearing timer if needed
                 }
                 else
                 {
                     StatusMessage = $"Entry '{entry.Service}' has no password stored.";
                 }
             }
             catch (Exception ex)
             {
                  StatusMessage = $"Error copying password: {ex.Message}";
                  MessageBox.Show($"Could not copy password to clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
             }
        }

        private void LockVault(object? parameter = null)
        {
            if (IsBusy) return;
            StatusMessage = "Locking vault...";
            VaultStatus = "Locked";

            // Clear sensitive data
            _masterPassword = string.Empty;
            _passwordData.Clear();
            PasswordEntries.Clear();
            SelectedEntry = null;

            // Clear clipboard as a precaution
            try { Clipboard.Clear(); } catch { /* Ignore */ }

            // Signal the MainWindow (View) to close and show LoginWindow
            RequestLock?.Invoke(this, EventArgs.Empty);
        }


        private async Task BackupBrowserFilesAsync()
        {
            if (IsBusy) return;

             // **CRITICAL: Prompt user to close browsers**
            var promptResult = MessageBox.Show(
                "IMPORTANT: Please ensure ALL web browsers (Chrome, Edge, Firefox, Brave, etc.) are completely closed before proceeding.\n\n" +
                "Backing up files while browsers are running can lead to corrupted backups.\n\n" +
                "Do you want to continue with the browser file backup?",
                "Close Browsers Before Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (promptResult != MessageBoxResult.Yes)
            {
                StatusMessage = "Browser file backup cancelled by user.";
                return;
            }


            IsBusy = true;
            StatusMessage = "Backing up browser files...";
            try
            {
                var (success, messages) = await _browserManager.BackupBrowserFilesAsync(_masterPassword);
                string resultMessage = string.Join("\n", messages);
                StatusMessage = success ? $"Browser Backup Finished. {messages.FirstOrDefault()}" : "Browser Backup Finished with warnings/errors.";

                MessageBox.Show(resultMessage, success ? "Backup Result" : "Backup Result (with issues)", MessageBoxButton.OK,
                                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Browser backup failed: {ex.Message}";
                MessageBox.Show($"An unexpected error occurred during browser backup: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RestoreBrowserFilesAsync()
        {
             if (IsBusy) return;

             // **CRITICAL: Prompt user to close browsers**
            var promptResult = MessageBox.Show(
                "IMPORTANT: Please ensure ALL web browsers (Chrome, Edge, Firefox, Brave, etc.) are completely closed before proceeding.\n\n" +
                "Restoring files while browsers are running WILL likely corrupt browser profiles or fail.\n\n" +
                "This will OVERWRITE current browser files with the backup.\n\n" +
                "Do you want to continue with the browser file restore?",
                "Close Browsers Before Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (promptResult != MessageBoxResult.Yes)
            {
                StatusMessage = "Browser file restore cancelled by user.";
                return;
            }


            IsBusy = true;
            StatusMessage = "Restoring browser files from backup...";
            try
            {
                var (success, messages) = await _browserManager.RestoreBrowserFilesAsync(_masterPassword);
                string resultMessage = string.Join("\n", messages);
                StatusMessage = success ? $"Browser Restore Finished. {messages.FirstOrDefault()}" : "Browser Restore Finished with warnings/errors.";

                MessageBox.Show(resultMessage, success ? "Restore Result" : "Restore Result (with issues)", MessageBoxButton.OK,
                                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                 StatusMessage = $"Browser restore failed: {ex.Message}";
                 MessageBox.Show($"An unexpected error occurred during browser restore: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // --- Window Closing Logic ---
        public bool CanExitApplication()
        {
             var result = MessageBox.Show("Lock vault and exit SAFP?", "Confirm Exit",
                                          MessageBoxButton.YesNo, MessageBoxImage.Question);
             if (result == MessageBoxResult.Yes)
             {
                 LockVault(); // Perform lock actions (clears password etc.)
                 // We don't trigger RequestLock here, just allow exit
                 return true; // Allow window to close
             }
             return false; // Prevent window from closing
        }

    }
}
