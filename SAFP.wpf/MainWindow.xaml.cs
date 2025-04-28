using SAFP.Core; // Access core logic and models
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // For ObservableCollection
using System.Diagnostics; // For Debug.WriteLine
using System.IO; // Needed in ViewModel constructor
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input; // For ICommand
using System.ComponentModel; // For ClosingEventArgs


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
            Debug.WriteLine("[MainWindow] Constructor starting...");
            try
            {
                InitializeComponent();
                Debug.WriteLine("[MainWindow] InitializeComponent finished.");
                _viewModel = new MainViewModel(masterPassword, initialData);
                Debug.WriteLine("[MainWindow] MainViewModel created.");
                DataContext = _viewModel;
                Debug.WriteLine("[MainWindow] DataContext set.");

                // Handle lock request from ViewModel
                _viewModel.RequestLock += (sender, args) =>
                {
                    Debug.WriteLine("[MainWindow] RequestLock event received. Showing LoginWindow.");
                    // Show login window again
                    var loginWindow = new LoginWindow(((App)Application.Current).VaultFilePath, isInitialSetup: false);
                    Application.Current.MainWindow = loginWindow; // Set as main
                    loginWindow.Show();
                    this.Close(); // Close this main window
                };

                // Add Loaded event handler for debugging
                this.Loaded += MainWindow_Loaded;

                Debug.WriteLine("[MainWindow] Constructor finished successfully.");
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"[MainWindow] FATAL ERROR in Constructor: {ex}");
                 MessageBox.Show($"Fatal error initializing main window: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 // Attempt to shutdown gracefully if constructor fails badly
                 Application.Current?.Shutdown(1);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
             Debug.WriteLine("[MainWindow] Loaded event fired.");
             // You could add initialization logic here that needs the window to be fully loaded
        }


        // Handle window closing event to prompt user
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Debug.WriteLine($"[MainWindow] Window_Closing event triggered. CancelEventArgs.Cancel = {e.Cancel}");
            // If closing is already cancelled by something else, don't ask again
            if (e.Cancel)
            {
                Debug.WriteLine("[MainWindow] Closing already cancelled. Skipping CanExitApplication check.");
                return;
            }

            try
            {
                // Ask ViewModel if exit should be allowed (it handles the lock prompt)
                 if (!_viewModel.CanExitApplication()) // This method now contains Debug.WriteLine
                 {
                     Debug.WriteLine("[MainWindow] Exit cancelled by ViewModel. Setting e.Cancel = true.");
                     e.Cancel = true; // Prevent window from closing
                 } else {
                      Debug.WriteLine("[MainWindow] Exit allowed by ViewModel.");
                 }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Error during Window_Closing: {ex}");
                // Decide if you want to force close or show another error
                MessageBox.Show($"Error during window closing: {ex.Message}", "Closing Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Maybe cancel closing if error occurs?
                // e.Cancel = true;
            }
             Debug.WriteLine($"[MainWindow] Window_Closing event finished. CancelEventArgs.Cancel = {e.Cancel}");
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
             Debug.WriteLine("[MainViewModel] Constructor starting...");
            _masterPassword = masterPassword;
            _passwordData = initialData ?? new Dictionary<string, PasswordEntry>();

            // Determine path for main vault file (e.g., in AppData)
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string safpDataPath = Path.Combine(appDataPath, "SAFP"); // App-specific folder
            string vaultFilePath = Path.Combine(safpDataPath, "vault.safp"); // Use new extension
            Debug.WriteLine($"[MainViewModel] Vault Logic Path: {vaultFilePath}");

            _vaultLogic = new PasswordManagerLogic(vaultFilePath);
            _browserManager = new BrowserFileManager(); // Uses its own logic instance for browser_vault.safp
            Debug.WriteLine("[MainViewModel] Logic instances created.");

            _passwordEntries = new ObservableCollection<PasswordEntry>(
                 _passwordData.Select(kvp => { kvp.Value.Id = kvp.Key; return kvp.Value; }) // Assign ID for UI use
                            .OrderBy(p => p.Service, StringComparer.OrdinalIgnoreCase) // Initial sort
            );
            Debug.WriteLine($"[MainViewModel] PasswordEntries collection initialized with {_passwordEntries.Count} items.");

            // Initialize Commands
            AddEntryCommand = new RelayCommand(AddEntry, CanExecuteSimpleCommand);
            EditEntryCommand = new RelayCommand<PasswordEntry>(EditEntry, CanExecuteOnSelectedEntry);
            DeleteEntryCommand = new RelayCommand<PasswordEntry>(async (entry) => await DeleteEntryAsync(entry), CanExecuteOnSelectedEntry);
            CopyUsernameCommand = new RelayCommand<PasswordEntry>(CopyUsername, CanExecuteOnSelectedEntry);
            CopyPasswordCommand = new RelayCommand<PasswordEntry>(CopyPassword, CanExecuteOnSelectedEntry);
            LockVaultCommand = new RelayCommand(LockVault, CanExecuteSimpleCommand);
            BackupBrowserFilesCommand = new RelayCommand(async (_) => await BackupBrowserFilesAsync(), CanExecuteSimpleCommand);
            RestoreBrowserFilesCommand = new RelayCommand(async (_) => await RestoreBrowserFilesAsync(), CanExecuteSimpleCommand);
            Debug.WriteLine("[MainViewModel] Commands initialized.");
            Debug.WriteLine("[MainViewModel] Constructor finished.");
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
                    Debug.WriteLine($"[MainViewModel] SelectedEntry changed to: {value?.Service ?? "null"}");
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
                    Debug.WriteLine($"[MainViewModel] IsBusy changed to: {value}");
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
            Debug.WriteLine("[MainViewModel] AddEntry command executed.");
            if (IsBusy) return;
            // Rest of AddEntry logic...
            var newEntry = new PasswordEntry();
            var dialog = new EntryDialog(new EntryDialogViewModel(_vaultLogic, _masterPassword, newEntry, isNewEntry: true));
             if (dialog.ShowDialog() == true)
             {
                 string newId = dialog.ViewModel.EntryId ?? "";
                 if (!string.IsNullOrEmpty(newId) && _passwordData.ContainsKey(newId))
                 {
                     newEntry.Id = newId;
                     PasswordEntries.Add(newEntry);
                     RefreshEntriesCollection();
                     SelectedEntry = PasswordEntries.FirstOrDefault(e => e.Id == newId);
                     StatusMessage = $"Entry '{newEntry.Service}' added.";
                 } else {
                     StatusMessage = "Add operation finished, but entry might not be fully saved or ID is missing.";
                     RefreshEntriesCollection();
                 }
             } else {
                  StatusMessage = "Add operation cancelled.";
             }
        }

        private void EditEntry(PasswordEntry? entry)
        {
             Debug.WriteLine($"[MainViewModel] EditEntry command executed for: {entry?.Service ?? "null"}");
            if (entry == null || IsBusy) return;
            // Rest of EditEntry logic...
             var entryCopy = new PasswordEntry { Id = entry.Id, Category = entry.Category, Service = entry.Service, Username = entry.Username, Password = entry.Password, Notes = entry.Notes };
             var dialog = new EntryDialog(new EntryDialogViewModel(_vaultLogic, _masterPassword, entryCopy, isNewEntry: false));
             if (dialog.ShowDialog() == true)
             {
                 if (entry.Id != null && _passwordData.ContainsKey(entry.Id))
                 {
                     var originalInCollection = PasswordEntries.FirstOrDefault(e => e.Id == entry.Id);
                     if (originalInCollection != null) { originalInCollection.Category = entryCopy.Category; originalInCollection.Service = entryCopy.Service; originalInCollection.Username = entryCopy.Username; originalInCollection.Password = entryCopy.Password; originalInCollection.Notes = entryCopy.Notes; }
                     StatusMessage = $"Entry '{entry.Service}' updated.";
                     RefreshEntriesCollection();
                     SelectedEntry = PasswordEntries.FirstOrDefault(e => e.Id == entry.Id);
                 } else {
                     StatusMessage = "Edit failed: Original entry not found after dialog close.";
                     RefreshEntriesCollection();
                 }
             } else {
                  StatusMessage = "Edit operation cancelled.";
             }
        }

        private async Task DeleteEntryAsync(PasswordEntry? entry)
        {
             Debug.WriteLine($"[MainViewModel] DeleteEntryAsync command executed for: {entry?.Service ?? "null"}");
            if (entry == null || entry.Id == null || IsBusy) return;
            // Rest of DeleteEntryAsync logic...
             var result = MessageBox.Show($"Are you sure you want to delete the entry for '{entry.Service}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
             if (result == MessageBoxResult.Yes)
             {
                 IsBusy = true; StatusMessage = $"Deleting '{entry.Service}'..."; PasswordEntry? entryToRemoveFromUI = PasswordEntries.FirstOrDefault(p => p.Id == entry.Id);
                 try { if (_passwordData.Remove(entry.Id)) { await _vaultLogic.SaveDataAsync(_passwordData, _masterPassword); if (entryToRemoveFromUI != null) { PasswordEntries.Remove(entryToRemoveFromUI); } StatusMessage = $"Entry '{entry.Service}' deleted."; SelectedEntry = null; } else { StatusMessage = "Delete failed: Entry not found in data."; if (entryToRemoveFromUI != null) PasswordEntries.Remove(entryToRemoveFromUI); } } catch (Exception ex) { StatusMessage = $"Error deleting entry: {ex.Message}"; MessageBox.Show($"Failed to delete entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); RefreshEntriesCollection(); } finally { IsBusy = false; }
             }
        }

        private void CopyUsername(PasswordEntry? entry)
        {
             Debug.WriteLine($"[MainViewModel] CopyUsername command executed for: {entry?.Service ?? "null"}");
            if (entry == null || IsBusy) return;
            // Rest of CopyUsername logic...
             try { Clipboard.SetText(entry.Username ?? ""); StatusMessage = $"Username for '{entry.Service}' copied to clipboard."; } catch (Exception ex) { StatusMessage = $"Error copying username: {ex.Message}"; MessageBox.Show($"Could not copy username to clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void CopyPassword(PasswordEntry? entry)
        {
             Debug.WriteLine($"[MainViewModel] CopyPassword command executed for: {entry?.Service ?? "null"}");
             if (entry == null || IsBusy) return;
            // Rest of CopyPassword logic...
              try { if (!string.IsNullOrEmpty(entry.Password)) { Clipboard.SetText(entry.Password); StatusMessage = $"Password for '{entry.Service}' copied to clipboard."; } else { StatusMessage = $"Entry '{entry.Service}' has no password stored."; } } catch (Exception ex) { StatusMessage = $"Error copying password: {ex.Message}"; MessageBox.Show($"Could not copy password to clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void LockVault(object? parameter = null)
        {
            Debug.WriteLine("[MainViewModel] LockVault command executed.");
            if (IsBusy) return;
            StatusMessage = "Locking vault...";
            VaultStatus = "Locked";
            _masterPassword = string.Empty;
            _passwordData.Clear();
            PasswordEntries.Clear();
            SelectedEntry = null;
            try { Clipboard.Clear(); } catch { /* Ignore */ }
            Debug.WriteLine("[MainViewModel] Firing RequestLock event.");
            RequestLock?.Invoke(this, EventArgs.Empty);
        }


        private async Task BackupBrowserFilesAsync()
        {
            Debug.WriteLine("[MainViewModel] BackupBrowserFilesAsync command executed.");
            if (IsBusy || string.IsNullOrEmpty(_masterPassword)) return;
            // Rest of BackupBrowserFilesAsync logic...
              var promptResult = MessageBox.Show("IMPORTANT: Please ensure ALL web browsers (...) are completely closed before proceeding.\n\n...Continue with the browser file backup?", "Close Browsers Before Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning); if (promptResult != MessageBoxResult.Yes) { StatusMessage = "Browser file backup cancelled by user."; return; } IsBusy = true; StatusMessage = "Backing up browser files..."; try { var (success, messages) = await _browserManager.BackupBrowserFilesAsync(_masterPassword); string resultMessage = string.Join("\n", messages); StatusMessage = success ? $"Browser Backup Finished. {messages.FirstOrDefault()}" : "Browser Backup Finished with warnings/errors."; MessageBox.Show(resultMessage, success ? "Backup Result" : "Backup Result (with issues)", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning); } catch (Exception ex) { StatusMessage = $"Browser backup failed: {ex.Message}"; MessageBox.Show($"An unexpected error occurred during browser backup: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error); } finally { IsBusy = false; }
        }

        private async Task RestoreBrowserFilesAsync()
        {
            Debug.WriteLine("[MainViewModel] RestoreBrowserFilesAsync command executed.");
             if (IsBusy || string.IsNullOrEmpty(_masterPassword)) return;
            // Rest of RestoreBrowserFilesAsync logic...
             var promptResult = MessageBox.Show("IMPORTANT: Please ensure ALL web browsers (...) are completely closed before proceeding.\n\n...Continue with the browser file restore?", "Close Browsers Before Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning); if (promptResult != MessageBoxResult.Yes) { StatusMessage = "Browser file restore cancelled by user."; return; } IsBusy = true; StatusMessage = "Restoring browser files from backup..."; try { var (success, messages) = await _browserManager.RestoreBrowserFilesAsync(_masterPassword); string resultMessage = string.Join("\n", messages); StatusMessage = success ? $"Browser Restore Finished. {messages.FirstOrDefault()}" : "Browser Restore Finished with warnings/errors."; MessageBox.Show(resultMessage, success ? "Restore Result" : "Restore Result (with issues)", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning); } catch (Exception ex) { StatusMessage = $"Browser restore failed: {ex.Message}"; MessageBox.Show($"An unexpected error occurred during browser restore: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error); } finally { IsBusy = false; }
        }

        // --- Window Closing Logic ---
        public bool CanExitApplication()
        {
             Debug.WriteLine("[MainViewModel] CanExitApplication called.");
             var result = MessageBox.Show("Lock vault and exit SAFP?", "Confirm Exit",
                                          MessageBoxButton.YesNo, MessageBoxImage.Question);
             if (result == MessageBoxResult.Yes)
             {
                 Debug.WriteLine("[MainViewModel] User confirmed exit. Clearing sensitive data.");
                 _masterPassword = string.Empty;
                 _passwordData.Clear();
                 PasswordEntries.Clear();
                 try { Clipboard.Clear(); } catch { /* Ignore */ }
                 return true; // Allow window to close
             }
             Debug.WriteLine("[MainViewModel] User cancelled exit.");
             return false; // Prevent window from closing
        }

        // Helper to refresh the ObservableCollection from the source dictionary and re-sort
        private void RefreshEntriesCollection()
        {
             Debug.WriteLine("[MainViewModel] RefreshEntriesCollection called.");
             var sortedEntries = _passwordData.Select(kvp => { kvp.Value.Id = kvp.Key; return kvp.Value; })
                                            .OrderBy(p => p.Service, StringComparer.OrdinalIgnoreCase)
                                            .ToList();

             PasswordEntries.Clear();
             foreach(var entry in sortedEntries)
             {
                 PasswordEntries.Add(entry);
             }
             SelectedEntry = null;
             Debug.WriteLine($"[MainViewModel] Entries collection refreshed. Count: {PasswordEntries.Count}");
        }

    }
}
