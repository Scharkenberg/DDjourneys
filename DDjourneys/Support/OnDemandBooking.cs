using DDjourneys.Core.Models;

namespace DDjourneys.Support;

/// <summary>
/// Where an on-demand leg (Anruf-Linien-Bus, the DVB brand alita) is booked. The app books nothing
/// itself: it shows the booking contact the provider's own data carries - a web page or a phone
/// number - and links the page of the transport authority otherwise. No phone number, lead time or
/// operator is ever invented; nothing but the link is the ceiling of what the app does.
/// </summary>
public static class OnDemandBooking
{
	// The VVO quotes no booking page of its own, and its site has no page about on-demand runs that a
	// public search finds. The on-demand lines of the region are the DVB's alita lines, and the DVB -
	// the authority that runs them - explains their booking on its Anruflinientaxi page (what to have
	// ready when you call). The same honest hand-off as FareLinks makes for tickets.
	private static readonly Uri InfoUrl =
		new("https://www.dvb.de/de-de/fahrplan/nachtverkehr/anruflinientaxi");

	/// <summary>Whether this leg is a booked on-demand ride rather than a line service.</summary>
	public static bool IsOnDemand(JourneyLeg leg)
	{
		ArgumentNullException.ThrowIfNull(leg);

		return leg.Mode == TransitMode.OnDemand;
	}

	/// <summary>
	/// The link for booking the leg: the provider's own contact when it carries one (a web page, or a
	/// phone number as a <c>tel:</c> link), else the authority's page. Never null.
	/// </summary>
	public static Uri LinkOf(string? contact)
	{
		if (ContactText(contact) is { } text)
		{
			// A phone link arrives as one.
			if (text.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
				&& text.Length > 4)
			{
				return new Uri(text);
			}

			// A booking page is a web page, like a sale page (see FareLinks): no scripts, files or app intents.
			if (Uri.TryCreate(text, UriKind.Absolute, out Uri? web)
				&& web.Scheme is "https" or "http")
			{
				return web;
			}

			if (DigitsOf(text) is { Length: >= 6 } digits)
			{
				return new Uri("tel:" + digits);
			}
		}

		return InfoUrl;
	}

	/// <summary>
	/// The words on the booking link: the provider's own number when its data carries one, otherwise
	/// nothing (the caller shows its own words).
	/// </summary>
	public static string? LabelOf(string? contact)
	{
		if (ContactText(contact) is not { } text)
		{
			return null;
		}

		if (text.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
		{
			return text["tel:".Length..].Trim() is { Length: > 0 } number
				? number
				: null;
		}

		return DigitsOf(text) is { Length: >= 6 }
			? text
			: null;
	}

	private static string? ContactText(string? contact) =>
		string.IsNullOrWhiteSpace(contact)
			? null
			: contact.Trim();

	/// <summary>
	/// The number a piece of text is, cleaned for a <c>tel:</c> link (digits, a leading plus): a phone
	/// number written the usual way passes, anything with a letter in it does not.
	/// </summary>
	private static string? DigitsOf(string text)
	{
		foreach (char character in text)
		{
			if (!char.IsAsciiDigit(character)
				&& character is not ' ' and not '+' and not '-' and not '/' and not '(' and not ')' and not '.')
			{
				return null;
			}
		}

		string digits =
			string.Concat(
				text.Where(
					(character, index) =>
						char.IsAsciiDigit(character)
						|| (character == '+' && index == 0)));

		return digits.Length >= 6
			? digits
			: null;
	}
}
