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
using System.Windows.Threading; // For Dispatcher
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging; // For Clipboard check

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
                try
                {
                    // Try to load the application icon
                    this.Icon = new BitmapImage(new Uri("pack://application:,,,/app.ico"));
                }
                catch (Exception iconEx) when (iconEx is IOException || iconEx is InvalidOperationException || iconEx is NotSupportedException)
                {
                    Debug.WriteLine($"[MainWindow] Warning: Failed to load window icon: {iconEx.Message}");
                    // Continue without icon - not critical for application functionality
                }

                InitializeComponent();
                Debug.WriteLine("[MainWindow] InitializeComponent finished.");
                _viewModel = new MainViewModel(masterPassword, initialData);
                Debug.WriteLine("[MainWindow] MainViewModel created.");
                DataContext = _viewModel;
                Debug.WriteLine("[MainWindow] DataContext set.");

                _viewModel.RequestLock += (sender, args) =>
                {
                    Debug.WriteLine("[MainWindow] RequestLock event received. Showing LoginWindow.");
                    var loginWindow = new LoginWindow(((App)Application.Current).VaultFilePath, isInitialSetup: false);
                    Application.Current.MainWindow = loginWindow;
                    loginWindow.Show();
                    this.Close();
                };

                this.Loaded += MainWindow_Loaded;
                Debug.WriteLine("[MainWindow] Constructor finished successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] ERROR during construction: {ex.Message}");
                MessageBox.Show($"فشل تهيئة النافذة الرئيسية: {ex.Message}", "خطأ في التهيئة", MessageBoxButton.OK, MessageBoxImage.Error);
                throw; // Re-throw to let App.xaml.cs handle it
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[MainWindow] Loaded event fired.");
        }


        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Debug.WriteLine($"[MainWindow] Window_Closing event triggered. CancelEventArgs.Cancel = {e.Cancel}");
            if (e.Cancel) { Debug.WriteLine("[MainWindow] Closing already cancelled."); return; }

            try
            {
                 if (!_viewModel.CanExitApplication())
                 {
                     Debug.WriteLine("[MainWindow] Exit cancelled by ViewModel. Setting e.Cancel = true.");
                     e.Cancel = true;
                 }
                 else
                 {
                      Debug.WriteLine("[MainWindow] Exit allowed by ViewModel.");
                      
                      // Check if we already performed the cleanup to avoid duplicate execution
                      if (!_viewModel.HasPerformedExitCleanup)
                      {
                          // Cancel the initial close event and perform async cleanup
                          e.Cancel = true;
                          Debug.WriteLine("[MainWindow] Cancelling close to perform async browser backup...");
                          
                          // Perform the async cleanup, then close the window programmatically
                          _ = PerformExitCleanupAndClose();
                      }
                      else
                      {
                          Debug.WriteLine("[MainWindow] Exit cleanup already performed. Allowing window to close.");
                          // Allow the window to close since cleanup is done
                      }
                 }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Error during Window_Closing: {ex}");
                MessageBox.Show($"خطأ أثناء إغلاق النافذة: {ex.Message}", "خطأ في الإغلاق", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
             Debug.WriteLine($"[MainWindow] Window_Closing event finished. CancelEventArgs.Cancel = {e.Cancel}");
        }

        private bool _isClosing = false; // Prevent re-entrant Close() calls
        
        private async Task PerformExitCleanupAndClose()
        {
            if (_isClosing) return; // Already in progress
            _isClosing = true;
            
            try
            {
                Debug.WriteLine("[MainWindow] Performing exit cleanup...");
                var (success, lockedFiles) = await _viewModel.BackupAndCleanupBrowserFilesOnExitAsync();
                
                if (!success && lockedFiles.Any())
                {
                    Debug.WriteLine($"[MainWindow] Cannot delete browser files - {lockedFiles.Count} files are locked.");
                    
                    // Show dialog to user about locked files
                    var lockedFileNames = string.Join("\n", lockedFiles.Select(f => "• " + Path.GetFileName(f)));
                    var result = MessageBox.Show(
                        $"Cannot close SAFP - The following browser files are currently in use:\n\n{lockedFileNames}\n\n" +
                        "Please close all web browsers and try again.\n\n" +
                        "Do you want to retry closing the application?",
                        "Browser Files Locked",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Reset the flags so user can retry
                        _viewModel.HasPerformedExitCleanup = false;
                        _isClosing = false;
                        // Retry the close operation
                        Application.Current.Dispatcher.Invoke(() => this.Close());
                    }
                    else
                    {
                        // User chose not to retry, keep the window open
                        _isClosing = false;
                    }
                    return;
                }
                
                Debug.WriteLine("[MainWindow] Exit cleanup completed successfully. Closing window programmatically.");
                
                // Close the window programmatically after cleanup completes
                Application.Current.Dispatcher.Invoke(() => this.Close());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Error during async exit cleanup: {ex}");
                MessageBox.Show($"حدث خطأ أثناء إنهاء التطبيق: {ex.Message}", "خطأ في الإنهاء", MessageBoxButton.OK, MessageBoxImage.Warning);
                
                // Reset the flags so user can retry
                _viewModel.HasPerformedExitCleanup = false;
                _isClosing = false;
            }
        }
    }

    // --- ViewModel for MainWindow ---
    public class MainViewModel : ViewModelBase
    {
        private readonly PasswordManagerLogic _vaultLogic;
        private readonly BrowserFileManager _browserManager;
        private string _masterPassword;
        private Dictionary<string, PasswordEntry> _passwordData;
        
        // Flag to track if exit cleanup has been performed
        public bool HasPerformedExitCleanup { get; private set; } = false;

        // *** WICHTIG: Initialisiere die Collection hier ***
        private ObservableCollection<PasswordEntry> _passwordEntries = new ObservableCollection<PasswordEntry>();
        private PasswordEntry? _selectedEntry;
        private string _statusMessage = "تم فتح الخزنة.";
        private string _vaultStatus = "مفتوحة";
        private bool _isBusy = false;
        private DispatcherTimer _clipboardTimer;
        private string _clipboardTimerMessage = "";
        private int _remainingSeconds = 0;

        public event EventHandler? RequestLock;

        // Commands
        public ICommand AddEntryCommand { get; }
        // *** Verwende wieder RelayCommand<PasswordEntry> für Commands mit Parameter ***
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

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string safpDataPath = Path.Combine(appDataPath, "SAFP");
            string vaultFilePath = Path.Combine(safpDataPath, "vault.safp");
            Debug.WriteLine($"[MainViewModel] Vault Logic Path: {vaultFilePath}");

            _vaultLogic = new PasswordManagerLogic(vaultFilePath);
            _browserManager = new BrowserFileManager();
            Debug.WriteLine("[MainViewModel] Logic instances created.");

            // Befülle die bereits initialisierte Collection
            RefreshEntriesCollection(false);
            Debug.WriteLine($"[MainViewModel] PasswordEntries collection initialized with {_passwordEntries.Count} items.");

            // *** Korrektur: Verwende RelayCommand<PasswordEntry> ***
            AddEntryCommand = new RelayCommand(AddEntry, CanExecuteSimpleCommand);
            EditEntryCommand = new RelayCommand<PasswordEntry>(EditEntry, CanExecuteOnSelectedEntry); // Generic
            DeleteEntryCommand = new RelayCommand<PasswordEntry>(async (entry) => await DeleteEntryAsync(entry), CanExecuteOnSelectedEntry); // Generic
            CopyUsernameCommand = new RelayCommand<PasswordEntry>(CopyUsername, CanExecuteOnSelectedEntry); // Generic
            CopyPasswordCommand = new RelayCommand<PasswordEntry>(CopyPassword, CanExecuteOnSelectedEntry); // Generic
            LockVaultCommand = new RelayCommand(LockVault, CanExecuteSimpleCommand);
            BackupBrowserFilesCommand = new RelayCommand(async (_) => await BackupBrowserFilesAsync(), CanExecuteSimpleCommand);
            RestoreBrowserFilesCommand = new RelayCommand(async (_) => await RestoreBrowserFilesAsync(), CanExecuteSimpleCommand);

            // Initialize clipboard timer
            _clipboardTimer = new DispatcherTimer();
            _clipboardTimer.Interval = TimeSpan.FromSeconds(1);
            _clipboardTimer.Tick += ClipboardTimer_Tick;

            Debug.WriteLine("[MainViewModel] Commands initialized.");
            Debug.WriteLine("[MainViewModel] Constructor finished.");
        }

        // --- Properties for Data Binding ---

        public ObservableCollection<PasswordEntry> PasswordEntries
        {
            get => _passwordEntries;
            // Setter bleibt private, Aktualisierung über RefreshEntriesCollection
            private set => SetProperty(ref _passwordEntries, value);
        }

        public PasswordEntry? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    Debug.WriteLine($"[MainViewModel] SelectedEntry changed to: {value?.Service ?? "null"}");
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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string ClipboardTimerMessage
        {
            get => _clipboardTimerMessage;
            set => SetProperty(ref _clipboardTimerMessage, value);
        }


        // --- Command Predicates (CanExecute logic) ---

        private bool CanExecuteSimpleCommand(object? parameter = null) => !IsBusy;

        // *** Korrektur: Predicate erwartet jetzt PasswordEntry? ***
        private bool CanExecuteOnSelectedEntry(PasswordEntry? entry)
        {
            bool canExecute = entry != null && !IsBusy;
            // Optional: Logging wieder aktivieren, falls nötig
            // string entryService = entry?.Service ?? "null";
            // Debug.WriteLine($"[MainViewModel] CanExecuteOnSelectedEntry called for '{entryService}'. Result: {canExecute}");
            return canExecute;
        }


        // --- Command Implementations ---

        private void AddEntry(object? parameter = null)
        {
            Debug.WriteLine("[MainViewModel] AddEntry command executed.");
            if (IsBusy) return;

            var entryForDialog = new PasswordEntry();
            var dialogViewModel = new EntryDialogViewModel(_vaultLogic, _masterPassword, entryForDialog, isNewEntry: true);
            var dialog = new EntryDialog(dialogViewModel);

            bool? dialogResult = null;
            try { dialogResult = dialog.ShowDialog(); }
            catch(Exception ex) { Debug.WriteLine($"[MainViewModel] Exception showing Add dialog: {ex}"); StatusMessage = $"خطأ في فتح نافذة الإضافة: {ex.Message}"; return; }

            if (dialogResult == true)
            {
                 string? newId = dialogViewModel.EntryId; PasswordEntry savedEntry = dialogViewModel.Entry;
                 if (!string.IsNullOrEmpty(newId) && savedEntry != null)
                 { Debug.WriteLine($"[MainViewModel] Add dialog succeeded. New ID: {newId}, Service: {savedEntry.Service}"); _passwordData[newId] = savedEntry; RefreshEntriesCollection(); SelectedEntry = PasswordEntries.FirstOrDefault(e => e.Id == newId); StatusMessage = $"تمت إضافة الإدخال '{savedEntry.Service}'."; }
                 else { StatusMessage = "عملية الإضافة أفادت بالنجاح، لكن المعرف أو بيانات الإدخال كانت مفقودة."; Debug.WriteLine("[MainViewModel] Add dialog success but ID/Entry missing from dialog VM."); RefreshEntriesCollection(); }
            } else { StatusMessage = "تم إلغاء عملية الإضافة."; Debug.WriteLine("[MainViewModel] Add dialog cancelled."); }
        }

        // *** Methode erwartet jetzt PasswordEntry? ***
        private void EditEntry(PasswordEntry? entry)
        {
            Debug.WriteLine($"[MainViewModel] EditEntry command executed for: {entry?.Service ?? "null"}");
            if (!CanExecuteOnSelectedEntry(entry)) { Debug.WriteLine($"[MainViewModel] EditEntry: Cannot execute."); return; }
            PasswordEntry entryNonNull = entry!;

            var entryCopy = new PasswordEntry { Id = entryNonNull.Id, Category = entryNonNull.Category, Service = entryNonNull.Service, Username = entryNonNull.Username, Password = entryNonNull.Password, Notes = entryNonNull.Notes };
            var dialogViewModel = new EntryDialogViewModel(_vaultLogic, _masterPassword, entryCopy, isNewEntry: false);
            var dialog = new EntryDialog(dialogViewModel);
            Debug.WriteLine($"[MainViewModel] Showing Edit dialog for ID: {entryNonNull.Id}");

            bool? dialogResult = null;
            try
            {
                 Debug.WriteLine("[MainViewModel] Attempting to show Edit dialog...");
                 dialogResult = dialog.ShowDialog(); // Ohne Dispatcher
                 Debug.WriteLine($"[MainViewModel] Edit dialog closed with result: {dialogResult}");
            }
            catch(Exception ex) { Debug.WriteLine($"[MainViewModel] Exception showing Edit dialog: {ex}"); StatusMessage = $"خطأ في فتح نافذة التعديل: {ex.Message}"; return; }

            if (dialogResult == true)
            {
                 if (entryNonNull.Id != null && _passwordData.ContainsKey(entryNonNull.Id))
                 { Debug.WriteLine($"[MainViewModel] Edit dialog succeeded for ID: {entryNonNull.Id}, Service: {entryCopy.Service}"); _passwordData[entryNonNull.Id] = entryCopy; RefreshEntriesCollection(); SelectedEntry = PasswordEntries.FirstOrDefault(e => e.Id == entryNonNull.Id); StatusMessage = $"تم تحديث الإدخال '{entryCopy.Service}'."; }
                 else { StatusMessage = "فشل التعديل: لم يتم العثور على الإدخال الأصلي بعد إغلاق النافذة."; Debug.WriteLine($"[MainViewModel] Edit dialog success but original ID {entryNonNull.Id} not found in dictionary."); RefreshEntriesCollection(); }
            } else { StatusMessage = "تم إلغاء عملية التعديل."; Debug.WriteLine("[MainViewModel] Edit dialog cancelled."); }
        }

        // *** Methode erwartet jetzt PasswordEntry? ***
        private async Task DeleteEntryAsync(PasswordEntry? entry)
        {
             Debug.WriteLine($"[MainViewModel] DeleteEntryAsync command executed for: {entry?.Service ?? "null"}");
             if (!CanExecuteOnSelectedEntry(entry)) { Debug.WriteLine($"[MainViewModel] DeleteEntryAsync: Cannot execute."); return; }
             PasswordEntry entryNonNull = entry!;

             var result = MessageBox.Show($"هل أنت متأكد من رغبتك في حذف الإدخال '{entryNonNull.Service}'؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
             if (result == MessageBoxResult.Yes)
             {
                 IsBusy = true; StatusMessage = $"جارٍ حذف '{entryNonNull.Service}'..."; bool removedFromDict = false;
                 try
                 {
                     if (_passwordData.Remove(entryNonNull.Id!))
                     { removedFromDict = true; Debug.WriteLine($"[MainViewModel] Removed entry {entryNonNull.Id} from dictionary."); await _vaultLogic.SaveDataAsync(_passwordData, _masterPassword); Debug.WriteLine($"[MainViewModel] Saved data after deletion."); RefreshEntriesCollection(); StatusMessage = $"تم حذف الإدخال '{entryNonNull.Service}'."; }
                     else
                     { StatusMessage = "فشل الحذف: لم يتم العثور على الإدخال في البيانات."; Debug.WriteLine($"[MainViewModel] Delete failed, entry {entryNonNull.Id} not found in dictionary."); RefreshEntriesCollection(); }
                 }
                 catch (Exception ex)
                 { StatusMessage = $"خطأ في حذف الإدخال: {ex.Message}"; Debug.WriteLine($"[MainViewModel] Exception during Delete/Save: {ex}"); MessageBox.Show($"فشل حذف الإدخال: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); if(removedFromDict && entryNonNull.Id != null) { _passwordData[entryNonNull.Id] = entryNonNull; } RefreshEntriesCollection(); }
                 finally { IsBusy = false; }
             } else { Debug.WriteLine("[MainViewModel] Delete cancelled by user."); }
        }

        // *** Methode erwartet jetzt PasswordEntry? ***
        private void CopyUsername(PasswordEntry? entry)
        {
            Debug.WriteLine($"[MainViewModel] CopyUsername command executed for: {entry?.Service ?? "null"}");
             if (!CanExecuteOnSelectedEntry(entry)) { Debug.WriteLine($"[MainViewModel] CopyUsername: Cannot execute."); return; }
             PasswordEntry entryNonNull = entry!;

            string? usernameToCopy = entryNonNull.Username;
            Debug.WriteLine($"[MainViewModel] Attempting to copy username: '{usernameToCopy ?? "<null>"}' for Service '{entryNonNull.Service}'");

            if (string.IsNullOrEmpty(usernameToCopy))
            { StatusMessage = $"الإدخال '{entryNonNull.Service}' ليس لديه اسم مستخدم لنسخه."; Debug.WriteLine("[MainViewModel] CopyUsername: Username is null or empty."); return; }
            try
            {
                Debug.WriteLine($"[MainViewModel] Calling Clipboard.SetText for username...");
                Clipboard.SetText(usernameToCopy);
                Debug.WriteLine("[MainViewModel] Clipboard.SetText call finished for username.");
                // Optional: Verification (kann fehlschlagen)
                // string? clipboardText = null;
                // try { clipboardText = Clipboard.GetText(); } catch {}
                // if (clipboardText == usernameToCopy) { StatusMessage = $"Username for '{entryNonNull.Service}' copied."; }
                // else { StatusMessage = $"Username copy verification failed for '{entryNonNull.Service}'."; }
                StatusMessage = $"تم نسخ اسم المستخدم لـ '{entryNonNull.Service}'."; // Assume success if no exception
            }
            catch (Exception ex)
            { StatusMessage = $"خطأ في نسخ اسم المستخدم: {ex.Message}"; Debug.WriteLine($"[MainViewModel] Exception during CopyUsername: {ex}"); MessageBox.Show($"تعذر نسخ اسم المستخدم إلى الحافظة: {ex.Message}", "خطأ في الحافظة", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // *** Methode erwartet jetzt PasswordEntry? ***
        private void CopyPassword(PasswordEntry? entry)
        {
             Debug.WriteLine($"[MainViewModel] CopyPassword command executed for: {entry?.Service ?? "null"}");
             if (!CanExecuteOnSelectedEntry(entry)) { Debug.WriteLine($"[MainViewModel] CopyPassword: Cannot execute."); return; }
             PasswordEntry entryNonNull = entry!;

             string? passwordToCopy = entryNonNull.Password;
             Debug.WriteLine($"[MainViewModel] Attempting to copy password (length: {passwordToCopy?.Length ?? 0}) for service '{entryNonNull.Service}'");

             if (string.IsNullOrEmpty(passwordToCopy))
             { StatusMessage = $"الإدخال '{entryNonNull.Service}' ليس لديه كلمة مرور مخزنة."; Debug.WriteLine("[MainViewModel] CopyPassword: Password is null or empty."); return; }
             try
             {
                 Debug.WriteLine($"[MainViewModel] Calling Clipboard.SetText for password...");
                 Clipboard.SetText(passwordToCopy);
                 Debug.WriteLine("[MainViewModel] Clipboard.SetText call finished for password.");
                 StatusMessage = $"تم نسخ كلمة المرور لـ '{entryNonNull.Service}'.";
                 StartClipboardTimer();
             }
             catch (Exception ex)
             { StatusMessage = $"خطأ في نسخ كلمة المرور: {ex.Message}"; Debug.WriteLine($"[MainViewModel] Exception during CopyPassword: {ex}"); MessageBox.Show($"تعذر نسخ كلمة المرور إلى الحافظة: {ex.Message}", "خطأ في الحافظة", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void StartClipboardTimer()
        {
            _remainingSeconds = 90;
            ClipboardTimerMessage = $"🔒 سيتم مسح كلمة المرور من الحافظة خلال {_remainingSeconds} ثانية";
            _clipboardTimer.Start();
            Debug.WriteLine("[MainViewModel] Clipboard timer started - 90 seconds until auto-clear.");
        }

        private void ClipboardTimer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;

            if (_remainingSeconds <= 0)
            {
                _clipboardTimer.Stop();
                try
                {
                    Clipboard.Clear();
                    ClipboardTimerMessage = "";
                    StatusMessage = "تم مسح الحافظة للأمان.";
                    Debug.WriteLine("[MainViewModel] Clipboard automatically cleared after 90 seconds.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainViewModel] Error clearing clipboard: {ex.Message}");
                }
            }
            else
            {
                ClipboardTimerMessage = $"🔒 سيتم مسح كلمة المرور من الحافظة خلال {_remainingSeconds} ثانية";
            }
        }

        private void StopClipboardTimer()
        {
            _clipboardTimer.Stop();
            ClipboardTimerMessage = "";
        }

        private void LockVault(object? parameter = null)
        {
            Debug.WriteLine("[MainViewModel] LockVault command executed."); if (IsBusy) return;
            StatusMessage = "جارٍ قفل الخزنة..."; VaultStatus = "مقفلة"; _masterPassword = string.Empty; _passwordData.Clear(); PasswordEntries.Clear(); SelectedEntry = null;
            StopClipboardTimer();
            try { Clipboard.Clear(); Debug.WriteLine("[MainViewModel] Clipboard cleared on lock."); } catch (Exception ex) { Debug.WriteLine($"[MainViewModel] Error clearing clipboard on lock: {ex.Message}"); }
            Debug.WriteLine("[MainViewModel] Firing RequestLock event."); RequestLock?.Invoke(this, EventArgs.Empty);
        }

        private async Task BackupBrowserFilesAsync()
        {
            Debug.WriteLine("[MainViewModel] BackupBrowserFilesAsync command executed."); if (IsBusy || string.IsNullOrEmpty(_masterPassword)) return;
            var promptResult = MessageBox.Show("مهم: يرجى التأكد من إغلاق جميع متصفحات الويب (...) بشكل كامل قبل المتابعة.\n\n...المتابعة مع النسخ الاحتياطي لملفات المتصفح؟", "أغلق المتصفحات قبل النسخ الاحتياطي", MessageBoxButton.YesNo, MessageBoxImage.Warning); if (promptResult != MessageBoxResult.Yes) { StatusMessage = "تم إلغاء النسخ الاحتياطي لملفات المتصفح من قبل المستخدم."; return; } IsBusy = true; StatusMessage = "جارٍ النسخ الاحتياطي لملفات المتصفح..."; try { var (success, messages) = await _browserManager.BackupBrowserFilesAsync(_masterPassword); string resultMessage = string.Join("\n", messages); StatusMessage = success ? $"انتهى النسخ الاحتياطي للمتصفح. {messages.FirstOrDefault()}" : "انتهى النسخ الاحتياطي للمتصفح مع تحذيرات/أخطاء."; MessageBox.Show(resultMessage, success ? "نتيجة النسخ الاحتياطي" : "نتيجة النسخ الاحتياطي (مع مشاكل)", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning); } catch (Exception ex) { StatusMessage = $"فشل النسخ الاحتياطي للمتصفح: {ex.Message}"; MessageBox.Show($"حدث خطأ غير متوقع أثناء النسخ الاحتياطي للمتصفح: {ex.Message}", "خطأ في النسخ الاحتياطي", MessageBoxButton.OK, MessageBoxImage.Error); } finally { IsBusy = false; }
        }

        private async Task RestoreBrowserFilesAsync()
        {
            Debug.WriteLine("[MainViewModel] RestoreBrowserFilesAsync command executed."); if (IsBusy || string.IsNullOrEmpty(_masterPassword)) return;
             var promptResult = MessageBox.Show("مهم: يرجى التأكد من إغلاق جميع متصفحات الويب (...) بشكل كامل قبل المتابعة.\n\n...المتابعة مع استعادة ملفات المتصفح؟", "أغلق المتصفحات قبل الاستعادة", MessageBoxButton.YesNo, MessageBoxImage.Warning); if (promptResult != MessageBoxResult.Yes) { StatusMessage = "تم إلغاء استعادة ملفات المتصفح من قبل المستخدم."; return; } IsBusy = true; StatusMessage = "جارٍ استعادة ملفات المتصفح من النسخة الاحتياطية..."; try { var (success, messages) = await _browserManager.RestoreBrowserFilesAsync(_masterPassword); string resultMessage = string.Join("\n", messages); StatusMessage = success ? $"انتهت استعادة المتصفح. {messages.FirstOrDefault()}" : "انتهت استعادة المتصفح مع تحذيرات/أخطاء."; MessageBox.Show(resultMessage, success ? "نتيجة الاستعادة" : "نتيجة الاستعادة (مع مشاكل)", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning); } catch (Exception ex) { StatusMessage = $"فشلت استعادة المتصفح: {ex.Message}"; MessageBox.Show($"حدث خطأ غير متوقع أثناء استعادة المتصفح: {ex.Message}", "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error); } finally { IsBusy = false; }
        }

        public bool CanExitApplication()
        {
             Debug.WriteLine("[MainViewModel] CanExitApplication called.");
             var result = MessageBox.Show("قفل الخزنة والخروج من SAFP؟", "تأكيد الخروج", MessageBoxButton.YesNo, MessageBoxImage.Question);
             if (result == MessageBoxResult.Yes)
             {
                 Debug.WriteLine("[MainViewModel] User confirmed exit.");
                 StopClipboardTimer();
                 // Note: Don't clear sensitive data here - it's all deferred until after backup
                 // in BackupAndCleanupBrowserFilesOnExitAsync to ensure the master password
                 // and data are available for the backup operation
                 try { Clipboard.Clear(); } catch { /* Ignore */ }
                 return true;
             }
             Debug.WriteLine("[MainViewModel] User cancelled exit.");
             return false;
        }

        /// <summary>
        /// Backs up browser files silently before exit, then securely deletes them.
        /// This is called automatically on application exit without user interaction.
        /// Returns true if cleanup succeeded, false if files are locked and user should be prompted.
        /// </summary>
        public async Task<(bool Success, List<string> LockedFiles)> BackupAndCleanupBrowserFilesOnExitAsync()
        {
            // Mark that we're performing exit cleanup
            HasPerformedExitCleanup = true;
            
            // Use the ViewModel's master password which should still be available at this point
            string? masterPassword = _masterPassword;
            var lockedFiles = new List<string>();
            
            if (_browserManager != null && !string.IsNullOrEmpty(masterPassword))
            {
                try
                {
                    // First, silently backup browser files to ensure they're up-to-date
                    Debug.WriteLine("[MainViewModel] Backing up browser files before exit...");
                    var (backupSuccess, backupMessages) = await _browserManager.BackupBrowserFilesAsync(masterPassword);
                    Debug.WriteLine($"[MainViewModel] Browser file backup completed. Success: {backupSuccess}");
                    
                    // Only proceed with deletion if backup was successful
                    if (backupSuccess)
                    {
                        Debug.WriteLine("[MainViewModel] Browser backup successful. Proceeding with secure deletion.");
                        
                        // Then securely delete browser files for security - require immediate deletion
                        Debug.WriteLine("[MainViewModel] Securely deleting browser files on exit...");
                        var (deleteSuccess, deleteMessages, deletionLockedFiles) = await _browserManager.SecureDeleteAllBrowserFilesAsync(requireImmediateDeletion: true);
                        Debug.WriteLine($"[MainViewModel] Browser file deletion completed. Success: {deleteSuccess}");
                        
                        if (!deleteSuccess)
                        {
                            lockedFiles = deletionLockedFiles;
                            Debug.WriteLine($"[MainViewModel] Some files are locked: {string.Join(", ", lockedFiles.Select(Path.GetFileName))}");
                            // Don't clear sensitive data yet - we'll need it if user retries
                            return (false, lockedFiles);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[MainViewModel] Browser backup failed or incomplete. Skipping deletion to preserve original files: " + string.Join("; ", backupMessages));
                        // Don't delete files if backup failed - preserve the originals
                    }
                }
                catch (FileLockedIOException ex)
                {
                    Debug.WriteLine($"[MainViewModel] Browser files are locked: {ex.Message}");
                    lockedFiles = ex.LockedFiles;
                    // Don't clear sensitive data yet - we'll need it if user retries
                    return (false, lockedFiles);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainViewModel] Error during browser file backup/cleanup: {ex.Message}");
                    // Don't show error to user - this is a silent operation
                    // Don't delete files if an exception occurred during backup
                }
            }
            else
            {
                Debug.WriteLine("[MainViewModel] Skipping browser backup/cleanup - manager or password not available");
            }
            
            // Now clear sensitive data after backup attempt is complete
            Debug.WriteLine("[MainViewModel] Clearing sensitive data after browser backup.");
            _masterPassword = string.Empty;
            _passwordData.Clear();
            PasswordEntries.Clear();
            
            // Also clear the App's master password to prevent duplicate backup in App.OnExit
            ((App)Application.Current).MasterPassword = null;
            
            return (true, lockedFiles);
        }

        // Helper to refresh the ObservableCollection from the source dictionary and re-sort
        private void RefreshEntriesCollection(bool log = true)
        {
             if(log) Debug.WriteLine("[MainViewModel] RefreshEntriesCollection called.");
             if (_passwordEntries == null) { _passwordEntries = new ObservableCollection<PasswordEntry>(); }

             var sortedEntries = _passwordData.Select(kvp => { kvp.Value.Id = kvp.Key; return kvp.Value; })
                                            .OrderBy(p => p.Service, StringComparer.OrdinalIgnoreCase).ToList();

             // *** VERSUCH 7: Modify existing collection on UI Thread ***
             Application.Current.Dispatcher.Invoke(() => {
                 try
                 {
                     // Compare counts and potentially items for efficiency later,
                     // but for now, Clear/Add is the most reliable way to trigger update
                     _passwordEntries.Clear();
                     foreach(var entry in sortedEntries)
                     {
                         _passwordEntries.Add(entry);
                     }
                     if(log) Debug.WriteLine($"[MainViewModel] _passwordEntries modified on UI thread. New Count: {_passwordEntries.Count}");
                 }
                 catch (Exception ex)
                 {
                      Debug.WriteLine($"[MainViewModel] Exception during _passwordEntries.Clear/Add: {ex}");
                      // Fallback: Recreate collection if modification fails (less ideal)
                      // PasswordEntries = new ObservableCollection<PasswordEntry>(sortedEntries);
                 }
             });

             SelectedEntry = null;
             if(log) Debug.WriteLine($"[MainViewModel] Entries collection refresh finished. Count: {PasswordEntries.Count}");
        }
    }
}