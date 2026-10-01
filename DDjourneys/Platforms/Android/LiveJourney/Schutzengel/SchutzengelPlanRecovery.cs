using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal static class SchutzengelPlanRecovery
{
	public static bool ContainsPlan(JsonElement plans, string planId)
	{
		if (plans.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in plans.EnumerateObject())
			{
				if (property.Name.Equals("plan_id", StringComparison.OrdinalIgnoreCase)
					|| property.Name.Equals("planId", StringComparison.OrdinalIgnoreCase)
					|| property.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
				{
					if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == planId) return true;
				}
				if (ContainsPlan(property.Value, planId)) return true;
			}
		}
		else if (plans.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in plans.EnumerateArray())
				if (ContainsPlan(item, planId)) return true;
		}
		return false;
	}
}
