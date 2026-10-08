using System.Collections.Specialized;
using DDjourneys.Controls;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>
/// One lent page inside its host (<see cref="PanePage"/>): a slim header (a back arrow on the left-most pane when the
/// host's own title bar offers no back, the page's title, its toolbar items and a close button on the right-most pane)
/// above the page's content. The content keeps the page's view model. Back and close both close the right-most pane.
/// </summary>
internal sealed partial class PaneSlot : Grid
{
	private readonly Label _title;
	private readonly HorizontalStackLayout _actions;
	private readonly IconButton _close;
	private readonly IconButton _back;
	private readonly Grid _header;
	private View? _content;

	public PaneSlot(PanePage page, View? content, Action close)
	{
		Page = page;
		_content = content;

		// The content no longer inherits the view model from its page.
		BindingContext = page.BindingContext;
		SafeAreaEdges = SafeAreaEdges.None;
		RowSpacing = 0;
		RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		RowDefinitions.Add(new RowDefinition(GridLength.Star));

		_title =
			new Label
			{
				VerticalOptions = LayoutOptions.Center,
				LineBreakMode = LineBreakMode.TailTruncation,
				MaxLines = 1,
				StyleClass = ["Subtitle"]
			};
		SemanticProperties.SetHeadingLevel(_title, SemanticHeadingLevel.Level1);
		_title.SetBinding(Label.TextProperty, static (Page p) => p.Title, source: page);

		_actions =
			new HorizontalStackLayout
			{
				Spacing = 4,
				VerticalOptions = LayoutOptions.Center
			};

		_close =
			new IconButton
			{
				Glyph = IconGlyph.Close,
				TargetSize = Token("HitIconTight", 36),
				Command = new Command(close),
				IsVisible = false
			};

		_back =
			new IconButton
			{
				Glyph = IconGlyph.ArrowLeft,
				TargetSize = Token("HitIconTight", 36),
				Command = new Command(close),
				IsVisible = false
			};

		var header =
			new Grid
			{
				ColumnSpacing = 4,
				Padding = new Thickness(16, 0, 8, 0),
				SafeAreaEdges = SafeAreaEdges.None
			};
		header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		// One fixed height for every pane header, whatever buttons it shows (back, actions, close).
		header.SetDynamicResource(HeightRequestProperty, "HitRow");
		header.Add(_back, 0);
		header.Add(_title, 1);
		header.Add(_actions, 2);
		header.Add(_close, 3);
		_header = header;

		this.Add(header, 0, 0);
		this.Add(new BoxView { StyleClass = ["Divider"] }, 0, 1);

		if (content is not null)
		{
			this.Add(content, 0, 2);
		}

		if (page.ToolbarItems is INotifyCollectionChanged items)
		{
			items.CollectionChanged += OnToolbarChanged;
		}

		UpdateActions();
	}

	public PanePage Page { get; }

	/// <summary>
	/// <paramref name="back"/>: the back arrow (left-most visible pane, when no title bar offers back);
	/// <paramref name="close"/>: the close button (right-most pane, unless it already shows the arrow).
	/// </summary>
	public void SetNavigation(bool back, bool close)
	{
		CommonStrings strings = LocalizationService.Current.CurrentStrings.Common;

		_back.IsVisible = back;
		_close.IsVisible = close && !back;
		_header.Padding = new Thickness(back ? 4 : 16, 0, 8, 0);

		SemanticProperties.SetDescription(_back, strings.Back);
		SemanticProperties.SetDescription(_close, strings.Close);
	}

	/// <summary>Lets go of the page: unsubscribes and hands the content back (it goes home to its page or is dropped).</summary>
	public View? Detach()
	{
		_title.RemoveBinding(Label.TextProperty);

		if (Page.ToolbarItems is INotifyCollectionChanged items)
		{
			items.CollectionChanged -= OnToolbarChanged;
		}

		_actions.Clear();

		View? content = _content;
		_content = null;

		if (content is not null)
		{
			Remove(content);
		}

		return content;
	}

	private void OnToolbarChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
		UpdateActions();

	/// <summary>The page's toolbar items as small tonal buttons (a lent page has no toolbar of its own).</summary>
	private void UpdateActions()
	{
		_actions.Clear();

		foreach (ToolbarItem item in Page.ToolbarItems)
		{
			var button =
				new Button
				{
					Padding = new Thickness(12, 0),
					StyleClass = ["Tonal"]
				};
			button.SetBinding(Button.TextProperty, static (ToolbarItem i) => i.Text, source: item);
			Dense.SetMinHeight(button, Token("HitButton", 36));
			button.Clicked += (_, _) => ((IMenuItemController)item).Activate();

			_actions.Add(button);
		}

		_actions.IsVisible = _actions.Count > 0;
	}

	private static double Token(string key, double fallback) =>
		Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is double size
			? size
			: fallback;
}
