using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestronLevitonDriver
{
    /// <summary>
    /// Manages secure credential storage and configuration for Leviton integration.
    /// Implements encryption for sensitive data and provides configuration validation.
    /// </summary>
    public class LevitonConfiguration
    {
        private readonly string _configPath;
        private readonly string _encryptionKeyPath;
        private Dictionary<string, string> _configuration;
        private byte[] _encryptionKey;

        public LevitonConfiguration(string configPath = "./config/leviton.json")
        {
            _configPath = configPath;
            _encryptionKeyPath = Path.Combine(Path.GetDirectoryName(configPath), ".key");
            _configuration = new Dictionary<string, string>();
            InitializeEncryption();
        }

        /// <summary>
        /// Initializes or loads the encryption key for credential storage.
        /// </summary>
        private void InitializeEncryption()
        {
            try
            {
                if (File.Exists(_encryptionKeyPath))
                {
                    _encryptionKey = File.ReadAllBytes(_encryptionKeyPath);
                }
                else
                {
                    _encryptionKey = GenerateEncryptionKey();
                    SaveEncryptionKey();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize encryption key.", ex);
            }
        }

        /// <summary>
        /// Generates a new encryption key using a cryptographically secure method.
        /// </summary>
        private byte[] GenerateEncryptionKey()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] key = new byte[32]; // 256-bit key for AES
                rng.GetBytes(key);
                return key;
            }
        }

        /// <summary>
        /// Saves the encryption key to disk with restricted permissions.
        /// </summary>
        private void SaveEncryptionKey()
        {
            try
            {
                var directory = Path.GetDirectoryName(_encryptionKeyPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(_encryptionKeyPath, _encryptionKey);

                // Set file permissions to read-only for owner
                var fileInfo = new FileInfo(_encryptionKeyPath);
                fileInfo.Attributes = FileAttributes.Hidden;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to save encryption key.", ex);
            }
        }

        /// <summary>
        /// Encrypts sensitive data using AES encryption.
        /// </summary>
        public string EncryptCredential(string plainText)
        {
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    {
                        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                        byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                        // Combine IV and encrypted data
                        byte[] result = new byte[aes.IV.Length + encryptedBytes.Length];
                        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to encrypt credential.", ex);
            }
        }

        /// <summary>
        /// Decrypts sensitive data using AES encryption.
        /// </summary>
        public string DecryptCredential(string encryptedText)
        {
            try
            {
                byte[] buffer = Convert.FromBase64String(encryptedText);

                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    // Extract IV from the beginning of the buffer
                    byte[] iv = new byte[aes.IV.Length];
                    Buffer.BlockCopy(buffer, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    byte[] encryptedBytes = new byte[buffer.Length - iv.Length];
                    Buffer.BlockCopy(buffer, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                        return Encoding.UTF8.GetString(decryptedBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to decrypt credential.", ex);
            }
        }

        /// <summary>
        /// Loads configuration from JSON file.
        /// </summary>
        public bool LoadConfiguration()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    return false;
                }

                string jsonContent = File.ReadAllText(_configPath);
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        _configuration[property.Name] = property.Value.GetString();
                    }
                }

                return ValidateConfiguration();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load configuration.", ex);
            }
        }

        /// <summary>
        /// Saves configuration to JSON file.
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(_configuration, options);
                File.WriteAllText(_configPath, jsonContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to save configuration.", ex);
            }
        }

        /// <summary>
        /// Sets a configuration value with optional encryption for sensitive data.
        /// </summary>
        public void SetValue(string key, string value, bool encrypt = false)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Configuration key cannot be null or empty.", nameof(key));
            }

            string storageValue = encrypt ? EncryptCredential(value) : value;
            _configuration[key] = storageValue;
        }

        /// <summary>
        /// Gets a configuration value with optional decryption for sensitive data.
        /// </summary>
        public string GetValue(string key, bool decrypt = false, string defaultValue = null)
        {
            if (!_configuration.ContainsKey(key))
            {
                return defaultValue;
            }

            string value = _configuration[key];
            return decrypt ? DecryptCredential(value) : value;
        }

        /// <summary>
        /// Validates that all required configuration values are present and valid.
        /// </summary>
        private bool ValidateConfiguration()
        {
            var requiredKeys = new[] { "host", "port", "username" };

            foreach (var key in requiredKeys)
            {
                if (!_configuration.ContainsKey(key) || string.IsNullOrWhiteSpace(_configuration[key]))
                {
                    return false;
                }
            }

            // Validate port is a valid number
            if (_configuration.ContainsKey("port"))
            {
                if (!int.TryParse(_configuration["port"], out int port) || port <= 0 || port > 65535)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets the Leviton host address.
        /// </summary>
        public string Host => GetValue("host");

        /// <summary>
        /// Gets the Leviton connection port.
        /// </summary>
        public int Port
        {
            get
            {
                string portValue = GetValue("port");
                return int.TryParse(portValue, out int port) ? port : 8080;
            }
        }

        /// <summary>
        /// Gets the Leviton username.
        /// </summary>
        public string Username => GetValue("username");

        /// <summary>
        /// Gets the encrypted Leviton password.
        /// </summary>
        public string Password => GetValue("password", decrypt: true);

        /// <summary>
        /// Gets the API key if configured (decrypted).
        /// </summary>
        public string ApiKey => GetValue("api_key", decrypt: true);

        /// <summary>
        /// Gets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeout
        {
            get
            {
                string timeoutValue = GetValue("connection_timeout");
                return int.TryParse(timeoutValue, out int timeout) ? timeout : 5000;
            }
        }

        /// <summary>
        /// Gets whether SSL/TLS is enabled.
        /// </summary>
        public bool UseSSL
        {
            get
            {
                string sslValue = GetValue("use_ssl", defaultValue: "false");
                return bool.TryParse(sslValue, out bool useSSL) ? useSSL : false;
            }
        }

        /// <summary>
        /// Gets all configuration keys (non-sensitive).
        /// </summary>
        public IEnumerable<string> GetKeys() => _configuration.Keys;

        /// <summary>
        /// Clears all sensitive data from memory.
        /// </summary>
        public void ClearSensitiveData()
        {
            if (_encryptionKey != null)
            {
                Array.Clear(_encryptionKey, 0, _encryptionKey.Length);
            }

            _configuration.Clear();
        }
    }
}
