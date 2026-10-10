using SkiaSharp;

namespace DDjourneys.Support;

/// <summary>
/// A plan that is one picture (a JPG or PNG on the device), shown at its full size: the web view tiles large
/// pictures itself, so nothing is scaled down. Only the header is read here, never the pixels.
/// </summary>
public sealed class ImagePlanTiles : IPlanTiles
{
	private readonly string _path;

	private ImagePlanTiles(string path, PlanSpec spec, string contentType)
	{
		_path = path;
		Spec = spec;
		ContentType = contentType;
	}

	public PlanSpec Spec { get; }

	public string ContentType { get; }

	public static Task<ImagePlanTiles> OpenAsync(string path, string name, CancellationToken cancellationToken) =>
		Task.Run(
			() =>
			{
				using SKCodec codec = SKCodec.Create(path) ?? throw new InvalidDataException("The plan picture could not be read.");

				SKImageInfo info = codec.Info;
				string type = codec.EncodedFormat == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg";
				string key = $"{name}-{PlanAddress.Fingerprint(path)}";

				// Past the picture's own pixels, so its small print can be read (blurred, but legible).
				var spec = new PlanSpec(key, info.Width, info.Height, [1d], OverZoom: 1.5, Image: true);

				return new ImagePlanTiles(path, spec, type);
			},
			cancellationToken);

	public Task<Stream?> OpenTileAsync(PlanTile tile, CancellationToken cancellationToken) =>
		Task.FromResult<Stream?>(null);

	public Task<Stream?> OpenImageAsync(CancellationToken cancellationToken)
	{
		try
		{
			return Task.FromResult<Stream?>(File.OpenRead(_path));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Task.FromResult<Stream?>(null);
		}
	}

	public void Dispose()
	{
	}
}
