using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// Namespace-agnostic reading of TRIAS XML. The request namespace and the response namespace differ
/// between servers and versions, so elements are matched by local name only.
/// </summary>
internal static class TriasXml
{
	public static XElement? Child(this XElement? element, string name) =>
		element?.Elements().FirstOrDefault(child => child.Name.LocalName == name);

	public static IEnumerable<XElement> Children(this XElement? element, string name) =>
		element?.Elements().Where(child => child.Name.LocalName == name) ?? [];

	/// <summary>Descendants by local name (in document order).</summary>
	public static IEnumerable<XElement> Deep(this XContainer? element, string name) =>
		element?.Descendants().Where(child => child.Name.LocalName == name) ?? [];

	public static string? Text(this XElement? element)
	{
		string? value = element?.Value;

		return string.IsNullOrWhiteSpace(value)
			? null
			: value.Trim();
	}

	public static string? ChildText(this XElement? element, string name) =>
		element.Child(name).Text();

	/// <summary>
	/// Text of an international text element: <c>&lt;X&gt;&lt;Text&gt;...&lt;/Text&gt;&lt;/X&gt;</c> (or plain text).
	/// </summary>
	public static string? Label(this XElement? element)
	{
		if (element is null)
		{
			return null;
		}

		XElement? leaf =
			element
				.DescendantsAndSelf()
				.FirstOrDefault(child => !child.HasElements && child.Name.LocalName == "Text");

		return leaf is not null
			? leaf.Text()
			: element.HasElements
				? null
				: element.Text();
	}

	public static string? ChildLabel(this XElement? element, string name) =>
		element.Child(name).Label();

	public static bool Flag(this XElement? element) =>
		string.Equals(element.Text(), "true", StringComparison.OrdinalIgnoreCase);

	/// <summary>A time without an offset is taken as UTC (TRIAS sends offsets or "Z").</summary>
	public static DateTimeOffset? Time(this XElement? element) =>
		DateTimeOffset.TryParse(
			element.Text(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal,
			out DateTimeOffset value)
			? value
			: null;

	public static TimeSpan? Duration(this XElement? element)
	{
		string? text = element.Text();

		if (text is null)
		{
			return null;
		}

		try
		{
			return XmlConvert.ToTimeSpan(text);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	public static double? Number(this XElement? element) =>
		double.TryParse(
			element.Text(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out double value)
			? value
			: null;

	public static decimal? Decimal(this XElement? element) =>
		decimal.TryParse(
			element.Text(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out decimal value)
			? value
			: null;
}
