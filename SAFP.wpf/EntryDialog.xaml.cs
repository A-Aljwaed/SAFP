using SAFP.Core; // Access core logic and models
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls; // For PasswordBox
using System.Windows.Input;

namespace SAFP.Wpf
{
    /// <summary>
    /// Interaction logic for EntryDialog.xaml
    /// </summary>
    public partial class EntryDialog : Window
    {
        // Expose ViewModel for potential access if needed (e.g., getting saved ID)
        public EntryDialogViewModel ViewModel => (EntryDialogViewModel)DataContext;

        public EntryDialog(EntryDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Handle save/cancel requests from ViewModel to close the dialog
            viewModel.RequestClose += (sender, success) =>
            {
                DialogResult = success; // Set DialogResult based on success
                Close();
            };

            // Set initial password box content if editing
            // *** CORRECTED LINE: Access the new public property ***
            if (!viewModel.IsNewEntry && !string.IsNullOrEmpty(viewModel.Entry.Password))
            {
                 PasswordEntryBox.Password = viewModel.Entry.Password;
            }
        }

        // --- PasswordBox Handling ---
        // Update ViewModel when PasswordBox content changes
        private void PasswordEntryBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is EntryDialogViewModel vm)
            {
                // Store the plain password in the ViewModel (use SecureString if higher security needed)
                vm.CurrentPassword = ((PasswordBox)sender).Password;
            }
        }

        // Toggle password visibility (Simple MessageBox reveal)
        private void ShowHideButton_Click(object sender, RoutedEventArgs e)
        {
             var vm = DataContext as EntryDialogViewModel;
             if (vm == null) return;

             // This is a UI concern, handled here for simplicity.
             // Ideally, use an attached property or behavior for better MVVM.
             // Consider security implications of showing password in MessageBox.
             MessageBox.Show($"كلمة المرور: {vm.CurrentPassword}", "إظهار كلمة المرور", MessageBoxButton.OK);
             // We don't actually change the PasswordBox display here.
        }
    }


    // --- ViewModel for EntryDialog ---
    public class EntryDialogViewModel : ViewModelBase
    {
        private readonly PasswordManagerLogic _vaultLogic;
        private readonly string _masterPassword;
        private readonly PasswordEntry _entry; // The entry being edited or added
        private readonly bool _isNewEntry; // Keep private field
        private string _currentPassword = string.Empty; // Temporary store for PasswordBox content
        private string _statusMessage = string.Empty;
        private bool _isBusy = false;
        private Visibility _copyButtonVisibility = Visibility.Collapsed;

        // Event to signal View to close
        public event EventHandler<bool>? RequestClose; // Passes true on save, false on cancel

        // Commands
        public ICommand GeneratePasswordCommand { get; }
        public ICommand CopyGeneratedPasswordCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        // Data for UI Binding
        public PasswordEntry Entry => _entry;
        public string WindowTitle => _isNewEntry ? "إضافة إدخال جديد" : "تعديل الإدخال";
        public List<string> Categories { get; } = new List<string> { "موقع ويب", "تطبيق", "بريد إلكتروني", "شبكة", "مفتاح SSH", "قاعدة بيانات", "أخرى" };
        public List<int> PasswordLengths { get; } = Enumerable.Range(8, 128 - 8 + 1).ToList(); // 8 to 128

        // *** ADDED PUBLIC PROPERTY ***
        /// <summary>
        /// Gets a value indicating whether this dialog is for adding a new entry.
        /// </summary>
        public bool IsNewEntry => _isNewEntry;

        private int _selectedPasswordLength = 18; // Default length
        public int SelectedPasswordLength
        {
            get => _selectedPasswordLength;
            set => SetProperty(ref _selectedPasswordLength, value);
        }

        // Holds the password from the PasswordBox via code-behind event handler
        public string CurrentPassword
        {
            get => _currentPassword;
            // Use SetProperty if binding needed elsewhere, otherwise just set field
            set => SetProperty(ref _currentPassword, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

         public bool IsBusy
        {
            get => _isBusy;
            set {
                if(SetProperty(ref _isBusy, value)) {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

         public Visibility CopyButtonVisibility
        {
            get => _copyButtonVisibility;
            set => SetProperty(ref _copyButtonVisibility, value);
        }

         // Property to hold the ID (especially after adding a new entry)
         public string? EntryId { get; private set; }


        public EntryDialogViewModel(PasswordManagerLogic vaultLogic, string masterPassword, PasswordEntry entry, bool isNewEntry)
        {
            _vaultLogic = vaultLogic;
            _masterPassword = masterPassword;
            _entry = entry; // This is the reference to the copy passed from MainViewModel
            _isNewEntry = isNewEntry; // Store the flag
            _currentPassword = entry.Password ?? ""; // Initialize with current password if editing

            EntryId = entry.Id; // Store initial ID (null if new)

            GeneratePasswordCommand = new RelayCommand(GeneratePassword, (_) => !IsBusy);
            CopyGeneratedPasswordCommand = new RelayCommand(CopyGeneratedPassword, (_) => !string.IsNullOrEmpty(CurrentPassword) && !IsBusy);
            SaveCommand = new RelayCommand(async (_) => await SaveAsync(), CanSave);
            CancelCommand = new RelayCommand((_) => Cancel(), (_) => !IsBusy);
        }

        private bool CanSave(object? parameter)
        {
            // Basic validation
            return !IsBusy &&
                   !string.IsNullOrWhiteSpace(Entry.Service) &&
                   !string.IsNullOrWhiteSpace(Entry.Username);
                   // Password check is optional based on previous logic
        }

        private void GeneratePassword(object? parameter)
        {
            if (IsBusy) return;
            try
            {
                StatusMessage = "";
                // Generate and immediately update CurrentPassword which the UI reads via code-behind
                CurrentPassword = _vaultLogic.GeneratePassword(SelectedPasswordLength);
                // Update the Entry model directly as well
                Entry.Password = CurrentPassword;

                CopyButtonVisibility = Visibility.Visible; // Show copy button
                StatusMessage = "تم توليد كلمة المرور. قد تحتاج إلى النقر على عرض/إخفاء أو نسخ لرؤيتها.";

                // Trigger PropertyChanged for CurrentPassword to ensure any potential bindings update
                OnPropertyChanged(nameof(CurrentPassword));

                // We still rely on the code-behind to potentially update the PasswordBox view
                // if we implemented a more complex show/hide mechanism.
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في توليد كلمة المرور: {ex.Message}";
            }
        }

         private void CopyGeneratedPassword(object? parameter)
        {
            if (string.IsNullOrEmpty(CurrentPassword) || IsBusy) return;
             try
             {
                 Clipboard.SetText(CurrentPassword);
                 StatusMessage = "تم نسخ كلمة المرور المُولَّدة إلى الحافظة.";
             }
             catch (Exception ex)
             {
                  StatusMessage = $"خطأ في نسخ كلمة المرور: {ex.Message}";
             }
        }

        private async Task SaveAsync()
        {
            if (!CanSave(null)) {
                StatusMessage = "يرجى ملء الخدمة واسم المستخدم.";
                return;
            }

            IsBusy = true;
            StatusMessage = "جارٍ الحفظ...";

            // Ensure the Entry object has the latest password from the UI (via CurrentPassword property)
            Entry.Password = CurrentPassword;

            try
            {
                // Load current full data
                var allData = await _vaultLogic.LoadDataAsync<Dictionary<string, PasswordEntry>>(_masterPassword);
                if (allData == null) {
                    allData = new Dictionary<string, PasswordEntry>();
                }

                if (_isNewEntry) // Use the private field here
                {
                    EntryId = _vaultLogic.GenerateUuid(); // Generate new ID
                    Entry.Id = EntryId; // Assign ID (transiently)
                    allData[EntryId] = Entry; // Add to dictionary
                }
                else
                {
                    if (string.IsNullOrEmpty(Entry.Id) || !allData.ContainsKey(Entry.Id))
                    {
                        throw new InvalidOperationException("لا يمكن حفظ الإدخال: المعرف الأصلي مفقود أو غير صالح.");
                    }
                    allData[Entry.Id] = Entry; // Update existing entry
                }

                // Save the entire updated dictionary
                await _vaultLogic.SaveDataAsync(allData, _masterPassword);
                StatusMessage = "تم حفظ الإدخال بنجاح.";
                RequestClose?.Invoke(this, true); // Close dialog, signal success
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في حفظ الإدخال: {ex.Message}";
                MessageBox.Show($"فشل حفظ الإدخال: {ex.Message}", "خطأ في الحفظ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Cancel()
        {
            RequestClose?.Invoke(this, false); // Close dialog, signal cancellation
        }
    }
}
