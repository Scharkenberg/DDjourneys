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
/// launcher allows is 120 x 120 dp; every size above shows more rows and more of each row.
/// </summary>
public sealed record WidgetLayout(int Rows, WidgetDetail Detail, bool ShowUpdated)
{
	/// <summary>Row slots in the widget layout.</summary>
	public const int MaxRows = 10;

	public const double MinSize = 120;

	private const double HeaderHeight = 40;
	private const double Padding = 16;

	private const double CompactWidth = 180;
	private const double FullWidth = 260;

	public static double RowHeight(WidgetDetail detail) =>
		detail switch
		{
			WidgetDetail.Minimal => 28,
			WidgetDetail.Compact => 34,
			_ => 44
		};

	/// <param name="widthDp">Width the launcher gives the widget.</param>
	/// <param name="heightDp">Height the launcher gives the widget.</param>
	/// <param name="maxRows">The user's cap; 0 fits as many as the height allows.</param>
	public static WidgetLayout For(double widthDp, double heightDp, int maxRows = 0)
	{
		double width = Math.Max(widthDp, MinSize);
		double height = Math.Max(heightDp, MinSize);

		WidgetDetail detail =
			width < CompactWidth
				? WidgetDetail.Minimal
				: width < FullWidth
					? WidgetDetail.Compact
					: WidgetDetail.Full;

		int fit = (int)Math.Floor((height - HeaderHeight - Padding) / RowHeight(detail));

		int rows = Math.Clamp(fit, 1, MaxRows);

		if (maxRows > 0)
		{
			rows = Math.Min(rows, maxRows);
		}

		return new WidgetLayout(rows, detail, detail == WidgetDetail.Full && height >= 160);
	}
}
