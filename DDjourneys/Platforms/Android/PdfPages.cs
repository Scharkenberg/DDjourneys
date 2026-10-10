using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;

namespace DDjourneys.Support;

public static partial class PdfPages
{
	public static partial Task<IPdfPage> OpenAsync(string path, CancellationToken cancellationToken) =>
		Task.Run<IPdfPage>(
			() =>
			{
				ParcelFileDescriptor descriptor =
					ParcelFileDescriptor.Open(new Java.IO.File(path), ParcelFileMode.ReadOnly)
						?? throw new IOException($"The PDF '{path}' could not be opened.");

				PdfRenderer? renderer = null;

				try
				{
					renderer = new PdfRenderer(descriptor);

					if (renderer.PageCount < 1)
					{
						throw new InvalidDataException("The PDF has no page.");
					}

					PdfRenderer.Page page = renderer.OpenPage(0)
						?? throw new InvalidDataException("The PDF page could not be opened.");

					return new AndroidPdfPage(descriptor, renderer, page);
				}
				catch
				{
					renderer?.Close();
					descriptor.Close();

					throw;
				}
			},
			cancellationToken);

	/// <summary>
	/// <c>PdfRenderer</c> is not thread-safe and allows one open page per renderer: the page stays open for the
	/// life of this object and every call holds the lock. Parallel rendering takes several instances.
	/// </summary>
	private sealed class AndroidPdfPage(ParcelFileDescriptor descriptor, PdfRenderer renderer, PdfRenderer.Page page) : IPdfPage
	{
		private readonly Lock _gate = new();
		private bool _disposed;

		public double Aspect => page.Height > 0 ? (double)page.Width / page.Height : 1;

		public Task<byte[]> RenderPngAsync(
			double left,
			double top,
			double width,
			double height,
			int pixelWidth,
			int pixelHeight,
			CancellationToken cancellationToken) =>
			Task.Run(
				() =>
				{
					lock (_gate)
					{
						ObjectDisposedException.ThrowIf(_disposed, this);
						cancellationToken.ThrowIfCancellationRequested();

						using Bitmap bitmap = Bitmap.CreateBitmap(pixelWidth, pixelHeight, Bitmap.Config.Argb8888!)!;

						// The renderer draws the content only; the paper is ours.
						bitmap.EraseColor(Android.Graphics.Color.White);

						// The matrix maps page points to bitmap pixels: the wanted part fills the bitmap.
						float scaleX = (float)(pixelWidth / (width * page.Width));
						float scaleY = (float)(pixelHeight / (height * page.Height));

						using var matrix = new Matrix();

						matrix.SetScale(scaleX, scaleY);
						matrix.PostTranslate((float)(-left * page.Width * scaleX), (float)(-top * page.Height * scaleY));

						page.Render(bitmap, null, matrix, PdfRenderMode.ForDisplay);

						using var stream = new MemoryStream();

						bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);

						return stream.ToArray();
					}
				},
				cancellationToken);

		public void Dispose()
		{
			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;

				page.Close();
				renderer.Close();
				descriptor.Close();
			}
		}
	}
}
