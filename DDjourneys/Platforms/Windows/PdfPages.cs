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
	private sealed class WindowsPdfPage(PdfPage page) : IPdfPage
	{
		private readonly Rect _box = page.Dimensions.CropBox;

		public double Aspect => _box.Height > 0 ? _box.Width / _box.Height : 1;

		public async Task<byte[]> RenderPngAsync(
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
					SourceRect = new Rect(
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

		public void Dispose() => page.Dispose();
	}
}
