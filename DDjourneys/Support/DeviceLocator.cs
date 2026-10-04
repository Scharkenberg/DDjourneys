using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>Why a device position could not be had.</summary>
public enum DeviceLocationFailure
{
	None,
	PermissionDenied,
	Unavailable
}

/// <summary>
/// The device's current position: asks for the location permission when needed, then takes a fresh
/// fix (a cached one can be hours old, which would put the passenger at the wrong stop).
/// </summary>
public sealed class DeviceLocator
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

	/// <returns>The position, or null with the reason in <see cref="Failure"/>.</returns>
	public async Task<(double Latitude, double Longitude)?> LocateAsync(
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Permission APIs and sensors expect the main thread.
			return await MainThread.InvokeOnMainThreadAsync(
				() => LocateCoreAsync(cancellationToken)).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is FeatureNotSupportedException
			or FeatureNotEnabledException
			or PermissionException
			or OperationCanceledException)
		{
			DiagnosticLog.Write($"Device location failed: {ex.Message}");

			Failure = ex is PermissionException
				? DeviceLocationFailure.PermissionDenied
				: DeviceLocationFailure.Unavailable;

			return null;
		}
	}

	/// <summary>Reason the last <see cref="LocateAsync"/> returned null.</summary>
	public DeviceLocationFailure Failure { get; private set; }

	private async Task<(double Latitude, double Longitude)?> LocateCoreAsync(
		CancellationToken cancellationToken)
	{
		Failure = DeviceLocationFailure.None;

		PermissionStatus status =
			await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

		if (status != PermissionStatus.Granted)
		{
			status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
		}

		if (status != PermissionStatus.Granted)
		{
			Failure = DeviceLocationFailure.PermissionDenied;

			return null;
		}

		Microsoft.Maui.Devices.Sensors.Location? fix =
			await Geolocation.Default.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, Timeout),
				cancellationToken);

		if (fix is null)
		{
			Failure = DeviceLocationFailure.Unavailable;

			return null;
		}

		return (fix.Latitude, fix.Longitude);
	}
}
