using DDjourneys.Core.Contract;
using DDjourneys.Core.Models;
using DDjourneys.Support;

namespace DDjourneys.Contract;

public enum HandOffResult
{
	/// <summary>The journey went back to the caller.</summary>
	Sent,

	/// <summary>No pick is waiting (it ended or timed out).</summary>
	Expired,

	/// <summary>The other app could not be opened.</summary>
	Failed
}

/// <summary>
/// The pick that is waiting for the user's choice. While one is active, the journey page offers to hand
/// the displayed journey back to the caller. A pick ends when it is answered, replaced by another
/// request, or after <see cref="Lifetime"/>; nothing is ever sent without the user's tap.
/// </summary>
public sealed class ContractSession(ContractResponder responder)
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

	private readonly Lock _gate = new();
	private ContractRequest? _request;
	private DateTimeOffset _expires;

	/// <summary>Raised (on any thread) when a pick starts or ends.</summary>
	public event EventHandler? Changed;

	/// <summary>The waiting pick; null when none, or when it timed out.</summary>
	public ContractRequest? Active
	{
		get
		{
			lock (_gate)
			{
				if (_request is not null && Format.Now() > _expires)
				{
					_request = null;
				}

				return _request;
			}
		}
	}

	public bool IsPicking => Active is not null;

	public void Begin(ContractRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		lock (_gate)
		{
			_request = request;
			_expires = Format.Now() + Lifetime;
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void End()
	{
		bool changed;

		lock (_gate)
		{
			changed = _request is not null;
			_request = null;
		}

		if (changed)
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>Sends <paramref name="journey"/> to the caller's <c>x-success</c> and ends the pick on success.</summary>
	public async Task<HandOffResult> HandOffAsync(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		if (Active is not { } request)
		{
			return HandOffResult.Expired;
		}

		var values = new List<KeyValuePair<string, string>>(JourneyPayload.Flat(journey))
		{
			new("journey", JourneyPayload.Json(journey))
		};

		ContractReply reply = ContractReply.Success(request, values);

		if (!await responder.SendAsync(reply, request.Callbacks, "journey").ConfigureAwait(false))
		{
			return HandOffResult.Failed;
		}

		End();

		return HandOffResult.Sent;
	}
}
