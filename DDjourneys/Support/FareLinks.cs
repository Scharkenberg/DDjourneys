using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Support;

/// <summary>
/// Where a quoted ticket can be bought. The app sells nothing itself: it hands a link to the
/// system only when the provider or the transport authority names a page.
/// </summary>
public static class FareLinks
{
	// TODO(owner): the VVO API quotes no sale page. Pick the tickets page of the transport
	// authority on device and set it here; that one line is the whole change.
	private static readonly Uri? VvoTicketUrl = null;

	/// <summary>
	/// The link for a quoted ticket: the provider's own URL first (TRIAS SaleUrl), then the
	/// authority's page for the VVO. Null when neither is known; no button is shown then.
	/// </summary>
	public static Uri? For(JourneyFare fare, string providerId)
	{
		if (fare.Url is { Length: > 0 } text
			&& Uri.TryCreate(text, UriKind.Absolute, out Uri? link))
		{
			return link;
		}

		if (VvoTicketUrl is { } vvo
			&& string.Equals(providerId, VvoProviderInfo.Id, StringComparison.OrdinalIgnoreCase))
		{
			return vvo;
		}

		return null;
	}
}
