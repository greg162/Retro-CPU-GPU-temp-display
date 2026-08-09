using System;
using LibreHardwareMonitor.Hardware;

namespace TempMonitor
{
    /// <summary>
    /// A CPU and GPU reading taken at the same moment. Either half is null when no provider
    /// could supply a value.
    /// </summary>
    /// <param name="Cpu">CPU temperature in °C, or null when unavailable.</param>
    /// <param name="Gpu">GPU temperature in °C, or null when unavailable.</param>
    public readonly record struct TemperatureReading(float? Cpu, float? Gpu)
    {
        /// <summary>A reading with neither sensor available.</summary>
        public static TemperatureReading None => new(null, null);
    }

    /// <summary>
    /// Owns the sensor stack: the LibreHardwareMonitor <see cref="Computer"/> handle and the
    /// providers that read through it. Because the handle is shared by several providers but
    /// outlives none of them, one object creates it, hands it out and closes it.
    /// </summary>
    public sealed class TemperatureMonitor : IDisposable
    {
        private readonly Computer computer;
        private readonly TemperatureFinder finder;
        private bool disposed;

        private TemperatureMonitor(Computer computer, TemperatureFinder finder)
        {
            this.computer = computer;
            this.finder = finder;
        }

        /// <summary>
        /// Builds the monitor with the standard providers. Registration order is the fallback
        /// order <see cref="TemperatureFinder"/> walks, so it is load-bearing rather than
        /// incidental: WMI is asked for the CPU before LibreHardwareMonitor.
        /// </summary>
        /// <returns>An open monitor the caller owns and must dispose.</returns>
        public static TemperatureMonitor CreateDefault()
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };
            computer.Open();

            var finder = new TemperatureFinder();
            finder.RegisterProvider(new WmiCpuProvider());         // Strategy 1: WMI
            finder.RegisterProvider(new LhmCpuProvider(computer)); // Strategy 2: LibreHardwareMonitor
            finder.RegisterProvider(new LhmGpuProvider(computer)); // Strategy 3: GPU via LHM

            return new TemperatureMonitor(computer, finder);
        }

        /// <summary>
        /// Takes a reading from both sensors.
        /// </summary>
        /// <returns>The reading, with null for whichever sensor could not be read.</returns>
        public TemperatureReading Read()
        {
            try
            {
                return new TemperatureReading(
                    finder.GetTemperature(TemperatureType.Cpu),
                    finder.GetTemperature(TemperatureType.Gpu));
            }
            catch (Exception)
            {
                // A sensor backend can fail transiently — LibreHardwareMonitor's Update() talks
                // to hardware. Report "no reading" and let the caller keep polling, so a blip
                // degrades to dashes and recovers on its own rather than freezing the last
                // temperature on screen, where it would be indistinguishable from a live one.
                return TemperatureReading.None;
            }
        }

        /// <summary>Closes the hardware handle. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            computer.Close();
        }
    }
}
