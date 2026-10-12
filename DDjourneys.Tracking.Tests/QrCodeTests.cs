using System.Security.Cryptography;
using DDjourneys.Core.Contract;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The vendored engine is deterministic, so these golden hashes pin the module matrix of exactly the geo
/// links the share sheet puts into its QR codes. The three payloads were verified once against a real
/// decoder (zbar, outside the suite): every matrix decodes back to its payload. A hash that moves means
/// the vendored engine moved - that is the whole point of the goldens.
/// </summary>
public sealed class QrCodeTests
{
	[Theory]
	[InlineData("geo:51.049311,13.737500?q=Postplatz", 3, "c70cb0dee0559a08914cfedf1bfe288df615675001014b140aa82d953ddfaab5")]
	[InlineData("geo:51.048123,13.738421?q=N%C3%BCrnberger%20Platz", 4, "916aacda3e00ce911eee51055d64902a989df05a5f6322ccc5b67f96207081ea")]
	[InlineData("geo:51.05,13.73?q=Hellerau", 2, "751db635e5adb02e7caf67a35ebc19620413415381d81d92358c1482cdb7ec65")]
	public void The_matrix_of_a_geo_link_is_the_golden_one(string payload, int version, string golden)
	{
		QrMatrix qr = QrCode.Encode(payload);

		Assert.Equal(version, qr.Version);

		// The side of the version plus the four-module quiet zone the engine adds on every side.
		Assert.Equal(21 + (4 * (version - 1)) + 8, qr.Size);

		Assert.Equal(golden, Hash(qr));
	}

	[Fact]
	public void The_same_payload_gives_the_same_matrix()
	{
		string first = Hash(QrCode.Encode("geo:51.05,13.73?q=Hellerau"));
		string second = Hash(QrCode.Encode("geo:51.05,13.73?q=Hellerau"));

		Assert.Equal(first, second);
	}

	[Fact]
	public void The_finder_patterns_sit_in_the_corners()
	{
		QrMatrix qr = QrCode.Encode("geo:51.05,13.73?q=Hellerau");

		Assert.True(IsFinder(qr, 4, 4), "top left");
		Assert.True(IsFinder(qr, qr.Size - 11, 4), "top right");
		Assert.True(IsFinder(qr, 4, qr.Size - 11), "bottom left");
	}

	[Fact]
	public void A_blank_payload_is_rejected() =>
		Assert.Throws<ArgumentException>(() => QrCode.Encode(" "));

	[Fact]
	public void A_payload_beyond_the_standard_is_refused()
	{
		// 4000 bytes at level M: more than the largest QR code holds (2331 at version 40).
		Assert.Throws<QRCoder.Exceptions.DataTooLongException>(() => QrCode.Encode(new string('a', 4000)));
	}

	private static string Hash(QrMatrix qr)
	{
		var bytes = new byte[qr.Size * qr.Size];

		for (int y = 0; y < qr.Size; y++)
		{
			for (int x = 0; x < qr.Size; x++)
			{
				bytes[(y * qr.Size) + x] = qr.Modules[y, x] ? (byte)1 : (byte)0;
			}
		}

		return Convert.ToHexStringLower(SHA256.HashData(bytes));
	}

	/// <summary>The seven-by-seven finder pattern of a QR code: dark border, light ring, dark centre.</summary>
	private static bool IsFinder(QrMatrix qr, int left, int top)
	{
		for (int y = 0; y < 7; y++)
		{
			for (int x = 0; x < 7; x++)
			{
				bool dark = y is 0 or 6 || x is 0 or 6 || (y is >= 2 and <= 4 && x is >= 2 and <= 4);

				if (qr.Modules[top + y, left + x] != dark)
				{
					return false;
				}
			}
		}

		return true;
	}
}
