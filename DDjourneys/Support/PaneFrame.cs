using Microsoft.Maui.Controls.Foldable;

namespace DDjourneys.Support;

/// <summary>Where a fold or hinge divides the frame: its offset from the frame's left (vertical) or top (horizontal) edge and its width.</summary>
internal readonly record struct FoldLayout(bool Vertical, double Offset, double Gap)
{
	public static FoldLayout None { get; } = new(false, -1, 0);

	public bool Exists => Offset >= 0;
}

/// <summary>
/// The pane frame of a <see cref="PanePage"/>: any number of panes (up to <see cref="PaneRules.MaxPanes"/>) side by side.
/// <para>
/// Fold awareness comes from MAUI's <see cref="TwoPaneView"/> (Microsoft.Maui.Controls.Foldable, Jetpack WindowManager on
/// Android), which this frame is. It is configured to split only when the window spans a fold, and then lays out its own
/// three rows and columns around the fold, relative to this frame; the frame reads them after each measure
/// (<see cref="Fold"/>). Its two pane containers stay empty, because a view handed between them would change its parent
/// (which breaks native layout on Windows). The panes live in one grid of their own (<see cref="Strip"/>) spanning the
/// frame: they only ever change cell, never parent.
/// </para>
/// <para>
/// No fold: equal columns. A vertical fold: the panes are split between the two sides, each side as many as fit (at least
/// one), the deepest on the right side; the fold itself stays empty. On a tri-fold MAUI reports one of the two folds, and
/// the side holding two sections gets two equal columns, whose border falls on the other fold. A horizontal fold
/// (tabletop): the deepest pane above it, the others in columns below it, where the hands are.
/// </para>
/// </summary>
internal sealed partial class PaneFrame : TwoPaneView
{
	private readonly Grid _strip;
	private readonly List<BoxView> _lines = [];
	private IReadOnlyList<View> _visible = [];
	private string? _shape;
	private (int Row, int Column, int Span)[] _cells = [];
	private double[] _lefts = [];
	private bool _leftsRelative;
	private List<(int Row, int Column, bool Horizontal)> _wantedLines = [];

	public PaneFrame()
	{
		IsClippedToBounds = true;

		// The frame is the page's direct content: it keeps everything out of the status and navigation bars itself,
		// from the first layout on (nested layouts only picked the insets up after a later re-layout).
		SafeAreaEdges = SafeAreaEdges.Container;

		// Split only across a fold; one screen is never split by TwoPaneView itself.
		MinWideModeWidth = double.MaxValue;
		MinTallModeHeight = double.MaxValue;
		WideModeConfiguration = TwoPaneViewWideModeConfiguration.LeftRight;
		TallModeConfiguration = TwoPaneViewTallModeConfiguration.TopBottom;

		_strip =
			new Grid
			{
				ColumnSpacing = 0,
				RowSpacing = 0,
				SafeAreaEdges = SafeAreaEdges.None,
				ZIndex = 1
			};

		Children.Add(_strip);
		SetRowSpan(_strip as IView, 3);
		SetColumnSpan(_strip as IView, 3);

		// Absolute columns on a fold follow the width.
		_strip.SizeChanged += (_, _) => Relayout();
	}

	/// <summary>The one parent of every pane of the page.</summary>
	public Grid Strip => _strip;

	/// <summary>The fold as TwoPaneView placed it in this frame, or <see cref="FoldLayout.None"/>.</summary>
	public FoldLayout Fold { get; private set; } = FoldLayout.None;

	/// <summary>A fold appeared, moved or went (raised after the layout pass).</summary>
	public event EventHandler? FoldChanged;

