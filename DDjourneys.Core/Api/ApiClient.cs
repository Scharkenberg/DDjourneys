namespace DDjourneys.Core.Api;

/// <summary>
/// Provides basic HTTP communication functionality.
/// </summary>
public sealed class ApiClient : IDisposable
{
	private readonly HttpClient _httpClient;

	private readonly bool _ownsClient;


	/// <summary>
	/// Creates an API client using its own HttpClient instance.
	/// </summary>
	public ApiClient()
	{
		_httpClient = CreateHttpClient();
		_ownsClient = true;
	}


	/// <summary>
	/// Creates an API client using an externally managed HttpClient.
	/// </summary>
	public ApiClient(HttpClient httpClient)
	{
		ArgumentNullException.ThrowIfNull(httpClient);

		_httpClient = httpClient;
		_ownsClient = false;
	}


	/// <summary>
	/// Sends a GET request and returns the response body.
	/// </summary>
	public async Task<string> GetAsync(
		string requestUri,
		CancellationToken cancellationToken = default)
	{
		using HttpResponseMessage response =
			await _httpClient.GetAsync(
				requestUri,
				cancellationToken)
			.ConfigureAwait(false);

		response.EnsureSuccessStatusCode();

		return await response.Content
			.ReadAsStringAsync(cancellationToken)
			.ConfigureAwait(false);
	}


	/// <summary>
	/// Sends a POST request containing JSON and returns the response body.
	/// </summary>
	public async Task<string> PostJsonAsync(
		string requestUri,
		string json,
		CancellationToken cancellationToken = default)
	{
		using var content = new StringContent(
			json,
			System.Text.Encoding.UTF8,
			"application/json");


		using HttpResponseMessage response =
			await _httpClient.PostAsync(
				requestUri,
				content,
				cancellationToken)
			.ConfigureAwait(false);


		response.EnsureSuccessStatusCode();

		return await response.Content
			.ReadAsStringAsync(cancellationToken)
			.ConfigureAwait(false);
	}


	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(15)
		};

		client.DefaultRequestHeaders.Accept.ParseAdd(
			"application/json");

		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"DDjourneys");

		return client;
	}


	public void Dispose()
	{
		if (_ownsClient)
		{
			_httpClient.Dispose();
		}
	}
}