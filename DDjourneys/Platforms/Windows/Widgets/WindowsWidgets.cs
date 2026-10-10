using System.Runtime.InteropServices;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support.Widgets;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace DDjourneys.Platforms.Windows.Widgets;

/// <summary>
/// The Windows Widgets Board wiring of the app (verified against the Windows App SDK docs, "Implement a
/// widget provider in a C# Windows app"): the board CoCreates a class, so the app registers a class factory
/// for <see cref="ProviderClass"/> at startup (a COM launch of the exe carries "-Embedding", which the
/// notification host already knows) and the factory hands out <see cref="DdWidgetProvider"/> instances.
/// The pin API does not exist (WidgetManager knows GetWidgetInfos, UpdateWidget and SendMessageToContent):
/// the user pins from the board's picker, and the widget's set-up action opens this app's set-up page.
/// </summary>
internal static class WindowsWidgets
{
	/// <summary>The class the board CoCreates; the same value as the manifest's com:Class and CreateInstance ClassId.</summary>
	internal const string ProviderClassId = "8F1D5A53-2A1F-4B0B-B5E1-1D2A6C90F3D7";

	/// <summary>The widget store (set while the app builds).</summary>
	internal static IWidgetStore? Store { get; private set; }

	/// <summary>The loader that fetches rows (set while the app builds).</summary>
	internal static Support.Widgets.WidgetLoader? Loader { get; private set; }

	private static readonly Lock Gate = new();
	private static uint _registrationCookie;
	private static string? _pendingSetup;

	internal static bool HasPinnedWidgets =>
		PinnedIds().Length > 0;

	/// <summary>Called while the app builds: the store, the loader, the class object, the refresh cycle.</summary>
	internal static void Initialize(
		IWidgetStore store,
		Support.Widgets.WidgetLoader loader)
	{
		Store = store;
		Loader = loader;

		RegisterClassObject();
		WidgetUpdaterWin.StartCycle();
	}

	/// <summary>Registers the widget provider class object, as the docs' Program.cs does.</summary>
	private static void RegisterClassObject()
	{
		try
		{
			int hr = CoRegisterClassObject(
				Guid.Parse(ProviderClassId),
				new WidgetProviderFactory(),
				0x4, // CLSCTX_LOCAL_SERVER
				0x1, // REGCLS_SINGLEUSE
				out uint cookie);

			Marshal.ThrowExceptionForHR(hr);

			lock (Gate)
			{
				_registrationCookie = cookie;
			}

			WindowsTrace.Write($"[Widgets] class object registered ({ProviderClassId})");
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Registering the widget provider failed", ex);
		}
	}

	/// <summary>Opens the app as the widget's "open" action asks: the board of the configured stop, or the plan of the saved route.</summary>
	internal static void OpenApp(string id)
	{
		WidgetConfig? config = Store?.LoadConfig(id);

		if (config is null || !config.IsComplete)
		{
			OpenSetup(id);

			return;
		}

		// The contract links are the one source of truth for where the app can be sent.
		Uri link =
			config.Kind is WidgetKind.Departures or WidgetKind.Arrivals
				? Contract.ContractLinks.Departures(Place(config.Stop))
				: Contract.ContractLinks.Go(Place(config.To), Place(config.From));

		_ = OpenAsync(link, id);
	}

	/// <summary>Opens the set-up page for one widget (its "set up" action); waits for the shell like the quick actions do.</summary>
	internal static void OpenSetup(string id)
	{
		lock (Gate)
		{
			_pendingSetup = id;
		}

		_ = DeliverSetupAsync();
	}

	private static async Task DeliverSetupAsync()
	{
		string? id;

		lock (Gate)
		{
			id = _pendingSetup;
		}

		if (id is null)
		{
			return;
		}

		// Like the quick actions: carry it out as soon as the shell is there, retrying quietly.
		for (int tries = 0; tries < 40; tries++)
		{
			try
			{
				bool delivered =
					await MainThread.InvokeOnMainThreadAsync(
						() =>
						{
							if (App.Current is not { } app
									|| app.Windows.Count == 0)
							{
								return false;
							}

							lock (Gate)
							{
								_pendingSetup = null;
							}

							_ = Support.Panes.GoToAsync(
								Support.Routes.WidgetSetup,
								new ShellNavigationQueryParameters
								{
									[Support.Routes.WidgetId] = id!
								});

							_ = WindowsBackground.RevealAsync();

							return true;
						})
						.ConfigureAwait(false);

				if (delivered)
				{
					return;
				}
			}
			catch (Exception ex)
			{
				WindowsTrace.Write("Opening the widget set-up failed", ex);
			}

			await Task.Delay(250).ConfigureAwait(false);
		}
	}

	private static async Task OpenAsync(Uri link, string id)
	{
		try
		{
			// The protocol is handled by this same app: the single-instance activation path takes it from here.
			await Launcher.Default.OpenAsync(link).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write($"Opening the app for widget {id} failed", ex);

			OpenSetup(id);
		}
	}

	/// <summary>A stored place as the contract names it: the stop key when there is one, else the coordinates.</summary>
	private static Contract.ContractPlace? Place(DDjourneys.Core.Models.Location? place) =>
		place is null
			? null
			: new Contract.ContractPlace(place.Name, place.StopKey, place.Latitude, place.Longitude);

	internal static string[] PinnedIds()
	{
		try
		{
			return
			[.. WidgetManager.GetDefault()
				.GetWidgetInfos()
				.Select(info => info.WidgetContext?.Id)
				.Where(static widgetId => widgetId is { Length: > 0 })
				.Select(static widgetId => widgetId!)];
		}
		catch
		{
			return [];
		}
	}

	[DllImport("ole32.dll")]
	private static extern int CoRegisterClassObject(
		[MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
		[MarshalAs(UnmanagedType.IUnknown)] object pUnk,
		uint dwClsContext,
		uint flags,
		out uint lpdwRegister);

	[DllImport("ole32.dll")]
	private static extern int CoRevokeClassObject(uint dwRegister);

	/// <summary>The IClassFactory of the docs' FactoryHelper, handed out for CoCreateInstance.</summary>
	private sealed class WidgetProviderFactory : IClassFactory
	{
		public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
		{
			ppvObject = IntPtr.Zero;

			if (pUnkOuter != IntPtr.Zero)
			{
				return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
			}

			if (riid == typeof(IWidgetProvider).GUID || riid == IUnknown)
			{
				return CreateDdWidgetProvider(out ppvObject);
			}

			return unchecked((int)0x80004002); // E_NOINTERFACE
		}

		int IClassFactory.LockServer(bool fLock) => 0;

		private static Guid IUnknown => Guid.Parse("00000000-0000-0000-C000-000000000046");

		private static int CreateDdWidgetProvider(out IntPtr ppvObject)
		{
			try
			{
				ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new DdWidgetProvider());

				return 0;
			}
			catch
			{
				ppvObject = IntPtr.Zero;

				return unchecked((int)0x80004002);
			}
		}
	}

	[ComImport, ComVisible(false), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000001-0000-0000-C000-000000000046")]
	private interface IClassFactory
	{
		[PreserveSig]
		int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

		[PreserveSig]
		int LockServer(bool fLock);
	}
}
