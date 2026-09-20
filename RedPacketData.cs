using LinePutScript.Converter;

namespace VPet.Plugin.OnlineInteraction;

public class RedPacketData
{
	[Line]
	public double Amount { get; set; }

	[Line]
	public string SenderName { get; set; } = "";


	[Line]
	public long Timestamp { get; set; }
}
