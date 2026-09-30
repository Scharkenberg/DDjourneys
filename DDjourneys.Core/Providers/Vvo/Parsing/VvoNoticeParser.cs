using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DDjourneys.Core.Providers.Vvo.Parsing;

/// <summary>
/// Converts VVO provider information into safe, readable plain text.
///
/// VVO information may be:
/// - ordinary text;
/// - multiline text;
/// - HTML-ish fragments;
/// - Markdown;
/// - a mixture of the above.
///
/// The parser deliberately does not render HTML. It extracts semantic text
/// and preserves useful structure such as paragraphs, lists, line breaks
/// and hyperlinks.
/// </summary>
public static class VvoNoticeParser
{
	private enum MarkupMode
	{
		PlainText,
		Html,
		Markdown,
		Mixed
	}


	private sealed class AnchorContext
	{
		public AnchorContext(string? href)
		{
			Href = href;
		}

		public string? Href { get; }

		public StringBuilder Text { get; } = new();
	}


	private sealed class HtmlTag
	{
		public required string Name { get; init; }

		public bool IsClosing { get; init; }

		public bool IsSelfClosing { get; init; }

		public string Attributes { get; init; } = string.Empty;
	}


	private static readonly HashSet<string> KnownHtmlTags =
		new(StringComparer.OrdinalIgnoreCase)
		{
			"a",
			"abbr",
			"address",
			"article",
			"aside",
			"b",
			"big",
			"blockquote",
			"body",
			"br",
			"caption",
			"center",
			"code",
			"col",
			"del",
			"details",
			"div",
			"em",
			"figcaption",
			"figure",
			"font",
			"footer",
			"h1",
			"h2",
			"h3",
			"h4",
			"h5",
			"h6",
			"head",
			"header",
			"hr",
			"i",
			"img",
			"ins",
			"li",
			"main",
			"mark",
			"nav",
			"ol",
			"p",
			"pre",
			"q",
			"rp",
			"rt",
			"s",
			"script",
			"section",
			"small",
			"span",
			"strike",
			"strong",
			"style",
			"sub",
			"summary",
			"sup",
			"table",
			"tbody",
			"td",
			"tfoot",
			"th",
			"thead",
			"tr",
			"u",
			"ul",
			"wbr"
		};


