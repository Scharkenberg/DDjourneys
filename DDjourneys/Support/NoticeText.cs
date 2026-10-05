using System.Text.RegularExpressions;

namespace DDjourneys.Support;

/// <summary>A run of notice text; <see cref="Url"/> is set when the run is a link.</summary>
public sealed record NoticeSpan(string Text, string? Url = null);

/// <summary>One paragraph or bullet of a notice.</summary>
public sealed record NoticeBlock(bool IsBullet, IReadOnlyList<NoticeSpan> Spans);

public enum NoticeSeverity
{
	Info,
	Warning
}

/// <summary>
/// Structures the provider's plain-text notices (already cleaned by VvoNoticeParser) for display:
/// paragraphs, "• " bullets, "label (url)" links and bare URLs. Pure and total: never throws.
/// </summary>
public static class NoticeText
{
	private const string Schemes = @"(?:https?://|mailto:|tel:)";

	// 1) "Some label (https://…)"  – the label is at most 5 words after the last punctuation
	// 2) a bare URL
	private static readonly Regex LinkRegex = new(
		@"(?<label>(?:[^\s.:;!?()]+\s+){0,4}[^\s.:;!?()]+)\s+\((?<url>" + Schemes + @"[^\s()]+)\)"
		+ @"|(?<bare>" + Schemes + @"[^\s<>()]+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
		TimeSpan.FromMilliseconds(250));

	private static readonly Regex WarningRegex = new(
		@"ausfall|f[aä]llt aus|entf[aä]llt|st[oö]rung|sperr|umleit|ersatzverkehr|versp[aä]t|unterbrech|not served|cancel|disrupt|replacement|detour|delay|closed|closure",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
		TimeSpan.FromMilliseconds(250));

	private static readonly string[] BulletMarkers = ["\u2022 ", "- ", "* ", "\u2013 "];

	public static IReadOnlyList<NoticeBlock> Parse(string? text, bool technical = false)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return [];
		}

		var blocks = new List<NoticeBlock>();

		foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
		{
			string line = raw.Trim();

			if (line.Length == 0)
			{
				continue;
			}

			bool bullet = false;

			foreach (string marker in BulletMarkers)
			{
				if (line.StartsWith(marker, StringComparison.Ordinal))
				{
					bullet = true;
					line = line[marker.Length..].TrimStart();
					break;
				}
			}

			if (line.Length > 0)
			{
				blocks.Add(new NoticeBlock(bullet, Spans(line, technical)));
			}
		}

		return blocks;
	}

	public static NoticeSeverity SeverityOf(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return NoticeSeverity.Info;
		}

		try
		{
			return WarningRegex.IsMatch(text) ? NoticeSeverity.Warning : NoticeSeverity.Info;
		}
		catch (RegexMatchTimeoutException)
		{
			return NoticeSeverity.Info;
		}
	}

	/// <summary>Everything without links, for accessibility descriptions and sharing.</summary>
	public static string Plain(IReadOnlyList<NoticeBlock> blocks) =>
		string.Join(". ", blocks.Select(b => string.Concat(b.Spans.Select(s => s.Text))));

	private static List<NoticeSpan> Spans(string line, bool technical)
	{
		var spans = new List<NoticeSpan>();

		try
		{
			int position = 0;

			foreach (Match match in LinkRegex.Matches(line))
			{
				bool labelled = match.Groups["url"].Success;
				string rawUrl = labelled ? match.Groups["url"].Value : match.Groups["bare"].Value;
				string url = rawUrl.TrimEnd('.', ',', ';', ':', '!', '?');

				if (!IsAllowed(url))
				{
					continue; // leave it as ordinary text
				}

				if (match.Index > position)
				{
					spans.Add(new NoticeSpan(line[position..match.Index]));
				}

				if (labelled)
				{
					spans.Add(new NoticeSpan(match.Groups["label"].Value, url));

					if (technical)
					{
						spans.Add(new NoticeSpan($" ({url})"));
					}
				}
				else
				{
					spans.Add(new NoticeSpan(technical ? url : Shorten(url), url));

					if (url.Length < rawUrl.Length)
					{
						spans.Add(new NoticeSpan(rawUrl[url.Length..]));
					}
				}

				position = match.Index + match.Length;
			}

			if (position < line.Length)
			{
				spans.Add(new NoticeSpan(line[position..]));
			}
		}
		catch (RegexMatchTimeoutException)
		{
			spans.Clear();
			spans.Add(new NoticeSpan(line));
		}

		return spans.Count == 0 ? [new NoticeSpan(line)] : spans;
	}

	private static bool IsAllowed(string url) =>
		Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
		&& uri.Scheme is "http" or "https" or "mailto" or "tel";

	/// <summary>"https://www.dvb.de/de/foo/bar?x=1" → "dvb.de/de/foo/bar…"</summary>
	private static string Shorten(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
		{
			return url;
		}

		if (uri.Scheme is "mailto" or "tel")
		{
			return uri.AbsolutePath;
		}

		string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
		string path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath.TrimEnd('/');
		string display = host + path;

		return display.Length > 36 ? display[..35] + "\u2026" : display;
	}
}
