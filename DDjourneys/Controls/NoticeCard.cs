using DDjourneys.Support;
using Microsoft.Maui.Controls.Shapes;

namespace DDjourneys.Controls;

/// <summary>
/// Displays one provider notice richly: severity accent bar and glyph, paragraphs, hanging bullets,
/// tappable links (label shown, raw URL hidden), and a "Show more" fold for long notices.
/// Built in code so the layout is identical wherever it is used; any failure falls back to plain text.
/// </summary>
public sealed class NoticeCard : ContentView
{
	private const int CollapsedBlocks = 2;
	private const int CollapsedLines = 3;
	private const int FoldThreshold = 240;

	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(NoticeCard), string.Empty);

	public static readonly BindableProperty ExpandedProperty =
		BindableProperty.Create(nameof(Expanded), typeof(bool), typeof(NoticeCard), false);

	public static readonly BindableProperty TechnicalProperty =
		BindableProperty.Create(nameof(Technical), typeof(bool), typeof(NoticeCard), false);

	private bool _open;
	private bool _openInitialised;

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>Start unfolded (the "Expand notices" setting).</summary>
	public bool Expanded
	{
		get => (bool)GetValue(ExpandedProperty);
		set => SetValue(ExpandedProperty, value);
	}

	/// <summary>Show full URLs next to link labels.</summary>
	public bool Technical
	{
		get => (bool)GetValue(TechnicalProperty);
		set => SetValue(TechnicalProperty, value);
	}

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (propertyName is nameof(Text) or nameof(Expanded) or nameof(Technical))
		{
			if (propertyName == nameof(Expanded) || propertyName == nameof(Text))
			{
				_openInitialised = false;
			}

			Rebuild();
		}
	}

	private void Rebuild()
	{
		try
		{
			if (!_openInitialised)
			{
				_open = Expanded;
				_openInitialised = true;
			}

			Content = Build();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"NoticeCard fallback: {ex}");
			Content = new Label { Text = Text ?? string.Empty, LineBreakMode = LineBreakMode.WordWrap };
		}
	}

	private View? Build()
	{
		string text = Text ?? string.Empty;
		IReadOnlyList<NoticeBlock> blocks = NoticeText.Parse(text, Technical);

		if (blocks.Count == 0)
		{
			return null;
		}

		bool warning = NoticeText.SeverityOf(text) == NoticeSeverity.Warning;
		string accentKey = warning ? "Delay" : "Accent";

		bool foldable = blocks.Count > CollapsedBlocks || text.Length > FoldThreshold;
		bool folded = foldable && !_open;

		var body = new VerticalStackLayout { Spacing = 4 };

		foreach (NoticeBlock block in blocks.Take(folded ? CollapsedBlocks : blocks.Count))
		{
			body.Add(BlockView(block, folded ? CollapsedLines : -1));
		}

		if (foldable)
		{
			var toggle = new Label
			{
				Text = folded ? "Show more \u25BE" : "Show less \u25B4",
				FontFamily = "OpenSansSemibold",
				FontSize = 13,
				Padding = new Thickness(0, 6, 0, 2)
			};
			toggle.SetDynamicResource(Label.TextColorProperty, accentKey);
			SemanticProperties.SetDescription(toggle, folded ? "Show the full notice" : "Show less of the notice");
			toggle.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Toggle) });
			body.Add(toggle);
		}

		var glyph = new Label
		{
			Text = warning ? "\u26A0\uFE0E" : "\u24D8",
			FontFamily = "OpenSansSemibold",
			VerticalOptions = LayoutOptions.Start
		};
		glyph.SetDynamicResource(Label.TextColorProperty, accentKey);

		var bar = new BoxView { WidthRequest = 3 };
		bar.SetDynamicResource(BoxView.ColorProperty, accentKey);

		var content = new Grid
		{
			ColumnDefinitions =
			[
				new ColumnDefinition(3),
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Star)
			],
			ColumnSpacing = 8
		};

		content.Add(bar, 0);
		glyph.Margin = new Thickness(0, 8, 0, 0);
		content.Add(glyph, 1);
		body.Margin = new Thickness(0, 8, 10, 8);
		content.Add(body, 2);

		var card = new Border
		{
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
			Padding = 0,
			Content = content
		};
		card.SetDynamicResource(VisualElement.BackgroundColorProperty, "Raised");
		SemanticProperties.SetDescription(card, (warning ? "Warning: " : "Notice: ") + NoticeText.Plain(blocks));

		return card;
	}

	private void Toggle()
	{
		_open = !_open;
		Rebuild();

		if (Content is VisualElement view)
		{
			_ = Motion.RevealAsync(view, 0, 180, 4);
		}
	}

	private static View BlockView(NoticeBlock block, int maxLines)
	{
		Label label = TextView(block.Spans, maxLines);

		if (!block.IsBullet)
		{
			return label;
		}

		var dot = new Label { Text = "\u2022", VerticalOptions = LayoutOptions.Start };
		dot.SetDynamicResource(Label.TextColorProperty, "InkMuted");

		var row = new Grid
		{
			ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
			ColumnSpacing = 6
		};

		row.Add(dot, 0);
		row.Add(label, 1);
		return row;
	}

	private static Label TextView(IReadOnlyList<NoticeSpan> spans, int maxLines)
	{
		var label = new Label
		{
			FontSize = 14,
			LineBreakMode = maxLines > 0 ? LineBreakMode.TailTruncation : LineBreakMode.WordWrap,
			MaxLines = maxLines,
			HorizontalOptions = LayoutOptions.Fill
		};
		label.SetDynamicResource(Label.TextColorProperty, "Ink");

		if (spans.All(s => s.Url is null))
		{
			label.Text = string.Concat(spans.Select(s => s.Text));
			return label;
		}

		var formatted = new FormattedString();

		foreach (NoticeSpan part in spans)
		{
			var span = new Span { Text = part.Text };

			if (part.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
			{
				span.TextDecorations = TextDecorations.Underline;
				span.SetDynamicResource(Span.TextColorProperty, "Accent");
				span.GestureRecognizers.Add(new TapGestureRecognizer
				{
					Command = new Command(async () => await OpenAsync(uri))
				});
			}

			formatted.Spans.Add(span);
		}

		label.FormattedText = formatted;
		return label;
	}

	private static async Task OpenAsync(Uri uri)
	{
		try
		{
			await Launcher.Default.OpenAsync(uri);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[NOTICE LINK] Failed to open {uri}: {ex.Message}");
		}
	}
}
