using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TempMonitor
{
    /// <summary>
    /// Draws the tray icon: the temperature as 7-segment digits on a dark bezel, tinted by
    /// how hot things are. Deliberately mirrors the look of the TM1637 displays the Pico
    /// drives, so the tray and the hardware read the same way.
    /// </summary>
    public static class TrayIconRenderer
    {
        /// <summary>
        /// Segment bits per digit, in the same order the Pico firmware uses:
        /// bit 0 = a (top), 1 = b (upper right), 2 = c (lower right), 3 = d (bottom),
        /// 4 = e (lower left), 5 = f (upper left), 6 = g (middle).
        /// Kept identical to DIGITS in PicoDisplayApp/main.py.
        /// </summary>
        private static readonly byte[] DigitSegments =
            { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        private const byte DashSegments = 0x40; // middle bar only

        // Heat bands. Below Warm is healthy, Hot and above wants attention.
        private const float WarmThreshold = 60f;
        private const float HotThreshold = 80f;

        private static readonly Color CoolColor = Color.FromArgb(255, 43, 224, 106);
        private static readonly Color WarmColor = Color.FromArgb(255, 255, 176, 32);
        private static readonly Color HotColor = Color.FromArgb(255, 255, 59, 48);
        private static readonly Color UnknownColor = Color.FromArgb(255, 130, 140, 150);

        /// <summary>Retro LED red, used for the static application icon.</summary>
        private static readonly Color BrandColor = Color.FromArgb(255, 255, 45, 26);

        private static readonly Color BezelFill = Color.FromArgb(240, 18, 20, 24);
        private static readonly Color BezelEdge = Color.FromArgb(255, 72, 78, 88);

        /// <summary>Alpha applied to segments that are off, giving the ghosting a real LED has.</summary>
        private const int UnlitAlpha = 40;

        /// <summary>
        /// Builds the tray icon for a temperature reading.
        /// </summary>
        /// <param name="celsius">The reading, or null when no sensor value is available.</param>
        /// <param name="size">Icon edge length in pixels.</param>
        /// <returns>A managed icon the caller owns and must dispose.</returns>
        public static Icon CreateTemperatureIcon(float? celsius, int size)
        {
            int? reading = Quantize(celsius);
            return CreateIcon(FormatReading(reading), ColorFor(reading), size);
        }

        /// <summary>
        /// Rounds a reading to the whole degrees the icon actually shows, or null when there is
        /// nothing to display. Appearance depends on nothing else, so callers can cache on this
        /// value alone and only redraw when it changes.
        /// </summary>
        /// <param name="celsius">The reading, or null when unavailable.</param>
        /// <returns>Whole degrees clamped to 0-999, or null.</returns>
        public static int? Quantize(float? celsius)
        {
            if (!celsius.HasValue) return null;

            int rounded = (int)Math.Round(celsius.Value);
            if (rounded < 0) return null;
            return Math.Min(rounded, 999);
        }

        /// <summary>
        /// Renders the readout for a temperature to a bitmap. Shares the whole appearance path
        /// with <see cref="CreateTemperatureIcon"/>, so preview tooling cannot drift from what
        /// the tray shows.
        /// </summary>
        /// <param name="celsius">The reading, or null when unavailable.</param>
        /// <param name="size">Bitmap edge length in pixels.</param>
        /// <returns>A new bitmap the caller owns and must dispose.</returns>
        public static Bitmap RenderTemperatureBitmap(float? celsius, int size)
        {
            int? reading = Quantize(celsius);
            return RenderBitmap(FormatReading(reading), ColorFor(reading), size);
        }

        /// <summary>
        /// Builds the static application icon — a fixed readout in retro LED red, used for the
        /// .exe and as the tray icon before the first sensor reading arrives.
        /// </summary>
        /// <param name="size">Icon edge length in pixels.</param>
        /// <returns>A managed icon the caller owns and must dispose.</returns>
        public static Icon CreateBrandIcon(int size)
        {
            return CreateIcon("42", BrandColor, size);
        }

        /// <summary>
        /// Renders a readout to a bitmap. Exposed so the icon generator can write .ico files
        /// from the same geometry the running app uses.
        /// </summary>
        /// <param name="text">Digits and dashes to show.</param>
        /// <param name="color">Colour of the lit segments.</param>
        /// <param name="size">Bitmap edge length in pixels.</param>
        /// <returns>A new bitmap the caller owns and must dispose.</returns>
        public static Bitmap RenderBitmap(string text, Color color, int size)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                DrawBezel(g, size);
                DrawReadout(g, text, color, size);
            }
            return bitmap;
        }

        /// <summary>
        /// Renders the brand readout at every size Windows asks for and returns a complete
        /// .ico file. Used by the icon generator, not at runtime.
        /// </summary>
        /// <param name="sizes">Edge lengths to include, e.g. 16, 32, 48, 256.</param>
        /// <returns>The bytes of a PNG-compressed .ico file.</returns>
        public static byte[] BuildBrandIcoFile(params int[] sizes)
        {
            var frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                using var bitmap = RenderBitmap("42", BrandColor, size);
                using var buffer = new MemoryStream();
                bitmap.Save(buffer, ImageFormat.Png);
                frames.Add(buffer.ToArray());
            }
            return PackIco(sizes, frames);
        }

        /// <summary>
        /// Chooses the readout text: two digits normally, three when the CPU is past 100 °C,
        /// dashes when there is nothing to show.
        /// </summary>
        /// <param name="reading">Whole degrees from <see cref="Quantize"/>, or null.</param>
        /// <returns>Text made only of digits and dashes.</returns>
        private static string FormatReading(int? reading)
        {
            if (!reading.HasValue) return "--";
            return reading.Value < 10 ? "0" + reading.Value : reading.Value.ToString();
        }

        /// <summary>
        /// Maps a reading to its heat band colour.
        /// </summary>
        /// <param name="reading">Whole degrees from <see cref="Quantize"/>, or null.</param>
        /// <returns>The colour for the lit segments.</returns>
        private static Color ColorFor(int? reading)
        {
            if (!reading.HasValue) return UnknownColor;
            if (reading.Value >= HotThreshold) return HotColor;
            if (reading.Value >= WarmThreshold) return WarmColor;
            return CoolColor;
        }

        /// <summary>
        /// Converts a bitmap into a managed icon, releasing the unmanaged HICON that
        /// <see cref="Bitmap.GetHicon"/> hands back so repeated redraws cannot leak handles.
        /// </summary>
        /// <param name="text">Digits and dashes to show.</param>
        /// <param name="color">Colour of the lit segments.</param>
        /// <param name="size">Icon edge length in pixels.</param>
        /// <returns>A managed icon the caller owns and must dispose.</returns>
        private static Icon CreateIcon(string text, Color color, int size)
        {
            using var bitmap = RenderBitmap(text, color, size);
            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using var unmanaged = Icon.FromHandle(hIcon);
                return (Icon)unmanaged.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        /// <summary>
        /// Draws the dark rounded panel the digits sit on. It guarantees contrast on both light
        /// and dark taskbars, which bare coloured digits would not.
        /// </summary>
        /// <param name="g">Target surface.</param>
        /// <param name="size">Icon edge length in pixels.</param>
        private static void DrawBezel(Graphics g, int size)
        {
            float inset = Math.Max(0.5f, size * 0.03f);
            var bounds = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
            float radius = size * 0.2f;

            using var panel = RoundedRect(bounds, radius);
            using var fill = new SolidBrush(BezelFill);
            g.FillPath(fill, panel);

            // Hairline edge; skip it on tiny icons where it would eat the digits.
            if (size >= 24)
            {
                using var pen = new Pen(BezelEdge, Math.Max(1f, size * 0.03f));
                g.DrawPath(pen, panel);
            }
        }

        /// <summary>
        /// Lays out the glyph cells inside the bezel and draws each one.
        /// </summary>
        /// <param name="g">Target surface.</param>
        /// <param name="text">Digits and dashes to show.</param>
        /// <param name="color">Colour of the lit segments.</param>
        /// <param name="size">Icon edge length in pixels.</param>
        private static void DrawReadout(Graphics g, string text, Color color, int size)
        {
            if (text.Length == 0) return;

            float margin = size * 0.16f;
            float available = size - 2 * margin;
            float gap = available * 0.10f / text.Length;
            float cellWidth = (available - gap * (text.Length - 1)) / text.Length;
            float cellHeight = size * 0.62f;
            float top = (size - cellHeight) / 2f;
            // Segment thickness. Held to a whole pixel minimum so 16 px icons stay crisp.
            float thickness = Math.Max(1.4f, Math.Min(cellWidth * 0.26f, cellHeight * 0.14f));

            using var lit = new SolidBrush(color);
            using var unlit = new SolidBrush(Color.FromArgb(UnlitAlpha, color));

            for (int i = 0; i < text.Length; i++)
            {
                var cell = new RectangleF(margin + i * (cellWidth + gap), top, cellWidth, cellHeight);
                DrawGlyph(g, cell, thickness, SegmentsFor(text[i]), lit, unlit);
            }
        }

        /// <summary>
        /// Looks up the segment bits for a character.
        /// </summary>
        /// <param name="c">A digit or '-'.</param>
        /// <returns>Segment bits, or 0 (blank) for anything unrecognised.</returns>
        private static byte SegmentsFor(char c)
        {
            if (c >= '0' && c <= '9') return DigitSegments[c - '0'];
            if (c == '-') return DashSegments;
            return 0;
        }

        /// <summary>
        /// Draws one 7-segment glyph, painting the off segments faintly for the LED look.
        /// </summary>
        /// <param name="g">Target surface.</param>
        /// <param name="cell">Bounds of this glyph.</param>
        /// <param name="thickness">Segment bar thickness in pixels.</param>
        /// <param name="segments">Which segments are lit.</param>
        /// <param name="lit">Brush for lit segments.</param>
        /// <param name="unlit">Brush for unlit segments.</param>
        private static void DrawGlyph(Graphics g, RectangleF cell, float thickness, byte segments,
                                      Brush lit, Brush unlit)
        {
            float half = thickness / 2f;
            float left = cell.Left + half;
            float right = cell.Right - half;
            float topY = cell.Top + half;
            float midY = cell.Top + cell.Height / 2f;
            float bottomY = cell.Bottom - half;

            DrawSegment(g, Horizontal(left, right, topY, half), segments, 0, lit, unlit);
            DrawSegment(g, Vertical(right, topY, midY, half), segments, 1, lit, unlit);
            DrawSegment(g, Vertical(right, midY, bottomY, half), segments, 2, lit, unlit);
            DrawSegment(g, Horizontal(left, right, bottomY, half), segments, 3, lit, unlit);
            DrawSegment(g, Vertical(left, midY, bottomY, half), segments, 4, lit, unlit);
            DrawSegment(g, Vertical(left, topY, midY, half), segments, 5, lit, unlit);
            DrawSegment(g, Horizontal(left, right, midY, half), segments, 6, lit, unlit);
        }

        /// <summary>
        /// Fills one segment with the lit or unlit brush depending on its bit.
        /// </summary>
        /// <param name="g">Target surface.</param>
        /// <param name="shape">The segment outline.</param>
        /// <param name="segments">Which segments are lit.</param>
        /// <param name="bit">Bit index of this segment.</param>
        /// <param name="lit">Brush for lit segments.</param>
        /// <param name="unlit">Brush for unlit segments.</param>
        private static void DrawSegment(Graphics g, PointF[] shape, byte segments, int bit,
                                        Brush lit, Brush unlit)
        {
            g.FillPolygon(((segments >> bit) & 1) == 1 ? lit : unlit, shape);
        }

        /// <summary>
        /// Builds a horizontal segment: a bar with mitred ends so neighbouring segments meet
        /// at a diagonal, the way a real 7-segment display is cut.
        /// </summary>
        /// <param name="left">Left end.</param>
        /// <param name="right">Right end.</param>
        /// <param name="y">Centre line.</param>
        /// <param name="half">Half the bar thickness.</param>
        /// <returns>The segment outline.</returns>
        private static PointF[] Horizontal(float left, float right, float y, float half)
        {
            return new[]
            {
                new PointF(left, y),
                new PointF(left + half, y - half),
                new PointF(right - half, y - half),
                new PointF(right, y),
                new PointF(right - half, y + half),
                new PointF(left + half, y + half)
            };
        }

        /// <summary>
        /// Builds a vertical segment, mitred to match <see cref="Horizontal"/>.
        /// </summary>
        /// <param name="x">Centre line.</param>
        /// <param name="top">Top end.</param>
        /// <param name="bottom">Bottom end.</param>
        /// <param name="half">Half the bar thickness.</param>
        /// <returns>The segment outline.</returns>
        private static PointF[] Vertical(float x, float top, float bottom, float half)
        {
            return new[]
            {
                new PointF(x, top),
                new PointF(x + half, top + half),
                new PointF(x + half, bottom - half),
                new PointF(x, bottom),
                new PointF(x - half, bottom - half),
                new PointF(x - half, top + half)
            };
        }

        /// <summary>
        /// Builds a rounded rectangle path.
        /// </summary>
        /// <param name="bounds">Outer bounds.</param>
        /// <param name="radius">Corner radius.</param>
        /// <returns>The path, which the caller must dispose.</returns>
        private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Assembles PNG frames into an .ico container. Windows Vista and later read
        /// PNG-compressed icon entries, which keeps the 256 px frame small.
        /// </summary>
        /// <param name="sizes">Edge length of each frame, in the same order as <paramref name="frames"/>.</param>
        /// <param name="frames">PNG bytes per frame.</param>
        /// <returns>The bytes of a complete .ico file.</returns>
        private static byte[] PackIco(int[] sizes, List<byte[]> frames)
        {
            const int headerSize = 6;
            const int entrySize = 16;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write((ushort)0);              // reserved
            writer.Write((ushort)1);              // type: icon
            writer.Write((ushort)frames.Count);

            int offset = headerSize + entrySize * frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                // 256 is encoded as 0 in the single-byte width/height fields.
                writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                writer.Write((byte)0);            // palette entries
                writer.Write((byte)0);            // reserved
                writer.Write((ushort)1);          // colour planes
                writer.Write((ushort)32);         // bits per pixel
                writer.Write(frames[i].Length);
                writer.Write(offset);
                offset += frames[i].Length;
            }

            foreach (byte[] frame in frames) writer.Write(frame);

            writer.Flush();
            return stream.ToArray();
        }

        // DllImport rather than LibraryImport: the source-generated variant needs
        // AllowUnsafeBlocks turned on project-wide, which is a poor trade for one call.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
