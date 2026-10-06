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

	/// <inheritdoc />
	public async IAsyncEnumerable<LiveVehicle> StreamAsync(
		VehicleFilter filter,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filter);

		using var socket = new ClientWebSocket();

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

		var buffer = new byte[16 * 1024];
		int messages = 0;

		try
		{
			while (socket.State == WebSocketState.Open
				&& !cancellationToken.IsCancellationRequested)
			{
				string? text =
					await ReadMessageAsync(socket, buffer, cancellationToken)
						.ConfigureAwait(false);

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
