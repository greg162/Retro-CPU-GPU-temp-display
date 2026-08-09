using System;
using System.Drawing;
using System.Windows.Forms;

namespace TempMonitor
{
    /// <summary>
    /// Everything the user actually sees: the tray icon, its tooltip and its context menu.
    /// Owns the generated icon and the cache that keeps it from being redrawn when the reading
    /// has not visibly changed.
    /// </summary>
    public sealed class TrayPresenter : IDisposable
    {
        /// <summary>Menu text shown when there is no CPU reading to display.</summary>
        private const string NoCpuReading = "CPU: --.- °C";

        /// <summary>Menu text shown when there is no GPU reading to display.</summary>
        private const string NoGpuReading = "GPU: --.- °C";

        /// <summary>
        /// Edge length used for the generated tray icon. Taken from the system rather than
        /// hard-coded to 16 so the digits stay sharp on scaled displays.
        /// </summary>
        private readonly int iconSize = SystemInformation.SmallIconSize.Width;

        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly ToolStripMenuItem statusMenuItem;
        private readonly ToolStripMenuItem cpuMenuItem;
        private readonly ToolStripMenuItem gpuMenuItem;

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
        /// Builds the tray icon and its menu, and makes it visible.
        /// </summary>
        /// <param name="onExit">Handler invoked when the user picks Exit.</param>
        public TrayPresenter(EventHandler onExit)
        {
            statusMenuItem = new ToolStripMenuItem();
            cpuMenuItem = new ToolStripMenuItem(NoCpuReading);
            gpuMenuItem = new ToolStripMenuItem(NoGpuReading);
            SetStatus("Initializing...");

            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(statusMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(cpuMenuItem);
            contextMenu.Items.Add(gpuMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, onExit);

            // The retro readout stands in until the first reading arrives, so the tray never
            // shows a stale or borrowed system icon.
            currentIcon = TrayIconRenderer.CreateBrandIcon(iconSize);

            notifyIcon = new NotifyIcon()
            {
                Icon = currentIcon,
                ContextMenuStrip = contextMenu,
                Text = "CPU/GPU Temp Monitor",
                Visible = true
            };
        }

        /// <summary>
        /// Updates the status line at the top of the menu.
        /// </summary>
        /// <param name="status">Status text, shown after a "Status: " prefix.</param>
        public void SetStatus(string status)
        {
            statusMenuItem.Text = $"Status: {status}";
        }

        /// <summary>
        /// Shows a reading across all three readouts: menu, tooltip and icon.
        /// </summary>
        /// <param name="reading">The reading to display.</param>
        public void ShowReading(TemperatureReading reading)
        {
            cpuMenuItem.Text = reading.Cpu.HasValue ? $"CPU: {reading.Cpu.Value:F1}°C" : NoCpuReading;
            gpuMenuItem.Text = reading.Gpu.HasValue ? $"GPU: {reading.Gpu.Value:F1}°C" : NoGpuReading;

            string cpuText = reading.Cpu.HasValue ? $"{reading.Cpu.Value:F1}" : "--.-";
            string gpuText = reading.Gpu.HasValue ? $"{reading.Gpu.Value:F1}" : "--.-";
            notifyIcon.Text = $"CPU:{cpuText} | GPU:{gpuText}";

            UpdateIcon(reading.Cpu);
        }

        /// <summary>
        /// Blanks every readout — tray icon, tooltip and menu — so a halted monitor cannot leave
        /// a plausible-looking temperature on display. The icon is the primary readout, so
        /// leaving the last good value there would actively mislead.
        /// </summary>
        /// <param name="reason">Short description of why monitoring stopped, shown to the user.</param>
        public void ShowStopped(string reason)
        {
            SetStatus(reason);
            cpuMenuItem.Text = NoCpuReading;
            gpuMenuItem.Text = NoGpuReading;
            notifyIcon.Text = $"Stopped: {reason}";
            UpdateIcon(null);
        }

        /// <summary>
        /// Redraws the tray icon for the current CPU reading, skipping the work when the whole
        /// degrees have not changed. The previous icon is disposed once it is no longer in use,
        /// as each generated icon holds an unmanaged handle.
        /// </summary>
        /// <param name="cpuTemp">The CPU reading, or null when unavailable.</param>
        private void UpdateIcon(float? cpuTemp)
        {
            int? reading = TrayIconRenderer.Quantize(cpuTemp);
            if (hasDrawnReading && reading == lastDrawnReading) return;

            Icon? previous = currentIcon;
            currentIcon = TrayIconRenderer.CreateTemperatureIcon(cpuTemp, iconSize);
            notifyIcon.Icon = currentIcon;
            previous?.Dispose();

            lastDrawnReading = reading;
            hasDrawnReading = true;
        }

        /// <summary>Hides and releases the tray icon, its menu and the generated icon.</summary>
        public void Dispose()
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            contextMenu.Dispose();

            // Disposed after the NotifyIcon so the tray is never left pointing at a
            // released icon handle.
            currentIcon?.Dispose();
        }
    }
}
