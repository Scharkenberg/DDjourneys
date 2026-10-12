namespace DDjourneys.Support;

/// <summary>
/// The first page of a PDF, drawn by the platform's own renderer (Android <c>PdfRenderer</c>, Windows
/// <c>Windows.Data.Pdf</c>). Vector content has no resolution of its own: any part renders at any size, so a plan
/// keeps all its detail however far it is zoomed. One instance is used by one thread at a time.
/// </summary>
public interface IPdfPage : IDisposable
{
	/// <summary>Width of the page divided by its height.</summary>
	double Aspect { get; }

	/// <summary>
	/// Renders a part of the page, given as fractions of it (0..1, inside the page), stretched to exactly
	/// <paramref name="pixelWidth"/> by <paramref name="pixelHeight"/> pixels on white paper, as PNG.
	/// </summary>
	Task<byte[]> RenderPngAsync(
		double left,
		double top,
		double width,
		double height,
		int pixelWidth,
		int pixelHeight,
		CancellationToken cancellationToken);
}

/// <summary>Opens PDF pages with the platform renderer (Platforms/Android and Platforms/Windows).</summary>
public static partial class PdfPages
{
	/// <summary>The first page of the file; throws when the platform cannot read it (damaged, protected, empty).</summary>
	public static partial Task<IPdfPage> OpenAsync(string path, CancellationToken cancellationToken);
}
