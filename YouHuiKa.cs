using System;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.YouHuiKa;

public class YouHuiKa : MainPlugin
{
	private const string StorageKey = "YouHuiKa.CouponPeriod";

	internal static readonly TimeSpan CouponValidDuration = TimeSpan.FromDays(30.0);

	public bool IsCouponValid => CouponPeriod >= DateTime.Now;

	public DateTime CouponPeriod
	{
		get
		{
			try
			{
				return base.MW.GameSavesData.GetDateTime("YouHuiKa.CouponPeriod", DateTime.MinValue);
			}
			catch
			{
				return DateTime.MinValue;
			}
		}
		set
		{
			try
			{
				base.MW.GameSavesData.SetDateTime("YouHuiKa.CouponPeriod", value);
			}
			catch
			{
			}
		}
	}

	public override string PluginName => "优惠券";

	public YouHuiKa(IMainWindow mainwin)
		: base(mainwin)
	{
	}

	public override void LoadPlugin()
	{
		base.MW.Event_TakeItem += TakeItem;
	}

	private void TakeItem(Food food)
	{
		try
		{
			if (((Item)food).Name == "优惠券")
			{
				if (!IsCouponValid)
				{
					CouponPeriod = DateTime.Now + CouponValidDuration;
				}
				else
				{
					CouponPeriod += CouponValidDuration;
				}
			}
			else if (IsCouponValid)
			{
				double num = ((Item)food).Price * 0.9;
				IGameSave save = base.MW.Core.Save;
				save.Money += num;
				base.MW.Main.Say($"当当~ 我有优惠券，减免了 {num:C2} 元！§(*\uffe3▽\uffe3*)§", (string)null, false, (string)null);
			}
		}
		catch (Exception)
		{
		}
	}
}