	private static readonly Regex KnownHtmlTagRegex =
		new(
			@"<\s*/?\s*(?:a|abbr|address|article|aside|b|big|blockquote|body|br|caption|center|code|col|del|details|div|em|figcaption|figure|font|footer|h[1-6]|head|header|hr|i|img|ins|li|main|mark|nav|ol|p|pre|q|rp|rt|s|script|section|small|span|strike|strong|style|sub|summary|sup|table|tbody|td|tfoot|th|thead|tr|u|ul|wbr)\b",
			RegexOptions.IgnoreCase
			| RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	private static readonly Regex HtmlEntityRegex =
		new(
			@"&(?:#\d+|#x[0-9a-f]+|[a-z][a-z0-9]+);",
			RegexOptions.IgnoreCase
			| RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	private static readonly Regex MarkdownBlockRegex =
		new(
			@"(?m)^\s{0,3}(?:#{1,6}\s|[-*+]\s|\d+[.)]\s|>\s)",
			RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	private static readonly Regex MarkdownInlineRegex =
		new(
			@"(?:\[[^\]\r\n]+\]\(\s*(?:<[^>\r\n]+>|[^)\r\n]+)\s*\)|\*\*[^*\r\n]+\*\*|__[^_\r\n]+__|`[^`\r\n]+`)",
			RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	private static readonly Regex MarkdownLinkRegex =
		new(
			@"(?<image>!?)[\[](?<text>[^\]\r\n]+)[\]]\(\s*(?:<(?<angle>[^>\r\n]+)>|(?<url>[^)\r\n]+))\s*\)",
			RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	private static readonly Regex MarkdownAutolinkRegex =
		new(
			@"<(?<url>(?:https?://|mailto:|tel:)[^>\r\n]+)>",
			RegexOptions.IgnoreCase
			| RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	private static readonly Regex HtmlAttributeRegex =
		new(
			@"(?<name>[A-Za-z_:][A-Za-z0-9_.:-]*)(?:\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s""'=<>`]+)))?",
			RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	public static IReadOnlyList<string> Parse(
		IEnumerable<string>? notices)
	{
		if (notices is null)
		{
			return Array.Empty<string>();
		}


		return notices
			.Select(Parse)
			.Where(static value =>
				!string.IsNullOrWhiteSpace(value))
			.ToArray();
	}


	public static string Parse(string? notice)
	{
		if (string.IsNullOrWhiteSpace(notice))
		{
			return string.Empty;
		}


		string normalized =
			NormalizeLineEndings(notice);


		MarkupMode mode =
			DetectMode(normalized);


		string result =
			mode switch
			{
				MarkupMode.Html =>
					ParseHtml(normalized),

				MarkupMode.Mixed =>
					ParseMixed(normalized),

				MarkupMode.Markdown =>
					ParseMarkdown(normalized),

				_ =>
					NormalizePlainText(normalized)
			};


		return NormalizeOutput(result);
	}


	private static MarkupMode DetectMode(string text)
	{
		bool html =
			KnownHtmlTagRegex.IsMatch(text)
			|| HtmlEntityRegex.IsMatch(text);


		bool markdown =
			MarkdownBlockRegex.IsMatch(text)
			|| MarkdownInlineRegex.IsMatch(text)
			|| MarkdownAutolinkRegex.IsMatch(text);


		return (html, markdown) switch
		{
			(true, true) => MarkupMode.Mixed,
			(true, false) => MarkupMode.Html,
			(false, true) => MarkupMode.Markdown,
			_ => MarkupMode.PlainText
		};
	}


	private static string ParseMixed(string text)
	{
		// HTML is parsed first so that HTML tags cannot destroy Markdown
		// content. Markdown links are then materialized as visible text.
		return ParseMarkdown(
			ParseHtml(text));
	}


	private static string ParseMarkdown(string text)
	{
		string result =
			MarkdownLinkRegex.Replace(
				text,
				static match =>
				{
					string label =
						match.Groups["text"].Value;

					string url =
						match.Groups["angle"].Success
							? match.Groups["angle"].Value
							: match.Groups["url"].Value;

					bool image =
						match.Groups["image"].Value == "!";


					return image
						? CleanInlineText(label)
						: FormatLink(label, url);
				});


		result =
			MarkdownAutolinkRegex.Replace(
				result,
				static match =>
					match.Groups["url"].Value);


		return WebUtility.HtmlDecode(result);
	}


	private static string ParseHtml(string text)
	{
		var output =
			new StringBuilder(text.Length);


		var anchors =
			new Stack<AnchorContext>();


		for (int index = 0; index < text.Length;)
		{
			if (text[index] != '<')
			{
				int next =
					text.IndexOf('<', index);


				if (next < 0)
				{
					next = text.Length;
				}


				AppendText(
					output,
					anchors,
					text[index..next]);


				index = next;
				continue;
			}


			// HTML comment.
			if (text.AsSpan(index).StartsWith(
				"<!--",
				StringComparison.Ordinal))
			{
				int end =
					text.IndexOf(
						"-->",
						index + 4,
						StringComparison.Ordinal);


				index =
					end < 0
						? text.Length
						: end + 3;


				continue;
			}


			if (!TryReadTag(
				text,
				index,
				out int endExclusive,
				out string raw))
			{
				// A '<' that does not form a complete tag is ordinary text.
				AppendText(
					output,
					anchors,
					text[index..]);


				break;
			}


			// Markdown-style autolinks: <https://...>
			if (TryReadAngleBracketUrl(
				raw,
				out string? angleUrl)
				&& angleUrl is not null)
			{
				AppendText(
					output,
					anchors,
					angleUrl);


				index = endExclusive;
				continue;
			}


			if (!TryParseTag(
				raw,
				out HtmlTag? tag)
				|| tag is null)
			{
				index = endExclusive;
				continue;
			}


			if (!KnownHtmlTags.Contains(tag.Name))
			{
				// Unknown HTML-ish tags are stripped, but their surrounding
				// textual content remains intact.
				index = endExclusive;
				continue;
			}


			if (!tag.IsClosing
				&& (tag.Name.Equals(
						"script",
						StringComparison.OrdinalIgnoreCase)
					|| tag.Name.Equals(
						"style",
						StringComparison.OrdinalIgnoreCase)))
			{
				index =
					SkipElement(
						text,
						endExclusive,
						tag.Name);


				continue;
			}


			ProcessTag(
				output,
				anchors,
				tag);


			index = endExclusive;
		}


		// A malformed provider response may omit </a>.
		// Flush any outstanding anchors rather than losing their content.
		while (anchors.Count > 0)
		{
			AnchorContext anchor =
				anchors.Pop();


			AppendLink(
				output,
				anchors,
				anchor.Text.ToString(),
				anchor.Href);
		}


		return output.ToString();
	}


	private static void ProcessTag(
		StringBuilder output,
		Stack<AnchorContext> anchors,
		HtmlTag tag)
	{
		StringBuilder target =
			CurrentTarget(output, anchors);


		if (tag.Name.Equals(
			"a",
			StringComparison.OrdinalIgnoreCase))
		{
			if (tag.IsClosing)
			{
				if (anchors.Count == 0)
				{
					return;
				}


				AnchorContext anchor =
					anchors.Pop();


				AppendLink(
					output,
					anchors,
					anchor.Text.ToString(),
					anchor.Href);


				return;
			}


			string? href =
				GetAttribute(
					tag.Attributes,
					"href");


			if (tag.IsSelfClosing)
			{
				AppendLink(
					output,
					anchors,
					string.Empty,
					href);


				return;
			}


			anchors.Push(
				new AnchorContext(href));


			return;
		}


		if (tag.Name.Equals(
			"img",
			StringComparison.OrdinalIgnoreCase)
			&& !tag.IsClosing)
		{
			string? alt =
				GetAttribute(
					tag.Attributes,
					"alt");


			string? src =
				GetAttribute(
					tag.Attributes,
					"src");


			if (!string.IsNullOrWhiteSpace(alt))
			{
				AppendText(
					output,
					anchors,
					alt);
			}
			else if (!string.IsNullOrWhiteSpace(src))
			{
				AppendText(
					output,
					anchors,
					src);
			}


			return;
		}


		if (tag.Name.Equals(
			"br",
			StringComparison.OrdinalIgnoreCase))
		{
			AppendNewLine(
				output,
				anchors);


			return;
		}


		if (tag.Name.Equals(
			"wbr",
			StringComparison.OrdinalIgnoreCase))
		{
			AppendText(
				output,
				anchors,
				" ");


			return;
		}


		if (tag.Name.Equals(
			"hr",
			StringComparison.OrdinalIgnoreCase))
		{
			AppendNewLine(
				output,
				anchors);


			AppendText(
				output,
				anchors,
				"—");


			AppendNewLine(
				output,
				anchors);


			return;
		}


		if (tag.Name.Equals(
			"li",
			StringComparison.OrdinalIgnoreCase))
		{
			if (tag.IsClosing)
			{
				AppendNewLine(
					output,
					anchors);
			}
			else
			{
				AppendNewLine(
					output,
					anchors);


				AppendText(
					output,
					anchors,
					"• ");
			}


			return;
		}


		if (tag.Name.Equals(
			"td",
			StringComparison.OrdinalIgnoreCase)
			|| tag.Name.Equals(
				"th",
				StringComparison.OrdinalIgnoreCase))
		{
			if (!tag.IsClosing
				&& target.Length > 0
				&& !EndsWithWhitespace(target))
			{
				target.Append(" · ");
			}


			return;
		}


		if (tag.Name.Equals(
			"tr",
			StringComparison.OrdinalIgnoreCase))
		{
			AppendNewLine(
				output,
				anchors);


			return;
		}


		if (IsBlockTag(tag.Name))
		{
			AppendNewLine(
				output,
				anchors);
		}
	}


	private static bool IsBlockTag(string name)
	{
		return name.Equals("p", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("div", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("section", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("article", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("header", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("footer", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("main", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("nav", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("aside", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("address", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("blockquote", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("pre", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("figure", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("figcaption", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("details", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("summary", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("ul", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("ol", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("table", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("caption", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("thead", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("tbody", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("tfoot", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("dt", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("dd", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("h1", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("h2", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("h3", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("h4", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("h5", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("h6", StringComparison.OrdinalIgnoreCase);
	}


	private static void AppendText(
		StringBuilder output,
		Stack<AnchorContext> anchors,
		string value)
	{
		if (value.Length == 0)
		{
			return;
		}


		value =
			WebUtility.HtmlDecode(value);


		CurrentTarget(
			output,
			anchors)
			.Append(value);
	}


	private static void AppendNewLine(
		StringBuilder output,
		Stack<AnchorContext> anchors)
	{
		StringBuilder target =
			CurrentTarget(
				output,
				anchors);


		if (target.Length == 0
			|| target[^1] == '\n')
		{
			return;
		}


		target.Append('\n');
	}


	private static void AppendLink(
		StringBuilder output,
		Stack<AnchorContext> anchors,
		string? label,
		string? href)
	{
		string link =
			FormatLink(
				label,
				href);


		if (link.Length > 0)
		{
			CurrentTarget(
				output,
				anchors)
				.Append(link);
		}
	}


	private static StringBuilder CurrentTarget(
		StringBuilder output,
		Stack<AnchorContext> anchors)
	{
		return anchors.Count == 0
			? output
			: anchors.Peek().Text;
	}


	private static string FormatLink(
		string? label,
		string? href)
	{
		string cleanLabel =
			CleanInlineText(label);


		string cleanHref =
			CleanInlineText(href);


		if (cleanLabel.Length == 0)
		{
			return cleanHref;
		}


		if (cleanHref.Length == 0)
		{
			return cleanLabel;
		}


		if (string.Equals(
			NormalizeUrlForComparison(cleanLabel),
			NormalizeUrlForComparison(cleanHref),
			StringComparison.OrdinalIgnoreCase))
		{
			return cleanLabel;
		}


		return $"{cleanLabel} ({cleanHref})";
	}


	private static string CleanInlineText(
		string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}


		string text =
			WebUtility.HtmlDecode(value)
				.Replace('\u00A0', ' ')
				.Replace('\uFEFF', ' ')
				.Replace('\r', ' ')
				.Replace('\n', ' ')
				.Replace('\t', ' ');


		while (text.Contains(
			"  ",
			StringComparison.Ordinal))
		{
			text =
				text.Replace(
					"  ",
					" ",
					StringComparison.Ordinal);
		}


		return text.Trim();
	}


	private static string NormalizeUrlForComparison(
		string value)
	{
		if (!Uri.TryCreate(
			value,
			UriKind.Absolute,
			out Uri? uri))
		{
			return value.TrimEnd('/');
		}


		return uri.GetComponents(
				UriComponents.SchemeAndServer
				| UriComponents.PathAndQuery,
				UriFormat.UriEscaped)
			.TrimEnd('/');
	}


	private static string? GetAttribute(
		string attributes,
		string wantedName)
	{
		foreach (Match match in
			HtmlAttributeRegex.Matches(attributes))
		{
			string name =
				match.Groups["name"].Value;


			if (!name.Equals(
				wantedName,
				StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}


			if (match.Groups["double"].Success)
			{
				return match.Groups["double"].Value;
			}


			if (match.Groups["single"].Success)
			{
				return match.Groups["single"].Value;
			}


			return match.Groups["bare"].Value;
		}


		return null;
	}


	private static bool TryParseTag(
		string raw,
		out HtmlTag? tag)
	{
		tag = null;


		if (raw.Length < 3
			|| raw[0] != '<'
			|| raw[^1] != '>')
		{
			return false;
		}


		string inner =
			raw[1..^1].Trim();


		if (inner.Length == 0
			|| inner[0] == '!'
			|| inner[0] == '?')
		{
			return false;
		}


		bool closing =
			inner.Length > 0
			&& inner[0] == '/';


		if (closing)
		{
			inner =
				inner[1..].TrimStart();
		}


		bool selfClosing =
			inner.Length > 0
			&& inner[^1] == '/';


		if (selfClosing)
		{
			inner =
				inner[..^1].TrimEnd();
		}


		int nameLength = 0;


		while (nameLength < inner.Length
			&& (char.IsLetterOrDigit(inner[nameLength])
				|| inner[nameLength] is ':' or '_' or '-'))
		{
			nameLength++;
		}


		if (nameLength == 0)
		{
			return false;
		}


		string name =
			inner[..nameLength];


		string attributes =
			nameLength < inner.Length
				? inner[nameLength..]
				: string.Empty;


		tag =
			new HtmlTag
			{
				Name = name,
				IsClosing = closing,
				IsSelfClosing = selfClosing,
				Attributes = attributes
			};


		return true;
	}


	private static bool TryReadTag(
		string text,
		int start,
		out int endExclusive,
		out string raw)
	{
		endExclusive = start;
		raw = string.Empty;


		char quote = '\0';


		for (int index = start + 1;
			index < text.Length;
			index++)
		{
			char c =
				text[index];


			if (quote != '\0')
			{
				if (c == quote)
				{
					quote = '\0';
				}


				continue;
			}


			if (c is '\'' or '"')
			{
				quote = c;
				continue;
			}


			if (c == '>')
			{
				endExclusive =
					index + 1;


				raw =
					text[start..endExclusive];


				return true;
			}
		}


		return false;
	}


	private static bool TryReadAngleBracketUrl(
		string raw,
		out string? url)
	{
		url = null;


		if (raw.Length < 3
			|| raw[0] != '<'
			|| raw[^1] != '>')
		{
			return false;
		}


		string candidate =
			raw[1..^1].Trim();


		if (candidate.StartsWith(
				"http://",
				StringComparison.OrdinalIgnoreCase)
			|| candidate.StartsWith(
				"https://",
				StringComparison.OrdinalIgnoreCase)
			|| candidate.StartsWith(
				"mailto:",
				StringComparison.OrdinalIgnoreCase)
			|| candidate.StartsWith(
				"tel:",
				StringComparison.OrdinalIgnoreCase))
		{
			url = candidate;
			return true;
		}


		return false;
	}


	private static int SkipElement(
		string text,
		int start,
		string elementName)
	{
		string closing =
			$"</{elementName}";


		int index =
			text.IndexOf(
				closing,
				start,
				StringComparison.OrdinalIgnoreCase);


		if (index < 0)
		{
			return text.Length;
		}


		int end =
			text.IndexOf(
				'>',
				index);


		return end < 0
			? text.Length
			: end + 1;
	}


	private static string NormalizePlainText(
		string text)
	{
		// Plain notes can still contain HTML entities without actually
		// containing HTML markup. Decode them but otherwise leave content alone.
		return WebUtility.HtmlDecode(text);
	}


	private static string NormalizeOutput(
		string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}


		value =
			value
				.Replace("\r\n", "\n", StringComparison.Ordinal)
				.Replace('\r', '\n')
				.Replace('\u00A0', ' ')
				.Replace('\uFEFF', ' ')
				.Replace("\u200B", string.Empty, StringComparison.Ordinal)
				.Replace("\u200C", string.Empty, StringComparison.Ordinal)
				.Replace("\u200D", string.Empty, StringComparison.Ordinal);


		string[] lines =
			value.Split('\n');


		int first = 0;


		while (first < lines.Length
			&& string.IsNullOrWhiteSpace(lines[first]))
		{
			first++;
		}


		int last =
			lines.Length - 1;


		while (last >= first
			&& string.IsNullOrWhiteSpace(lines[last]))
		{
			last--;
		}


		if (first > last)
		{
			return string.Empty;
		}


		var output =
			new StringBuilder(value.Length);


		bool previousBlank = false;


		for (int index = first;
			index <= last;
			index++)
		{
			string line =
				lines[index].Trim();


			bool blank =
				line.Length == 0;


			// Allow a maximum of one completely blank line between
			// paragraphs/sections.
			if (blank && previousBlank)
			{
				continue;
			}


			if (output.Length > 0)
			{
				output.Append('\n');
			}


			output.Append(line);


			previousBlank = blank;
		}


		return output.ToString();
	}


	private static string NormalizeLineEndings(
		string value)
	{
		return value
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n');
	}


	private static bool EndsWithWhitespace(
		StringBuilder value)
	{
		return value.Length > 0
			&& char.IsWhiteSpace(value[^1]);
	}
}
