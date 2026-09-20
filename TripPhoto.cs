using System.IO;

namespace VPet.Plugin.DayTrip;

public class TripPhoto
{
	public string ImageFile { get; set; } = "";


	public string FeelingText { get; set; } = "";


	public string Tags { get; set; } = "";


	public string GetImagePath(string photoFolder)
	{
		return Path.Combine(photoFolder, ImageFile);
	}
}
