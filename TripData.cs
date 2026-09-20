using System;

namespace VPet.Plugin.DayTrip;

public class TripData
{
	public DateTime StartTime { get; set; }

	public DateTime EndTime { get; set; }

	public bool IsTraveling { get; set; }

	public string ModeId { get; set; } = "";


	public string ModeName { get; set; } = "";

}
