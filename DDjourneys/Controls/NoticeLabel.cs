using System.Text.RegularExpressions;

namespace DDjourneys.Controls;

/// <summary>
/// Displays notice text with automatically detected hyperlinks.
/// Ordinary text, line breaks and wrapping remain unchanged.
/// </summary>
public sealed class NoticeLabel : Label
{
	private static readonly Regex UrlRegex =
		new(
			@"(?<url>(?:https?://|mailto:|tel:)[^\s<>\r\n]+)",
			RegexOptions.IgnoreCase
			| RegexOptions.Compiled
			| RegexOptions.CultureInvariant);


	public NoticeLabel()
	{
		LineBreakMode = LineBreakMode.WordWrap;
		MaxLines = -1;
		HorizontalOptions = LayoutOptions.Fill;
		VerticalOptions = LayoutOptions.Start;
	}


	protected override void OnPropertyChanged(
		string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (propertyName == TextProperty.PropertyName)
		{
			RebuildFormattedText();
		}
	}


	private void RebuildFormattedText()
	{
		string text = Text ?? string.Empty;

		if (text.Length == 0)
		{
			FormattedText = null;
			return;
		}


		MatchCollection matches =
			UrlRegex.Matches(text);

		if (matches.Count == 0)
		{
			FormattedText = null;
			return;
		}


		var formatted =
			new FormattedString();

		int position = 0;


		foreach (Match match in matches)
		{
			if (match.Index > position)
			{
				formatted.Spans.Add(
					new Span
					{
						Text = text[position..match.Index]
					});
			}


			string rawUrl =
				match.Groups["url"].Value;


			string url =
				TrimTrailingPunctuation(rawUrl);


			int linkLength =
				url.Length;


			if (linkLength > 0
				&& Uri.TryCreate(
					url,
					UriKind.Absolute,
					out Uri? uri)
				&& IsAllowedScheme(uri))
			{
				var span =
					new Span
					{
						Text = url,
						TextDecorations =
							TextDecorations.Underline
					};


				span.SetDynamicResource(
					Span.TextColorProperty,
					"Accent");


				span.GestureRecognizers.Add(
					new TapGestureRecognizer
					{
						Command =
							new Command(
								async () =>
									await OpenAsync(uri))
					});


				formatted.Spans.Add(span);


				if (linkLength < rawUrl.Length)
				{
					formatted.Spans.Add(
						new Span
						{
							Text =
								rawUrl[linkLength..]
						});
				}
			}
			else
			{
				formatted.Spans.Add(
					new Span
					{
						Text = rawUrl
					});
			}


			position =
				match.Index
				+ match.Length;
		}


		if (position < text.Length)
		{
			formatted.Spans.Add(
				new Span
				{
					Text = text[position..]
				});
		}


		FormattedText = formatted;
	}


	private static string TrimTrailingPunctuation(
		string value)
	{
		return value.TrimEnd(
			'.',
			',',
			';',
			':',
			'!',
			'?');
	}


	private static bool IsAllowedScheme(
		Uri uri)
	{
		return uri.Scheme.Equals(
			"http",
			StringComparison.OrdinalIgnoreCase)
			|| uri.Scheme.Equals(
				"https",
				StringComparison.OrdinalIgnoreCase)
			|| uri.Scheme.Equals(
				"mailto",
				StringComparison.OrdinalIgnoreCase)
			|| uri.Scheme.Equals(
				"tel",
				StringComparison.OrdinalIgnoreCase);
	}


	private static async Task OpenAsync(Uri uri)
	{
		try
		{
			await Launcher.Default.OpenAsync(uri);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"[NOTICE LINK] Failed to open {uri}: {ex}");
		}
	}
}
