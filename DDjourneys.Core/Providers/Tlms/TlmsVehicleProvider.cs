using DDjourneys.Core.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Serialization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Providers.Tlms;

/// <summary>
/// Live positions from the TLMS WebSocket (<c>wss://socket.tlm.solutions</c>): vehicle positions decoded
/// from radio telegrams and GPS, streamed as JSON objects. The filter is sent after connecting.
/// </summary>
public sealed class TlmsVehicleProvider : ILiveVehicleProvider
{
	private static readonly Uri Endpoint = new(InterfaceSchemas.TlmsUrl);

	private const int MaxMessageBytes = 1 << 20;

	/// <summary>Pings that keep the connection alive through networks that drop an idle socket.</summary>
	private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

	/// <summary>
	/// How long a session may carry nothing before the read is given up as a dead connection and
	/// retried: a filter for one quiet line holds the stream silent for minutes, so this only bites when
	/// nothing at all arrives — the service gone without a close.
	/// </summary>
	private static readonly TimeSpan IdleReceive = TimeSpan.FromMinutes(4);

	/// <summary>Sessions in a row that never carried a position: then the service is down, not flaky.</summary>
	private const int MaxEmptySessions = 3;

	/// <inheritdoc />
	public async IAsyncEnumerable<LiveVehicle> StreamAsync(
		VehicleFilter filter,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filter);

		// The sessions of one stream: a dropped or refused connection is retried, so a hiccup of the
		// service or of the network does not end it. A session that carried positions earned another go
		// at once; one that never did, three times over, is the service being down — the caller says what
		// that means.
		int empty = 0;

