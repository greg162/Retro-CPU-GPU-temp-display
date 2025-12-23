using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing; // Required for Icon

namespace TempMonitor
{
    /// <summary>
    /// Main program entry point.
    /// </summary>
    class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
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

        /// <summary>
        /// Terminates other running instances of this process to ensure a clean restart.
        /// </summary>
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

    /// <summary>
    /// The main application context that manages the tray icon and background logic.
    /// </summary>
    public class TempSensorApplicationContext : ApplicationContext
    {
        private SerialPort? serialPort;
        private Computer computer = null!;
        private NotifyIcon notifyIcon = null!;
        private TemperatureFinder temperatureFinder = null!;
        private System.Windows.Forms.Timer? monitorTimer;

        private ToolStripMenuItem statusMenuItem = null!;
        private ToolStripMenuItem cpuMenuItem = null!;
        private ToolStripMenuItem gpuMenuItem = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="TempSensorApplicationContext"/> class.
        /// Sets up the UI, Hardware Monitor, and Serial connection.
        /// </summary>
        public TempSensorApplicationContext()
        {
            InitializeContext();
            InitializeHardware();
            ConnectToPico();
        }

        /// <summary>
        /// Configures the NotifyIcon and ContextMenu for the system tray.
        /// </summary>
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

        /// <summary>
        /// Initializes the LibreHardwareMonitor computer object.
        /// </summary>
        private void InitializeHardware()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };
            computer.Open();

            // Initialize TemperatureFinder and register providers
            temperatureFinder = new TemperatureFinder();
            temperatureFinder.RegisterProvider(new WmiCpuProvider()); // Strategy 1: WMI
            temperatureFinder.RegisterProvider(new LhmCpuProvider(computer)); // Strategy 2: LibreHardwareMonitor
            temperatureFinder.RegisterProvider(new LhmGpuProvider(computer)); // Strategy 3: GPU via LHM
        }

        /// <summary>
        /// Attempts to locate and connect to the Raspberry Pi Pico via Serial.
        /// </summary>
        private void ConnectToPico()
        {
            string? comPort = FindPicoPort();
            if (comPort == null)
            {
                statusMenuItem.Text = "Status: Pico not found!";
                MessageBox.Show("Could not find Raspberry Pi Pico. Check connection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }

            serialPort = new SerialPort(comPort, 115200);
            try
            {
                serialPort.Open();
                statusMenuItem.Text = $"Status: Connected to {comPort}";

                // Start timer only after successful connection
                monitorTimer = new System.Windows.Forms.Timer();
                monitorTimer.Interval = 2000; // 2 seconds
                monitorTimer.Tick += MonitorTimer_Tick;
                monitorTimer.Start();
            }
            catch (Exception ex)
            {
                statusMenuItem.Text = "Status: Connection failed";
                MessageBox.Show($"Error opening serial port: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }
        
        /// <summary>
        /// Timer event that reads temperatures and updates the UI and Serial output.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            float? cpuTemp = temperatureFinder.GetTemperature(TemperatureType.Cpu);
            float? gpuTemp = temperatureFinder.GetTemperature(TemperatureType.Gpu);

            // Update UI
            cpuMenuItem.Text = cpuTemp.HasValue ? $"CPU: {cpuTemp.Value:F1}°C" : "CPU: --.- °C";
            gpuMenuItem.Text = gpuTemp.HasValue ? $"GPU: {gpuTemp.Value:F1}°C" : "GPU: --.- °C";
            notifyIcon.Text = $"CPU:{cpuTemp.Value:F1} | GPU:{gpuTemp.Value:F1}";

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
                    statusMenuItem.Text = "Status: Write error";
                    monitorTimer?.Stop();
                }
            }
        }

        /// <summary>
        /// Heuristic to find the COM port used by the Pico.
        /// </summary>
        /// <returns>The COM port name (e.g., COM3) or null if not found.</returns>
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

        /// <summary>
        /// Cleans up resources when the application exits.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void OnExit(object? sender, EventArgs e)
        {
            // Clean up resources
            notifyIcon.Visible = false;
            monitorTimer?.Stop();
            serialPort?.Close();
            computer.Close();
            Application.Exit();
        }
    }
}