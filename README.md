
# 🔐 Secure Automated File Protection (SAFP)

A robust, secure password manager and browser data protection solution built with modern .NET technologies.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)
![Framework](https://img.shields.io/badge/.NET-9.0-purple.svg)

## 🌟 Key Features

- **Secure Password Management**
  - AES-GCM encryption for maximum security
  - PBKDF2 key derivation with 390,000 iterations
  - Zero-knowledge architecture
  - Secure password generation

- **Browser Data Protection**
  - Automated browser profile backup
  - Encrypted storage of sensitive browser data
  - Secure restore functionality
  - Multi-browser support

- **Modern WPF Interface**
  - Clean, intuitive design
  - Password strength assessment
  - Quick copy functionality
  - Category organization
  - Secure note storage

## 🔒 Security Features

- **Advanced Encryption**
  - AES-256-GCM authenticated encryption
  - 96-bit nonces for perfect forward secrecy
  - 128-bit authentication tags
  - Cryptographically secure random number generation

- **Zero-Knowledge Design**
  - Master password never stored
  - No cloud integration - full local control
  - Memory protection for sensitive data
  - Automatic vault locking

## 🚀 Getting Started

### System Requirements

- **Operating System**: Windows 7 or later (Windows 10/11 recommended)
- **Framework**: .NET 9.0 Runtime
- **Privileges**: Administrator rights required

### Installation & Running

SAFP requires administrator privileges to perform secure browser data operations. When you launch the application, Windows will display a User Account Control (UAC) prompt requesting administrator access. This is necessary to:

- Schedule deletion of locked browser files on system reboot
- Perform secure file operations on browser data
- Ensure complete cleanup of sensitive data

Simply approve the UAC prompt when launching SAFP.exe to proceed.

## 🛠️ Technical Stack

- **Framework**: .NET 9.0
- **UI**: Windows Presentation Foundation (WPF)
- **Architecture**: MVVM Pattern
- **Security**: AES-GCM, PBKDF2
- **Password Analysis**: zxcvbn-cs

## 🔍 Core Components

- **SAFP.Core**
  - Encryption/decryption logic
  - Password generation
  - Browser file management
  - Security utilities

- **SAFP.WPF**
  - User interface
  - MVVM implementation
  - Dialog management
  - Clipboard handling

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
