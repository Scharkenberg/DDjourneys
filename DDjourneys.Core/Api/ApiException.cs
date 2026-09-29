namespace DDjourneys.Core.Api;

/// <summary>
/// Represents an HTTP or API communication failure.
/// </summary>
public class ApiException : Exception
{
	/// <summary>
	/// HTTP status code returned by the server.
	/// </summary>
	public int? StatusCode { get; }


	/// <summary>
	/// Indicates whether retrying the request may succeed.
	/// </summary>
	public bool IsTransient { get; }


	/// <summary>
	/// Response body returned by the server, if available.
	/// </summary>
	public string? ResponseBody { get; }

	/// <summary>
	/// Server requested delay before retrying the request.
	/// </summary>
	public TimeSpan? RetryAfter { get; }

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
}