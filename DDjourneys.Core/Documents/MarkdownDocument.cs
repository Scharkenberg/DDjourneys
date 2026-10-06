using System.Text;

namespace DDjourneys.Core.Documents;

/// <summary>How a run of text is shown.</summary>
[Flags]
public enum InlineStyle
{
	None = 0,
	Bold = 1,
	Italic = 2,
	Code = 4,
	Link = 8
}

/// <summary>A run of text with one style; a link carries its target.</summary>
public sealed record MarkdownInline(string Text, InlineStyle Style = InlineStyle.None, string? Url = null);

/// <summary>A block of a document.</summary>
public abstract record MarkdownBlock;

/// <summary>A heading, level 1 to 6.</summary>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownInline> Content) : MarkdownBlock;

/// <summary>A paragraph; a hard line break inside it is a <c>\n</c> in the text.</summary>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Content) : MarkdownBlock;

/// <summary>Preformatted text, shown as it is.</summary>
public sealed record MarkdownCode(string Text, string Language) : MarkdownBlock;

/// <summary>A list whose items hold only inline text (no nested lists).</summary>
public sealed record MarkdownList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<MarkdownInline>> Items) : MarkdownBlock;

/// <summary>A quotation made of blocks.</summary>
public sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>A horizontal rule.</summary>
public sealed record MarkdownRule : MarkdownBlock;

/// <summary>
/// A small reader for the part of Markdown the app's own documents use: headings, paragraphs, lists, quotations,
/// fenced code, rules, and inside the text emphasis, code spans, links and bare web addresses. Anything else is shown as
/// the text it is. Pure and platform-neutral, so the same file reads the same everywhere.
/// </summary>
public static class MarkdownDocument
{
	public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
	{
		string text = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

		if (text.Length > 0 && text[0] == '﻿')
		{
			text = text[1..];
		}

		return ParseBlocks(text.Split('\n'));
	}

	/// <summary>The text of a document without any formatting: for search, tests and screen readers.</summary>
	public static string PlainText(IEnumerable<MarkdownBlock> blocks)
	{
		var text = new StringBuilder();

		foreach (MarkdownBlock block in blocks)
		{
			if (text.Length > 0)
			{
				text.Append("\n\n");
			}

			switch (block)
			{
				case MarkdownHeading heading:
					text.Append(Join(heading.Content));
					break;
				case MarkdownParagraph paragraph:
					text.Append(Join(paragraph.Content));
					break;
				case MarkdownCode code:
					text.Append(code.Text);
					break;
				case MarkdownList list:
					text.Append(string.Join("\n", list.Items.Select(item => Join(item))));
					break;
				case MarkdownQuote quote:
					text.Append(PlainText(quote.Blocks));
					break;
			}
		}

		return text.ToString();
	}

	private static string Join(IEnumerable<MarkdownInline> inlines) =>
		string.Concat(inlines.Select(item => item.Text));

	// ----- blocks -----

	private static List<MarkdownBlock> ParseBlocks(string[] lines)
	{
		var blocks = new List<MarkdownBlock>();
		var paragraph = new List<string>();
		int i = 0;

		void FlushParagraph()
		{
			if (paragraph.Count > 0)
			{
				blocks.Add(new MarkdownParagraph(ParseInlines(JoinLines(paragraph))));
				paragraph.Clear();
			}
		}

		while (i < lines.Length)
		{
			string line = lines[i];
			string trimmed = line.Trim();

			if (trimmed.Length == 0)
			{
				FlushParagraph();
				i++;
				continue;
			}

			// A comment for the author, not for the reader.
			if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
			{
				FlushParagraph();

				while (i < lines.Length && !lines[i].Contains("-->", StringComparison.Ordinal))
				{
					i++;
				}

				i++;
				continue;
			}

			if (FenceOf(trimmed) is { } fence)
			{
				FlushParagraph();
				i++;

				var code = new List<string>();

				while (i < lines.Length && !lines[i].Trim().StartsWith(fence.Marker, StringComparison.Ordinal))
				{
					code.Add(lines[i]);
					i++;
				}

				i++;
				blocks.Add(new MarkdownCode(string.Join("\n", code), fence.Language));
				continue;
			}

			if (HeadingOf(trimmed) is { } heading)
			{
				FlushParagraph();
				blocks.Add(new MarkdownHeading(heading.Level, ParseInlines(heading.Text)));
				i++;
				continue;
			}

			if (IsRule(trimmed))
			{
				FlushParagraph();
				blocks.Add(new MarkdownRule());
				i++;
				continue;
			}

			if (trimmed[0] == '>')
			{
				FlushParagraph();

				var quoted = new List<string>();

				while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
				{
					string inner = lines[i].TrimStart()[1..];
					quoted.Add(inner.StartsWith(' ') ? inner[1..] : inner);
					i++;
				}

				blocks.Add(new MarkdownQuote(ParseBlocks([.. quoted])));
				continue;
			}

			if (ListMarkerOf(line) is { } first)
			{
				FlushParagraph();

				var items = new List<IReadOnlyList<MarkdownInline>>();
				var item = new List<string> { first.Text };

				i++;

				while (i < lines.Length)
				{
					string next = lines[i];

					if (next.Trim().Length == 0)
					{
						break;
					}

					if (ListMarkerOf(next) is { } another && another.Ordered == first.Ordered)
					{
						items.Add(ParseInlines(JoinLines(item)));
						item = [another.Text];
					}
					else if (char.IsWhiteSpace(next[0]))
					{
						item.Add(next.Trim());
					}
					else
					{
						break;
					}

					i++;
				}

				items.Add(ParseInlines(JoinLines(item)));
				blocks.Add(new MarkdownList(first.Ordered, first.Number, items));
				continue;
			}

			paragraph.Add(line);
			i++;
		}

		FlushParagraph();

		return blocks;
	}

