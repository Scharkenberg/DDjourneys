namespace DDjourneys.Core.Providers.Vvo.Extensions;

using DDjourneys.Core.Providers.Vvo.Models;

public static class VvoStatusExtensions
{
	public static bool IsSuccess(
		this VvoStatus? status)
	{
		return status?.Code == "Ok";
	}


	public static bool IsNoData(
		this VvoStatus? status)
	{
		return status?.Code == "NoData";
	}


	public static bool IsInvalidRequest(
		this VvoStatus? status)
	{
		return status?.Code == "InvalidRequest";
	}


	public static bool IsServerError(
		this VvoStatus? status)
	{
		return status?.Code == "ServerError";
	}
}