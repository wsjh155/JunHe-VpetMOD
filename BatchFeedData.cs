using System.Collections.Generic;
using LinePutScript.Converter;

namespace VPet.Plugin.OnlineInteraction;

public class BatchFeedData
{
	[Line]
	public string FoodName { get; set; } = "";


	[Line]
	public int Quantity { get; set; } = 1;


	[Line]
	public double Price { get; set; }

	[Line]
	public string SenderName { get; set; } = "";


	[Line]
	public Dictionary<string, double> RawStats { get; set; } = new Dictionary<string, double>();

}
