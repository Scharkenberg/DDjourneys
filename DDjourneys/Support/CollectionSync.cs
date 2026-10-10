using System.Collections.ObjectModel;

namespace DDjourneys.Support;

/// <summary>
/// Brings an observable list that a <c>BindableLayout</c> shows to new contents with the least work for the layout: what
/// is the same stays (its views are not built again), what differs is replaced in place, the rest is added or removed.
/// Clearing and filling a list instead builds every row's views again on every refresh.
/// </summary>
public static class CollectionSync
{
	public static void Merge<T>(ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, T, bool> same)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(same);

		for (int i = 0; i < source.Count; i++)
		{
			if (i >= target.Count)
			{
				target.Add(source[i]);
			}
			else if (!same(target[i], source[i]))
			{
				target[i] = source[i];
			}
		}

		while (target.Count > source.Count)
		{
			target.RemoveAt(target.Count - 1);
		}
	}
}
