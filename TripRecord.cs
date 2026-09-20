using System;
using System.Collections.Generic;
using LinePutScript;

namespace VPet.Plugin.DayTrip;

public class TripRecord
{
	public DateTime StartTime { get; set; }

	public DateTime EndTime { get; set; }

	public string ModeId { get; set; } = "";


	public string ModeName { get; set; } = "";


	public string FeelingText { get; set; } = "";


	public string ImageFile { get; set; } = "";


	public string RewardText { get; set; } = "";


	public ILine ToLine()
	{
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected O, but got Unknown
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Expected O, but got Unknown
		//IL_00e4: Expected O, but got Unknown
		Line val = new Line("record", $"{StartTime:O}|{EndTime:O}|{ModeId}|{ModeName}", "", Array.Empty<ISub>());
		((Line<List<ISub>>)val).FindorAdd("feeling").Info = FeelingText ?? "";
		((Line<List<ISub>>)val).FindorAdd("image").Info = ImageFile ?? "";
		((Line<List<ISub>>)val).FindorAdd("reward").Info = RewardText ?? "";
		return (ILine)val;
	}

	public static TripRecord FromLine(ILine line)
	{
		string[] array = (((ISub)line).Info ?? "").Split('|');
		TripRecord tripRecord = new TripRecord();
		if (array.Length >= 4)
		{
			DateTime.TryParse(array[0], out var result);
			DateTime.TryParse(array[1], out var result2);
			tripRecord.StartTime = result;
			tripRecord.EndTime = result2;
			tripRecord.ModeId = array[2];
			tripRecord.ModeName = array[3];
		}
		ISub obj = line.Find("feeling");
		tripRecord.FeelingText = ((obj != null) ? obj.Info : null) ?? "";
		ISub obj2 = line.Find("image");
		tripRecord.ImageFile = ((obj2 != null) ? obj2.Info : null) ?? "";
		ISub obj3 = line.Find("reward");
		tripRecord.RewardText = ((obj3 != null) ? obj3.Info : null) ?? "";
		return tripRecord;
	}
}