	/// <summary>Soft line breaks become a space; two trailing spaces or a backslash make a hard break.</summary>
	private static string JoinLines(List<string> lines)
	{
		var text = new StringBuilder();

		for (int index = 0; index < lines.Count; index++)
		{
			string line = lines[index];
			bool hard = line.EndsWith("  ", StringComparison.Ordinal) || line.TrimEnd().EndsWith('\\');
			string content = line.Trim();

			if (content.EndsWith('\\') && hard)
			{
				content = content[..^1];
			}

			text.Append(content);

			if (index < lines.Count - 1)
			{
				text.Append(hard ? '\n' : ' ');
			}
		}

		return text.ToString();
	}

	private static (string Marker, string Language)? FenceOf(string trimmed)
	{
		foreach (string marker in new[] { "```", "~~~" })
		{
			if (trimmed.StartsWith(marker, StringComparison.Ordinal))
			{
				return (marker, trimmed[marker.Length..].Trim());
			}
		}

		return null;
	}

	private static (int Level, string Text)? HeadingOf(string trimmed)
	{
		int level = 0;

		while (level < trimmed.Length && trimmed[level] == '#')
		{
			level++;
		}

		if (level is < 1 or > 6 || level >= trimmed.Length || trimmed[level] != ' ')
		{
			return null;
		}

		string title = trimmed[(level + 1)..].Trim();
		int end = title.Length;

		while (end > 0 && title[end - 1] == '#')
		{
			end--;
		}

		// Closing hashes only count after a space ("# C#" is the title C#).
		if (end < title.Length && (end == 0 || title[end - 1] == ' '))
		{
			title = title[..end].TrimEnd();
		}

		return (level, title);
	}

	private static bool IsRule(string trimmed)
	{
		string compact = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);

