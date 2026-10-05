using System.Windows.Input;
using DDjourneys.Controls;

namespace DDjourneys.Support;

/// <summary>
/// The actions of a journey as icons in its overview card: the PDF, handing it to another app, following it,
/// pausing the following, and the notices. The journey page owns the state; the card only shows it.
/// </summary>
public sealed class JourneyActions : ObservableObject
{
	public ICommand? PdfCommand { get; init; }

	public ICommand? HandOffCommand { get; init; }

	public ICommand? FollowCommand { get; init; }

	public ICommand? PauseCommand { get; init; }

	/// <summary>Scrolls to the notices of the journey.</summary>
	public ICommand? NoticesCommand { get; init; }

	public bool HasPdf
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	public bool HasHandOff
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	/// <summary>The provider can follow journeys at all.</summary>
	public bool CanFollow
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	public bool IsFollowed
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(FollowGlyph));
			}
		}
	}

	/// <summary>Following can be paused (it is followed, running, and not already over).</summary>
	public bool CanPause
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	public bool IsPaused
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(PauseGlyph));
			}
		}
	}

	/// <summary>Pause or resume: shown while it is followed and running or paused.</summary>
	public bool ShowPause =>
		IsFollowed && (CanPause || IsPaused);

	public IconGlyph FollowGlyph =>
		IsFollowed
			? IconGlyph.BellFilled
			: IconGlyph.Bell;

	public IconGlyph PauseGlyph =>
		IsPaused
			? IconGlyph.Play
			: IconGlyph.Pause;

	public string PdfDescription
	{
		get => field;
		set => SetProperty(ref field, value);
	} = string.Empty;

	public string HandOffDescription
	{
		get => field;
		set => SetProperty(ref field, value);
	} = string.Empty;

	public string FollowDescription
	{
		get => field;
		set => SetProperty(ref field, value);
	} = string.Empty;

	public string PauseDescription
	{
		get => field;
		set => SetProperty(ref field, value);
	} = string.Empty;

	/// <summary>Tells the bindings that pause visibility changed after several flags moved.</summary>
	public void Touch() =>
		OnPropertyChanged(nameof(ShowPause));
}
