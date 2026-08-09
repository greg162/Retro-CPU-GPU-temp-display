using System;
using System.Diagnostics;
using System.Windows.Forms;

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
    /// Coordinates the three halves of the app: it polls <see cref="TemperatureMonitor"/> on a
    /// timer, shows the result through <see cref="TrayPresenter"/> and pushes it down
    /// <see cref="PicoLink"/>. Deliberately holds no sensor, drawing or serial logic itself.
    /// </summary>
    public class TempSensorApplicationContext : ApplicationContext
    {
        /// <summary>How often the sensors are polled and the Pico updated, in milliseconds.</summary>
        private const int PollIntervalMs = 2000;

        private readonly TrayPresenter tray;
        private readonly TemperatureMonitor monitor;
        private PicoLink? pico;
        private System.Windows.Forms.Timer? monitorTimer;

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
            tray = new TrayPresenter(OnExit);
            monitor = TemperatureMonitor.CreateDefault();
            ConnectToPico();
        }

        /// <summary>
        /// Attempts to connect to the Pico, and starts polling once connected.
        /// </summary>
        private void ConnectToPico()
        {
            PicoLinkResult result = PicoLink.Connect();
            if (!result.Connected)
            {
                ReportConnectionFailure(result);
                StartupFailed = true;
                return;
            }

            pico = result.Link!;
            tray.SetStatus($"Connected to {pico.PortName}");

            // Start timer only after successful connection.
            monitorTimer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
            monitorTimer.Tick += MonitorTimer_Tick;
            monitorTimer.Start();
        }

        /// <summary>
        /// Turns a failed connection attempt into a status line and a message box. Kept here
        /// rather than in <see cref="PicoLink"/> so the transport carries no UI.
        /// </summary>
        /// <param name="result">The failed result.</param>
        private void ReportConnectionFailure(PicoLinkResult result)
        {
            string status;
            string message;

            switch (result.Failure)
            {
                case PicoLinkFailure.NoPortFound:
                    status = "Pico not found!";
                    message = "Could not find Raspberry Pi Pico. Check connection.";
                    break;

                case PicoLinkFailure.PortInUse:
                    status = "Port in use";
                    message =
                        $"{result.PortName} is already in use — most likely by another running copy of this app.\n\n" +
                        "Close it (or run TempSensorApp.exe --kill) and try again.";
                    break;

                default:
                    status = "Connection failed";
                    message = $"Error opening serial port: {result.ErrorDetail}";
                    break;
            }

            tray.SetStatus(status);
            MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Timer event that reads temperatures and updates the UI and Serial output.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            TemperatureReading reading = monitor.Read();
            tray.ShowReading(reading);

            if (pico != null && !pico.Send(reading))
            {
                StopMonitoring("Write error");
            }
        }

        /// <summary>
        /// Stops the monitor timer and blanks the readouts. Nothing restarts the timer, so this
        /// is terminal until the app is restarted.
        /// </summary>
        /// <param name="reason">Short description of why monitoring stopped, shown to the user.</param>
        private void StopMonitoring(string reason)
        {
            monitorTimer?.Stop();
            tray.ShowStopped(reason);
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
        /// Releases the timer, serial port, hardware monitor and tray icon. Runs both on a
        /// normal exit and when startup fails before the message loop starts.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                monitorTimer?.Stop();
                monitorTimer?.Dispose();
                pico?.Dispose();
                monitor.Dispose();
                tray.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
