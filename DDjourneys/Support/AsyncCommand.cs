using DDjourneys.Core.Diagnostics;
using System.Windows.Input;

namespace DDjourneys.Support;

/// <summary>
/// An ICommand for async work that cannot crash the app: exceptions are caught and reported,
/// cancellation is ignored, and it refuses to run twice at once (double taps, impatient users).
/// </summary>
public sealed class AsyncCommand : ICommand
{
	private readonly Func<Task> _execute;
	private readonly Func<bool>? _canExecute;
	private readonly Action<Exception>? _onError;
	private bool _running;

	public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
	{
		ArgumentNullException.ThrowIfNull(execute);
		_execute = execute;
		_canExecute = canExecute;
		_onError = onError;
	}

	public event EventHandler? CanExecuteChanged;

	public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

	// Total try/catch: async void is safe here because nothing can escape.
	public async void Execute(object? parameter) => await ExecuteAsync();

	public async Task ExecuteAsync()
	{
		if (!CanExecute(null))
		{
			return;
		}

		_running = true;
		RaiseCanExecuteChanged();

		try
		{
			await _execute();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Command failed:\n{ex}");

			try
			{
				_onError?.Invoke(ex);
			}
			catch (Exception inner)
			{
				DiagnosticLog.Write($"Command error handler failed: {inner.Message}");
			}
		}
		finally
		{
			_running = false;
			RaiseCanExecuteChanged();
		}
	}
}

/// <summary>Typed variant. A parameter of the wrong type is ignored, never cast blindly.</summary>
public sealed class AsyncCommand<T> : ICommand
{
	private readonly AsyncCommand _inner;
	private T? _parameter;

	public AsyncCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null, Action<Exception>? onError = null)
	{
		ArgumentNullException.ThrowIfNull(execute);
		_inner = new AsyncCommand(
			() => _parameter is { } value ? execute(value) : Task.CompletedTask,
			() => _parameter is { } value && (canExecute?.Invoke(value) ?? true),
			onError);
		_inner.CanExecuteChanged += (s, e) => CanExecuteChanged?.Invoke(this, e);
	}

	public event EventHandler? CanExecuteChanged;

	public bool CanExecute(object? parameter)
	{
		_parameter = parameter is T value ? value : default;
		return _inner.CanExecute(null);
	}

	public async void Execute(object? parameter)
	{
		_parameter = parameter is T value ? value : default;
		await _inner.ExecuteAsync();
	}
}
