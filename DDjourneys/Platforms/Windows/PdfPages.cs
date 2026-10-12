using DDjourneys.Core.Diagnostics;
using SkiaSharp;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DDjourneys.Support;

public static partial class PdfPages
{
	public static partial async Task<IPdfPage> OpenAsync(string path, CancellationToken cancellationToken)
	{
		StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
		PdfDocument document = await PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken).ConfigureAwait(false);

		return document.PageCount < 1
			? throw new InvalidDataException("The PDF has no page.")
			: new WindowsPdfPage(document.GetPage(0));
	}

	/// <summary>
	/// The part to draw is taken from the page's crop box, the frame <c>SourceRect</c> shares its units with
	/// (Microsoft's PdfDocument sample does the same with the trim box).
	/// </summary>
	private sealed partial class WindowsPdfPage(PdfPage page) : IPdfPage
	{
		private readonly Windows.Foundation.Rect _box = page.Dimensions.CropBox;

		public double Aspect => _box.Height > 0 ? _box.Width / _box.Height : 1;

		/// <summary>
		/// What the renderer makes of a destination size: it came back 1.5 times larger than asked on a 150 % display
		/// (seen in the log: 768x768 for 512x512), so the size asked for is divided by the factor measured on the first
		/// answer, and what still differs by a pixel is resampled to the exact size.
		/// </summary>
		private static double s_scale = 1;

		public async Task<byte[]> RenderPngAsync(
			double left,
			double top,
			double width,
			double height,
			int pixelWidth,
			int pixelHeight,
			CancellationToken cancellationToken)
		{
			int askedWidth = Ask(pixelWidth);
			int askedHeight = Ask(pixelHeight);

			byte[] png = await DrawAsync(left, top, width, height, askedWidth, askedHeight, cancellationToken).ConfigureAwait(false);

			if (SizeOf(png) is not { } size)
			{
				return png;
			}

			if (size.Width == pixelWidth && size.Height == pixelHeight)
			{
				return png;
			}

			// The renderer's factor: its answer per pixel asked for (not multiplied again and again - it is a property
			// of the display, so it is SET from the answer; a sliver rounds too coarsely to tell).
			if (askedWidth >= 64 && askedHeight >= 64)
			{
				double factor = size.Width / (double)askedWidth;

				if (factor is > 0.2 and < 8 && Math.Abs(factor - s_scale) > 0.03)
				{
					s_scale = factor;
					DiagnosticLog.Write($"[Plan] the PDF renderer answers {factor:F2}x the size asked for; asking for 1/{factor:F2} from now on");

					png = await DrawAsync(left, top, width, height, Ask(pixelWidth), Ask(pixelHeight), cancellationToken).ConfigureAwait(false);
				}
			}

			return Exact(png, pixelWidth, pixelHeight);
		}

		private static int Ask(int pixels) =>
			Math.Max(1, (int)Math.Round(pixels / s_scale));

		private async Task<byte[]> DrawAsync(
			double left,
			double top,
			double width,
			double height,
			int pixelWidth,
			int pixelHeight,
			CancellationToken cancellationToken)
		{
			var options =
				new PdfPageRenderOptions
				{
					SourceRect = new Windows.Foundation.Rect(
						_box.X + left * _box.Width,
						_box.Y + top * _box.Height,
						width * _box.Width,
						height * _box.Height),
					DestinationWidth = (uint)pixelWidth,
					DestinationHeight = (uint)pixelHeight,
					BackgroundColor = Microsoft.UI.Colors.White
				};

			using var stream = new InMemoryRandomAccessStream();

			// PNG is the default encoder.
			await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken).ConfigureAwait(false);

			stream.Seek(0);

			using var output = new MemoryStream();
			using Stream input = stream.AsStreamForRead();

			await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

			return output.ToArray();
		}

		private static SKSizeI? SizeOf(byte[] png)
		{
			using var data = SKData.CreateCopy(png);
			using SKCodec? codec = SKCodec.Create(data);

			return codec?.Info.Size;
		}

		/// <summary>The picture at exactly the wanted size (a pixel of rounding is resampled, not rejected).</summary>
		private static byte[] Exact(byte[] png, int pixelWidth, int pixelHeight)
		{
			using SKBitmap? source = SKBitmap.Decode(png);

			if (source is null || (source.Width == pixelWidth && source.Height == pixelHeight))
			{
				return png;
			}

			using SKBitmap? scaled = source.Resize(new SKImageInfo(pixelWidth, pixelHeight, source.ColorType, source.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));

			if (scaled is null)
			{
				return png;
			}

			using SKData encoded = scaled.Encode(SKEncodedImageFormat.Png, 100);

			return encoded.ToArray();
		}

		public void Dispose() => page.Dispose();
	}
}
