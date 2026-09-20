using System;
using System.Collections.Generic;
using LinePutScript;

namespace VPet.Plugin.DayTrip;

public class DayTripSetting : LPS
{
	public int TripDurationSeconds
	{
		get
		{
			if (((LpsDocument<List<ILine>>)this)[(gint)"tripDuration"] == 0)
			{
				return 10;
			}
			return ((LpsDocument<List<ILine>>)this)[(gint)"tripDuration"];
		}
		set
		{
			((LpsDocument<List<ILine>>)this)[(gint)"tripDuration"] = value;
		}
	}

	public double TripCost
	{
		get
		{
			if (((LpsDocument<List<ILine>>)this)[(gint)"tripCost"] == 0)
			{
				return 5000.0;
			}
			return ((LpsDocument<List<ILine>>)this)[(gint)"tripCost"];
		}
		set
		{
			((LpsDocument<List<ILine>>)this)[(gint)"tripCost"] = (int)value;
		}
	}

	public DayTripSetting(ILine? line)
		: base((ILine)(((object)line) ?? ((object)new Line("DayTrip"))), Array.Empty<ILine>())
	{
	}//IL_000b: Unknown result type (might be due to invalid IL or missing references)

}
