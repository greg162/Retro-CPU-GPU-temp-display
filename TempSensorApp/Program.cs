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

            // Startup failures happen in the context's constructor, before the message
            // loop exists, so Application.Exit() cannot be used there. Check and bail out.
            var context = new TempSensorApplicationContext();
            if (context.StartupFailed)
            {
                context.Dispose();
                return;
            }

            Application.Run(context);
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

        /// <summary>Menu text shown when there is no CPU reading to display.</summary>
        private const string NoCpuReading = "CPU: --.- °C";

        /// <summary>Menu text shown when there is no GPU reading to display.</summary>
        private const string NoGpuReading = "GPU: --.- °C";

        /// <summary>
        /// Edge length used for the generated tray icon. Taken from the system rather than
        /// hard-coded to 16 so the digits stay sharp on scaled displays. Fully qualified because
        /// LibreHardwareMonitor also defines a SystemInformation.
        /// </summary>
        private readonly int trayIconSize = System.Windows.Forms.SystemInformation.SmallIconSize.Width;

        /// <summary>The icon currently shown, owned by this class and replaced on each change.</summary>
        private Icon? currentIcon;

        /// <summary>
        /// Whole degrees last drawn into the icon, used to skip redrawing when the reading has
        /// not visibly changed. Null means the icon showed dashes.
        /// </summary>
        private int? lastDrawnReading;

        /// <summary>
        /// False until the first reading has been drawn, so a null first reading still replaces
        /// the startup icon instead of matching <see cref="lastDrawnReading"/>.
        /// </summary>
        private bool hasDrawnReading;

        /// <summary>
        /// True when startup could not complete (no Pico, or the serial port could not be
        /// opened). <see cref="Program.Main"/> checks this and exits without running the
        /// message loop.
        /// </summary>
        public bool StartupFailed { get; private set; }

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
            cpuMenuItem = new ToolStripMenuItem(NoCpuReading);
            gpuMenuItem = new ToolStripMenuItem(NoGpuReading);

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(statusMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(cpuMenuItem);
            contextMenu.Items.Add(gpuMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, OnExit);

            // The retro readout stands in until the first reading arrives, so the tray never
            // shows a stale or borrowed system icon.
            currentIcon = TrayIconRenderer.CreateBrandIcon(trayIconSize);

            notifyIcon = new NotifyIcon()
            {
                Icon = currentIcon,
                ContextMenuStrip = contextMenu,
                Text = "CPU/GPU Temp Monitor",
                Visible = true
            };
        }

        /// <summary>
        /// Redraws the tray icon for the current CPU reading, skipping the work when the whole
        /// degrees have not changed. The previous icon is disposed once it is no longer in use,
        /// as each generated icon holds an unmanaged handle.
        /// </summary>
        /// <param name="cpuTemp">The CPU reading, or null when unavailable.</param>
        private void UpdateTrayIcon(float? cpuTemp)
        {
            int? reading = TrayIconRenderer.Quantize(cpuTemp);
            if (hasDrawnReading && reading == lastDrawnReading) return;

            Icon? previous = currentIcon;
            currentIcon = TrayIconRenderer.CreateTemperatureIcon(cpuTemp, trayIconSize);
            notifyIcon.Icon = currentIcon;
            previous?.Dispose();

            lastDrawnReading = reading;
            hasDrawnReading = true;
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
                StartupFailed = true;
                return;
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
            catch (UnauthorizedAccessException)
            {
                // Almost always another instance of this app still holding the port.
                statusMenuItem.Text = "Status: Port in use";
                MessageBox.Show(
                    $"{comPort} is already in use — most likely by another running copy of this app.\n\n" +
                    "Close it (or run TempSensorApp.exe --kill) and try again.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StartupFailed = true;
            }
            catch (Exception ex)
            {
                statusMenuItem.Text = "Status: Connection failed";
                MessageBox.Show($"Error opening serial port: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StartupFailed = true;
            }
        }
        
        /// <summary>
        /// Timer event that reads temperatures and updates the UI and Serial output.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            float? cpuTemp;
            float? gpuTemp;
            try
            {
                cpuTemp = temperatureFinder.GetTemperature(TemperatureType.Cpu);
                gpuTemp = temperatureFinder.GetTemperature(TemperatureType.Gpu);
            }
            catch (Exception)
            {
                // A sensor backend can fail transiently — LibreHardwareMonitor's Update() talks
                // to hardware. Report "no reading" and keep ticking, so a blip degrades to
                // dashes and recovers on its own rather than freezing the last temperature on
                // screen, where it would be indistinguishable from a live one.
                cpuTemp = null;
                gpuTemp = null;
            }

            // Update UI
            cpuMenuItem.Text = cpuTemp.HasValue ? $"CPU: {cpuTemp.Value:F1}°C" : NoCpuReading;
            gpuMenuItem.Text = gpuTemp.HasValue ? $"GPU: {gpuTemp.Value:F1}°C" : NoGpuReading;
            string cpuText = cpuTemp.HasValue ? $"{cpuTemp.Value:F1}" : "--.-";
            string gpuText = gpuTemp.HasValue ? $"{gpuTemp.Value:F1}" : "--.-";
            notifyIcon.Text = $"CPU:{cpuText} | GPU:{gpuText}";
            UpdateTrayIcon(cpuTemp);

            // Send to Pico every tick, even when a sensor is unavailable. The firmware
            // treats 0.0 as "no reading" and shows dashes; staying silent instead would
            // trip its 10-second timeout and put E-20 on both displays.
            if (serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    string data = $"{cpuTemp ?? 0f:F1},{gpuTemp ?? 0f:F1}\n";
                    serialPort.Write(data);
                }
                catch (Exception)
                {
                    StopMonitoring("Write error");
                }
            }
        }

        /// <summary>
        /// Stops the monitor timer and blanks every readout — tray icon, tooltip and menu — so a
        /// halted monitor cannot leave a plausible-looking temperature on display. The icon is the
        /// primary readout now, so leaving the last good value there would actively mislead.
        /// Nothing restarts the timer, so this is terminal until the app is restarted.
        /// </summary>
        /// <param name="reason">Short description of why monitoring stopped, shown to the user.</param>
        private void StopMonitoring(string reason)
        {
            monitorTimer?.Stop();

            statusMenuItem.Text = $"Status: {reason}";
            cpuMenuItem.Text = NoCpuReading;
            gpuMenuItem.Text = NoGpuReading;
            notifyIcon.Text = $"Stopped: {reason}";
            UpdateTrayIcon(null);
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
            Application.Exit();
        }

        /// <summary>
        /// Releases the tray icon, timer, serial port and hardware monitor. Runs both on a
        /// normal exit and when startup fails before the message loop starts.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                monitorTimer?.Stop();
                monitorTimer?.Dispose();
                serialPort?.Close();
                serialPort?.Dispose();
                computer?.Close();

                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }

                // Disposed after the NotifyIcon so the tray is never left pointing at a
                // released icon handle.
                currentIcon?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}