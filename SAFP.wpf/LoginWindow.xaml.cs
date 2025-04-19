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
            // if (isInitialSetup) { Title = "SAFP - Create Master Password"; }
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

        // Trigger UnlockCommand on Enter key press in PasswordBox
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
        public string WindowTitle => _isInitialSetup ? "SAFP - Create Master Password" : "SAFP Login";
        public string PromptText => _isInitialSetup ? "Create Your Master Password" : "Enter Master Password";
        public string ActionButtonText => _isInitialSetup ? "Create Vault" : "Unlock Vault";
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
                StatusMessage = "Create a strong master password.";
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
                StatusMessage = "Password cannot be empty.";
                return;
            }

            IsBusy = true;
            StatusMessage = _isInitialSetup ? "Setting up..." : "Unlocking...";

            // Convert SecureString to plain string for logic (handle with care)
            string password = SecureStringToString(SecurePassword);

            // --- Password Confirmation Logic (for initial setup) ---
            string? confirmPassword = null;
            if (_isInitialSetup)
            {
                // 1. Check strength first
                try
                {
                    var strength = _logic.CheckPasswordStrength(password);
                    if (strength.Score < 3)
                    {
                        string suggestions = string.Join("\n- ", strength.Feedback?.Suggestions ?? new List<string>());
                        string warning = strength.Feedback?.Warning ?? "";
                        StatusMessage = $"Password Score: {strength.Score}/4 (Too Weak)\n" +
                                        (!string.IsNullOrEmpty(warning) ? $"Warning: {warning}\n" : "") +
                                        (!string.IsNullOrEmpty(suggestions) ? $"Suggestions:\n- {suggestions}" : "");
                        password = string.Empty; // Clear weak password
                        IsBusy = false;
                        return; // Stay on setup window
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Strength check failed: {ex.Message}. Please try a different password.";
                    Console.WriteLine($"Zxcvbn error: {ex}"); // Log full error
                    password = string.Empty;
                    IsBusy = false;
                    return;
                }

                // 2. Ask for confirmation using a simple input dialog
                // NOTE: This uses a basic input dialog. A custom dialog with a PasswordBox would be better.
                var confirmDialog = new InputDialog("Confirm Master Password", "Please re-enter your master password to confirm:", isPassword: true);
                if (confirmDialog.ShowDialog() == true)
                {
                    confirmPassword = confirmDialog.ResponseText;
                }
                else
                {
                    StatusMessage = "Password confirmation cancelled.";
                    password = string.Empty; // Clear original password
                    IsBusy = false;
                    return; // Cancelled confirmation
                }

                if (password != confirmPassword)
                {
                    StatusMessage = "Passwords do not match. Please try again.";
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
                    await _logic.SaveDataAsync(new Dictionary<string, PasswordEntry>(), password);
                    StatusMessage = "Vault created successfully!";
                    LoginSuccess?.Invoke(this, password); // Signal success
                }
                else
                {
                    // Normal Unlock Logic: Try loading data to verify password
                    var data = await _logic.LoadDataAsync<Dictionary<string, PasswordEntry>>(password);
                    // If LoadDataAsync doesn't throw, password is correct
                    StatusMessage = "Unlock successful!";
                    LoginSuccess?.Invoke(this, password); // Signal success
                }
            }
            catch (DecryptionException dex) // Specific error for wrong password/corruption
            {
                 StatusMessage = $"Error: {dex.Message}";
            }
            catch (FileOperationException fex)
            {
                 StatusMessage = $"File Error: {fex.Message}";
            }
            catch (Exception ex) // Catch other unexpected errors
            {
                StatusMessage = $"An unexpected error occurred: {ex.Message}";
                // Log the full exception ex
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

    // --- Simple Input Dialog Helper Class (Add this within the same file or separate) ---
    public partial class InputDialog : Window
    {
        public string ResponseText { get; private set; } = "";
        private bool _isPassword;

        public InputDialog(string title, string question, bool isPassword = false)
        {
            InitializeComponent(); // Needs XAML definition below
            Title = title;
            QuestionLabel.Text = question;
            _isPassword = isPassword;

            if (_isPassword)
            {
                ResponsePasswordBox.Visibility = Visibility.Visible;
                ResponseTextBox.Visibility = Visibility.Collapsed;
                ResponsePasswordBox.Focus();
            }
            else
            {
                ResponsePasswordBox.Visibility = Visibility.Collapsed;
                ResponseTextBox.Visibility = Visibility.Visible;
                ResponseTextBox.Focus();
            }
        }

        // Minimal XAML needed for InputDialog (place in InputDialog.xaml)
        /*
        <Window x:Class="SAFP.Wpf.InputDialog" ... Height="180" Width="350" ... >
            <Grid Margin="15">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                <TextBlock x:Name="QuestionLabel" Grid.Row="0" TextWrapping="Wrap" Margin="0,0,0,10"/>
                <TextBox x:Name="ResponseTextBox" Grid.Row="1" MinWidth="250" KeyDown="Response_KeyDown"/>
                <PasswordBox x:Name="ResponsePasswordBox" Grid.Row="1" MinWidth="250" KeyDown="Response_KeyDown"/>
                <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,15,0,0">
                    <Button Content="OK" IsDefault="True" MinWidth="70" Margin="0,0,10,0" Click="OkButton_Click"/>
                    <Button Content="Cancel" IsCancel="True" MinWidth="70"/>
                </StackPanel>
            </Grid>
        </Window>
        */

        // Code-behind for InputDialog.xaml.cs
        private void InitializeComponent() // Placeholder if XAML isn't fully defined elsewhere
        {
             // This would normally be generated from XAML
            // Define QuestionLabel, ResponseTextBox, ResponsePasswordBox etc. programmatically if needed
             // Or ensure you have a corresponding InputDialog.xaml file
             // For now, assume controls exist for logic below.
             // Example (needs real UI):
             this.QuestionLabel = new TextBlock();
             this.ResponseTextBox = new TextBox();
             this.ResponsePasswordBox = new PasswordBox();
             // ... add buttons etc. ...
             // Set basic window properties
             this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
             this.ResizeMode = ResizeMode.NoResize;
             this.SizeToContent = SizeToContent.WidthAndHeight;
             this.MinWidth = 300;
             this.MinHeight = 150;

             // Simplified layout (replace with proper XAML Grid/StackPanel)
             var stack = new StackPanel { Margin = new Thickness(15) };
             stack.Children.Add(QuestionLabel);
             stack.Children.Add(ResponseTextBox);
             stack.Children.Add(ResponsePasswordBox);
             var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,15,0,0) };
             var okButton = new Button { Content="OK", IsDefault=true, MinWidth=70, Margin=new Thickness(0,0,10,0) };
             okButton.Click += OkButton_Click;
             var cancelButton = new Button { Content="Cancel", IsCancel=true, MinWidth=70 };
             buttonPanel.Children.Add(okButton);
             buttonPanel.Children.Add(cancelButton);
             stack.Children.Add(buttonPanel);
             this.Content = stack;

        }
        // Need to declare the controls used in code-behind if not in XAML
        internal TextBlock QuestionLabel = null!;
        internal TextBox ResponseTextBox = null!;
        internal PasswordBox ResponsePasswordBox = null!;


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
                 OkButton_Click(sender, e);
             }
        }
    }

}
