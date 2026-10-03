using System.Text;

namespace DDjourneys.Core.Api;

/// <summary>
/// Represents an HTTP or API communication failure.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is empty for failures without an answer (no connection, timeout):
/// callers show their own localized text then, and <see cref="Detail"/> still carries the cause for logs.
/// For an error response it holds the status line and the start of the response body.
/// </remarks>
public class ApiException : Exception
{
	/// <summary>How much of an error body is copied into <see cref="Exception.Message"/>.</summary>
	public const int MaxBodyCharactersInMessage = 300;

	/// <summary>
	/// HTTP status code returned by the server.
	/// </summary>
	public int? StatusCode { get; }


	/// <summary>
	/// Indicates whether retrying the request may succeed.
	/// </summary>
	public bool IsTransient { get; }


	/// <summary>
	/// Complete response body returned by the server, if available.
	/// </summary>
	public string? ResponseBody { get; }

	/// <summary>
	/// Server requested delay before retrying the request.
	/// </summary>
	public TimeSpan? RetryAfter { get; }

	/// <summary>
	/// Text for logs and diagnostic UI: the message, or the cause when there is no message.
	/// </summary>
	public string Detail =>
		!string.IsNullOrWhiteSpace(Message)
			? Message
			: InnerException?.Message
				?? string.Empty;

	/// <summary>
	/// Creates a new API exception.
	/// </summary>
	public ApiException(
		string message,
		int? statusCode = null,
		bool isTransient = false,
		string? responseBody = null,
		Exception? innerException = null,
		TimeSpan? retryAfter = null)
		: base(message, innerException)
	{
		StatusCode = statusCode;
		IsTransient = isTransient;
		ResponseBody = responseBody;
		RetryAfter = retryAfter;
	}

	/// <summary>
	/// Creates the exception for a non-success response; the message names the status and quotes the
	/// start of the body, so a failure can be diagnosed from the UI and from logs alone.
	/// </summary>
	public static ApiException FromResponse(
		int statusCode,
		string? reasonPhrase,
		string? responseBody,
		bool isTransient,
		TimeSpan? retryAfter = null) =>
		new(
			Describe(
				statusCode,
				reasonPhrase,
				responseBody),
			statusCode,
			isTransient,
			responseBody,
			retryAfter: retryAfter);

	private static string Describe(
		int statusCode,
		string? reasonPhrase,
		string? responseBody)
	{
		string status =
			string.IsNullOrWhiteSpace(reasonPhrase)
				? $"HTTP {statusCode}"
				: $"HTTP {statusCode} {reasonPhrase.Trim()}";

		string body =
			Condense(
				responseBody,
				MaxBodyCharactersInMessage);

		return body.Length == 0
			? status
			: $"{status}: {body}";
	}

	/// <summary>Collapses whitespace runs (error pages are often multi-line) and shortens to a limit.</summary>
	private static string Condense(
		string? text,
		int maxCharacters)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		var builder = new StringBuilder(Math.Min(text.Length, maxCharacters + 1));
		bool pendingSpace = false;

		foreach (char character in text)
		{
			if (char.IsWhiteSpace(character))
			{
				pendingSpace = builder.Length > 0;

				continue;
			}

			if (pendingSpace)
			{
				builder.Append(' ');
				pendingSpace = false;
			}

			builder.Append(character);

			if (builder.Length > maxCharacters)
			{
				builder.Length = maxCharacters;
				builder.Append('…');

				break;
			}
		}

		return builder.ToString();
	}
}
