using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using DDjourneys.Core.Contract;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Widgets;
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

	/// <summary>The manifest's id of the route widget (the other definition is the departures widget).</summary>
	internal const string RouteDefinition = "Route_Widget";

	/// <summary>The widget store (set while the app builds).</summary>
	internal static IWidgetStore? Store { get; private set; }

	/// <summary>The loader that fetches rows (set while the app builds).</summary>
	internal static Support.Widgets.WidgetLoader? Loader { get; private set; }

	private static readonly Lock Gate = new();
	private static uint _registrationCookie;
	private static string? _pendingSetup;
	private static int _registered;
	private static string _registration = "not tried yet";

	/// <summary>Widgets the board created before the store existed: id and the manifest definition they came from.</summary>
	private static readonly ConcurrentDictionary<string, string> Early = new(StringComparer.Ordinal);

	internal static bool HasPinnedWidgets =>
		PinnedIds().Length > 0;

	/// <summary>Called while the app builds: the store, the loader, the refresh cycle - and what came in before.</summary>
	internal static void Initialize(
		IWidgetStore store,
		Support.Widgets.WidgetLoader loader)
	{
		Store = store;
		Loader = loader;

		// Normally already done in the App constructor; here for a start path that skipped it.
		RegisterClassObject();
		WidgetUpdaterWin.StartCycle();

		try
		{
			// Widgets the board created while the app was still building: kind from their picker entry, then a card.
			foreach (KeyValuePair<string, string> early in Early)
			{
				if (store.LoadConfig(early.Key) is null)
				{
					store.SaveConfig(
						early.Key,
						new WidgetConfig
						{
							Kind = string.Equals(early.Value, RouteDefinition, StringComparison.Ordinal)
								? WidgetKind.Route
								: WidgetKind.Departures
						});
				}
			}

			Early.Clear();

			// Every widget on the board gets its card: the ones the board only asked about before there was a store too.
			foreach (string id in PinnedIds())
			{
				WidgetUpdaterWin.Serve(id);
			}
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("[Widgets] serving the pinned widgets failed", ex);
		}
	}

	/// <summary>A widget the board created before the app had a store: remembered until it has one.</summary>
	internal static void RememberEarly(string id, string definition) =>
		Early[id] = definition;

	/// <summary>
	/// Registers the widget provider class object, as the docs' Program.cs does. First thing in the process (the
	/// App constructor): the board waits for this registration after it started the app, and a MAUI start takes
	/// seconds before it would get to it by itself.
	/// </summary>
	internal static void RegisterClassObject()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
		{
			return;
		}

		try
		{
			int hr = CoRegisterClassObject(
				Guid.Parse(ProviderClassId),
				new WidgetProviderFactory(),
				0x4, // CLSCTX_LOCAL_SERVER
				0x1, // REGCLS_MULTIPLEUSE (REGCLS_SINGLEUSE is 0): the one running app serves every request of the board
				out uint cookie);

			Marshal.ThrowExceptionForHR(hr);

			lock (Gate)
			{
				_registrationCookie = cookie;
				_registration = $"registered (cookie {cookie}) at {DateTimeOffset.Now:HH:mm:ss.fff}";
			}

			WindowsTrace.Write($"[Widgets] class object registered ({ProviderClassId})");
		}
		catch (Exception ex)
		{
			lock (Gate)
			{
				_registration = $"FAILED: {ex.Message}";
			}

			WindowsTrace.Write("[Widgets] registering the widget provider failed", ex);
		}
	}

	/// <summary>
	/// Everything that decides whether the Widgets Board can add a widget of this app, as text to copy: the
	/// process, the class registration, the package and the extensions Windows has on record for it, and the
	/// picture files the manifest names. "Error adding your widget" has no more detail than this.
	/// </summary>
	internal static async Task<string> DiagnosticsAsync()
	{
		StringBuilder report = new();

		report.AppendLine($"Process {Environment.ProcessId}: {string.Join(' ', Environment.GetCommandLineArgs())}");

		lock (Gate)
		{
			report.AppendLine($"Class object {ProviderClassId}: {_registration}");
		}

		report.AppendLine(SelfTest());

		report.AppendLine($"Store: {(Store is null ? "not ready" : "ready")}, loader: {(Loader is null ? "not ready" : "ready")}");

		try
		{
			global::Windows.ApplicationModel.Package package = global::Windows.ApplicationModel.Package.Current;

			report.AppendLine($"Package: {package.Id.FullName}");
			report.AppendLine($"Installed at: {package.InstalledLocation.Path}");

			foreach (string name in new[] { "provider", "departures", "departures_dark", "route", "route_dark" })
			{
				string file = Path.Combine(package.InstalledLocation.Path, "Assets", "Widgets", name + ".png");

				report.AppendLine($"  {name}.png: {(File.Exists(file) ? $"{new FileInfo(file).Length} bytes" : "MISSING")}");
			}

			global::Windows.ApplicationModel.AppExtensions.AppExtensionCatalog catalog =
				global::Windows.ApplicationModel.AppExtensions.AppExtensionCatalog.Open("com.microsoft.windows.widgets");

			IReadOnlyList<global::Windows.ApplicationModel.AppExtensions.AppExtension> found = await catalog.FindAllAsync();

			int own = 0;

			foreach (global::Windows.ApplicationModel.AppExtensions.AppExtension extension in found)
			{
				if (extension.Package.Id.FamilyName == package.Id.FamilyName)
				{
					own++;

					global::Windows.Storage.StorageFolder folder = await extension.GetPublicFolderAsync();

					report.AppendLine($"Windows knows the widget extension '{extension.Id}' of this package (public folder: {folder.Path})");
				}
			}

			report.AppendLine(own == 0
				? "Windows knows NO widget extension of this package: the registration in the manifest did not take (reinstall the package)."
				: $"{own} widget extension(s) registered.");
		}
		catch (Exception ex)
		{
			report.AppendLine($"Package information failed: {ex.Message}");
		}

		try
		{
			string[] pinned = PinnedIds();

			report.AppendLine($"Widgets on the board: {pinned.Length}");

			foreach (string id in pinned)
			{
				WidgetConfig? config = Store?.LoadConfig(id);

				report.AppendLine($"  {id}: {(config is null ? "no settings" : $"{config.Kind}, complete: {config.IsComplete}")}");
			}
		}
		catch (Exception ex)
		{
			report.AppendLine($"Widget list failed: {ex.Message}");
		}

		return report.ToString();
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
				? ContractLinks.Departures(Place(config.Stop), config.Kind == WidgetKind.Arrivals)
				: ContractLinks.Plan(Place(config.From), Place(config.To), search: true);

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

	/// <summary>A configured end as the contract names it: "here" is the contract's own default (the device), so it needs no key.</summary>
	private static ContractPlace? Place(WidgetPlace? place) =>
		place is { IsHere: true }
			? null
			: Place(place?.Place);

	/// <summary>A stored place as the contract names it: the stop key when there is one, else the coordinates.</summary>
	private static ContractPlace? Place(DDjourneys.Core.Models.Location? place) =>
		place is null
			? null
			: new ContractPlace(place.Name, place.StopKey, place.Latitude, place.Longitude);

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
	private static extern int CoCreateInstance(
		ref Guid clsid,
		nint outer,
		uint context,
		ref Guid iid,
		out nint instance);

	/// <summary>Asks COM for the provider class the way the Widgets Board does (local server): the answer tells whether COM can reach it.</summary>
	private static string SelfTest()
	{
		Guid clsid = new(ProviderClassId);
		Guid iid = new("00000000-0000-0000-C000-000000000046");

		try
		{
			int hr = CoCreateInstance(ref clsid, 0, 0x4 /* CLSCTX_LOCAL_SERVER */, ref iid, out nint instance);

			if (instance != 0)
			{
				Marshal.Release(instance);
			}

			return $"COM self-test (CoCreateInstance, local server): 0x{hr:X8} {(hr == 0 ? "ok" : "FAILED")}";
		}
		catch (Exception ex)
		{
			return $"COM self-test threw: {ex.Message}";
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
	[ComVisible(true)]
	private sealed class WidgetProviderFactory : IClassFactory
	{
		// One provider for every request: it tracks which widgets are active, and two instances would each know half.
		private static readonly Lazy<DdWidgetProvider> Provider = new(static () => new DdWidgetProvider());

		public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
		{
			ppvObject = IntPtr.Zero;

			WindowsTrace.Write($"[Widgets] the board asks for the provider (interface {riid})");

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
				ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(Provider.Value);

				WindowsTrace.Write("[Widgets] provider handed out");

				return 0;
			}
			catch (Exception ex)
			{
				// The one place that says why the board gets nothing: the log names it.
				WindowsTrace.Write("[Widgets] handing out the provider FAILED", ex);

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
