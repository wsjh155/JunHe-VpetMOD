using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.BackpackTrade;

public class BackpackTrade : MainPlugin
{
	public const double TaxRate = 0.01;

	public const double DailyGiftBuyPrice = 1000.0;

	public const double DailyGiftSellPrice = 15.0;

	public const double MagicPouchBuyPrice = 100.0;

	public const double MagicPouchSellPrice = 1.0;

	private static readonly HashSet<FoodType> TradeableTypes = new HashSet<FoodType>
	{
		(FoodType)0,
		(FoodType)2,
		(FoodType)3,
		(FoodType)4,
		(FoodType)5,
		(FoodType)6,
		(FoodType)7
	};

	public override string PluginName => "跳蚤超市";

	public BackpackTrade(IMainWindow mainwin)
		: base(mainwin)
	{
	}

	public override void LoadPlugin()
	{
		MenuItem menuFeed = base.MW.Main.ToolBar.MenuFeed;
		MenuItem menuItem = new MenuItem
		{
			Header = "跳蚤超市"
		};
		MenuItem menuItem2 = new MenuItem
		{
			Header = "优惠券查询"
		};
		menuItem2.Click += delegate
		{
			QueryCoupon();
		};
		MenuItem menuItem3 = new MenuItem
		{
			Header = "交易系统"
		};
		menuItem3.Click += delegate
		{
			OpenTradeWindow();
		};
		MenuItem menuItem4 = new MenuItem
		{
			Header = "注销优惠券"
		};
		menuItem4.Click += delegate
		{
			RevokeCoupon();
		};
		menuItem.Items.Add(menuItem2);
		menuItem.Items.Add(menuItem3);
		menuItem.Items.Add(menuItem4);
		menuFeed.Items.Add(menuItem);
	}

	private void QueryCoupon()
	{
		try
		{
			object obj = FindYouHuiKaPlugin();
			if (obj != null)
			{
				PropertyInfo? property = obj.GetType().GetProperty("IsCouponValid");
				PropertyInfo property2 = obj.GetType().GetProperty("CouponPeriod");
				bool num = (bool)(property?.GetValue(obj) ?? ((object)false));
				DateTime value = (DateTime)(property2?.GetValue(obj) ?? ((object)DateTime.MinValue));
				if (num)
				{
					base.MW.Main.Say($"优惠券有效期限至 {value:D} {value:t}，享受9折优惠~", (string)null, false, (string)null);
				}
				else
				{
					base.MW.Main.Say("当前没有有效的优惠券喵~ 快去跳蚤超市购买吧！", (string)null, false, (string)null);
				}
				return;
			}
			try
			{
				DateTime dateTime = base.MW.GameSavesData.GetDateTime("YouHuiKa.CouponPeriod", DateTime.MinValue);
				if (dateTime >= DateTime.Now)
				{
					base.MW.Main.Say($"优惠券有效期限至 {dateTime:D} {dateTime:t}，享受9折优惠~（数据读取）", (string)null, false, (string)null);
				}
				else if (dateTime > DateTime.MinValue)
				{
					base.MW.Main.Say("优惠券已过期喵~ 去跳蚤超市买一张新的吧！", (string)null, false, (string)null);
				}
				else
				{
					base.MW.Main.Say("当前没有有效的优惠券喵~ 快去跳蚤超市购买吧！", (string)null, false, (string)null);
				}
			}
			catch
			{
				base.MW.Main.Say("优惠券插件未加载，无法查询状态喵~", (string)null, false, (string)null);
			}
		}
		catch (Exception)
		{
			base.MW.Main.Say("查询优惠券时出错喵~", (string)null, false, (string)null);
		}
	}

