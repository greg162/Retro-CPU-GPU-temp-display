using System;
using System.Collections.Generic;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace TempMonitor
{
    public enum TemperatureType { Cpu, Gpu }

    public interface ITemperatureProvider
    {
        TemperatureType Type { get; }
        float? GetTemperature();
    }

    public class TemperatureFinder
    {
        private readonly List<ITemperatureProvider> _providers = new List<ITemperatureProvider>();

        public void RegisterProvider(ITemperatureProvider provider)
        {
            _providers.Add(provider);
        }

        public float? GetTemperature(TemperatureType type)
        {
            foreach (var provider in _providers)
            {
                if (provider.Type == type)
                {
                    float? temp = provider.GetTemperature();
                    if (temp.HasValue && temp.Value > 0) return temp;
                }
            }
            return null;
        }
    }

    public class WmiCpuProvider : ITemperatureProvider
    {
        public TemperatureType Type => TemperatureType.Cpu;

        public float? GetTemperature()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    double temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    float celsius = (float)((temp - 2732) / 10.0);
                    if (celsius > 0) return celsius;
                }
            }
            catch { /* Ignore */ }
            return null;
        }
    }

    public class LhmCpuProvider : ITemperatureProvider
    {
        private readonly Computer _computer;
        public TemperatureType Type => TemperatureType.Cpu;

        public LhmCpuProvider(Computer computer)
        {
            _computer = computer;
        }

        public float? GetTemperature()
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && 
                            (sensor.Name.Contains("Tctl") || sensor.Name.Contains("Tdie") || 
                             sensor.Name.Contains("Package") || sensor.Name.Contains("Average") || 
                             sensor.Name.Contains("Core")))
                            return sensor.Value;
                }
            }
            return null;
        }
    }

    public class LhmGpuProvider : ITemperatureProvider
    {
        private readonly Computer _computer;
        public TemperatureType Type => TemperatureType.Gpu;

        public LhmGpuProvider(Computer computer)
        {
            _computer = computer;
        }

        public float? GetTemperature()
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia ||
                    hardware.HardwareType == HardwareType.GpuAmd ||
                    hardware.HardwareType == HardwareType.GpuIntel)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                            return sensor.Value;
                }
            }
            return null;
        }
    }
}