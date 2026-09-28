using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Diagnostics;

/// <summary>
/// Provides diagnostic helpers for validating journey provider integration.
/// </summary>
public sealed class JourneyProviderDiagnostics
{
	private readonly JourneyService _journeyService;


	public JourneyProviderDiagnostics(
		JourneyService journeyService)
	{
		ArgumentNullException.ThrowIfNull(journeyService);

		_journeyService = journeyService;
	}


	/// <summary>
	/// Executes a simple journey search between two known provider locations.
	/// </summary>
	public async Task<JourneyResult> TestVvoAsync(
		string fromId,
		string toId,
		CancellationToken cancellationToken = default)
	{
		var query = new JourneyQuery
		{
			From = new Location
			{
				Id = fromId,
				Name = fromId
			},

			To = new Location
			{
				Id = toId,
				Name = toId
			},

			DateTime = DateTimeOffset.Now.AddMinutes(10),

			SearchMode = JourneySearchMode.Departure,

			MaxResults = 5
		};


		return await _journeyService.SearchAsync(
			query,
			cancellationToken)
			.ConfigureAwait(false);
	}
}