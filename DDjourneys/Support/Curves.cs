namespace DDjourneys.Support;

/// <summary>
/// Easing curves of Material 3 motion (the cubic-bezier tokens of m3.material.io, "Easing and duration"), plus one
/// overshoot curve. MAUI's built-in easings are all polynomial or sinusoidal; these are the curves M3 uses, with
/// the asymmetry that makes motion feel designed: things that arrive start fast and settle gently, things that leave
/// start slowly and go quickly.
/// </summary>
public static class Curves
{
	/// <summary>Default for most transitions: (0.2, 0, 0, 1).</summary>
	public static readonly Easing Emphasized = Bezier(0.2, 0, 0, 1);

	/// <summary>Something arriving: fast start, gentle settle. (0.05, 0.7, 0.1, 1).</summary>
	public static readonly Easing Decelerate = Bezier(0.05, 0.7, 0.1, 1);

	/// <summary>Something leaving: slow start, fast exit. (0.3, 0, 0.8, 0.15).</summary>
	public static readonly Easing Accelerate = Bezier(0.3, 0, 0.8, 0.15);

	/// <summary>Arrives past its place by a few percent and settles back (ease-out-back): pops, pills, taps.</summary>
	public static readonly Easing Settle = new(t =>
	{
		const double Overshoot = 1.9;

		double u = t - 1;

		return 1 + ((Overshoot + 1) * u * u * u) + (Overshoot * u * u);
	});

	private static Easing Bezier(double x1, double y1, double x2, double y2) =>
		new(t => Solve(t, x1, y1, x2, y2));

	private static double Solve(double t, double x1, double y1, double x2, double y2)
	{
		if (t <= 0)
		{
			return 0;
		}

		if (t >= 1)
		{
			return 1;
		}

		// Newton's method on x(s) = t, which converges in a few steps for these curves ...
		double s = t;

		for (int i = 0; i < 8; i++)
		{
			double error = Sample(s, x1, x2) - t;

			if (Math.Abs(error) < 1e-5)
			{
				return Sample(s, y1, y2);
			}

			double slope = Slope(s, x1, x2);

			if (Math.Abs(slope) < 1e-6)
			{
				break;
			}

			s -= error / slope;
		}

		// ... and bisection when it does not (a nearly flat start).
		double low = 0;
		double high = 1;
		s = t;

		for (int i = 0; i < 24; i++)
		{
			double x = Sample(s, x1, x2);

			if (Math.Abs(x - t) < 1e-5)
			{
				break;
			}

			if (x < t)
			{
				low = s;
			}
			else
			{
				high = s;
			}

			s = (low + high) / 2;
		}

		return Sample(s, y1, y2);
	}

	// One coordinate of a cubic bezier from (0,0) to (1,1) with control values a and b.
	private static double Sample(double s, double a, double b)
	{
		double u = 1 - s;

		return (3 * u * u * s * a) + (3 * u * s * s * b) + (s * s * s);
	}

	private static double Slope(double s, double a, double b)
	{
		double u = 1 - s;

		return (3 * u * u * a) + (6 * u * s * (b - a)) + (3 * s * s * (1 - b));
	}
}
