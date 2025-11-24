using System;
using System.IO.Ports;
using System.Threading;
using System.Management;
using LibreHardwareMonitor.Hardware;
using System.Diagnostics; // <--- ADD THIS for Process control

namespace TempMonitor
{
    class Program
    {
        private static SerialPort? serialPort;
        private static Computer? computer;
        
        static void Main(string[] args)
        {
            // --- KILL SWITCH LOGIC START ---
            // Check if the user passed the "--kill" argument
            if (args.Length > 0 && (args[0] == "--kill" || args[0] == "-k"))
            {
                KillRunningInstances();
                return; // Exit this instance immediately after killing the others
            }
            
            Console.WriteLine("CPU/GPU Temperature Monitor");
            Console.WriteLine("============================\n");
            
            // Find the Pico's COM port
            string? comPort = FindPicoPort();
            if (comPort == null)
            {
                Console.WriteLine("Could not find Raspberry Pi Pico.");
                Console.WriteLine("Please check connection and press any key to exit...");
                Console.ReadKey();
                return;
            }
            
            Console.WriteLine($"Found Pico on {comPort}");
            
            // Initialize serial port
            serialPort = new SerialPort(comPort, 115200);
            try
            {
                serialPort.Open();
                Console.WriteLine("Serial port opened successfully\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening serial port: {ex.Message}");
                Console.ReadKey();
                return;
            }
            
            // Initialize hardware monitor for GPU
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };
            computer.Open();
            
            Console.WriteLine("Starting temperature monitoring...");
            Console.WriteLine("Note: Using WMI for AMD CPU temperature");
            Console.WriteLine("Press Ctrl+C to exit\n");
            
            // Main loop
            while (true)
            {
                try
                {
                    float ?cpuTemp = GetCpuTempWMI(); // Call WMI to keep it responsive
                    if (!cpuTemp.HasValue|| cpuTemp.Value <= 0)
                    {
                        cpuTemp = GetCpuTempLHM();
                    }
                    float? gpuTemp = GetGpuTemp();
                    
                    if (cpuTemp.HasValue && gpuTemp.HasValue)
                    {
                        string data = $"{cpuTemp.Value:F1},{gpuTemp.Value:F1}\n";
                        serialPort.Write(data);
                        Console.WriteLine($"CPU: {cpuTemp.Value:F1}°C  |  GPU: {gpuTemp.Value:F1}°C");
                    }
                    else if (cpuTemp.HasValue)
                    {
                        Console.WriteLine($"CPU: {cpuTemp.Value:F1}°C  |  GPU: Waiting...");
                    }
                    else if (gpuTemp.HasValue)
                    {
                        Console.WriteLine($"CPU: Waiting...  |  GPU: {gpuTemp.Value:F1}°C");
                    }
                    else
                    {
                        Console.WriteLine("Waiting for sensor data...");
                    }
                    
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void KillRunningInstances()
        {
            // Get the name of the current executable (without .exe)
            Process current = Process.GetCurrentProcess();
            
            // Find all other processes with the same name
            Process[] processes = Process.GetProcessesByName(current.ProcessName);

            foreach (Process p in processes)
            {
                // Make sure we don't commit suicide (don't kill the current process)
                if (p.Id != current.Id)
                {
                    try
                    {
                        p.Kill();
                        // Note: Since this is a WinExe, these WriteLines won't show 
                        // in the console, but it's good practice for debugging.
                    }
                    catch
                    {
                        // Ignore permission errors if any
                    }
                }
            }
        }
        
        static string? FindPicoPort()
        {
            string[] ports = SerialPort.GetPortNames();
            
            if (ports.Length == 1)
                return ports[0];
            
            if (ports.Length > 1)
            {
                Console.WriteLine("Multiple COM ports found:");
                for (int i = 0; i < ports.Length; i++)
                {
                    Console.WriteLine($"{i + 1}. {ports[i]}");
                }
                Console.Write("\nEnter port number (or press Enter for COM3): ");
                string? input = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input))
                    return "COM3";
                
                if (int.TryParse(input, out int choice) && choice > 0 && choice <= ports.Length)
                    return ports[choice - 1];
            }
            
            return ports.Length > 0 ? ports[0] : null;
        }
        
        static float? GetCpuTempLHM()
        {
            if (computer == null) return null;
            
            foreach (var hardware in computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            // Accept any temperature sensor that has a value
                            if (sensor.Name.Contains("Tctl") || 
                                sensor.Name.Contains("Tdie") || 
                                sensor.Name.Contains("Package") ||
                                sensor.Name.Contains("Average") ||
                                sensor.Name.Contains("Core"))
                            {
                                return sensor.Value;
                            }
                        }
                    }
                }
            }
            return null;
        }

    static float? GetCpuTempWMI()
    {
        try
        {
            // Try the standard Thermal Zone information
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                // Values are usually in decikelvins
                double temp = Convert.ToDouble(obj["CurrentTemperature"]);
                float celsius = (float)((temp - 2732) / 10.0);
                if (celsius > 0) return celsius;
            }
        }
        catch 
        { 
            // WMI often throws exceptions on unsupported hardware, just ignore it.
        }
        return null;
    }
        
        static float? GetGpuTemp()
        {
            if (computer == null) return null;
            
            foreach (var hardware in computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || 
                    hardware.HardwareType == HardwareType.GpuAmd ||
                    hardware.HardwareType == HardwareType.GpuIntel)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            return sensor.Value;
                        }
                    }
                }
            }
            return null;
        }
    }
}