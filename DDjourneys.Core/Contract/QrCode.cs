using QRCoder;

namespace DDjourneys.Core.Contract;

/// <summary>
/// A generated QR code: the module matrix and the version it was built at. The matrix carries the
/// standard four-module quiet zone on every side (the vendored engine adds it), so a renderer paints
/// it as it is - dark modules on white - and the quiet zone comes with the picture.
/// </summary>
/// <param name="Modules">Dark modules as <c>modules[y, x]</c>, quiet zone included.</param>
public sealed record QrMatrix(bool[,] Modules, int Version)
{
	/// <summary>The side length in modules, quiet zone included.</summary>
	public int Size => Modules.GetLength(0);
}

/// <summary>
/// The one QR call of the app: a payload string to the module matrix the picture is drawn from
/// (see <see cref="StopShare"/> for what is shared and <c>StopShareImage</c> in the app for the PNG).
/// Thin on purpose, so the vendored engine file stays upstream-shaped.
/// </summary>
public static class QrCode
{
	/// <summary>
	/// Error correction M: about 15&nbsp;% of the code may be damaged and still read. The balanced choice
	/// for a code shown on a screen and scanned by a phone - L saves modules nothing needs, Q and H only
	/// make the code denser, which scans worse from a screen than the damage they guard against.
	/// </summary>
	public const QRCodeGenerator.ECCLevel EccLevel = QRCodeGenerator.ECCLevel.M;

	/// <summary>Encodes the payload at error correction level M. Throws when the payload cannot fit a QR code.</summary>
	public static QrMatrix Encode(string payload)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(payload);

		using QRCodeData data = QRCodeGenerator.GenerateQrCode(payload, EccLevel);

		int size = data.ModuleMatrix.Count;
		var modules = new bool[size, size];

		for (int y = 0; y < size; y++)
		{
			System.Collections.BitArray row = data.ModuleMatrix[y];

			for (int x = 0; x < size; x++)
			{
				modules[y, x] = row[x];
			}
		}

		return new QrMatrix(modules, data.Version);
	}
}
