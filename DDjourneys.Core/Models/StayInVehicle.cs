namespace DDjourneys.Core.Models;

/// <summary>
/// One stop of a run, as far as the caller knows it: the provider's stop id when it has one, always a name,
/// and the time the run reaches it when the schedule carries one.
/// </summary>
public readonly record struct CourseStop(string? Id, string Name, DateTimeOffset? At);

/// <summary>
/// The "stay in the vehicle" verdict of roadmap #6: a journey plans a transfer at stop X, but the run the
/// passenger is riding also serves the journey's destination later in its stop list. Pure containment - the
/// destination must come AFTER the transfer stop in the run's own order, and it is matched by stop id, or,
/// when either side has no id, by normalized name (the <see cref="Services.LineCourseService"/> rule for
/// lines: spaces and case do not matter, but a name must be the WHOLE name - a partial name never matches).
/// </summary>
public static class StayInVehicle
{
	/// <summary>Where the run reaches the destination: the stop, its index in the run, the arrival time the run's schedule carries (when it carries one) and how it was matched.</summary>
	public sealed record Hint(int Index, string Name, DateTimeOffset? At, bool ById);

	/// <summary>
	/// The run also serves the destination after the transfer stop, or null when it does not (also when the
	/// transfer stop is not in the run's list at all - nothing is guessed then).
	/// </summary>
	/// <param name="run">The run's stops in travel order (the vehicle's course, which may reach past the transfer stop).</param>
	/// <param name="transferId">The stop id the plan transfers at, when known.</param>
	/// <param name="transferName">The name of the stop the plan transfers at.</param>
	/// <param name="destinationId">The stop id of the journey's destination, when known.</param>
	/// <param name="destinationName">The name of the journey's destination.</param>
	public static Hint? After(
		IReadOnlyList<CourseStop> run,
		string? transferId,
		string transferName,
		string? destinationId,
		string destinationName)
	{
		ArgumentNullException.ThrowIfNull(run);

		if (run.Count < 2
			|| string.IsNullOrWhiteSpace(transferName)
			|| string.IsNullOrWhiteSpace(destinationName))
		{
			return null;
		}

		int at = -1;

		for (int i = 0; i < run.Count; i++)
		{
			if (IsStop(run[i], transferId, transferName))
			{
				at = i;
				break;
			}
		}

		// The run does not even serve the stop the plan transfers at: no statement.
		if (at < 0)
		{
			return null;
		}

		for (int i = at + 1; i < run.Count; i++)
		{
			if (IsStop(run[i], destinationId, destinationName))
			{
				return new Hint(i, run[i].Name, run[i].At, ById(run[i].Id, destinationId));
			}
		}

		return null;
	}

	private static bool IsStop(CourseStop stop, string? id, string name) =>
		ById(stop.Id, id)
			? string.Equals(stop.Id, id, StringComparison.OrdinalIgnoreCase)
			: SameName(stop.Name, name);

	private static bool ById(string? one, string? other) =>
		!string.IsNullOrEmpty(one) && !string.IsNullOrEmpty(other);

	/// <summary>Spaces and case do not matter; the name must match as a whole (never a part of it).</summary>
	private static bool SameName(string one, string other) =>
		string.Equals(Normalize(one), Normalize(other), StringComparison.OrdinalIgnoreCase);

	private static string Normalize(string name) =>
		string.Concat(name.Where(character => !char.IsWhiteSpace(character)));
}
