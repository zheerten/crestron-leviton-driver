using System;
using System.Collections.Generic;

namespace CrestronLevitonDriver
{
    /// <summary>
    /// Base class for all Leviton devices
    /// </summary>
    public abstract class LevitonDevice
    {
        /// <summary>
        /// Gets or sets the device ID
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets or sets the device name
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// Gets or sets the device type
        /// </summary>
        public string DeviceType { get; set; }

        /// <summary>
        /// Gets or sets whether the device is online
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// Gets or sets the last communication timestamp
        /// </summary>
        public DateTime LastCommunication { get; set; }

        /// <summary>
        /// Initializes a new instance of the LevitonDevice class
        /// </summary>
        /// <param name="deviceId">The unique device identifier</param>
        /// <param name="deviceName">The friendly name of the device</param>
        /// <param name="deviceType">The type of device</param>
        protected LevitonDevice(string deviceId, string deviceName, string deviceType)
        {
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            DeviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
            DeviceType = deviceType ?? throw new ArgumentNullException(nameof(deviceType));
            IsOnline = false;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Abstract method to be implemented by derived classes for device initialization
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// Abstract method to be implemented by derived classes for device shutdown
        /// </summary>
        public abstract void Shutdown();
    }

    /// <summary>
    /// Represents a Leviton Switch device
    /// </summary>
    public class LevitonSwitch : LevitonDevice
    {
        /// <summary>
        /// Gets or sets the current state of the switch (on/off)
        /// </summary>
        public bool IsOn { get; set; }

        /// <summary>
        /// Gets or sets the switch load level (0-100%)
        /// </summary>
        public int LoadLevel { get; set; }

        /// <summary>
        /// Event raised when the switch state changes
        /// </summary>
        public event EventHandler<SwitchStateChangedEventArgs> StateChanged;

        /// <summary>
        /// Initializes a new instance of the LevitonSwitch class
        /// </summary>
        /// <param name="deviceId">The unique device identifier</param>
        /// <param name="deviceName">The friendly name of the device</param>
        public LevitonSwitch(string deviceId, string deviceName)
            : base(deviceId, deviceName, "Switch")
        {
            IsOn = false;
            LoadLevel = 0;
        }

        /// <summary>
        /// Turns the switch on
        /// </summary>
        public void TurnOn()
        {
            if (!IsOn)
            {
                IsOn = true;
                LoadLevel = 100;
                LastCommunication = DateTime.UtcNow;
                OnStateChanged(new SwitchStateChangedEventArgs { IsOn = true, LoadLevel = LoadLevel });
            }
        }

        /// <summary>
        /// Turns the switch off
        /// </summary>
        public void TurnOff()
        {
            if (IsOn)
            {
                IsOn = false;
                LoadLevel = 0;
                LastCommunication = DateTime.UtcNow;
                OnStateChanged(new SwitchStateChangedEventArgs { IsOn = false, LoadLevel = LoadLevel });
            }
        }

        /// <summary>
        /// Toggles the switch state
        /// </summary>
        public void Toggle()
        {
            if (IsOn)
                TurnOff();
            else
                TurnOn();
        }

