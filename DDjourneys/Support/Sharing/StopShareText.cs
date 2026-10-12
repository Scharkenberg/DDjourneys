using System.Globalization;
using System.Text;
using DDjourneys.Core.Contract;
using DDjourneys.Localization;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// The stop as plain text for messengers: one localized line that says where to meet, then the links -
/// the geo link first, the one form that works for everyone without the app, and the board link of the
/// app itself. The QR code of the share sheet carries the same geo link, so text and code never disagree.
/// No MAUI types on purpose: the wording is unit-tested like the rest of the sharing texts.
/// </summary>
public static class StopShareText
{
	public static string Build(StopSharePayload share, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(share);
		ArgumentNullException.ThrowIfNull(strings);

		ExtrasStrings text = strings.Extras;
		var builder = new StringBuilder();

		builder.AppendLine(string.Format(CultureInfo.CurrentCulture, text.ShareStopMeetAt, share.Name));

		// The geo link only when the stop has coordinates; the board link always - the share stays useful
		// without a position. OriginalString keeps the encoded form, so a pasted link stays one link.
		if (share.GeoLink is { } geo)
		{
			builder.AppendLine(geo);
		}

		builder.AppendLine(share.AppLink.OriginalString);
		builder.Append("— DDjourneys");

		return builder.ToString();
	}
}
