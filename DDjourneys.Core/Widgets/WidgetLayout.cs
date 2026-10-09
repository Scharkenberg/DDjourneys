namespace DDjourneys.Core.Widgets;

/// <summary>How much a widget shows, by its size.</summary>
public enum WidgetDetail
{
	/// <summary>Chip and time only.</summary>
	Minimal = 0,

	/// <summary>Chip, main text and time.</summary>
	Compact,

	/// <summary>Everything: second line, delay, the time of the last update.</summary>
	Full
}

/// <summary>
/// The number of rows and the level of detail for a widget of a given size (in dp). The smallest widget the
/// launcher allows is 2 cells wide and 1 high (120 x 54 dp, one row under the header); every size above shows more rows
/// and more of each row.
/// </summary>
public sealed record WidgetLayout(int Rows, WidgetDetail Detail, bool ShowUpdated)
{
	/// <summary>Row slots in the widget layout.</summary>
	public const int MaxRows = 10;

	public const double MinWidth = 120;

	/// <summary>One launcher cell (the smallest height a widget can be given).</summary>
	public const double MinHeight = 54;

	// What the layout really is (widget_main.xml), in dp at the normal text size; text grows with the system's text scale,
	// the icon of the header and the paddings do not. An estimate that is too generous leaves an empty strip under the last
	// row (it was 6 dp for the header and 1.5 dp for every row), one that is too small clips the last row: so the numbers
	// are the measured ones plus half a dp, and what is left over is spread over the rows (see <see cref="RowPadding"/>).
	private const double HeaderIcon = 16;
	private const double HeaderText = 14.1;
	private const double Padding = 6;
	private const double RowPaddingBase = 2;
	private const double RowText = 15;
	private const double RowTextWithSecondLine = 25.8;
	private const double Safety = 0.5;

	/// <summary>The most the leftover space adds to a row (dp, half above and half below the text).</summary>
	private const double MaxExtraPerRow = 6;

	private const double CompactWidth = 170;
	private const double FullWidth = 230;

	/// <summary>The height of one row without extra space, at the given text scale.</summary>
	public static double RowHeight(WidgetDetail detail, double fontScale = 1) =>
		((detail == WidgetDetail.Full ? RowTextWithSecondLine : RowText) * Math.Max(1, fontScale)) + RowPaddingBase + Safety;

	/// <summary>The height of the header line: the icon does not scale with the text.</summary>
	public static double HeaderHeight(double fontScale = 1) =>
		Math.Max(HeaderIcon, HeaderText * Math.Max(1, fontScale));

	/// <summary>
	/// The padding (dp, above and below each row together) that spreads the space the widget has left under its
	/// <paramref name="shownRows"/> over the rows, so the rows fill the widget instead of sitting at the top of an empty
	/// strip. Never negative, never more than <see cref="MaxExtraPerRow"/> a row, and 15 % of the space stays unused
	/// as a margin for the estimate.
	/// </summary>
	public static double RowPadding(double heightDp, int shownRows, WidgetDetail detail, double fontScale = 1, bool message = false)
	{
		if (shownRows <= 0)
		{
			return 0;
		}

		double scale = Math.Max(1, fontScale);
		double used = Padding + HeaderHeight(scale) + (message ? (11 * 1.17 * scale) + 3 : 0) + (shownRows * RowHeight(detail, scale));
		double spare = Math.Max(heightDp, MinHeight) - used;

		return Math.Clamp(spare * 0.85 / shownRows, 0, MaxExtraPerRow);
	}

	/// <param name="widthDp">Width the launcher gives the widget.</param>
	/// <param name="heightDp">Height the launcher gives the widget.</param>
	/// <param name="maxRows">The user's cap; 0 fits as many as the height allows.</param>
	/// <param name="fontScale">The system's text scale: sp text grows with it, so fewer rows fit.</param>
	public static WidgetLayout For(double widthDp, double heightDp, int maxRows = 0, double fontScale = 1)
	{
		double width = Math.Max(widthDp, MinWidth);
		double height = Math.Max(heightDp, MinHeight);

		WidgetDetail detail =
			width < CompactWidth
				? WidgetDetail.Minimal
				: width < FullWidth
					? WidgetDetail.Compact
					: WidgetDetail.Full;

		double scale = Math.Max(1, fontScale);

		int fit = (int)Math.Floor((height - Padding - HeaderHeight(scale)) / RowHeight(detail, scale));

		int rows = Math.Clamp(fit, 1, MaxRows);

		if (maxRows > 0)
		{
			rows = Math.Min(rows, maxRows);
		}

		return new WidgetLayout(rows, detail, detail == WidgetDetail.Full && height >= 160);
	}
}