        /// <summary>
        /// Initializes the switch device
        /// </summary>
        public override void Initialize()
        {
            IsOnline = true;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Shuts down the switch device
        /// </summary>
        public override void Shutdown()
        {
            IsOnline = false;
            IsOn = false;
            LoadLevel = 0;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Raises the StateChanged event
        /// </summary>
        protected virtual void OnStateChanged(SwitchStateChangedEventArgs e)
        {
            StateChanged?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Represents a Leviton Dimmer device
    /// </summary>
    public class LevitonDimmer : LevitonDevice
    {
        /// <summary>
        /// Gets or sets the dimmer brightness level (0-100%)
        /// </summary>
        public int BrightnessLevel { get; set; }

        /// <summary>
        /// Gets or sets whether the dimmer is currently on
        /// </summary>
        public bool IsOn { get; set; }

        /// <summary>
        /// Gets or sets the fade time in milliseconds
        /// </summary>
        public int FadeTimeMs { get; set; }

        /// <summary>
        /// Gets the minimum brightness level supported
        /// </summary>
        public int MinBrightness { get; private set; }

        /// <summary>
        /// Gets the maximum brightness level supported
        /// </summary>
        public int MaxBrightness { get; private set; }

        /// <summary>
        /// Event raised when the brightness level changes
        /// </summary>
        public event EventHandler<DimmerBrightnessChangedEventArgs> BrightnessChanged;

        /// <summary>
        /// Initializes a new instance of the LevitonDimmer class
        /// </summary>
        /// <param name="deviceId">The unique device identifier</param>
        /// <param name="deviceName">The friendly name of the device</param>
        public LevitonDimmer(string deviceId, string deviceName)
            : base(deviceId, deviceName, "Dimmer")
        {
            BrightnessLevel = 0;
            IsOn = false;
            FadeTimeMs = 500;
            MinBrightness = 0;
            MaxBrightness = 100;
        }

        /// <summary>
        /// Sets the brightness level of the dimmer
        /// </summary>
        /// <param name="level">The brightness level (0-100)</param>
        public void SetBrightness(int level)
        {
            if (level < MinBrightness || level > MaxBrightness)
                throw new ArgumentOutOfRangeException(nameof(level), $"Brightness level must be between {MinBrightness} and {MaxBrightness}");

            int previousLevel = BrightnessLevel;
            BrightnessLevel = level;
            IsOn = level > 0;
            LastCommunication = DateTime.UtcNow;

            if (previousLevel != BrightnessLevel)
                OnBrightnessChanged(new DimmerBrightnessChangedEventArgs { PreviousLevel = previousLevel, CurrentLevel = BrightnessLevel });
        }

        /// <summary>
        /// Increases the brightness level by the specified increment
        /// </summary>
        /// <param name="increment">The amount to increase brightness</param>
        public void IncreaseBrightness(int increment = 10)
        {
            int newLevel = Math.Min(BrightnessLevel + increment, MaxBrightness);
            SetBrightness(newLevel);
        }

        /// <summary>
        /// Decreases the brightness level by the specified decrement
        /// </summary>
        /// <param name="decrement">The amount to decrease brightness</param>
        public void DecreaseBrightness(int decrement = 10)
        {
            int newLevel = Math.Max(BrightnessLevel - decrement, MinBrightness);
            SetBrightness(newLevel);
        }

        /// <summary>
        /// Initializes the dimmer device
        /// </summary>
        public override void Initialize()
        {
            IsOnline = true;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Shuts down the dimmer device
        /// </summary>
        public override void Shutdown()
        {
            IsOnline = false;
            BrightnessLevel = 0;
            IsOn = false;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Raises the BrightnessChanged event
        /// </summary>
        protected virtual void OnBrightnessChanged(DimmerBrightnessChangedEventArgs e)
        {
            BrightnessChanged?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Represents a Leviton Fan device
    /// </summary>
    public class LevitonFan : LevitonDevice
    {
        /// <summary>
        /// Enumeration for fan speed levels
        /// </summary>
        public enum FanSpeed
        {
            Off = 0,
            Low = 1,
            Medium = 2,
            High = 3
        }

        /// <summary>
        /// Gets or sets the current fan speed
        /// </summary>
        public FanSpeed CurrentSpeed { get; set; }

        /// <summary>
        /// Gets or sets whether the fan is running
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// Gets the current fan speed as a percentage (0-100)
        /// </summary>
        public int SpeedPercentage
        {
            get
            {
                return (int)CurrentSpeed * 33;
            }
        }

        /// <summary>
        /// Event raised when the fan speed changes
        /// </summary>
        public event EventHandler<FanSpeedChangedEventArgs> SpeedChanged;

        /// <summary>
        /// Initializes a new instance of the LevitonFan class
        /// </summary>
        /// <param name="deviceId">The unique device identifier</param>
        /// <param name="deviceName">The friendly name of the device</param>
        public LevitonFan(string deviceId, string deviceName)
            : base(deviceId, deviceName, "Fan")
        {
            CurrentSpeed = FanSpeed.Off;
            IsRunning = false;
        }

        /// <summary>
        /// Sets the fan speed
        /// </summary>
        /// <param name="speed">The desired fan speed</param>
        public void SetSpeed(FanSpeed speed)
        {
            FanSpeed previousSpeed = CurrentSpeed;
            CurrentSpeed = speed;
            IsRunning = speed != FanSpeed.Off;
            LastCommunication = DateTime.UtcNow;

            if (previousSpeed != CurrentSpeed)
                OnSpeedChanged(new FanSpeedChangedEventArgs { PreviousSpeed = previousSpeed, CurrentSpeed = CurrentSpeed });
        }

        /// <summary>
        /// Turns the fan off
        /// </summary>
        public void TurnOff()
        {
            SetSpeed(FanSpeed.Off);
        }

        /// <summary>
        /// Sets the fan to low speed
        /// </summary>
        public void SetLowSpeed()
        {
            SetSpeed(FanSpeed.Low);
        }

        /// <summary>
        /// Sets the fan to medium speed
        /// </summary>
        public void SetMediumSpeed()
        {
            SetSpeed(FanSpeed.Medium);
        }

        /// <summary>
        /// Sets the fan to high speed
        /// </summary>
        public void SetHighSpeed()
        {
            SetSpeed(FanSpeed.High);
        }

        /// <summary>
        /// Cycles the fan to the next speed level
        /// </summary>
        public void CycleSpeed()
        {
            FanSpeed nextSpeed = (FanSpeed)(((int)CurrentSpeed + 1) % 4);
            SetSpeed(nextSpeed);
        }

        /// <summary>
        /// Initializes the fan device
        /// </summary>
        public override void Initialize()
        {
            IsOnline = true;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Shuts down the fan device
        /// </summary>
        public override void Shutdown()
        {
            IsOnline = false;
            CurrentSpeed = FanSpeed.Off;
            IsRunning = false;
            LastCommunication = DateTime.UtcNow;
        }

        /// <summary>
        /// Raises the SpeedChanged event
        /// </summary>
        protected virtual void OnSpeedChanged(FanSpeedChangedEventArgs e)
        {
            SpeedChanged?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Event arguments for switch state changes
    /// </summary>
    public class SwitchStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets whether the switch is on
        /// </summary>
        public bool IsOn { get; set; }

        /// <summary>
        /// Gets or sets the load level
        /// </summary>
        public int LoadLevel { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the change
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for dimmer brightness changes
    /// </summary>
    public class DimmerBrightnessChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the previous brightness level
        /// </summary>
        public int PreviousLevel { get; set; }

        /// <summary>
        /// Gets or sets the current brightness level
        /// </summary>
        public int CurrentLevel { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the change
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for fan speed changes
    /// </summary>
    public class FanSpeedChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the previous fan speed
        /// </summary>
        public LevitonFan.FanSpeed PreviousSpeed { get; set; }

        /// <summary>
        /// Gets or sets the current fan speed
        /// </summary>
        public LevitonFan.FanSpeed CurrentSpeed { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the change
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
