using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Crestron.Leviton.Driver
{
    /// <summary>
    /// Provides secure credential storage and configuration management for Leviton devices.
    /// Implements encryption/decryption of sensitive data and configuration persistence.
    /// </summary>
    public class LevitonConfiguration
    {
        private const string CONFIG_FILE_NAME = "leviton-config.json";
        private const string ENCRYPTION_KEY_FILE = ".leviton-key";
        private const string ALGORITHM = "AES256";
        private const int KEY_SIZE = 32; // 256 bits
        private const int IV_SIZE = 16;  // 128 bits

        private readonly string _configDirectory;
        private readonly Dictionary<string, object> _configuration;
        private byte[] _encryptionKey;

        /// <summary>
        /// Initializes a new instance of the LevitonConfiguration class.
        /// </summary>
        /// <param name="configDirectory">Directory where configuration files will be stored</param>
        public LevitonConfiguration(string configDirectory = null)
        {
            _configDirectory = configDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Crestron", "Leviton");

            _configuration = new Dictionary<string, object>();

            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }

            InitializeEncryption();
        }

        /// <summary>
        /// Initializes or loads the encryption key.
        /// </summary>
        private void InitializeEncryption()
        {
            string keyFilePath = Path.Combine(_configDirectory, ENCRYPTION_KEY_FILE);

            if (File.Exists(keyFilePath))
            {
                _encryptionKey = File.ReadAllBytes(keyFilePath);
                if (_encryptionKey.Length != KEY_SIZE)
                {
                    throw new InvalidOperationException("Invalid encryption key size in stored key file.");
                }
            }
            else
            {
                _encryptionKey = GenerateEncryptionKey();
                try
                {
                    File.WriteAllBytes(keyFilePath, _encryptionKey);
                    // Restrict file access to current user only
                    var fileInfo = new FileInfo(keyFilePath);
                    var fileSecurity = fileInfo.GetAccessControl();
                    fileInfo.SetAccessControl(fileSecurity);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to save encryption key.", ex);
                }
            }
        }

        /// <summary>
        /// Generates a new encryption key using cryptographically secure random.
        /// </summary>
        /// <returns>A new encryption key of 256 bits</returns>
        private byte[] GenerateEncryptionKey()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] key = new byte[KEY_SIZE];
                rng.GetBytes(key);
                return key;
            }
        }

        /// <summary>
        /// Encrypts a string value using AES-256 encryption.
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64-encoded encrypted data with IV prefix</returns>
        public string EncryptValue(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using (var aes = new AesCryptoServiceProvider())
                {
                    aes.Key = _encryptionKey;
                    aes.GenerateIV();

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream())
                    {
                        // Write IV to the beginning of the stream
                        ms.Write(aes.IV, 0, aes.IV.Length);

                        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        using (var sw = new StreamWriter(cs, Encoding.UTF8))
                        {
                            sw.Write(plainText);
                        }

                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Encryption failed.", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted string value.
        /// </summary>
        /// <param name="encryptedText">Base64-encoded encrypted data with IV prefix</param>
        /// <returns>The decrypted plaintext</returns>
        public string DecryptValue(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                byte[] encryptedData = Convert.FromBase64String(encryptedText);

                using (var aes = new AesCryptoServiceProvider())
                {
                    aes.Key = _encryptionKey;

                    // Extract IV from the beginning of the data
                    byte[] iv = new byte[IV_SIZE];
                    Array.Copy(encryptedData, 0, iv, 0, IV_SIZE);
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(encryptedData, IV_SIZE, encryptedData.Length - IV_SIZE))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs, Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Decryption failed.", ex);
            }
        }

        /// <summary>
        /// Sets a configuration value with optional encryption.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="value">Configuration value</param>
        /// <param name="encrypt">Whether to encrypt the value</param>
        public void Set(string key, object value, bool encrypt = false)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (encrypt && value is string stringValue)
            {
                _configuration[key] = new EncryptedValue { 
                    IsEncrypted = true, 
                    Value = EncryptValue(stringValue) 
                };
            }
            else
            {
                _configuration[key] = value;
            }
        }

        /// <summary>
        /// Gets a configuration value, decrypting if necessary.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>The configuration value, decrypted if it was encrypted</returns>
        public object Get(string key, object defaultValue = null)
        {
            if (!_configuration.TryGetValue(key, out var value))
                return defaultValue;

            if (value is EncryptedValue encryptedValue && encryptedValue.IsEncrypted)
            {
                return DecryptValue(encryptedValue.Value);
            }

            return value;
        }

        /// <summary>
        /// Gets a configuration value as a string.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>The configuration value as string</returns>
        public string GetString(string key, string defaultValue = null)
        {
            return Get(key, defaultValue) as string ?? defaultValue;
        }

        /// <summary>
        /// Gets a configuration value as an integer.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>The configuration value as integer</returns>
        public int GetInt(string key, int defaultValue = 0)
        {
            var value = Get(key);
            if (value == null)
                return defaultValue;

            if (int.TryParse(value.ToString(), out int result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Gets a configuration value as a boolean.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>The configuration value as boolean</returns>
        public bool GetBool(string key, bool defaultValue = false)
        {
            var value = Get(key);
            if (value == null)
                return defaultValue;

            if (bool.TryParse(value.ToString(), out bool result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Saves the configuration to disk in JSON format.
        /// </summary>
        public void Save()
        {
            try
            {
                string configPath = Path.Combine(_configDirectory, CONFIG_FILE_NAME);
                var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to save configuration.", ex);
            }
        }

        /// <summary>
        /// Loads the configuration from disk.
        /// </summary>
        public void Load()
        {
            try
            {
                string configPath = Path.Combine(_configDirectory, CONFIG_FILE_NAME);
                if (!File.Exists(configPath))
                    return;

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                if (loaded != null)
                {
                    _configuration.Clear();
                    foreach (var kvp in loaded)
                    {
                        // Re-deserialize nested encrypted values properly
                        if (kvp.Value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                        {
                            var encryptedValue = JsonSerializer.Deserialize<EncryptedValue>(element.GetRawText());
                            _configuration[kvp.Key] = encryptedValue;
                        }
                        else
                        {
                            _configuration[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load configuration.", ex);
            }
        }

        /// <summary>
        /// Clears all configuration values.
        /// </summary>
        public void Clear()
        {
            _configuration.Clear();
        }

        /// <summary>
        /// Gets all configuration keys.
        /// </summary>
        /// <returns>Array of configuration keys</returns>
        public string[] GetKeys()
        {
            return _configuration.Keys.ToArray();
        }

        /// <summary>
        /// Removes a configuration value.
        /// </summary>
        /// <param name="key">Configuration key to remove</param>
        /// <returns>True if the key was removed, false if it didn't exist</returns>
        public bool Remove(string key)
        {
            return _configuration.Remove(key);
        }

        /// <summary>
        /// Internal class for storing encrypted values with metadata.
        /// </summary>
        private class EncryptedValue
        {
            public bool IsEncrypted { get; set; }
            public string Value { get; set; }
        }
    }
}
