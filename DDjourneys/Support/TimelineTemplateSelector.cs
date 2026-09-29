namespace DDjourneys.Support;

/// <summary>Picks the row template by row type. Templates are set in XAML.</summary>
public sealed class TimelineTemplateSelector : DataTemplateSelector
{
	public DataTemplate? Stop { get; set; }
	public DataTemplate? Intermediate { get; set; }
	public DataTemplate? Leg { get; set; }
	public DataTemplate? Walk { get; set; }
	public DataTemplate? Interchange { get; set; }
	public DataTemplate? Notice { get; set; }

	protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
		(item switch
		{
			StopRow => Stop,
			IntermediateRow => Intermediate,
			LegRow => Leg,
			WalkRow => Walk,
			InterchangeRow => Interchange,
			NoticeRow => Notice,
			_ => null
		}) ?? throw new InvalidOperationException($"No timeline template for {item.GetType().Name}.");
}