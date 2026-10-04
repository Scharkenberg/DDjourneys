using System.Xml;
using System.Xml.Linq;
using DDjourneys.Core.Api;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// Posts TRIAS requests. The VVO endpoint speaks plain HTTP (the platforms need a cleartext exception
/// for this host) and takes one XML document per request.
/// </summary>
public sealed class TriasClient
{
	private const string Endpoint =
		"http://efa.vvo-online.de:8080/std3/trias";

	private readonly ApiClient _apiClient;

	public TriasClient(ApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

		_apiClient = apiClient;
	}

	internal async Task<XDocument> SendAsync(
		XDocument request,
		CancellationToken cancellationToken,
		TimeSpan? timeout = null)
	{
		string response =
			await _apiClient
				.PostXmlAsync(
					Endpoint,
					request.Declaration + Environment.NewLine + request.Root,
					cancellationToken,
					timeout)
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
