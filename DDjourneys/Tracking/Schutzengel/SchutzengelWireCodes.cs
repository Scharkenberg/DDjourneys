using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Numeric codes of the Schutzengel wire format and the VVO/domain vocabulary mapped onto them.
/// </summary>
internal static class SchutzengelWireCodes
{
	internal const int TransportationCategoryMot = 0;
	internal const int TransportationCategoryTransition = 1;

	internal const int TrafficNodeTypeStop = 1;
	internal const int TrafficNodeTypePoi = 2;
	internal const int TrafficNodeTypeAddress = 3;

	internal const int StopSequenceOnward = 2;

	internal const int PlatformTypeAny = 0;
	internal const int PlatformTypePlatform = 1;
	internal const int PlatformTypeRailtrack = 2;

	internal const int MotTypeAny = 0;
	internal const int MotTypeTram = 1;
	internal const int MotTypeSubway = 3;
	internal const int MotTypeBus = 4;
	internal const int MotTypeBusRegional = 7;
	internal const int MotTypeBusIntercity = 8;
	internal const int MotTypeBusNightline = 9;
	internal const int MotTypeTrain = 11;
	internal const int MotTypeTrainUrban = 12;
	internal const int MotTypeHailedSharedTaxi = 16;
	internal const int MotTypeTaxi = 17;
	internal const int MotTypeWalking = 19;
	internal const int MotTypeFerry = 25;
	internal const int MotTypeCableway = 26;

	internal const int TransitionTypeAny = 0;
	internal const int TransitionTypeWalking = 1;
	internal const int TransitionTypeStairsUp = 2;
	internal const int TransitionTypeStairsDown = 3;
	internal const int TransitionTypeElevatorUp = 4;
	internal const int TransitionTypeElevatorDown = 5;
	internal const int TransitionTypeEscalatorUp = 6;
	internal const int TransitionTypeEscalatorDown = 7;
	internal const int TransitionTypeRampUp = 8;
	internal const int TransitionTypeRampDown = 9;
	internal const int TransitionTypeStayInVehicle = 10;
	internal const int TransitionTypeChangeVehicles = 11;
	internal const int TransitionTypeEnsuredConnection = 12;

	internal const int OccupancyUnknown = 0;
	internal const int OccupancyManySeats = 2;
	internal const int OccupancyFewSeats = 3;
	internal const int OccupancyStandingOnly = 4;
	internal const int OccupancyFull = 6;

	internal const int MapProjectionWgs84 = 1;


	internal static int TransitionType(
		string? type) =>
		type switch
		{
			"Footpath" =>
				TransitionTypeWalking,

			"StayForConnection" =>
				TransitionTypeEnsuredConnection,

			"StayInVehicle" =>
				TransitionTypeStayInVehicle,

			"MobilityRampUp" =>
				TransitionTypeRampUp,

			"MobilityRampDown" =>
				TransitionTypeRampDown,

			"MobilityStairsUp" =>
				TransitionTypeStairsUp,

			"MobilityStairsDown" =>
				TransitionTypeStairsDown,

			"MobilityElevatorUp" =>
				TransitionTypeElevatorUp,

			"MobilityElevatorDown" =>
				TransitionTypeElevatorDown,

			"MobilityEscalatorUp" =>
				TransitionTypeEscalatorUp,

			"MobilityEscalatorDown" =>
				TransitionTypeEscalatorDown,

			_ =>
				TransitionTypeAny
		};


	internal static int MotType(
		TransitMode mode) =>
		mode switch
		{
			TransitMode.Tram =>
				MotTypeTram,

			TransitMode.Bus =>
				MotTypeBus,

			TransitMode.Subway =>
				MotTypeSubway,

			TransitMode.SuburbanRail =>
				MotTypeTrainUrban,

			TransitMode.RegionalTrain =>
				MotTypeTrain,

			TransitMode.LongDistanceTrain =>
				MotTypeTrain,

			TransitMode.Ferry =>
				MotTypeFerry,

			TransitMode.CableCar =>
				MotTypeCableway,

			TransitMode.Taxi =>
				MotTypeTaxi,

			TransitMode.OnDemand =>
				MotTypeHailedSharedTaxi,

			TransitMode.Walk =>
				MotTypeWalking,

			_ =>
				MotTypeAny
		};


	internal static int TrafficNodeType(
		string? type) =>
		type switch
		{
			"Poi" or "p" =>
				TrafficNodeTypePoi,

			"Address" or "a" or "c" =>
				TrafficNodeTypeAddress,

			_ =>
				TrafficNodeTypeStop
		};


	internal static int PlatformType(
		string? type) =>
		type switch
		{
			"Platform" =>
				PlatformTypePlatform,

			"Railtrack" =>
				PlatformTypeRailtrack,

			_ =>
				PlatformTypeAny
		};


	internal static int Occupancy(
		string? occupancy) =>
		occupancy switch
		{
			"ManySeats" =>
				OccupancyManySeats,

			"FewSeats" =>
				OccupancyFewSeats,

			"StandingOnly" =>
				OccupancyStandingOnly,

			"Full" =>
				OccupancyFull,

			_ =>
				OccupancyUnknown
		};
}
