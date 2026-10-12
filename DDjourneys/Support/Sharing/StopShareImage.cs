using DDjourneys.Core.Contract;
using SkiaSharp;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// Draws a QR code as a PNG: black modules on white, whatever the theme is - a QR code is a printed
/// thing, like the plan pages that stay light paper in dark mode. The quiet zone comes with the matrix
/// (the engine adds four white modules on every side); the modules are painted as whole pixels, the
/// module size a whole number, so every module is equally wide and the code stays crisp at any zoom.
/// </summary>
public static class StopShareImage
{
	/// <summary>Drawn at about twice the size the share sheet shows it, so it is sharp on dense screens.</summary>
	private const int TargetPixels = 720;

	/// <summary>Renders the code and writes it to a PNG in the cache directory; returns its path.</summary>
	public static string Render(QrMatrix qr)
	{
		ArgumentNullException.ThrowIfNull(qr);

		string folder = Path.Combine(FileSystem.CacheDirectory, "share");
		Directory.CreateDirectory(folder);

		string path = Path.Combine(folder, "stop-qr.png");
		int module = Math.Max(1, TargetPixels / qr.Size);
		int side = module * qr.Size;

		var info = new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);

		using SKSurface surface = SKSurface.Create(info);

		surface.Canvas.Clear(SKColors.White);

		using var dark = new SKPaint { Color = SKColors.Black };

		for (int y = 0; y < qr.Size; y++)
		{
			for (int x = 0; x < qr.Size; x++)
			{
				if (qr.Modules[y, x])
				{
					surface.Canvas.DrawRect(x * module, y * module, module, module, dark);
				}
			}
		}

		using SKImage image = surface.Snapshot();
		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

		using FileStream stream = File.Create(path);

		data.SaveTo(stream);

		return path;
	}
}
