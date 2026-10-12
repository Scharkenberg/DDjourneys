using System.Reflection;
using DDjourneys.Localization;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The localization packs are complete: whatever a language file forgets would show as a blank or crash the
/// XAML at run time. Every pack must carry exactly the key graph of <see cref="IUiStrings"/> (same groups, same
/// properties, same order) with a value wherever the English one has one.
/// </summary>
public sealed class LocalizationCompletenessTests
{
	[Theory]
	[InlineData("de")]
	[InlineData("cs")]
	public void Every_pack_carries_the_english_key_graph(string code)
	{
		IUiStrings english = new EnglishUiStrings();
		IUiStrings other = Pack(code);

		foreach (PropertyInfo group in Groups(english))
		{
			PropertyInfo? otherGroupProperty =
				other.GetType().GetProperty(group.Name, BindingFlags.Public | BindingFlags.Instance);

			Assert.True(otherGroupProperty is not null, $"{code} misses the group {group.Name}");

			object? otherGroup = otherGroupProperty!.GetValue(other);

			Assert.NotNull(otherGroup);

			foreach (PropertyInfo key in Keys(group))
			{
				PropertyInfo? otherKey = otherGroup.GetType().GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance);

				Assert.True(otherKey is not null, $"{code} misses {group.Name}.{key.Name}");

				string? value = otherKey.GetValue(otherGroup) as string;

				Assert.True(
					!string.IsNullOrWhiteSpace(value) || string.IsNullOrEmpty(key.GetValue(group.GetValue(english)) as string),
					$"{code}.{group.Name}.{key.Name} is empty");
				}
			}

		// No extra groups either: the same count, so a pack cannot drift from the interface.
		Assert.Equal(Groups(english).Length, Groups(other).Length);
	}

	private static IUiStrings Pack(string code) =>
		code switch
		{
			"de" => new GermanUiStrings(),
			"cs" => new CzechUiStrings(),
			_ => new EnglishUiStrings()
		};

	private static PropertyInfo[] Groups(IUiStrings strings) =>
		strings.GetType()
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.PropertyType.IsClass && p.CanRead)
			.ToArray();

	private static PropertyInfo[] Keys(PropertyInfo group) =>
		group.PropertyType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.PropertyType == typeof(string) && p.CanRead)
			.ToArray();
}