	/// <summary>How many panes the frame can show now, given the count shown now (for the width margin).</summary>
	public int Capacity(int current)
	{
		double width = Width;

		if (width <= 0)
		{
			return 1;
		}

		FoldLayout fold = Fold;

		if (fold.Exists && fold.Vertical)
		{
			return Math.Min(
				PaneRules.MaxPanes,
				PaneRules.Region(fold.Offset) + PaneRules.Region(width - fold.Offset - fold.Gap));
		}

		if (fold.Exists)
		{
			// Tabletop: one pane above the fold, the others side by side below it; only where the width already splits.
			int below = PaneRules.Columns(width, Math.Max(1, current - 1));

			return below >= 2
				? Math.Min(PaneRules.MaxPanes, below + 1)
				: below;
		}

		return PaneRules.Columns(width, current);
	}

	/// <summary>Shows <paramref name="visible"/> (left to right; the last is the deepest) in their cells.</summary>
	public void Layout(IReadOnlyList<View> visible)
	{
		_visible = visible;
		Relayout();
	}

	/// <summary>Left edge of the cell of visible pane <paramref name="index"/> in the current layout (for motion).</summary>
	public double LeftOf(int index)
	{
		if (index < 0 || index >= _lefts.Length)
		{
			return 0;
		}

		double width = _strip.Width > 0 ? _strip.Width : Width;

		return _leftsRelative ? _lefts[index] * width : _lefts[index];
	}

	protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
	{
		Size size = base.MeasureOverride(widthConstraint, heightConstraint);

		FoldLayout fold = ReadFold();

		if (fold != Fold)
		{
			Fold = fold;

			// Not inside the layout pass.
			Dispatcher.Dispatch(
				() =>
				{
					_shape = null;
					Relayout();
					FoldChanged?.Invoke(this, EventArgs.Empty);
				});
		}

		return size;
	}

	/// <summary>TwoPaneView only leaves single-pane mode across a fold, and then sizes its rows or columns around it.</summary>
	private FoldLayout ReadFold()
	{
		if (Mode == TwoPaneViewMode.Wide
			&& ColumnDefinitions.Count == 3
			&& ColumnDefinitions[0].Width.IsAbsolute
			&& ColumnDefinitions[1].Width.IsAbsolute)
		{
			return new FoldLayout(true, ColumnDefinitions[0].Width.Value, ColumnDefinitions[1].Width.Value);
		}

		if (Mode == TwoPaneViewMode.Tall
			&& RowDefinitions.Count == 3
			&& RowDefinitions[0].Height.IsAbsolute
			&& RowDefinitions[1].Height.IsAbsolute)
		{
			return new FoldLayout(false, RowDefinitions[0].Height.Value, RowDefinitions[1].Height.Value);
		}

		return FoldLayout.None;
	}

	/// <summary>Rows, columns, cells and hairlines for the visible panes; definitions are replaced only when the shape changes.</summary>
	private void Relayout()
	{
		int count = _visible.Count;

		if (count == 0)
		{
			return;
		}

		double width = _strip.Width > 0 ? _strip.Width : Width;
		FoldLayout fold = Fold;
		bool tall = count >= 2 && fold.Exists && !fold.Vertical;
		bool atFold = count >= 2 && fold.Exists && fold.Vertical && width > fold.Offset + fold.Gap;

		string shape = $"{count} {fold} {(atFold ? Math.Round(width) : 0)}";

		if (shape != _shape)
		{
			_shape = shape;
			BuildShape(count, width, fold, tall, atFold);
		}

		for (int i = 0; i < count && i < _cells.Length; i++)
		{
			View view = _visible[i];
			(int row, int column, int span) = _cells[i];

			// Only real changes: an unchanged cell must not invalidate the layout.
			if (Grid.GetRow(view) != row)
			{
				Grid.SetRow(view, row);
			}

			if (Grid.GetRowSpan(view) != 1)
			{
				Grid.SetRowSpan(view, 1);
			}

			if (Grid.GetColumn(view) != column)
			{
				Grid.SetColumn(view, column);
			}

			if (Grid.GetColumnSpan(view) != span)
			{
				Grid.SetColumnSpan(view, span);
			}
		}
	}

