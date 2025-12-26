using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net;
using Crestron.SimplSharp.Net.Http;

namespace CrestronLevitonDriver
{
    /// <summary>
    /// Leviton API client for communicating with Leviton devices and services
    /// </summary>
    public class LevitonApi : IDisposable
    {
        private const string BaseUrl = "https://api.leviton.com/v1";
        private const int DefaultTimeout = 5000; // milliseconds
        
        private readonly string _apiKey;
        private readonly string _userId;
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the LevitonApi class
        /// </summary>
        /// <param name="apiKey">The API key for authentication</param>
        /// <param name="userId">The user ID for the Leviton account</param>
        public LevitonApi(string apiKey, string userId)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

            _apiKey = apiKey;
            _userId = userId;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Gets device information from the Leviton API
        /// </summary>
        /// <param name="deviceId">The device ID to retrieve</param>
        /// <returns>Device information as a string</returns>
        public async Task<string> GetDeviceAsync(string deviceId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));

            try
            {
                string endpoint = $"{BaseUrl}/devices/{deviceId}";
                HttpRequestMessage request = CreateRequest(HttpMethod.Get, endpoint);
                
                HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    throw new InvalidOperationException($"Failed to get device. Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine($"Error getting device {deviceId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets all devices for the authenticated user
        /// </summary>
        /// <returns>List of devices as a string</returns>
        public async Task<string> GetDevicesAsync()
        {
            ThrowIfDisposed();

            try
            {
                string endpoint = $"{BaseUrl}/users/{_userId}/devices";
                HttpRequestMessage request = CreateRequest(HttpMethod.Get, endpoint);
                
                HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    throw new InvalidOperationException($"Failed to get devices. Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine($"Error getting devices: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Controls a device by sending a command
        /// </summary>
        /// <param name="deviceId">The device ID to control</param>
        /// <param name="command">The command to send</param>
        /// <param name="parameters">Optional parameters for the command</param>
        /// <returns>The response from the API</returns>
        public async Task<string> SendDeviceCommandAsync(string deviceId, string command, Dictionary<string, string> parameters = null)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));
            if (string.IsNullOrEmpty(command))
                throw new ArgumentException("Command cannot be null or empty", nameof(command));

            try
            {
                string endpoint = $"{BaseUrl}/devices/{deviceId}/commands";
                HttpRequestMessage request = CreateRequest(HttpMethod.Post, endpoint);
                
                // Build JSON payload
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"command\":\"");
                jsonBuilder.Append(command);
                jsonBuilder.Append("\"");
                
                if (parameters != null && parameters.Count > 0)
                {
                    jsonBuilder.Append(",\"parameters\":{");
                    bool first = true;
                    foreach (var kvp in parameters)
                    {
                        if (!first) jsonBuilder.Append(",");
                        jsonBuilder.Append("\"");
                        jsonBuilder.Append(kvp.Key);
                        jsonBuilder.Append("\":\"");
                        jsonBuilder.Append(kvp.Value);
                        jsonBuilder.Append("\"");
                        first = false;
                    }
                    jsonBuilder.Append("}");
                }
                jsonBuilder.Append("}");
                
                request.Content = new HttpContent(jsonBuilder.ToString());
                request.Header.AddHeader("Content-Type", "application/json");
                
                HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
                
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    throw new InvalidOperationException($"Failed to send command. Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine($"Error sending command to device {deviceId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sets the state of a device
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <param name="state">The desired state</param>
        /// <returns>The API response</returns>
        public async Task<string> SetDeviceStateAsync(string deviceId, string state)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));
            if (string.IsNullOrEmpty(state))
                throw new ArgumentException("State cannot be null or empty", nameof(state));

            var parameters = new Dictionary<string, string> { { "state", state } };
            return await SendDeviceCommandAsync(deviceId, "SetState", parameters);
        }

        /// <summary>
        /// Gets the current state of a device
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <returns>The device state</returns>
        public async Task<string> GetDeviceStateAsync(string deviceId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));

            try
            {
                string endpoint = $"{BaseUrl}/devices/{deviceId}/state";
                HttpRequestMessage request = CreateRequest(HttpMethod.Get, endpoint);
                
                HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    throw new InvalidOperationException($"Failed to get device state. Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine($"Error getting state for device {deviceId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Authenticates with the Leviton API using credentials
        /// </summary>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <returns>Authentication token</returns>
        public async Task<string> AuthenticateAsync(string username, string password)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(username))
                throw new ArgumentException("Username cannot be null or empty", nameof(username));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            try
            {
                string endpoint = $"{BaseUrl}/auth/login";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"username\":\"");
                jsonBuilder.Append(username);
                jsonBuilder.Append("\",\"password\":\"");
                jsonBuilder.Append(password);
                jsonBuilder.Append("\"}");
                
                request.Content = new HttpContent(jsonBuilder.ToString());
                request.Header.AddHeader("Content-Type", "application/json");
                request.RequestTimeout = DefaultTimeout;
                
                HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    throw new InvalidOperationException($"Authentication failed. Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine($"Error during authentication: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates an HTTP request with proper headers and authentication
        /// </summary>
        /// <param name="method">The HTTP method</param>
        /// <param name="url">The request URL</param>
        /// <returns>Configured HttpRequestMessage</returns>
        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            
            // Add authentication header
            request.Header.AddHeader("Authorization", $"Bearer {_apiKey}");
            request.Header.AddHeader("User-Agent", "CrestronLevitonDriver/1.0");
            request.Header.AddHeader("Accept", "application/json");
            
            // Set timeout
            request.RequestTimeout = DefaultTimeout;
            
            return request;
        }

        /// <summary>
        /// Checks if the object has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LevitonApi));
        }

        /// <summary>
        /// Disposes the API client and releases resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose implementation
        /// </summary>
        /// <param name="disposing">Indicates if called from Dispose method</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_httpClient != null)
                    {
                        _httpClient.Dispose();
                    }
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~LevitonApi()
        {
            Dispose(false);
        }
    }
}
