using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Trias;
using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Support;

/// <summary>
/// Where a quoted ticket can be bought. The app sells nothing itself: it hands a link to the
/// system only when the provider or the transport authority names a page.
/// </summary>
public static class FareLinks
{
	// The VVO API quotes no sale page, and the authority sells through several apps (VVO mobil / DVB mobil,
	// DB Navigator, FAIRTIQ, HandyTicket Deutschland, Moovme). Its page "Handy- und OnlineTickets" lists them
	// all and says which suits whom (checked on vvo-online.de, tarif-tickets/ticketkauf): the honest hand-off.
	private static readonly Uri? VvoTicketUrl =
		new("https://www.vvo-online.de/de/tarif-tickets/ticketkauf/handy-und-onlinetickets-6514.cshtml");

	/// <summary>
	/// The link for a quoted ticket: the provider's own URL first (TRIAS SaleUrl), then the
	/// authority's page for the VVO. Null when neither is known; no button is shown then.
	/// </summary>
	public static Uri? For(JourneyFare fare, string providerId)
	{
		// A sale page is a web page: the provider's text never names a file, an app intent or a script.
		if (fare.Url is { Length: > 0 } text
			&& Uri.TryCreate(text, UriKind.Absolute, out Uri? link)
			&& link.Scheme is "https" or "http")
		{
			return link;
		}

		if (VvoTicketUrl is { } vvo
			&& (string.Equals(providerId, VvoProviderInfo.Id, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(providerId, TriasProviderInfo.Id, StringComparison.OrdinalIgnoreCase)))
		{
			return vvo;
		}

		return null;
	}
}