	/// <summary>Definitions, cells, cell positions and hairlines of one shape (pane count, fold, width on a fold).</summary>
	private void BuildShape(int count, double width, FoldLayout fold, bool tall, bool atFold)
	{
		double[] lefts = new double[count];

		var columns = new ColumnDefinitionCollection();
		var rows = new RowDefinitionCollection();
		var cells = new (int Row, int Column, int Span)[count];
		var lines = new List<(int Row, int Column, bool Horizontal)>();

		if (tall)
		{
			rows.Add(new RowDefinition(new GridLength(fold.Offset)));
			rows.Add(new RowDefinition(new GridLength(fold.Gap)));
			rows.Add(new RowDefinition(GridLength.Star));

			int below = count - 1;

			for (int i = 0; i < below; i++)
			{
				columns.Add(new ColumnDefinition(GridLength.Star));
				cells[i] = (2, i, 1);
				lefts[i] = (double)i / below;

				lines.Add(i == 0 ? (2, 0, true) : (2, i, false));
			}

			cells[count - 1] = (0, 0, below);
		}
		else if (atFold)
		{
			double first = fold.Offset;
			double second = width - fold.Offset - fold.Gap;

			// The deepest panes on the far side of the fold; each side keeps at least one.
			int right = Math.Min(PaneRules.Region(second), count - 1);
			int left = Math.Min(PaneRules.Region(first), count - right);
			right = count - left;

			for (int i = 0; i < left; i++)
			{
				columns.Add(new ColumnDefinition(new GridLength(first / left)));
				cells[i] = (0, i, 1);
				lefts[i] = first * i / left;

				if (i > 0)
				{
					lines.Add((0, i, false));
				}
			}

			columns.Add(new ColumnDefinition(new GridLength(fold.Gap)));

			for (int i = 0; i < right; i++)
			{
				columns.Add(new ColumnDefinition(GridLength.Star));
				cells[left + i] = (0, left + 1 + i, 1);
				lefts[left + i] = first + fold.Gap + (second * i / right);

				// A fold without width (a flat foldable) gets the hairline; a hinge separates by itself.
				if (i > 0 || fold.Gap < 1)
				{
					lines.Add((0, left + 1 + i, false));
				}
			}
		}
		else
		{
			for (int i = 0; i < count; i++)
			{
				columns.Add(new ColumnDefinition(GridLength.Star));
				cells[i] = (0, i, 1);
				lefts[i] = (double)i / count;

				if (i > 0)
				{
					lines.Add((0, i, false));
				}
			}
		}

		if (rows.Count == 0)
		{
			rows.Add(new RowDefinition(GridLength.Star));
		}

		_strip.ColumnDefinitions = columns;
		_strip.RowDefinitions = rows;
		_cells = cells;
		_lefts = lefts;

		// Equal columns are fractions of the width; columns on a fold are absolute (the shape changes with the width there).
		_leftsRelative = !atFold;

		PlaceLines(lines);
	}

	/// <summary>A hairline on the left edge of every pane after the first (or on the top edge of the panes below a fold).</summary>
	private void PlaceLines(List<(int Row, int Column, bool Horizontal)> wanted)
	{
		while (_lines.Count < wanted.Count)
		{
			var line = new BoxView { InputTransparent = true, ZIndex = 2 };
			line.SetDynamicResource(BoxView.ColorProperty, "Outline");
			_strip.Add(line);
			_lines.Add(line);
		}

		for (int i = 0; i < _lines.Count; i++)
		{
			BoxView line = _lines[i];

			if (i >= wanted.Count)
			{
				line.IsVisible = false;
				continue;
			}

			(int row, int column, bool horizontal) = wanted[i];

			Grid.SetRow(line, row);
			Grid.SetColumn(line, column);
			Grid.SetColumnSpan(line, horizontal ? Math.Max(1, _strip.ColumnDefinitions.Count) : 1);
			line.WidthRequest = horizontal ? -1 : 1;
			line.HeightRequest = horizontal ? 1 : -1;
			line.HorizontalOptions = horizontal ? LayoutOptions.Fill : LayoutOptions.Start;
			line.VerticalOptions = horizontal ? LayoutOptions.Start : LayoutOptions.Fill;
			line.IsVisible = true;
		}
	}
}