		while (empty < MaxEmptySessions
			&& !cancellationToken.IsCancellationRequested)
		{
			bool delivered = false;

			await foreach (LiveVehicle vehicle in
				Session(filter, cancellationToken).ConfigureAwait(false))
			{
				delivered = true;

				yield return vehicle;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}

			if (delivered)
			{
				empty = 0;
			}
			else
			{
				empty++;
			}

			if (empty >= MaxEmptySessions)
			{
				break;
			}

			await Task.Delay(
				delivered
					? TimeSpan.FromSeconds(1)
					: empty == 1
						? TimeSpan.FromSeconds(1)
						: TimeSpan.FromSeconds(2),
				cancellationToken);
		}
	}

	/// <summary>One connection: opened, the filter sent, the messages read until it ends.</summary>
	private static async IAsyncEnumerable<LiveVehicle> Session(
		VehicleFilter filter,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var socket = new ClientWebSocket();

		socket.Options.KeepAliveInterval = KeepAlive;

		bool open = false;

		try
		{
			await socket
				.ConnectAsync(Endpoint, cancellationToken)
				.ConfigureAwait(false);

			string requestJson =
					new JsonObject
					{
						["lines"] = Wire.Array(filter.Lines.Select(line => (JsonNode?)line)),
						["positions"] = new JsonArray(),
						["regions"] = Wire.Array(filter.Region),
						["enrich"] = false
					}.ToJsonString();

			DiagnosticLog.Api("TLMS", $"connect {Endpoint}, request:", requestJson);

			byte[] request = System.Text.Encoding.UTF8.GetBytes(requestJson);

			await socket
				.SendAsync(
					request,
					WebSocketMessageType.Text,
					endOfMessage: true,
					cancellationToken)
				.ConfigureAwait(false);

			open = true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[TLMS] connect failed: {ex.Message}");
		}

		if (!open
			|| cancellationToken.IsCancellationRequested)
		{
			yield break;
		}

		// A stream that carries nothing for minutes is a connection that died silently (the service
		// gone without a close, or the network dropped it): the read is given up and the session retried.
		// Anything that arrives pushes the deadline back.
		using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		idle.CancelAfter(IdleReceive);

		var buffer = new byte[16 * 1024];
		int messages = 0;

		try
		{
			while (socket.State == WebSocketState.Open
				&& !cancellationToken.IsCancellationRequested
				&& !idle.IsCancellationRequested)
			{
				string? text;

				try
				{
					text =
						await ReadMessageAsync(socket, buffer, idle.Token)
							.ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					DiagnosticLog.Write($"[TLMS] read failed: {ex.Message}");

					break;
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (text is null)
				{
					DiagnosticLog.Write($"[TLMS] closed by the server after {messages} messages");

					yield break;
				}

				// The first few messages show what the service sends; the rest would only fill the file.
				if (++messages <= 3)
				{
					DiagnosticLog.Api("TLMS", $"message {messages}:", text);
				}

				foreach (LiveVehicle vehicle in Parse(text, filter))
				{
					yield return vehicle;
				}

				// Anything that arrives — a position, or a message the filter did not use — says the
				// connection is alive: the deadline moves with it.
				idle.CancelAfter(IdleReceive);
			}
		}
		finally
		{
			if (socket.State == WebSocketState.Open)
			{
				try
				{
					using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));

					await socket
						.CloseOutputAsync(
							WebSocketCloseStatus.NormalClosure,
							string.Empty,
							closing.Token)
						.ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					DiagnosticLog.Write($"TLMS close failed: {ex.Message}");
				}
			}
		}
	}


	/// <summary>One complete text message, or null when the server closed the connection.</summary>
	private static async Task<string?> ReadMessageAsync(
		ClientWebSocket socket,
		byte[] buffer,
		CancellationToken cancellationToken)
	{
		using var message = new MemoryStream();

		while (true)
		{
			WebSocketReceiveResult result =
				await socket
					.ReceiveAsync(buffer, cancellationToken)
					.ConfigureAwait(false);

			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			message.Write(buffer, 0, result.Count);

			if (message.Length > MaxMessageBytes)
			{
				throw new InvalidDataException("TLMS message too large.");
			}

			if (result.EndOfMessage)
			{
				return result.MessageType == WebSocketMessageType.Text
					? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)
					: string.Empty;
			}
		}
	}


	/// <summary>A message is one position object (or an array of them); anything else is ignored.</summary>
	internal static IReadOnlyList<LiveVehicle> Parse(
		string json,
		VehicleFilter filter)
	{
		var found = new List<LiveVehicle>();

		if (string.IsNullOrWhiteSpace(json))
		{
			return found;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);

			JsonElement root = document.RootElement;

			if (root.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in root.EnumerateArray())
				{
					Add(found, item, filter);
				}
			}
			else
			{
				Add(found, root, filter);
			}
		}
		catch (JsonException)
		{
			// Not a position message.
		}

		return found;
	}


	private static void Add(
		List<LiveVehicle> found,
		JsonElement item,
		VehicleFilter filter)
	{
		if (item.ValueKind == JsonValueKind.Object
			&& Read(item, filter) is { } vehicle)
		{
			found.Add(vehicle);
		}
	}


	private static LiveVehicle? Read(
		JsonElement item,
		VehicleFilter filter)
	{
		if (!TryNumber(item, "lat", out double latitude)
			|| !TryNumber(item, "lon", out double longitude)
			|| !TryNumber(item, "line", out double line))
		{
			return null;
		}

		TryNumber(item, "run", out double run);
		TryNumber(item, "time", out double milliseconds);
		TryNumber(item, "region", out double region);
		TryNumber(item, "source", out double source);

		TimeSpan? delay =
			TryNumber(item, "delayed", out double seconds)
				? TimeSpan.FromSeconds(seconds)
				: null;

		DateTimeOffset time =
			milliseconds > 0
				? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds)
				: DateTimeOffset.UtcNow;

		return new LiveVehicle
		{
			Line = (int)line,
			Run = (int)run,
			Latitude = latitude,
			Longitude = longitude,
			Time = time,
			Delay = delay,
			Region = item.TryGetProperty("region", out _)
				? (int)region
				: filter.Region,
			Source = source switch
			{
				1 => VehicleSource.Telegram,
				2 => VehicleSource.Gps,
				_ => VehicleSource.Unknown
			}
		};
	}


	/// <summary>The API sends numbers as numbers or as strings (the timestamp is a string).</summary>
	private static bool TryNumber(
		JsonElement item,
		string name,
		out double value)
	{
		value = 0;

		if (!item.TryGetProperty(name, out JsonElement property))
		{
			return false;
		}

		return property.ValueKind switch
		{
			JsonValueKind.Number => property.TryGetDouble(out value),
			JsonValueKind.String => double.TryParse(
				property.GetString(),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out value),
			_ => false
		};
	}
}
