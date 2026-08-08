using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TempMonitor;

// Writes TempSensorApp/app.ico, plus a magnified preview sheet for checking how the readout
// survives being squeezed into 16 px. Run it after changing colours or segment geometry in
// TrayIconRenderer:
//
//   dotnet run --project tools/IconGen
//
// Output goes to TempSensorApp/ by default; pass a directory to write elsewhere.

string outDir = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TempSensorApp");
outDir = Path.GetFullPath(outDir);

if (!Directory.Exists(outDir))
{
    Console.Error.WriteLine($"Output directory not found: {outDir}");
    return 1;
}

// The sizes Windows picks between for the taskbar, Alt-Tab, Explorer and the shortcut icon.
int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };

string icoPath = Path.Combine(outDir, "app.ico");
File.WriteAllBytes(icoPath, TrayIconRenderer.BuildBrandIcoFile(sizes));
Console.WriteLine($"wrote {icoPath} ({sizes.Length} frames)");

string previewPath = Path.Combine(outDir, "..", "media", "tray-icon-preview.png");
previewPath = Path.GetFullPath(previewPath);
if (Directory.Exists(Path.GetDirectoryName(previewPath)!))
{
    WritePreview(previewPath);
    Console.WriteLine($"wrote {previewPath}");
}

return 0;

// Renders each sample at true tray size then magnifies with nearest-neighbour, so the sheet
// shows exactly the pixels the taskbar receives rather than a flattering smooth upscale.
static void WritePreview(string path)
{
    (string Label, float? Temp)[] samples =
    {
        ("07 °C", 7f),
        ("42 °C", 42f),
        ("68 °C", 68f),
        ("91 °C", 91f),
        ("103 °C", 103f),
        ("no data", null),
    };

    const int Native = 16;
    const int Zoom = 8;
    const int Cell = Native * Zoom;
    const int Pad = 12;

    using var sheet = new Bitmap(samples.Length * (Cell + Pad) + Pad, Cell + 2 * Pad + 24);
    using (var g = Graphics.FromImage(sheet))
    {
        g.Clear(Color.FromArgb(255, 245, 245, 247));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using var font = new Font("Segoe UI", 9f);
        using var text = new SolidBrush(Color.FromArgb(255, 40, 40, 45));

        for (int i = 0; i < samples.Length; i++)
        {
            int x = Pad + i * (Cell + Pad);
            using var icon = TrayIconRenderer.RenderTemperatureBitmap(samples[i].Temp, Native);
            g.DrawImage(icon, new Rectangle(x, Pad, Cell, Cell));
            g.DrawString(samples[i].Label, font, text, x, Pad + Cell + 4);
        }
    }

    sheet.Save(path, ImageFormat.Png);
}
