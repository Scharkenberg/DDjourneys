using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Documents;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// Shows a Markdown document with native controls (read by <see cref="MarkdownDocument"/>): headings, paragraphs with
/// emphasis, code and links, lists, quotations, code blocks and rules, in the fonts and colours of the app theme.
/// A tap on a link is reported through <see cref="LinkTapped"/>; the page decides what to do with it.
/// </summary>
public sealed partial class MarkdownView : ContentView
{
	public static readonly BindableProperty MarkdownProperty =
		BindableProperty.Create(
			nameof(Markdown),
			typeof(string),
			typeof(MarkdownView),
			string.Empty,
			propertyChanged: (view, _, value) => ((MarkdownView)view).Render((string?)value));

	private readonly VerticalStackLayout _body =
		new()
		{
			Spacing = 12
		};

	private bool _subscribed;

	public MarkdownView()
	{
		Content = _body;

		HandlerChanged += OnHandlerChanged;
	}

	// The colours are resolved when the views are built: on a theme change the document is built again.
	private void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler is null)
		{
			if (_subscribed)
			{
				Theme.Changed -= OnThemeChanged;
				_subscribed = false;
			}

			return;
		}

		if (!_subscribed)
		{
			Theme.Changed += OnThemeChanged;
			_subscribed = true;
		}
	}

	private void OnThemeChanged(object? sender, EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(() => Render(Markdown));

	public string Markdown
	{
		get => (string)GetValue(MarkdownProperty);
		set => SetValue(MarkdownProperty, value);
	}

	/// <summary>A link was tapped (the address as written in the document).</summary>
	public event EventHandler<string>? LinkTapped;

	private void Render(string? markdown)
	{
		_body.Children.Clear();

		try
		{
			foreach (MarkdownBlock block in MarkdownDocument.Parse(markdown))
			{
				if (Build(block) is { } view)
				{
					_body.Children.Add(view);
				}
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Markdown not shown: {ex.Message}");
		}
	}

	private View? Build(MarkdownBlock block) =>
		block switch
		{
			MarkdownHeading heading => Heading(heading),
			MarkdownParagraph paragraph => Text(paragraph.Content),
			MarkdownCode code => Code(code),
			MarkdownList list => List(list),
			MarkdownQuote quote => Quote(quote),
			MarkdownRule => new BoxView { StyleClass = ["Divider"], Margin = new Thickness(0, 4) },
			_ => null
		};

	private Label Heading(MarkdownHeading heading)
	{
		Label label = Text(heading.Content);

		SemanticProperties.SetHeadingLevel(label, heading.Level == 1 ? SemanticHeadingLevel.Level1 : SemanticHeadingLevel.Level2);

		switch (heading.Level)
		{
			case 1:
				label.StyleClass = ["Title"];
				label.Margin = new Thickness(0, 0, 0, 4);
				break;
			case 2:
				label.StyleClass = ["Subtitle"];
				label.Margin = new Thickness(0, 12, 0, 0);
				break;
			default:
				label.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");
				label.Margin = new Thickness(0, 8, 0, 0);
				break;
		}

		return label;
	}

	/// <summary>One label whose text is built from spans, so links and emphasis share a line.</summary>
	private Label Text(IReadOnlyList<MarkdownInline> inlines)
	{
		var text = new FormattedString();

		foreach (MarkdownInline inline in inlines)
		{
			text.Spans.Add(CreateSpan(inline));
		}

		var label =
			new Label
			{
				FormattedText = text,
				LineBreakMode = LineBreakMode.WordWrap,
				LineHeight = 1.25
			};

		label.SetDynamicResource(Label.TextColorProperty, "Ink");

		return label;
	}

	private Span CreateSpan(MarkdownInline inline)
	{
		var span = new Span { Text = inline.Text };

		bool link = inline.Style.HasFlag(InlineStyle.Link);

		if (inline.Style.HasFlag(InlineStyle.Code))
		{
			span.FontFamily = MonospaceFont;
			span.SetDynamicResource(Microsoft.Maui.Controls.Span.BackgroundColorProperty, "Raised");
		}
		else if (inline.Style.HasFlag(InlineStyle.Bold))
		{
			span.SetDynamicResource(Microsoft.Maui.Controls.Span.FontFamilyProperty, "FontSemibold");
		}

		if (inline.Style.HasFlag(InlineStyle.Italic))
		{
			span.FontAttributes = FontAttributes.Italic;
		}

		if (link)
		{
			span.SetDynamicResource(Microsoft.Maui.Controls.Span.TextColorProperty, "Accent");
			span.TextDecorations = TextDecorations.Underline;

			if (inline.Url is { Length: > 0 } url)
			{
				var tap = new TapGestureRecognizer();
				tap.Tapped += (_, _) => LinkTapped?.Invoke(this, url);
				span.GestureRecognizers.Add(tap);
			}
		}

		return span;
	}

	private static string MonospaceFont =>
		DeviceInfo.Platform == DevicePlatform.Android
			? "monospace"
			: "Consolas";

	private static Border Code(MarkdownCode code)
	{
		var label =
			new Label
			{
				Text = code.Text,
				FontFamily = MonospaceFont,
				FontSize = 13,
				LineBreakMode = LineBreakMode.NoWrap
			};

		label.SetDynamicResource(Label.TextColorProperty, "Ink");

		// A long line scrolls sideways instead of wrapping: code is read as it is written.
		var scroll =
			new ScrollView
			{
				Orientation = ScrollOrientation.Horizontal,
				Content = label
			};

		var border =
			new Border
			{
				Content = scroll,
				Padding = new Thickness(12, 8),
				StrokeThickness = 0,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }
			};

		border.SetDynamicResource(Border.BackgroundColorProperty, "Raised");

		return border;
	}

	private Grid List(MarkdownList list)
	{
		var grid =
			new Grid
			{
				ColumnDefinitions =
				[
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Star)
				],
				ColumnSpacing = 8,
				RowSpacing = 6
			};

		for (int row = 0; row < list.Items.Count; row++)
		{
			grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

			var marker =
				new Label
				{
					Text = list.Ordered ? $"{list.Start + row}." : "•",
					HorizontalTextAlignment = TextAlignment.End
				};

			marker.SetDynamicResource(Label.TextColorProperty, "InkMuted");

			Label item = Text(list.Items[row]);

			grid.Add(marker, 0, row);
			grid.Add(item, 1, row);
		}

		return grid;
	}

	private Grid Quote(MarkdownQuote quote)
	{
		var inner = new VerticalStackLayout { Spacing = 8 };

		foreach (MarkdownBlock block in quote.Blocks)
		{
			if (Build(block) is { } view)
			{
				inner.Children.Add(view);
			}
		}

		var bar = new BoxView { WidthRequest = 3, CornerRadius = 1.5 };

		bar.SetDynamicResource(BoxView.ColorProperty, "Outline");

		var grid =
			new Grid
			{
				ColumnDefinitions =
				[
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Star)
				],
				ColumnSpacing = 10
			};

		grid.Add(bar, 0);
		grid.Add(inner, 1);

		return grid;
	}
}
