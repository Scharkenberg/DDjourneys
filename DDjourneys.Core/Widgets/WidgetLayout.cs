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

	// As dense as the layout is: the header is one 12 sp line, a row one 12 sp line (two at Full), with 1 dp between.
	private const double HeaderHeight = 20;
	private const double Padding = 8;

	private const double CompactWidth = 170;
	private const double FullWidth = 230;

	public static double RowHeight(WidgetDetail detail) =>
		detail switch
		{
			WidgetDetail.Minimal => 19,
			WidgetDetail.Compact => 19,
			_ => 30
		};

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

		int fit = (int)Math.Floor((height - ((HeaderHeight + Padding) * scale)) / (RowHeight(detail) * scale));

		int rows = Math.Clamp(fit, 1, MaxRows);

		if (maxRows > 0)
		{
			rows = Math.Min(rows, maxRows);
		}

		return new WidgetLayout(rows, detail, detail == WidgetDetail.Full && height >= 160);
	}
}