		return compact.Length >= 3
			&& compact[0] is '-' or '*' or '_'
			&& compact.All(c => c == compact[0]);
	}

	private static (bool Ordered, int Number, string Text)? ListMarkerOf(string line)
	{
		string trimmed = line.TrimStart();

		if (trimmed.Length >= 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ' && !IsRule(trimmed))
		{
			return (false, 1, trimmed[2..].Trim());
		}

		int digits = 0;

		while (digits < trimmed.Length && digits < 9 && char.IsAsciiDigit(trimmed[digits]))
		{
			digits++;
		}

		if (digits > 0
			&& digits + 1 < trimmed.Length
			&& trimmed[digits] is '.' or ')'
			&& trimmed[digits + 1] == ' ')
		{
			return (true, int.Parse(trimmed[..digits], System.Globalization.CultureInfo.InvariantCulture), trimmed[(digits + 2)..].Trim());
		}

		return null;
	}

	// ----- inline text -----

	public static IReadOnlyList<MarkdownInline> ParseInlines(string text) =>
		Merge(Inlines(text, InlineStyle.None, null));

	private static List<MarkdownInline> Inlines(string text, InlineStyle style, string? url)
	{
		var result = new List<MarkdownInline>();
		var plain = new StringBuilder();
		int i = 0;

		void Flush()
		{
			if (plain.Length > 0)
			{
				result.Add(new MarkdownInline(plain.ToString(), style, url));
				plain.Clear();
			}
		}

		while (i < text.Length)
		{
			char c = text[i];

			if (c == '\\' && i + 1 < text.Length && char.IsAsciiLetterOrDigit(text[i + 1]) == false && !char.IsWhiteSpace(text[i + 1]))
			{
				plain.Append(text[i + 1]);
				i += 2;
				continue;
			}

			if (c == '`')
			{
				int run = 1;

				while (i + run < text.Length && text[i + run] == '`')
				{
					run++;
				}

				string ticks = new('`', run);
				int close = text.IndexOf(ticks, i + run, StringComparison.Ordinal);

				if (close > 0)
				{
					Flush();
					result.Add(new MarkdownInline(text[(i + run)..close].Trim(), (style & InlineStyle.Link) | InlineStyle.Code, url));
					i = close + run;
					continue;
				}
			}

			if (c == '!' && i + 1 < text.Length && text[i + 1] == '[' && LinkAt(text, i + 1) is { } picture)
			{
				// An image is shown as its description.
				Flush();
				result.Add(new MarkdownInline(picture.Label, style, url));
				i = picture.End;
				continue;
			}

			if (c == '[' && LinkAt(text, i) is { } link)
			{
				Flush();
				result.AddRange(Inlines(link.Label, style | InlineStyle.Link, link.Target));
				i = link.End;
				continue;
			}

			if (c == '<' && AutoLinkAt(text, i) is { } auto)
			{
				Flush();
				result.Add(new MarkdownInline(auto.Target, style | InlineStyle.Link, auto.Target));
				i = auto.End;
				continue;
			}

			if ((c == 'h') && BareUrlAt(text, i) is { } bare)
			{
				Flush();
				result.Add(new MarkdownInline(bare.Target, style | InlineStyle.Link, bare.Target));
				i = bare.End;
				continue;
			}

			if (c is '*' or '_' && EmphasisAt(text, i) is { } emphasis)
			{
				Flush();
				result.AddRange(Inlines(emphasis.Inner, style | emphasis.Style, url));
				i = emphasis.End;
				continue;
			}

			plain.Append(c);
			i++;
		}

		Flush();

		return result;
	}

	private static List<MarkdownInline> Merge(List<MarkdownInline> inlines)
	{
		var merged = new List<MarkdownInline>();

		foreach (MarkdownInline inline in inlines)
		{
			if (inline.Text.Length == 0)
			{
				continue;
			}

			if (merged.Count > 0
				&& merged[^1].Style == inline.Style
				&& merged[^1].Url == inline.Url)
			{
				merged[^1] = merged[^1] with { Text = merged[^1].Text + inline.Text };
			}
			else
			{
				merged.Add(inline);
			}
		}

		return merged;
	}

	private static (string Label, string Target, int End)? LinkAt(string text, int start)
	{
		int depth = 0;
		int labelEnd = -1;

		for (int i = start; i < text.Length; i++)
		{
			if (text[i] == '\\')
			{
				i++;
			}
			else if (text[i] == '[')
			{
				depth++;
			}
			else if (text[i] == ']' && --depth == 0)
			{
				labelEnd = i;
				break;
			}
		}

		if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
		{
			return null;
		}

		int close = text.IndexOf(')', labelEnd + 2);

		if (close < 0)
		{
			return null;
		}

		string target = text[(labelEnd + 2)..close].Trim();

		// A title after the address ("https://example.org "Title"") is not part of it.
		int space = target.IndexOf(' ', StringComparison.Ordinal);

		if (space > 0)
		{
			target = target[..space];
		}

		target = target.Trim('<', '>');

		return target.Length == 0
			? null
			: (text[(start + 1)..labelEnd], target, close + 1);
	}

	private static (string Target, int End)? AutoLinkAt(string text, int start)
	{
		int close = text.IndexOf('>', start + 1);

		if (close < 0)
		{
			return null;
		}

		string inner = text[(start + 1)..close];

		return IsWebAddress(inner) && !inner.Contains(' ', StringComparison.Ordinal)
			? (inner, close + 1)
			: null;
	}

	private static (string Target, int End)? BareUrlAt(string text, int start)
	{
		if (!IsWebAddress(text.AsSpan(start))
			|| (start > 0 && char.IsLetterOrDigit(text[start - 1])))
		{
			return null;
		}

		int end = start;

		while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not '<' and not '>' and not ')')
		{
			end++;
		}

		// Sentence punctuation after an address is not part of it.
		while (end > start && text[end - 1] is '.' or ',' or ';' or ':' or '!' or '?')
		{
			end--;
		}

		return end - start > 8
			? (text[start..end], end)
			: null;
	}

	private static bool IsWebAddress(ReadOnlySpan<char> text) =>
		text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
		|| text.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

	private static (string Inner, InlineStyle Style, int End)? EmphasisAt(string text, int start)
	{
		char mark = text[start];
		int run = 1;

		while (start + run < text.Length && text[start + run] == mark)
		{
			run++;
		}

		if (run > 2 || start + run >= text.Length || char.IsWhiteSpace(text[start + run]))
		{
			return null;
		}

		// An underscore inside a word is just an underscore (snake_case).
		if (mark == '_' && start > 0 && char.IsLetterOrDigit(text[start - 1]))
		{
			return null;
		}

		string closing = new(mark, run);
		int from = start + run;

		while (from < text.Length)
		{
			int close = text.IndexOf(closing, from, StringComparison.Ordinal);

			if (close < 0)
			{
				return null;
			}

			bool followedByMore = close + run < text.Length && text[close + run] == mark;
			bool afterSpace = char.IsWhiteSpace(text[close - 1]);
			bool insideWord = mark == '_' && close + run < text.Length && char.IsLetterOrDigit(text[close + run]);

			if (!afterSpace && !followedByMore && !insideWord && close > start + run)
			{
				return (
					text[(start + run)..close],
					run == 2 ? InlineStyle.Bold : InlineStyle.Italic,
					close + run);
			}

			from = close + run;
		}

		return null;
	}
}
