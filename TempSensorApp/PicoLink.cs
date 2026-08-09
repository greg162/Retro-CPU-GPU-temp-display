using System;
using System.IO.Ports;

namespace TempMonitor
{
    /// <summary>Why <see cref="PicoLink.Connect"/> could not produce a link.</summary>
    public enum PicoLinkFailure
    {
        /// <summary>Connected successfully.</summary>
        None,

        /// <summary>No serial ports were present at all.</summary>
        NoPortFound,

        /// <summary>The port is held by something else — almost always another copy of this app.</summary>
        PortInUse,

        /// <summary>The port could not be opened for some other reason.</summary>
        OpenFailed
    }

    /// <summary>
    /// Outcome of a connection attempt. Carries the facts about a failure rather than a message,
    /// so how it gets reported to the user stays a decision for the caller.
    /// </summary>
    /// <param name="Link">The open link, or null when the attempt failed.</param>
    /// <param name="Failure">Why it failed, or <see cref="PicoLinkFailure.None"/> on success.</param>
    /// <param name="PortName">The port that was tried, or null when no port was found.</param>
    /// <param name="ErrorDetail">Underlying error text, set only for <see cref="PicoLinkFailure.OpenFailed"/>.</param>
    public readonly record struct PicoLinkResult(
        PicoLink? Link,
        PicoLinkFailure Failure,
        string? PortName,
        string? ErrorDetail)
    {
        /// <summary>True when <see cref="Link"/> is usable.</summary>
        public bool Connected => Link is not null;
    }

    /// <summary>
    /// The serial link to the Pico: port discovery, the open port, and the wire format the
    /// firmware in <c>PicoDisplayApp/main.py</c> parses.
    /// </summary>
    public sealed class PicoLink : IDisposable
    {
        /// <summary>Baud rate the Pico firmware expects.</summary>
        public const int BaudRate = 115200;

        private readonly SerialPort port;

        private PicoLink(SerialPort port) => this.port = port;

        /// <summary>Name of the port in use, e.g. COM3.</summary>
        public string PortName => port.PortName;

        /// <summary>
        /// Finds the Pico and opens the port.
        /// </summary>
        /// <returns>A result holding the link on success, or the reason it failed.</returns>
        public static PicoLinkResult Connect()
        {
            string? portName = FindPicoPort();
            if (portName == null)
            {
                return new PicoLinkResult(null, PicoLinkFailure.NoPortFound, null, null);
            }

            var port = new SerialPort(portName, BaudRate);
            try
            {
                port.Open();
                return new PicoLinkResult(new PicoLink(port), PicoLinkFailure.None, portName, null);
            }
            catch (UnauthorizedAccessException)
            {
                port.Dispose();
                return new PicoLinkResult(null, PicoLinkFailure.PortInUse, portName, null);
            }
            catch (Exception ex)
            {
                port.Dispose();
                return new PicoLinkResult(null, PicoLinkFailure.OpenFailed, portName, ex.Message);
            }
        }

        /// <summary>
        /// Sends one reading as <c>&lt;cpu&gt;,&lt;gpu&gt;\n</c>.
        /// </summary>
        /// <param name="reading">The reading to send.</param>
        /// <returns>True on success; false when the link is no longer usable.</returns>
        public bool Send(TemperatureReading reading)
        {
            if (!port.IsOpen) return false;

            try
            {
                // Sent every tick, even when a sensor is unavailable. The firmware treats 0.0 as
                // "no reading" and shows dashes; staying silent instead would trip its 10-second
                // timeout and put E-20 on both displays.
                port.Write($"{reading.Cpu ?? 0f:F1},{reading.Gpu ?? 0f:F1}\n");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Heuristic to find the COM port used by the Pico.
        /// </summary>
        /// <returns>The COM port name (e.g., COM3) or null if not found.</returns>
        private static string? FindPicoPort()
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

        /// <summary>Closes and releases the serial port.</summary>
        public void Dispose()
        {
            port.Close();
            port.Dispose();
        }
    }
}
