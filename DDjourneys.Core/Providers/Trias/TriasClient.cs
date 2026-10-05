using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.Linq;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// Posts TRIAS requests. The VVO endpoint speaks plain HTTP (the platforms need a cleartext exception
/// for this host) and takes one XML document per request.
/// </summary>
/// <remarks>
/// Every request is written for TRIAS 1.4 first. When the server rejects it (HTTP error, unreadable answer or
/// a TRIAS error message without any result) it is written again for 1.3, then 1.2, then 1.1. The version
/// that worked is remembered per kind of request, and 1.4 is tried again after <see cref="Reprobe"/>.
/// Unreachable servers and timeouts never trigger a downgrade: a different version cannot fix them.
/// </remarks>
public sealed class TriasClient
{
	private const string Endpoint =
		InterfaceSchemas.TriasUrl;

	private static readonly TimeSpan Reprobe = TimeSpan.FromMinutes(30);

	private static readonly string[] ResultElements =
	[
		"TripResult",
		"StopEventResult",
		"LocationResult",
		"TripInfoResult"
	];

	private readonly ApiClient _apiClient;
	private readonly ConcurrentDictionary<TriasRequestKind, (int Minor, DateTimeOffset Since)> _working = new();

	public TriasClient(ApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

		_apiClient = apiClient;
	}

	/// <summary>The version a kind of request currently uses (1.4 until a downgrade was needed).</summary>
	internal TriasDialect Current(TriasRequestKind kind)
	{
		if (_working.TryGetValue(kind, out (int Minor, DateTimeOffset Since) known)
			&& DateTimeOffset.UtcNow - known.Since < Reprobe)
		{
			return new TriasDialect(known.Minor);
		}

		return TriasDialect.Latest;
	}

	internal async Task<XDocument> SendAsync(
		TriasRequestKind kind,
		Func<TriasDialect, XDocument> build,
		CancellationToken cancellationToken,
		TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(build);

		TriasDialect? next = Current(kind);
		XDocument? rejected = null;
		Exception? failure = null;

		while (next is { } dialect)
		{
			string outcome;

			try
			{
				XDocument response =
					await PostAsync(build(dialect), timeout, cancellationToken)
						.ConfigureAwait(false);

				if (IsAccepted(response, out string? reason))
				{
					Remember(kind, dialect);

					DiagnosticLog.Write($"[TRIAS] {kind}: version {dialect.Version} accepted");

					return response;
				}

				rejected = response;
				failure = null;
				outcome = reason ?? "error message";
			}
			catch (ApiException ex) when (!ex.IsTransient)
			{
				failure = ex;
				outcome = "HTTP " + (ex.StatusCode?.ToString() ?? "error");
			}
			catch (InvalidOperationException ex)
			{
				failure = ex;
				outcome = "unreadable response";
			}

			next = dialect.Older;

			DiagnosticLog.Write(
				next is { } older
					? $"[TRIAS] {kind}: version {dialect.Version} rejected ({outcome}), falling back to {older.Version}"
					: $"[TRIAS] {kind}: version {dialect.Version} rejected ({outcome}), no older version left");
		}

		if (rejected is not null)
		{
			// The caller reads the TRIAS error from the last answer.
			return rejected;
		}

		ExceptionDispatchInfo.Capture(failure ?? new InvalidOperationException("trias_unreadable_response")).Throw();

		return null!;
	}

	private void Remember(TriasRequestKind kind, TriasDialect dialect)
	{
		if (dialect.Minor == TriasDialect.Latest.Minor)
		{
			_working.TryRemove(kind, out _);

			return;
		}

		_working[kind] = (dialect.Minor, DateTimeOffset.UtcNow);
	}

	/// <summary>
	/// An answer is accepted when it carries a result, reports no error, or reports "nothing found". Anything
	/// else is taken as the server not understanding the request.
	/// </summary>
	private static bool IsAccepted(XDocument response, out string? reason)
	{
		reason = null;

		if (ResultElements.Any(name => response.Deep(name).Any()))
		{
			return true;
		}

		if (TriasMapper.Error(response) is not { } error)
		{
			return true;
		}

		if (TriasMapper.IsNoResult(error.Code))
		{
			return true;
		}

		reason = $"{error.Code}: {error.Text}";

		return false;
	}

	private async Task<XDocument> PostAsync(
		XDocument request,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
	{
		string response =
			await _apiClient
				.PostXmlAsync(
					Endpoint,
					request.Declaration + Environment.NewLine + request.Root,
					timeout,
					cancellationToken)
				.ConfigureAwait(false);

		try
		{
			return XDocument.Parse(response);
		}
		catch (XmlException ex)
		{
			throw new InvalidOperationException("trias_unreadable_response", ex);
		}
	}
}
