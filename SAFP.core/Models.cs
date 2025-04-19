using System;
using System.Collections.Generic;
using System.ComponentModel; // Required for INotifyPropertyChanged
using System.Runtime.CompilerServices; // Required for CallerMemberName

// Namespace updated to SAFP.Core
namespace SAFP.Core
{
    /// <summary>
    /// Base class for models that need to notify the UI of property changes.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }


    /// <summary>
    /// Represents a single password entry in the vault.
    /// Inherits from ObservableObject for potential direct data binding scenarios.
    /// </summary>
    public class PasswordEntry : ObservableObject
    {
        private string _category = "Other";
        private string _service = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _notes = string.Empty;

        // Properties notify UI on change
        public string Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }
        public string Service
        {
            get => _service;
            set => SetProperty(ref _service, value);
        }
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }
        public string Password // The actual password; UI should handle masking
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        // ID is not part of the serialized data, it's the key in the main dictionary.
        // We might add it here temporarily if needed for UI identification.
        [System.Text.Json.Serialization.JsonIgnore] // Ensure ID isn't saved in the JSON value
        public string? Id { get; set; } = null; // Used transiently in the ViewModel list
    }

    /// <summary>
    /// Represents the structure of the data stored in the browser backup file.
    /// </summary>
    public class BrowserBackupData
    {
        /// <summary>
        /// Dictionary mapping a unique key (original full path) to the Base64 encoded file content.
        /// </summary>
        public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Dictionary mapping the unique key (original full path) back to the absolute original file path.
        /// (Slightly redundant if key IS the path, but good for structure clarity).
        /// </summary>
        public Dictionary<string, string> OriginalPaths { get; set; } = new Dictionary<string, string>();
    }
}
