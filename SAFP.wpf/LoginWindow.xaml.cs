using SAFP.Core; // Access core logic
using System;
using System.Collections.Generic; // For Dictionary
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls; // For PasswordBox
using System.Windows.Input; // For KeyEventArgs
using System.Security; // For SecureString

namespace SAFP.Wpf
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        // Removed _isInitialSetup field, ViewModel handles the state

        // Constructor now only needs vault path, ViewModel determines setup state
        public LoginWindow(string vaultFilePath, bool isInitialSetup)
        {
            InitializeComponent();
            _viewModel = new LoginViewModel(vaultFilePath, isInitialSetup); // Pass setup flag to VM
            DataContext = _viewModel;

            // Handle successful login/setup
            _viewModel.LoginSuccess += (sender, masterPassword) =>
            {
                // Store successful password and close login window
                ((App)Application.Current).MasterPassword = masterPassword;
                DialogResult = true; // Indicates success to App.xaml.cs
                Close();
            };

             // Handle cancellation during initial setup
            _viewModel.SetupCancelled += (sender, args) => {
                DialogResult = false; // Indicate cancellation
                Close();
            };

            // Set focus to password box on load
            Loaded += (sender, e) => PasswordBox.Focus();

            // Title is now bound in XAML via ViewModel property
        }


        // --- PasswordBox Handling ---
        // Update ViewModel property when PasswordBox changes
        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
            {
                vm.SecurePassword = ((PasswordBox)sender).SecurePassword;
            }
        }

        // Trigger SubmitCommand on Enter key press in PasswordBox
        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Use the correct command based on the ViewModel's state
                if (_viewModel.SubmitCommand.CanExecute(null))
                {
                    _viewModel.SubmitCommand.Execute(null);
                }
            }
        }
    }

    // --- ViewModel for LoginWindow ---
    public class LoginViewModel : ViewModelBase
    {
        private readonly PasswordManagerLogic _logic;
        private readonly bool _isInitialSetup;
        private SecureString? _securePassword; // Use SecureString
        private string _statusMessage = string.Empty;
        private bool _isBusy = false;

        // Properties for UI Binding
        public string WindowTitle => _isInitialSetup ? "SAFP - إنشاء كلمة المرور الرئيسية" : "تسجيل الدخول إلى SAFP";
        public string PromptText => _isInitialSetup ? "أنشئ كلمة المرور الرئيسية الخاصة بك" : "أدخل كلمة المرور الرئيسية";
        public string ActionButtonText => _isInitialSetup ? "إنشاء الخزنة" : "فتح الخزنة";
        public Visibility CancelButtonVisibility => _isInitialSetup ? Visibility.Visible : Visibility.Collapsed; // Show Cancel only during setup

        public event EventHandler<string>? LoginSuccess; // Event passes master password string on success
        public event EventHandler? SetupCancelled; // Event for cancellation during initial setup

        public ICommand SubmitCommand { get; } // Renamed from UnlockCommand
        public ICommand CancelSetupCommand { get; }

        public LoginViewModel(string vaultFilePath, bool isInitialSetup)
        {
            _logic = new PasswordManagerLogic(vaultFilePath);
            _isInitialSetup = isInitialSetup;

            // SubmitCommand handles both Unlock and Setup
            SubmitCommand = new RelayCommand(async (_) => await UnlockOrSetupAsync(), CanSubmit);
            CancelSetupCommand = new RelayCommand((_) => CancelSetup(), (_) => _isInitialSetup && !_isBusy); // Only enabled during setup

            if (_isInitialSetup)
            {
                StatusMessage = "أنشئ كلمة مرور رئيسية قوية.";
            }
        }

        // Property to hold the password securely
        public SecureString? SecurePassword
        {
            get => _securePassword;
            set
            {
                 if (SetProperty(ref _securePassword, value))
                 {
                    // Trigger CanExecute re-evaluation when password changes
                    CommandManager.InvalidateRequerySuggested();
                 }
            }
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
                if (SetProperty(ref _isBusy, value)) {
                     // This ensures the command's CanExecute is re-evaluated when busy state changes
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private bool CanSubmit(object? parameter) => !_isBusy && SecurePassword?.Length > 0;

        private async Task UnlockOrSetupAsync()
        {
            if (SecurePassword == null || SecurePassword.Length == 0)
            {
                StatusMessage = "كلمة المرور لا يمكن أن تكون فارغة.";
                return;
            }

            IsBusy = true;
            StatusMessage = _isInitialSetup ? "جارٍ التحقق والإنشاء..." : "جارٍ الفتح..."; // Updated setup message

            // Convert SecureString to plain string for logic (handle with care)
            string password = SecureStringToString(SecurePassword);

            // --- Password Confirmation Logic (for initial setup) ---
            string? confirmPassword = null;
            if (_isInitialSetup)
            {
                // 1. Check strength first
                try
                {
                    // Ensure Zxcvbn library is referenced and working
                    var strength = _logic.CheckPasswordStrength(password);
                    if (strength.Score < 3)
                    {
                        string suggestions = string.Join("\n- ", strength.Feedback?.Suggestions ?? new List<string>());
                        string warning = strength.Feedback?.Warning ?? "";
                        StatusMessage = $"درجة كلمة المرور: {strength.Score}/4 (ضعيفة جدًا)\n" +
                                        (!string.IsNullOrEmpty(warning) ? $"تحذير: {warning}\n" : "") +
                                        (!string.IsNullOrEmpty(suggestions) ? $"اقتراحات:\n- {suggestions}" : "");
                        password = string.Empty; // Clear weak password
                        IsBusy = false;
                        return; // Stay on setup window
                    }
                }
                catch (Exception ex)
                {
                    // Handle case where Zxcvbn might fail (e.g., library not found)
                    StatusMessage = $"فشل التحقق من القوة: {ex.Message}. لا يمكن المتابعة.";
                    Console.WriteLine($"Zxcvbn error: {ex}"); // Log full error
                    password = string.Empty;
                    IsBusy = false;
                    // Optionally allow proceeding without strength check if desired, but risky
                    return;
                }

                // 2. Ask for confirmation using a simple input dialog
                // NOTE: Ensure InputDialog class/XAML is correctly implemented in your project.
                var confirmDialog = new InputDialog("تأكيد كلمة المرور الرئيسية", "الرجاء إعادة إدخال كلمة المرور الرئيسية للتأكيد:", isPassword: true);
                if (confirmDialog.ShowDialog() == true)
                {
                    confirmPassword = confirmDialog.ResponseText;
                }
                else
                {
                    StatusMessage = "تم إلغاء تأكيد كلمة المرور.";
                    password = string.Empty; // Clear original password
                    IsBusy = false;
                    return; // Cancelled confirmation
                }

                if (password != confirmPassword)
                {
                    StatusMessage = "كلمات المرور غير متطابقة. يرجى المحاولة مرة أخرى.";
                    password = string.Empty;
                    confirmPassword = string.Empty; // Clear confirmation
                    IsBusy = false;
                    return; // Mismatch
                }
                 // Passwords match and strength is okay - proceed to save
            }


            // --- Save or Unlock ---
            try
            {
                if (_isInitialSetup)
                {
                    // Save initial empty vault (passwords matched)
                    StatusMessage = "جارٍ إنشاء ملف الخزنة..."; // Give feedback before save
                    await _logic.SaveDataAsync(new Dictionary<string, PasswordEntry>(), password);
                    StatusMessage = "تم إنشاء الخزنة بنجاح!";
                    LoginSuccess?.Invoke(this, password); // Signal success
                }
                else
                {
                    // Normal Unlock Logic: Try loading data to verify password
                    StatusMessage = "جارٍ فك التشفير..."; // Give feedback before load
                    var data = await _logic.LoadDataAsync<Dictionary<string, PasswordEntry>>(password);
                    // If LoadDataAsync doesn't throw, password is correct
                    StatusMessage = "تم الفتح بنجاح!";
                    LoginSuccess?.Invoke(this, password); // Signal success
                }
            }
            catch (DecryptionException dex) // Specific error for wrong password/corruption
            {
                 StatusMessage = $"خطأ: {dex.Message}";
            }
            catch (FileOperationException fex)
            {
                 StatusMessage = $"خطأ في الملف: {fex.Message}";
            }
            catch (Exception ex) // Catch other unexpected errors
            {
                StatusMessage = $"حدث خطأ غير متوقع: {ex.Message}";
                // Log the full exception ex
                Console.WriteLine($"UnlockOrSetup Error: {ex}");
            }
            finally
            {
                IsBusy = false;
                // Clear plain text passwords immediately after use
                password = string.Empty;
                confirmPassword = string.Empty;
            }
        }

        private void CancelSetup() {
            // Only callable during initial setup
            if (_isInitialSetup) {
                SetupCancelled?.Invoke(this, EventArgs.Empty);
            }
        }


        // Helper to convert SecureString to string (use carefully and clear result quickly)
        private string SecureStringToString(SecureString value)
        {
            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(value);
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(valuePtr) ?? string.Empty;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }
    }

    // --- Simple Input Dialog Helper Class (Ensure this is correctly implemented) ---
    public partial class InputDialog : Window
    {
        public string ResponseText { get; private set; } = "";
        private bool _isPassword;

        // Basic controls - ensure these names match your XAML if you created one
        internal TextBlock QuestionLabel = new TextBlock();
        internal TextBox ResponseTextBox = new TextBox();
        internal PasswordBox ResponsePasswordBox = new PasswordBox();

        public InputDialog(string title, string question, bool isPassword = false)
        {
            InitializeComponent(); // This MUST exist if you have XAML
            Title = title;
            QuestionLabel.Text = question;
            _isPassword = isPassword;

            if (_isPassword)
            {
                ResponsePasswordBox.Visibility = Visibility.Visible;
                ResponseTextBox.Visibility = Visibility.Collapsed;
                // Delay focus slightly to ensure window is ready
                Dispatcher.BeginInvoke(new Action(() => ResponsePasswordBox.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                ResponsePasswordBox.Visibility = Visibility.Collapsed;
                ResponseTextBox.Visibility = Visibility.Visible;
                Dispatcher.BeginInvoke(new Action(() => ResponseTextBox.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        // Minimal programmatic setup if InitializeComponent() is missing (i.e., no XAML)
        // Remove this method if you have InputDialog.xaml
        private void InitializeComponent()
        {
            // Check if content is already set (by XAML)
            if (this.Content != null) return;

            // Programmatic setup as fallback
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ResizeMode = ResizeMode.NoResize;
            this.SizeToContent = SizeToContent.WidthAndHeight;
            this.MinWidth = 350; this.MaxWidth = 500;
            this.MinHeight = 150;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            QuestionLabel.TextWrapping = TextWrapping.Wrap;
            QuestionLabel.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(QuestionLabel, 0);

            ResponseTextBox.MinWidth = 250;
            ResponseTextBox.KeyDown += Response_KeyDown;
            Grid.SetRow(ResponseTextBox, 1);

            ResponsePasswordBox.MinWidth = 250;
            ResponsePasswordBox.KeyDown += Response_KeyDown;
            Grid.SetRow(ResponsePasswordBox, 1);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            var okButton = new Button { Content = "موافق", IsDefault = true, MinWidth = 70, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += OkButton_Click;
            var cancelButton = new Button { Content = "إلغاء", IsCancel = true, MinWidth = 70 };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(QuestionLabel);
            grid.Children.Add(ResponseTextBox);
            grid.Children.Add(ResponsePasswordBox);
            grid.Children.Add(buttonPanel);

            this.Content = grid;
        }


        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPassword)
                ResponseText = ResponsePasswordBox.Password;
            else
                ResponseText = ResponseTextBox.Text;
            DialogResult = true;
        }

        private void Response_KeyDown(object sender, KeyEventArgs e)
        {
             if (e.Key == Key.Enter)
             {
                 // Simulate OK button click
                 OkButton_Click(sender, new RoutedEventArgs());
             }
        }
    }

}
