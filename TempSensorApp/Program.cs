using System;
using System.IO.Ports;
using System.Management;
using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing; // Required for Icon

namespace TempMonitor
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --- KILL SWITCH LOGIC START ---
            if (args.Length > 0 && (args[0] == "--kill" || args[0] == "-k"))
            {
                KillRunningInstances();
                return; 
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TempSensorApplicationContext());
        }

        static void KillRunningInstances()
        {
            Process current = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(current.ProcessName);
            foreach (Process p in processes)
            {
                if (p.Id != current.Id)
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }
    }

    public class TempSensorApplicationContext : ApplicationContext
    {
        private SerialPort? serialPort;
        private Computer? computer;
        private NotifyIcon? notifyIcon;
        private System.Windows.Forms.Timer? monitorTimer;

        private ToolStripMenuItem? statusMenuItem;
        private ToolStripMenuItem? cpuMenuItem;
        private ToolStripMenuItem? gpuMenuItem;

        public TempSensorApplicationContext()
        {
            InitializeContext();
            InitializeHardware();
            ConnectToPico();
        }

        private void InitializeContext()
        {
            statusMenuItem = new ToolStripMenuItem("Status: Initializing...");
            cpuMenuItem = new ToolStripMenuItem("CPU: --.- °C");
            gpuMenuItem = new ToolStripMenuItem("GPU: --.- °C");

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(statusMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(cpuMenuItem);
            contextMenu.Items.Add(gpuMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, OnExit);

            notifyIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Information, // Placeholder icon
                ContextMenuStrip = contextMenu,
                Text = "CPU/GPU Temp Monitor",
                Visible = true
            };
        }

        private void InitializeHardware()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };
            computer.Open();
        }

        private void ConnectToPico()
        {
            string? comPort = FindPicoPort();
            if (comPort == null)
            {
                statusMenuItem!.Text = "Status: Pico not found!";
                MessageBox.Show("Could not find Raspberry Pi Pico. Check connection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }

            serialPort = new SerialPort(comPort, 115200);
            try
            {
                serialPort.Open();
                statusMenuItem!.Text = $"Status: Connected to {comPort}";

                // Start timer only after successful connection
                monitorTimer = new System.Windows.Forms.Timer();
                monitorTimer.Interval = 2000; // 2 seconds
                monitorTimer.Tick += MonitorTimer_Tick;
                monitorTimer.Start();
            }
            catch (Exception ex)
            {
                statusMenuItem!.Text = "Status: Connection failed";
                MessageBox.Show($"Error opening serial port: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }
        
        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            float? cpuTemp = GetCpuTempWMI();
            if (!cpuTemp.HasValue || cpuTemp.Value <= 0)
            {
                cpuTemp = GetCpuTempLHM();
            }
            float? gpuTemp = GetGpuTemp();

            // Update UI
            cpuMenuItem!.Text = cpuTemp.HasValue ? $"CPU: {cpuTemp.Value:F1}°C" : "CPU: --.- °C";
            gpuMenuItem!.Text = gpuTemp.HasValue ? $"GPU: {gpuTemp.Value:F1}°C" : "GPU: --.- °C";
            notifyIcon!.Text = $"CPU:{cpuTemp.Value:F1} | GPU:{gpuTemp.Value:F1}";

            // Send to Pico
            if (cpuTemp.HasValue && gpuTemp.HasValue && serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    string data = $"{cpuTemp.Value:F1},{gpuTemp.Value:F1}\n";
                    serialPort.Write(data);
                }
                catch (Exception)
                {
                    statusMenuItem!.Text = "Status: Write error";
                    monitorTimer?.Stop();
                }
            }
        }

        private string? FindPicoPort()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length > 0)
            {
                // Simple logic: return the first port.
                // For a real app, you might have a selection UI or more robust detection.
                return ports[0]; 
            }
            return null;
        }

        private float? GetCpuTempLHM()
        {
            if (computer == null) return null;
            foreach (var hardware in computer.Hardware)
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

        private float? GetCpuTempWMI()
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

        private float? GetGpuTemp()
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
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                            return sensor.Value;
                }
            }
            return null;
        }

        private void OnExit(object? sender, EventArgs e)
        {
            // Clean up resources
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
            }
            monitorTimer?.Stop();
            serialPort?.Close();
            computer?.Close();
            Application.Exit();
        }
    }
}