using System.Windows.Input;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// One row of a list of options or details: icon, title, a line that says what is behind it and a chevron.
/// Rows that lead somewhere show a right chevron; rows that unfold in place (<see cref="IsExpandable"/>) show a
/// chevron that points down when closed and up when open, so it is always clear what a tap does.
/// </summary>
public sealed class SectionRow : ContentView
{
	public static readonly BindableProperty GlyphProperty =
		BindableProperty.Create(nameof(Glyph), typeof(IconGlyph), typeof(SectionRow), IconGlyph.None,
			propertyChanged: (row, _, value) => ((SectionRow)row)._icon.Glyph = (IconGlyph)value!);

	public static readonly BindableProperty TitleProperty =
		BindableProperty.Create(nameof(Title), typeof(string), typeof(SectionRow), string.Empty,
			propertyChanged: (row, _, value) => ((SectionRow)row)._title.Text = (string?)value);

	public static readonly BindableProperty SummaryProperty =
		BindableProperty.Create(nameof(Summary), typeof(string), typeof(SectionRow), string.Empty,
			propertyChanged: (row, _, value) =>
			{
				var self = (SectionRow)row;
				self._summary.Text = (string?)value;
				self._summary.IsVisible = !string.IsNullOrEmpty((string?)value);
			});

	public static readonly BindableProperty IsExpandableProperty =
		BindableProperty.Create(nameof(IsExpandable), typeof(bool), typeof(SectionRow), false,
			propertyChanged: (row, _, _) => ((SectionRow)row).RefreshChevron());

	public static readonly BindableProperty IsExpandedProperty =
		BindableProperty.Create(nameof(IsExpanded), typeof(bool), typeof(SectionRow), false,
			propertyChanged: (row, _, _) => ((SectionRow)row).RefreshChevron());

	public static readonly BindableProperty CommandProperty =
		BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SectionRow), null);

	private readonly Icon _icon = new() { Size = 22, VerticalOptions = LayoutOptions.Center };
	private readonly Icon _chevron = new() { Size = 18, VerticalOptions = LayoutOptions.Center };
	private readonly Label _title = new() { LineBreakMode = LineBreakMode.TailTruncation };
	private readonly Label _summary = new() { IsVisible = false, StyleClass = ["Caption"] };

	public SectionRow()
	{
		_icon.SetDynamicResource(Icon.ColorProperty, "Accent");
		_chevron.SetDynamicResource(Icon.ColorProperty, "InkMuted");
		_title.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");

		var text =
			new VerticalStackLayout
			{
				Spacing = 1,
				VerticalOptions = LayoutOptions.Center,
				Children = { _title, _summary }
			};

		var grid =
			new Grid
			{
				ColumnSpacing = 12,
				ColumnDefinitions =
				[
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Auto)
				],
				Children = { _icon, text, _chevron }
			};

		Grid.SetColumn(text, 1);
		Grid.SetColumn(_chevron, 2);

		Dense.SetPadding(grid, new Thickness(14, 8));
		Dense.SetMinHeight(grid, 56);

		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) =>
		{
			if (Command is { } command && command.CanExecute(null))
			{
				command.Execute(null);
			}
		};

		grid.GestureRecognizers.Add(tap);
		Motion.SetFeedback(grid, true);

		Content = grid;

		RefreshChevron();
	}

	public IconGlyph Glyph
	{
		get => (IconGlyph)GetValue(GlyphProperty);
		set => SetValue(GlyphProperty, value);
	}

	public string Title
	{
		get => (string)GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public string Summary
	{
		get => (string)GetValue(SummaryProperty);
		set => SetValue(SummaryProperty, value);
	}

	/// <summary>The row unfolds something below it (chevron down/up) instead of leading to another page.</summary>
	public bool IsExpandable
	{
		get => (bool)GetValue(IsExpandableProperty);
		set => SetValue(IsExpandableProperty, value);
	}

	public bool IsExpanded
	{
		get => (bool)GetValue(IsExpandedProperty);
		set => SetValue(IsExpandedProperty, value);
	}

	public ICommand? Command
	{
		get => (ICommand?)GetValue(CommandProperty);
		set => SetValue(CommandProperty, value);
	}

	private void RefreshChevron()
	{
		_chevron.Glyph =
			IsExpandable
				? IsExpanded ? IconGlyph.ChevronUp : IconGlyph.ChevronDown
				: IconGlyph.ChevronRight;

	}
}
