using Android.Content;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>Resource ids by name (the app reads its resources by name elsewhere too), cached.</summary>
internal static class WidgetResources
{
	private static readonly Dictionary<string, int> Cache = [];
	private static readonly Lock Gate = new();

	public static int Id(Context context, string name) =>
		Find(context, name, "id");

	public static int Layout(Context context, string name) =>
		Find(context, name, "layout");

	public static int Drawable(Context context, string name) =>
		Find(context, name, "drawable");

	public static int Color(Context context, string name) =>
		Find(context, name, "color");

	/// <summary>The colour as an ARGB value, for tints (night mode follows the configuration).</summary>
	public static int ColorValue(Context context, string name) =>
		context.GetColor(Color(context, name));

	/// <summary>The colour as an Android colour, for text colours.</summary>
	public static global::Android.Graphics.Color ColorOf(Context context, string name) =>
		new(ColorValue(context, name));

	private static int Find(Context context, string name, string type)
	{
		string key = $"{type}/{name}";

		lock (Gate)
		{
			if (!Cache.TryGetValue(key, out int id))
			{
				id = context.Resources?.GetIdentifier(name, type, context.PackageName) ?? 0;
				Cache[key] = id;
			}

			return id;
		}
	}
}
