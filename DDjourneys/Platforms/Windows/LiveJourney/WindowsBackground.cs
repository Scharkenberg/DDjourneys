using Microsoft.UI.Windowing;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// Windows keeps no process alive for us (no foreground service) and desktop apps are not suspended
/// when hidden. So while a journey is monitored, closing the window only hides it; the process goes on
/// polling and updating the notification. A process started only to run a notification button stays
/// invisible and ends as soon as nothing is monitored. (Windows services and background tasks do not
/// fit: services run in session 0 without UI and need a restricted capability, time-triggered
/// background tasks run at most every 15 minutes.)
/// </summary>
internal static class WindowsBackground
{
	private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan StealthLimit = TimeSpan.FromSeconds(45);

	private static readonly Lock Gate = new();

	private static Microsoft.UI.Xaml.Window? _window;
	private static bool _keepAlive;
	private static bool _stealth;
	private static bool _activationSeen;
	private static bool _quitting;
	private static int _exitToken;

	/// <summary>True while the process runs without a window the user asked for.</summary>
	public static bool Stealth
	{
		get
		{
			lock (Gate)
			{
				return _stealth;
			}
		}
	}

	/// <param name="startedForNotification">Windows started this process to deliver a notification click.</param>
	public static void Initialize(bool startedForNotification)
	{
		lock (Gate)
		{
			_stealth = startedForNotification;
		}

		if (startedForNotification)
		{
			_ = WatchdogAsync();
		}
	}

	/// <summary>Hooks a MAUI window (once): close hides while monitoring, and a stealth start stays hidden.</summary>
	public static void Attach(Microsoft.UI.Xaml.Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		lock (Gate)
		{
			if (ReferenceEquals(_window, window))
			{
				return;
			}

			_window = window;
		}

		try
		{
			window.AppWindow.Closing += OnClosing;
			window.Activated += OnActivated;

			if (Stealth)
			{
				window.AppWindow.Hide();
			}

			WindowsTrace.Write($"Window attached, stealth: {Stealth}");
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Attaching the window failed", ex);
		}
	}

	/// <summary>The live presentation says whether the process must keep monitoring.</summary>
	public static void SetKeepAlive(bool keep)
	{
		lock (Gate)
		{
			if (_keepAlive == keep)
			{
				return;
			}

			_keepAlive = keep;
		}

		WindowsTrace.Write($"Keep alive: {keep}");

		if (!keep)
		{
			ScheduleExitCheck();
		}
	}

	/// <summary>A notification activation reached this process.</summary>
	public static void NoteActivation()
	{
		lock (Gate)
		{
			_activationSeen = true;
		}
	}

	/// <summary>Ends a hidden process that has nothing left to monitor, after a short grace.</summary>
	public static void ScheduleExitCheck()
	{
		int token = Interlocked.Increment(ref _exitToken);

		_ = Task.Run(
			async () =>
			{
				await Task.Delay(ExitGrace);

				if (token != Volatile.Read(ref _exitToken))
				{
					return;
				}

				bool quit;

				try
				{
					// The window state is read on the UI thread.
					quit =
						await MainThread.InvokeOnMainThreadAsync(
							() =>
							{
								lock (Gate)
								{
									return !_keepAlive && !_quitting && IsHiddenLocked();
								}
							});
				}
				catch (InvalidOperationException)
				{
					// No UI thread yet: the app never got as far as showing anything.
					lock (Gate)
					{
						quit = !_keepAlive && !_quitting;
					}
				}

				if (quit)
				{
					lock (Gate)
					{
						_quitting = true;
					}

					await QuitAsync();
				}
			});
	}

	/// <summary>Shows and focuses the window, waiting for it on a cold start.</summary>
	public static async Task RevealAsync()
	{
		for (int i = 0; i < 80; i++)
		{
			try
			{
				bool done = await MainThread.InvokeOnMainThreadAsync(Reveal);

				if (done)
				{
					return;
				}
			}
			catch (InvalidOperationException)
			{
				// The main thread is not there yet (cold start).
			}
			catch (Exception ex)
			{
				WindowsTrace.Write("Showing the window failed", ex);

				return;
			}

			await Task.Delay(250);
		}

		WindowsTrace.Write("No window to show");
	}

	private static bool Reveal()
	{
		Microsoft.UI.Xaml.Window? window;

		lock (Gate)
		{
			window = _window;
			_stealth = false;
			_activationSeen = true;
		}

		Interlocked.Increment(ref _exitToken);

		if (window is null)
		{
			return false;
		}

		window.AppWindow.Show();
		window.Activate();

		if (Microsoft.Maui.Controls.Application.Current is { } app
			&& app.Windows is [{ } mauiWindow, ..])
		{
			app.ActivateWindow(mauiWindow);
		}

		return true;
	}

	private static void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		bool hide;

		lock (Gate)
		{
			hide = _keepAlive && !_quitting;
		}

		if (!hide)
		{
			return;
		}

		args.Cancel = true;
		sender.Hide();

		WindowsTrace.Write("Window closed: hidden, monitoring goes on");
	}

	private static void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
	{
		Microsoft.UI.Xaml.Window? window;

		lock (Gate)
		{
			window = _stealth ? _window : null;
		}

		try
		{
			// MAUI activates its window after creating it; a stealth start must not show it.
			window?.AppWindow.Hide();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Hiding the window failed", ex);
		}
	}

	private static bool IsHiddenLocked()
	{
		if (_stealth || _window is null)
		{
			return true;
		}

		try
		{
			return !_window.AppWindow.IsVisible;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>A process started for a notification that never delivers one must not linger.</summary>
	private static async Task WatchdogAsync()
	{
		await Task.Delay(StealthLimit);

		bool quit;

		lock (Gate)
		{
			quit = _stealth && !_activationSeen && !_keepAlive && !_quitting;

			_quitting |= quit;
		}

		if (quit)
		{
			WindowsTrace.Write("No activation arrived; exiting");

			await QuitAsync();
		}
	}

	private static async Task QuitAsync()
	{
		WindowsTrace.Write("Nothing left to monitor; exiting");

		// Safety net: if closing the windows does not end the process, end it.
		_ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Environment.Exit(0), TaskScheduler.Default);

		try
		{
			await MainThread.InvokeOnMainThreadAsync(
				() =>
				{
					if (Microsoft.Maui.Controls.Application.Current is { } app)
					{
						app.Quit();
					}
					else
					{
						Environment.Exit(0);
					}
				});
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Quitting failed", ex);

			Environment.Exit(0);
		}
	}
}