	private object FindYouHuiKaPlugin()
	{
		try
		{
			if (!(((object)base.MW.Main).GetType().GetField("Plugins", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(base.MW.Main) is IEnumerable enumerable))
			{
				return null;
			}
			foreach (object item in enumerable)
			{
				if (item != null)
				{
					string fullName = item.GetType().FullName;
					if (fullName.Contains("YouHuiKa") || fullName.Contains("youhuika"))
					{
						return item;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private void RevokeCoupon()
	{
		try
		{
			DateTime dateTime;
			try
			{
				dateTime = base.MW.GameSavesData.GetDateTime("YouHuiKa.CouponPeriod", DateTime.MinValue);
			}
			catch
			{
				dateTime = DateTime.MinValue;
			}
			if (dateTime <= DateTime.Now)
			{
				base.MW.Main.Say("当前没有有效的优惠券可以注销喵~", (string)null, false, (string)null);
				return;
			}
			double totalDays = (dateTime - DateTime.Now).TotalDays;
			double num = Math.Round(1000000.0 * (totalDays / 30.0), 2);
			if (MessageBox.Show($"优惠券有效期至 {dateTime:D}\n剩余 {totalDays:F1} 天\n\n注销将退还 ¥{FmtMoney(num)}\n\n确定注销？", "注销优惠券", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
			{
				try
				{
					base.MW.GameSavesData.SetDateTime("YouHuiKa.CouponPeriod", DateTime.MinValue);
				}
				catch
				{
				}
				IGameSave save = base.MW.Core.Save;
				save.Money += num;
				base.MW.Main.Say($"优惠券已注销，退还 ¥{FmtMoney(num)}（剩余 {totalDays:F1} 天）~", (string)null, false, (string)null);
			}
		}
		catch (Exception)
		{
			base.MW.Main.Say("注销优惠券时出错喵~", (string)null, false, (string)null);
		}
	}

	private void OpenTradeWindow()
	{
		try
		{
			new TradeWindow(base.MW, this).Show();
		}
		catch (Exception)
		{
			base.MW.Main.Say("打开交易系统失败喵~", (string)null, false, (string)null);
		}
	}

	public bool BuyItem(Food food, int count = 1)
	{
		try
		{
			if (food == null || count <= 0)
			{
				return false;
			}
			if (!IsTradeable(food))
			{
				base.MW.Main.Say(((Item)food).TranslateName + " 不在可交易列表中喵~", (string)null, false, (string)null);
				return false;
			}
			double num = GetBuyPrice(food) * (double)count;
			double num2 = num * 0.01;
			double num3 = num + num2;
			double money = base.MW.Core.Save.Money;
			if (money < num3)
			{
				base.MW.Main.Say("金钱不足喵~ 需要 " + FmtMoney(num3) + "（含1%税），当前只有 " + FmtMoney(money), (string)null, false, (string)null);
				return false;
			}
			IGameSave save = base.MW.Core.Save;
			save.Money -= num3;
			for (int i = 0; i < count; i++)
			{
				Food val = CloneFood(food);
				if (val != null)
				{
					base.MW.ItemsAdd((Item)(object)val);
				}
			}
			base.MW.Main.Say($"成功购买 {((Item)food).TranslateName} x{count}，花费 ¥{FmtMoney(num3)} 金钱（含税 {FmtMoney(num2)}）~", (string)null, false, (string)null);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool SellItem(Item item, int count = 1)
	{
		try
		{
			if (item == null || count <= 0)
			{
				return false;
			}
			Item val = ((IEnumerable<Item>)base.MW.Items).FirstOrDefault((Func<Item, bool>)((Item x) => x.Name == item.Name && x.Price == item.Price));
			if (val == null)
			{
				base.MW.Main.Say("背包中没有 " + item.TranslateName + " 喵~", (string)null, false, (string)null);
				return false;
			}
			int num = Math.Min(count, val.Count);
			double num2 = GetSellPrice(val) * (double)num;
			double num3 = num2 * 0.01;
			double num4 = num2 - num3;
			if (val.Count <= num)
			{
				base.MW.Items.Remove(val);
			}
			else
			{
				val.Count -= num;
			}
			IGameSave save = base.MW.Core.Save;
			save.Money += num4;
			base.MW.Main.Say($"出售 {val.TranslateName} x{num}，获得 ¥{FmtMoney(num4)} 金钱（扣除1%税 {FmtMoney(num3)}）~", (string)null, false, (string)null);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public double GetBuyPrice(Food food)
	{
		if (((Item)food).Name == "每日礼包")
		{
			return 1000.0;
		}
		if (((Item)food).Name == "锦囊")
		{
			return 100.0;
		}
		return ((Item)food).Price;
	}

	public double GetSellPrice(Item item)
	{
		if (item.Name == "每日礼包")
		{
			return 15.0;
		}
		if (item.Name == "锦囊")
		{
			return 1.0;
		}
		return item.Price;
	}

	public bool IsTradeable(Food food)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		if (food == null)
		{
			return false;
		}
		if (((Item)food).Name == "每日礼包")
		{
			return true;
		}
		if (((Item)food).Name == "锦囊")
		{
			return true;
		}
		return TradeableTypes.Contains(food.Type);
	}

	public bool IsTradeable(Item item)
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		if (item == null)
		{
			return false;
		}
		if (item.Name == "每日礼包")
		{
			return true;
		}
		if (item.Name == "锦囊")
		{
			return true;
		}
		try
		{
			Food val = ((IEnumerable<Food>)base.MW.Foods).FirstOrDefault((Func<Food, bool>)((Food f) => ((Item)f).Name == item.Name));
			if (val != null)
			{
				return TradeableTypes.Contains(val.Type);
			}
		}
		catch
		{
		}
		return false;
	}

	internal Food CloneFood(Food source)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		try
		{
			object obj = ((object)source).GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(source, null);
			Food val = (Food)((obj is Food) ? obj : null);
			if (val != null)
			{
				((Item)val).Count = 1;
				val.Exp = Math.Clamp(val.Exp, -2000000, 2000000);
				val.Strength = Math.Clamp(val.Strength, -100000.0, 100000.0);
				val.StrengthFood = Math.Clamp(val.StrengthFood, -100000.0, 100000.0);
				val.StrengthDrink = Math.Clamp(val.StrengthDrink, -100000.0, 100000.0);
				val.Feeling = Math.Clamp(val.Feeling, -100000.0, 100000.0);
				val.Health = Math.Clamp(val.Health, -100000.0, 100000.0);
				val.Likability = Math.Clamp(val.Likability, -100000.0, 100000.0);
				((Item)val).Price = Math.Clamp(((Item)val).Price, 0.0, 2000000000.0);
			}
			return val;
		}
		catch (Exception)
		{
			return null;
		}
	}

	internal Item CloneItem(Item source)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		if (source == null)
		{
			return null;
		}
		try
		{
			object obj = ((object)source).GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(source, null);
			return (Item)((obj is Item) ? obj : null);
		}
		catch
		{
			return null;
		}
	}

	private bool OnMagicPouchUsed(IMainWindow mw, Item item)
	{
		if (item == null || item.Name != "锦囊")
		{
			return false;
		}
		try
		{
			GameSave_VPet gameSave = mw.GameSavesData.GameSave;
			int moneylimit = Math.Min(20000, (50 * (gameSave.LevelMax + 1) + gameSave.Level + 1) * 50);
			List<Food> list = mw.Foods.Where((Food f) => ((Item)f).Price > 10.0 && ((Item)f).Price < (double)moneylimit).ToList();
			if (list.Count == 0)
			{
				mw.Main.Say("锦囊空空如也~ 没有可发放的道具喵~", (string)null, false, (string)null);
				return true;
			}
			Random random = new Random();
			int num = 0;
			for (int i = 0; i < 3; i++)
			{
				Food source = list[random.Next(list.Count)];
				Food val = CloneFood(source);
				if (val != null)
				{
					mw.ItemsAdd((Item)(object)val);
					num++;
				}
			}
			int count = item.Count;
			item.Count = count - 1;
			if (item.Count <= 0)
			{
				mw.Items.Remove(item);
			}
			mw.Main.Say($"锦囊开启！获得 {num} 个随机道具~", (string)null, false, (string)null);
			return true;
		}
		catch (Exception)
		{
			mw.Main.Say("锦囊使用失败喵~", (string)null, false, (string)null);
			return true;
		}
	}

	private static string FmtMoney(double v)
	{
		double num = Math.Abs(v);
		string value = ((v < 0.0) ? "-" : "");
		if (!(num >= 1000000000.0))
		{
			if (!(num >= 1000000.0))
			{
				if (!(num >= 10000.0))
				{
					return $"{value}{num:F0}";
				}
				return $"{value}{num / 1000.0:F1}K";
			}
			return $"{value}{num / 1000000.0:F2}M";
		}
		return $"{value}{num / 1000000000.0:F2}B";
	}
}
