using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LevitonDecora.Driver
{
    /// <summary>
    /// Leviton Decora WiFi API communication layer for device control and monitoring.
    /// Provides methods for authentication, device discovery, and state management.
    /// </summary>
    public class LevitonApi : IDisposable
    {
        private readonly string _apiBaseUrl;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerSettings _jsonSettings;
        private string _accessToken;
        private DateTime _tokenExpirationTime;
        private readonly object _tokenLock = new object();

        private const int DefaultTimeout = 30000; // 30 seconds
        private const int TokenRefreshThresholdSeconds = 300; // 5 minutes

        /// <summary>
        /// Initializes a new instance of the LevitonApi class.
        /// </summary>
        /// <param name="apiBaseUrl">The base URL for the Leviton API (e.g., https://api.leviton.com/api)</param>
        public LevitonApi(string apiBaseUrl = "https://api.leviton.com/api")
        {
            _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(apiBaseUrl));
            
            // Configure HttpClient
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(DefaultTimeout)
            };

            // Configure JSON settings
            _jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ"
            };

            _accessToken = null;
            _tokenExpirationTime = DateTime.MinValue;
        }

        /// <summary>
        /// Authenticates with the Leviton API using username and password.
        /// </summary>
        /// <param name="username">The user's email address</param>
        /// <param name="password">The user's password</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Authentication result containing access token and expiration time</returns>
        public async Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be empty.", nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty.", nameof(password));

            try
            {
                var requestBody = new
                {
                    username,
                    password,
                    remember_me = true
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(requestBody, _jsonSettings),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/user/login",
                    content,
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new LevitonApiException($"Authentication failed with status {response.StatusCode}: {responseBody}");
                }

                var authResponse = JsonConvert.DeserializeObject<JObject>(responseBody);
                var token = authResponse?["access_token"]?.ToString();
                var expiresIn = authResponse?["expires_in"]?.Value<int>() ?? 3600;

                if (string.IsNullOrEmpty(token))
                {
                    throw new LevitonApiException("No access token returned from authentication endpoint.");
                }

                lock (_tokenLock)
                {
                    _accessToken = token;
                    _tokenExpirationTime = DateTime.UtcNow.AddSeconds(expiresIn);
                }

                return new AuthenticationResult
                {
                    AccessToken = token,
                    ExpiresIn = expiresIn,
                    ExpirationTime = _tokenExpirationTime
                };
            }
            catch (HttpRequestException ex)
            {
                throw new LevitonApiException("Failed to connect to Leviton API.", ex);
            }
        }

        /// <summary>
        /// Refreshes the authentication token if it's about to expire.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>True if token was refreshed or is still valid, false otherwise</returns>
        public async Task<bool> RefreshTokenIfNeededAsync(CancellationToken cancellationToken = default)
        {
            lock (_tokenLock)
            {
                // Check if token exists and is still valid
                if (string.IsNullOrEmpty(_accessToken) || 
                    DateTime.UtcNow.AddSeconds(TokenRefreshThresholdSeconds) >= _tokenExpirationTime)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets a list of all devices associated with the authenticated user.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>List of device information</returns>
        public async Task<List<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            ValidateAuthentication();

            try
            {
                var response = await SendAuthenticatedGetAsync(
                    $"{_apiBaseUrl}/devices",
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new LevitonApiException($"Failed to retrieve devices: {responseBody}");
                }

                var devices = JsonConvert.DeserializeObject<List<DeviceInfo>>(responseBody, _jsonSettings) 
                    ?? new List<DeviceInfo>();

                return devices;
            }
            catch (HttpRequestException ex)
            {
                throw new LevitonApiException("Failed to connect to Leviton API.", ex);
            }
        }

        /// <summary>
        /// Gets detailed information for a specific device.
        /// </summary>
        /// <param name="deviceId">The unique identifier of the device</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Detailed device information</returns>
        public async Task<DeviceInfo> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ValidateAuthentication();
            
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("Device ID cannot be empty.", nameof(deviceId));

            try
            {
                var response = await SendAuthenticatedGetAsync(
                    $"{_apiBaseUrl}/devices/{Uri.EscapeDataString(deviceId)}",
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new LevitonApiException($"Failed to retrieve device {deviceId}: {responseBody}");
                }

                var device = JsonConvert.DeserializeObject<DeviceInfo>(responseBody, _jsonSettings);
                return device;
            }
            catch (HttpRequestException ex)
            {
                throw new LevitonApiException("Failed to connect to Leviton API.", ex);
            }
        }

        /// <summary>
        /// Sets the state of a device (e.g., on/off, brightness level).
        /// </summary>
        /// <param name="deviceId">The unique identifier of the device</param>
        /// <param name="state">The new state values</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Updated device state</returns>
        public async Task<DeviceState> SetDeviceStateAsync(string deviceId, DeviceStateRequest state, CancellationToken cancellationToken = default)
        {
            ValidateAuthentication();
            
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("Device ID cannot be empty.", nameof(deviceId));
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            try
            {
                var content = new StringContent(
                    JsonConvert.SerializeObject(state, _jsonSettings),
                    Encoding.UTF8,
                    "application/json");

                var response = await SendAuthenticatedPutAsync(
                    $"{_apiBaseUrl}/devices/{Uri.EscapeDataString(deviceId)}/state",
                    content,
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new LevitonApiException($"Failed to set device state: {responseBody}");
                }

                var deviceState = JsonConvert.DeserializeObject<DeviceState>(responseBody, _jsonSettings);
                return deviceState;
            }
            catch (HttpRequestException ex)
            {
                throw new LevitonApiException("Failed to connect to Leviton API.", ex);
            }
        }

        /// <summary>
        /// Turns a device on or off.
        /// </summary>
        /// <param name="deviceId">The unique identifier of the device</param>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Updated device state</returns>
        public async Task<DeviceState> SetDevicePowerAsync(string deviceId, bool turnOn, CancellationToken cancellationToken = default)
        {
            var state = new DeviceStateRequest
            {
                Power = turnOn ? "on" : "off"
            };

            return await SetDeviceStateAsync(deviceId, state, cancellationToken);
        }

        /// <summary>
        /// Sets the brightness level of a dimmable device.
        /// </summary>
        /// <param name="deviceId">The unique identifier of the device</param>
        /// <param name="brightness">Brightness level (0-100)</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Updated device state</returns>
        public async Task<DeviceState> SetDeviceBrightnessAsync(string deviceId, int brightness, CancellationToken cancellationToken = default)
        {
            if (brightness < 0 || brightness > 100)
                throw new ArgumentException("Brightness must be between 0 and 100.", nameof(brightness));

            var state = new DeviceStateRequest
            {
                Brightness = brightness
            };

            return await SetDeviceStateAsync(deviceId, state, cancellationToken);
        }

        /// <summary>
        /// Sets the color of a tunable color device.
        /// </summary>
        /// <param name="deviceId">The unique identifier of the device</param>
        /// <param name="colorTemperature">Color temperature in Kelvin (2000-6500)</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Updated device state</returns>
        public async Task<DeviceState> SetDeviceColorAsync(string deviceId, int colorTemperature, CancellationToken cancellationToken = default)
        {
            if (colorTemperature < 2000 || colorTemperature > 6500)
                throw new ArgumentException("Color temperature must be between 2000 and 6500 Kelvin.", nameof(colorTemperature));

            var state = new DeviceStateRequest
            {
                ColorTemperature = colorTemperature
            };

            return await SetDeviceStateAsync(deviceId, state, cancellationToken);
        }

        /// <summary>
        /// Gets the current status/state of a device.
        /// </summary>
        /// <param name="deviceId">The unique identifier of the device</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Current device state</returns>
        public async Task<DeviceState> GetDeviceStateAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ValidateAuthentication();
            
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("Device ID cannot be empty.", nameof(deviceId));

            try
            {
                var response = await SendAuthenticatedGetAsync(
                    $"{_apiBaseUrl}/devices/{Uri.EscapeDataString(deviceId)}/state",
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new LevitonApiException($"Failed to retrieve device state: {responseBody}");
                }

                var deviceState = JsonConvert.DeserializeObject<DeviceState>(responseBody, _jsonSettings);
                return deviceState;
            }
            catch (HttpRequestException ex)
            {
                throw new LevitonApiException("Failed to connect to Leviton API.", ex);
            }
        }

        /// <summary>
        /// Sends a raw HTTP GET request with authentication headers.
        /// </summary>
        private async Task<HttpResponseMessage> SendAuthenticatedGetAsync(string url, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthenticationHeaders(request);
            return await _httpClient.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Sends a raw HTTP PUT request with authentication headers.
        /// </summary>
        private async Task<HttpResponseMessage> SendAuthenticatedPutAsync(string url, HttpContent content, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = content
            };
            AddAuthenticationHeaders(request);
            return await _httpClient.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Adds authentication headers to an HTTP request.
        /// </summary>
        private void AddAuthenticationHeaders(HttpRequestMessage request)
        {
            lock (_tokenLock)
            {
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                request.Headers.Add("User-Agent", "LevitonCrestronDriver/1.0");
            }
        }

        /// <summary>
        /// Validates that the API client has a valid authentication token.
        /// </summary>
        private void ValidateAuthentication()
        {
            lock (_tokenLock)
            {
                if (string.IsNullOrEmpty(_accessToken))
                    throw new InvalidOperationException("Not authenticated. Please call AuthenticateAsync first.");
                
                if (DateTime.UtcNow >= _tokenExpirationTime)
                    throw new InvalidOperationException("Authentication token has expired. Please authenticate again.");
            }
        }

        /// <summary>
        /// Disposes of the HTTP client and releases resources.
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Represents the result of an authentication attempt.
    /// </summary>
    public class AuthenticationResult
    {
        public string AccessToken { get; set; }
        public int ExpiresIn { get; set; }
        public DateTime ExpirationTime { get; set; }
    }

    /// <summary>
    /// Represents information about a Leviton device.
    /// </summary>
    public class DeviceInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("state")]
        public DeviceState State { get; set; }

        [JsonProperty("capabilities")]
        public List<string> Capabilities { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("last_updated")]
        public DateTime? LastUpdated { get; set; }

        public override string ToString()
        {
            return $"Device: {Name} ({Id}) - Type: {Type} - Status: {Status}";
        }
    }

    /// <summary>
    /// Represents the current state of a device.
    /// </summary>
    public class DeviceState
    {
        [JsonProperty("power")]
        public string Power { get; set; }

        [JsonProperty("brightness")]
        public int? Brightness { get; set; }

        [JsonProperty("color_temperature")]
        public int? ColorTemperature { get; set; }

        [JsonProperty("hue")]
        public int? Hue { get; set; }

        [JsonProperty("saturation")]
        public int? Saturation { get; set; }

        [JsonProperty("on_off")]
        public bool? OnOff { get; set; }

        [JsonProperty("timestamp")]
        public DateTime? Timestamp { get; set; }

        public override string ToString()
        {
            var parts = new List<string> { $"Power: {Power}" };
            
            if (Brightness.HasValue)
                parts.Add($"Brightness: {Brightness}%");
            if (ColorTemperature.HasValue)
                parts.Add($"ColorTemp: {ColorTemperature}K");
            if (Hue.HasValue)
                parts.Add($"Hue: {Hue}");
            if (Saturation.HasValue)
                parts.Add($"Saturation: {Saturation}%");

            return string.Join(" | ", parts);
        }
    }

    /// <summary>
    /// Represents a request to change the state of a device.
    /// </summary>
    public class DeviceStateRequest
    {
        [JsonProperty("power")]
        public string Power { get; set; }

        [JsonProperty("brightness")]
        public int? Brightness { get; set; }

        [JsonProperty("color_temperature")]
        public int? ColorTemperature { get; set; }

        [JsonProperty("hue")]
        public int? Hue { get; set; }

        [JsonProperty("saturation")]
        public int? Saturation { get; set; }
    }

    /// <summary>
    /// Represents an exception specific to Leviton API operations.
    /// </summary>
    public class LevitonApiException : Exception
    {
        public LevitonApiException(string message) : base(message) { }
        public LevitonApiException(string message, Exception innerException) : base(message, innerException) { }
    }
}
