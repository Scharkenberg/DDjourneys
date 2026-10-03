using DDjourneys.Localization;
using DDjourneys.Support;
using Microsoft.Maui.Controls.Shapes;

namespace DDjourneys.Controls;

/// <summary>
/// Displays one provider notice richly: severity accent bar and glyph, paragraphs, hanging bullets,
/// tappable links, and a fold for long notices.
/// </summary>
public sealed class NoticeCard : ContentView
{
	private const int CollapsedBlocks = 2;
	private const int CollapsedLines = 3;
	private const int FoldThreshold = 240;

	private readonly LocalizationService _localization =
		LocalizationService.Current;

	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(
			nameof(Text),
			typeof(string),
			typeof(NoticeCard),
			string.Empty);

	public static readonly BindableProperty ExpandedProperty =
		BindableProperty.Create(
			nameof(Expanded),
			typeof(bool),
			typeof(NoticeCard),
			false);

	public static readonly BindableProperty TechnicalProperty =
		BindableProperty.Create(
			nameof(Technical),
			typeof(bool),
			typeof(NoticeCard),
			false);

	private bool _open;
	private bool _openInitialised;

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>Start unfolded.</summary>
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

	protected override void OnHandlerChanged()
	{
		// Detach first so a handler swap never subscribes twice.
		_localization.PropertyChanged -= OnLocalizationChanged;

		if (Handler is not null)
		{
			_localization.PropertyChanged += OnLocalizationChanged;
		}

		base.OnHandlerChanged();
	}

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (propertyName is nameof(Text)
			or nameof(Expanded)
			or nameof(Technical))
		{
			if (propertyName == nameof(Expanded)
				|| propertyName == nameof(Text))
			{
				_openInitialised = false;
			}

			Rebuild();
		}
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(Rebuild);
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
			System.Diagnostics.Debug.WriteLine(
				$"NoticeCard fallback: {ex}");

			Content = new Label
			{
				Text = Text ?? string.Empty,
				LineBreakMode = LineBreakMode.WordWrap
			};
		}
	}

	private View? Build()
	{
		string text = Text ?? string.Empty;

		IReadOnlyList<NoticeBlock> blocks =
			NoticeText.Parse(text, Technical);

		if (blocks.Count == 0)
		{
			return null;
		}

		bool warning =
			NoticeText.SeverityOf(text)
			== NoticeSeverity.Warning;

		string accentKey =
			warning ? "Delay" : "Accent";

		JourneyStrings strings =
			_localization.CurrentStrings.Journey;

		bool foldable =
			blocks.Count > CollapsedBlocks
			|| text.Length > FoldThreshold;

		bool folded =
			foldable && !_open;

		var body =
			new VerticalStackLayout
			{
				Spacing = 4
			};

		foreach (
			NoticeBlock block
			in blocks.Take(
				folded
					? CollapsedBlocks
					: blocks.Count))
		{
			body.Add(
				BlockView(
					block,
					folded
						? CollapsedLines
						: -1));
		}

		if (foldable)
		{
			var toggleLabel =
				new Label
				{
					Text =
						folded
							? strings.ShowMore
							: strings.ShowLess,
					FontFamily = "OpenSansSemibold",
					FontSize = 13,
					VerticalOptions =
						LayoutOptions.Center
				};

			toggleLabel.SetDynamicResource(
				Label.TextColorProperty,
				accentKey);

			var toggleIcon =
				new Icon
				{
					Glyph =
						folded
							? IconGlyph.ChevronDown
							: IconGlyph.ChevronUp,
					Size = 14,
					VerticalOptions =
						LayoutOptions.Center
				};

			toggleIcon.SetDynamicResource(
				Icon.ColorProperty,
				accentKey);

			var toggle =
				new HorizontalStackLayout
				{
					Spacing = 5,
					Padding =
						new Thickness(
							0,
							6,
							0,
							2),
					Children =
					{
						toggleLabel,
						toggleIcon
					}
				};

			SemanticProperties.SetDescription(
				toggle,
				folded
					? strings.ShowFullNotice
					: strings.ShowLessNotice);

			toggle.GestureRecognizers.Add(
				new TapGestureRecognizer
				{
					Command =
						new Command(Toggle)
				});

			body.Add(toggle);
		}

		var glyph =
			new Icon
			{
				Glyph =
					warning
						? IconGlyph.Warning
						: IconGlyph.Info,
				Size = 18,
				VerticalOptions =
					LayoutOptions.Start
			};

		glyph.SetDynamicResource(
			Icon.ColorProperty,
			accentKey);

		var bar =
			new BoxView
			{
				WidthRequest = 3
			};

		bar.SetDynamicResource(
			BoxView.ColorProperty,
			accentKey);

		var content =
			new Grid
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

		glyph.Margin =
			new Thickness(
				0,
				7,
				0,
				0);

		content.Add(glyph, 1);

		body.Margin =
			new Thickness(
				0,
				8,
				10,
				8);

		content.Add(body, 2);

		var card =
			new Border
			{
				StrokeThickness = 0,
				StrokeShape =
					new RoundRectangle
					{
						CornerRadius =
							new CornerRadius(4)
					},
				Padding = 0,
				Content = content
			};

		card.SetDynamicResource(
			VisualElement.BackgroundColorProperty,
			"Raised");

		string prefix =
			warning
				? strings.Warning
				: strings.Notice;

		SemanticProperties.SetDescription(
			card,
			$"{prefix}: {NoticeText.Plain(blocks)}");

		return card;
	}

	private void Toggle()
	{
		_open = !_open;
		Rebuild();

		if (Content is VisualElement view)
		{
			_ = Motion.RevealAsync(
				view,
				0,
				180,
				4);
		}
	}

	private static View BlockView(
		NoticeBlock block,
		int maxLines)
	{
		Label label =
			TextView(
				block.Spans,
				maxLines);

		if (!block.IsBullet)
		{
			return label;
		}

		var dot =
			new Label
			{
				Text = "\u2022",
				VerticalOptions =
					LayoutOptions.Start
			};

		dot.SetDynamicResource(
			Label.TextColorProperty,
			"InkMuted");

		var row =
			new Grid
			{
				ColumnDefinitions =
				[
					new ColumnDefinition(
						GridLength.Auto),
					new ColumnDefinition(
						GridLength.Star)
				],
				ColumnSpacing = 6
			};

		row.Add(dot, 0);
		row.Add(label, 1);

		return row;
	}

	private static Label TextView(
		IReadOnlyList<NoticeSpan> spans,
		int maxLines)
	{
		var label =
			new Label
			{
				FontSize = 14,
				LineBreakMode =
					maxLines > 0
						? LineBreakMode.TailTruncation
						: LineBreakMode.WordWrap,
				MaxLines = maxLines,
				HorizontalOptions =
					LayoutOptions.Fill
			};

		label.SetDynamicResource(
			Label.TextColorProperty,
			"Ink");

		if (spans.All(s => s.Url is null))
		{
			label.Text =
				string.Concat(
					spans.Select(s => s.Text));

			return label;
		}

		var formatted =
			new FormattedString();

		foreach (NoticeSpan part in spans)
		{
			var span =
				new Span
				{
					Text = part.Text
				};

			if (part.Url is { } url
				&& Uri.TryCreate(
					url,
					UriKind.Absolute,
					out Uri? uri))
			{
				span.TextDecorations =
					TextDecorations.Underline;

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
			System.Diagnostics.Debug.WriteLine(
				$"[NOTICE LINK] Failed to open {uri}: {ex.Message}");
		}
	}
}