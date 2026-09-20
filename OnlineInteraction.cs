using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.OnlineInteraction;

public class OnlineInteraction : MainPlugin
{
	private const int MSGTYPE_REDPACKET = 100;

	private const int MSGTYPE_BATCHFEED = 101;

	private IMPWindows? _mpWin;

	private TabItem? _tabItem;

	private TabItem? _logTabItem;

	internal InteractionPanel? _panel;

	internal LogPanel? _logPanel;

	public override string PluginName => "VPet联机互动";

	public OnlineInteraction(IMainWindow mainwin)
		: base(mainwin)
	{
	}

	public override void LoadPlugin()
	{
		base.MW.MutiPlayerHandle += OnMutiPlayerStart;
	}

	private void OnMutiPlayerStart(IMPWindows mpWin)
	{
		_mpWin = mpWin;
		_mpWin.ReceivedMessage += OnReceivedMessage;
		_mpWin.ClosingMutiPlayer += OnClosingMutiPlayer;
		((DispatcherObject)base.MW.Main).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			_panel = new InteractionPanel(this, _mpWin);
			_tabItem = new TabItem
			{
				Header = "互动",
				Content = _panel
			};
			_mpWin.TabControl.Items.Add(_tabItem);
			_logPanel = new LogPanel();
			_logTabItem = new TabItem
			{
				Header = "互动日志",
				Content = _logPanel
			};
			_mpWin.TabControl.Items.Add(_logTabItem);
		}, Array.Empty<object>());
	}

	private void OnClosingMutiPlayer()
	{
		if (_mpWin != null)
		{
			_mpWin.ReceivedMessage -= OnReceivedMessage;
		}
		((DispatcherObject)base.MW.Main).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			TabItem[] array = new TabItem[2] { _tabItem, _logTabItem };
			foreach (TabItem tabItem in array)
			{
				if (tabItem?.Parent is ItemsControl itemsControl)
				{
					itemsControl.Items.Remove(tabItem);
				}
			}
		}, Array.Empty<object>());
		_mpWin = null;
		_tabItem = null;
		_logTabItem = null;
		_panel = null;
		_logPanel = null;
	}

	private void OnReceivedMessage(ulong friendId, MPMessage msg)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		switch (((MPMessage)(ref msg)).Type)
		{
		case 1:
		{
			Chat chat = ((MPMessage)(ref msg)).GetContent<Chat>();
			if (!string.IsNullOrEmpty(((Chat)(ref chat)).Content))
			{
				((DispatcherObject)base.MW.Main).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					//IL_0017: Unknown result type (might be due to invalid IL or missing references)
					HandleChatMessage(friendId, chat);
				}, Array.Empty<object>());
			}
			break;
		}
		case 3:
		{
			Interact interact = ((MPMessage)(ref msg)).GetContent<Interact>();
			((DispatcherObject)base.MW.Main).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				//IL_005d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0063: Expected I4, but got Unknown
				IMPWindows mpWin = _mpWin;
				IMPFriend val = ((mpWin == null) ? null : mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId)));
				string text = ((val != null) ? val.Name : null) ?? friendId.ToString();
				switch ((int)interact)
				{
				case 0:
					base.MW.Main.DisplayToTouchHead();
					_logPanel?.AddLog(text + " 摸了摸你的头");
					break;
				case 1:
					base.MW.Main.DisplayToTouchBody();
					_logPanel?.AddLog(text + " 摸了摸你的身子");
					break;
				case 2:
					base.MW.Main.DisplayToTouchHead();
					_logPanel?.AddLog(text + " 捏了捏你的脸");
					break;
				}
			}, Array.Empty<object>());
			break;
		}
		case 100:
		{
			RedPacketData content2 = ((MPMessage)(ref msg)).GetContent<RedPacketData>();
			RedPacketData safe2 = new RedPacketData
			{
				Amount = content2.Amount,
				SenderName = content2.SenderName,
				Timestamp = content2.Timestamp
			};
			((DispatcherObject)base.MW.Main).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				IMPWindows mpWin2 = _mpWin;
				IMPFriend val2 = ((mpWin2 == null) ? null : mpWin2.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId)));
				string value = ((val2 != null) ? val2.Name : null) ?? safe2.SenderName;
				if (safe2.Amount > 0.0)
				{
					IGameSave save = base.MW.Core.Save;
					save.Money += safe2.Amount;
					_logPanel?.AddLog($"收到 {value} 的红包: +{safe2.Amount:F0} 金币");
				}
			}, Array.Empty<object>());
			break;
		}
		case 101:
		{
			BatchFeedData content = ((MPMessage)(ref msg)).GetContent<BatchFeedData>();
			BatchFeedData safe = new BatchFeedData
			{
				FoodName = content.FoodName,
				Quantity = content.Quantity,
				Price = content.Price,
				SenderName = content.SenderName,
				RawStats = new Dictionary<string, double>(content.RawStats)
			};
			((DispatcherObject)base.MW.Main).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				ApplyBatchFeed(safe, friendId);
			}, Array.Empty<object>());
			break;
		}
		}
	}

	private void ApplyBatchFeed(BatchFeedData bf, ulong friendId)
	{
		IMPWindows mpWin = _mpWin;
		IMPFriend val = ((mpWin == null) ? null : mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId)));
		string value = ((val != null) ? val.Name : null) ?? bf.SenderName;
		for (int i = 0; i < bf.Quantity; i++)
		{
			Food val2 = base.MW.Foods?.FirstOrDefault((Func<Food, bool>)((Food f) => ((Item)f).Name == bf.FoodName));
			if (val2 != null)
			{
				base.MW.Core.Save.EatFood((IFood)(object)val2);
				if (i == 0)
				{
					base.MW.DisplayFoodAnimation(val2.GetGraph(), (ImageSource)((Item)val2).ImageSource);
				}
			}
			else
			{
				ApplyRawStats(bf.RawStats, bf.FoodName, i == 0);
			}
		}
		_logPanel?.AddLog($"收到 {value} 的投喂: {bf.FoodName} x{bf.Quantity}");
	}

	private void ApplyRawStats(Dictionary<string, double> stats, string foodName, bool logFirst)
	{
		IGameSave save = base.MW.Core.Save;
		bool flag = false;
		if (stats.TryGetValue("exp", out var value) && value > 0.0)
		{
			save.Exp += value;
			flag = true;
		}
		if (stats.TryGetValue("strength_food", out var value2) && value2 > 0.0)
		{
			save.StrengthFood += value2;
			flag = true;
		}
		if (stats.TryGetValue("strength_drink", out var value3) && value3 > 0.0)
		{
			save.StrengthDrink += value3;
			flag = true;
		}
		if (stats.TryGetValue("strength", out var value4) && value4 > 0.0)
		{
			save.Strength += value4;
			flag = true;
		}
		if (stats.TryGetValue("feeling", out var value5) && value5 > 0.0)
		{
			save.Feeling += value5;
			flag = true;
		}
		if (stats.TryGetValue("health", out var value6) && value6 > 0.0)
		{
			save.Health += value6;
			flag = true;
		}
		if (stats.TryGetValue("likability", out var value7) && value7 > 0.0)
		{
			save.Likability += value7;
			flag = true;
		}
		if (logFirst && flag)
		{
			_logPanel?.AddLog("\"" + foodName + "\" 物品未找到，属性已直接加成");
		}
	}

	private void HandleChatMessage(ulong friendId, Chat chat)
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		IMPWindows mpWin = _mpWin;
		IMPFriend val = ((mpWin == null) ? null : mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId)));
		string text = ((val != null) ? val.Name : null) ?? friendId.ToString();
		if (((Chat)(ref chat)).Content.StartsWith("[GREET]"))
		{
			string text2 = ((Chat)(ref chat)).Content.Substring(7).Trim();
			_logPanel?.AddLog(text + " 打招呼: " + text2);
			if (val != null)
			{
				val.DisplayMessage(chat);
			}
		}
		else if (((Chat)(ref chat)).Content.StartsWith("[REDPACKET]"))
		{
			string[] array = ((Chat)(ref chat)).Content.Substring(10).Split('|');
			if (array.Length != 0 && double.TryParse(array[0], out var result) && result > 0.0)
			{
				IGameSave save = base.MW.Core.Save;
				save.Money += result;
				_logPanel?.AddLog($"收到 {text} 的红包: +{result:F0} 金币");
			}
		}
		else
		{
			_logPanel?.AddLog(text + ": " + ((Chat)(ref chat)).Content);
		}
	}

	public void SendGreet(ulong? friendId, string message)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		if (_mpWin == null)
		{
			_logPanel?.AddLog("未连接到服务器");
			return;
		}
		Chat content;
		if (!friendId.HasValue)
		{
			content = default(Chat);
			((Chat)(ref content)).Content = "[GREET] " + message;
			((Chat)(ref content)).ChatType = (Type)2;
			((Chat)(ref content)).SendName = base.MW.Core.Save.Name;
			_mpWin.SendMessageALL(MakeMPMsg<Chat>(1, content));
			_logPanel?.AddLog("向大家打招呼: " + message);
			return;
		}
		IMPFriend val = _mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId));
		if (val == null)
		{
			_logPanel?.AddLog("好友不存在");
			return;
		}
		content = default(Chat);
		((Chat)(ref content)).Content = "[GREET] " + message;
		((Chat)(ref content)).ChatType = (Type)0;
		((Chat)(ref content)).SendName = base.MW.Core.Save.Name;
		((Chat)(ref content)).ToName = val.Name;
		_mpWin.SendMessage(friendId.Value, MakeMPMsg<Chat>(1, content));
		_logPanel?.AddLog("向 " + val.Name + " 打招呼: " + message);
	}

	public void SendRedPacket(ulong? friendId, double amount)
	{
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		if (_mpWin == null)
		{
			_logPanel?.AddLog("未连接到服务器");
			return;
		}
		if (amount <= 0.0)
		{
			_logPanel?.AddLog("红包金额必须大于0");
			return;
		}
		double maxAmount = GetMaxAmount();
		if (amount > maxAmount)
		{
			_logPanel?.AddLog($"单次最多 {maxAmount:F0} (Lv.{base.MW.Core.Save.Level})");
			return;
		}
		int num = _mpWin.Friends?.Count() ?? 0;
		double num2 = (friendId.HasValue ? amount : (amount * (double)num));
		if (base.MW.Core.Save.Money < num2)
		{
			_logPanel?.AddLog($"金币不足! 需要 {num2:F0}，当前 {base.MW.Core.Save.Money:F0}");
			return;
		}
		IGameSave save = base.MW.Core.Save;
		save.Money -= num2;
		RedPacketData content = new RedPacketData
		{
			Amount = amount,
			SenderName = base.MW.Core.Save.Name,
			Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		};
		MPMessage val = MakeMPMsg(100, content);
		if (!friendId.HasValue)
		{
			foreach (IMPFriend item in _mpWin.Friends ?? Enumerable.Empty<IMPFriend>())
			{
				_mpWin.SendMessage(item.FriendID, val);
			}
			_logPanel?.AddLog($"给大家发红包 (每人 {amount:F0}，共{num}人，共-{num2:F0})");
		}
		else
		{
			IMPFriend val2 = _mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId));
			_mpWin.SendMessage(friendId.Value, val);
			_logPanel?.AddLog($"给 {((val2 != null) ? val2.Name : null) ?? friendId.ToString()} 发红包 (-{amount:F0})");
		}
	}

	public void SendBatchFeed(ulong? friendId, Food food, int quantity)
	{
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		if (_mpWin == null)
		{
			_logPanel?.AddLog("未连接到服务器");
			return;
		}
		if (quantity <= 0)
		{
			_logPanel?.AddLog("投喂数量必须大于0");
			return;
		}
		quantity = Math.Min(quantity, 99);
		double num = ((Item)food).Price * (double)quantity;
		int num2 = _mpWin.Friends?.Count() ?? 0;
		double num3 = (friendId.HasValue ? num : (num * (double)num2));
		if (base.MW.Core.Save.Money < num3)
		{
			_logPanel?.AddLog($"金币不足! 需要 {num3:F0}，当前 {base.MW.Core.Save.Money:F0}");
			return;
		}
		IGameSave save = base.MW.Core.Save;
		save.Money -= num3;
		Dictionary<string, double> rawStats = CollectFoodStats(food);
		BatchFeedData content = new BatchFeedData
		{
			FoodName = ((Item)food).Name,
			Quantity = quantity,
			Price = ((Item)food).Price,
			SenderName = base.MW.Core.Save.Name,
			RawStats = rawStats
		};
		MPMessage val = MakeMPMsg(101, content);
		if (!friendId.HasValue)
		{
			int num4 = 0;
			foreach (IMPFriend item in _mpWin.Friends ?? Enumerable.Empty<IMPFriend>())
			{
				if (!_mpWin.SendMessage(item.FriendID, val))
				{
					_logPanel?.AddLog($"[调试] SendMessageALL→{item.Name}(ID:{item.FriendID}) 失败");
				}
				num4++;
			}
			_logPanel?.AddLog($"向大家投喂: {((Item)food).Name} x{quantity} x{num2}人 (-{num3:F0})");
		}
		else
		{
			IMPFriend val2 = _mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId));
			if (!_mpWin.SendMessage(friendId.Value, val))
			{
				_logPanel?.AddLog($"[调试] SendMessage→{((val2 != null) ? val2.Name : null) ?? friendId.ToString()}(ID:{friendId}) 失败");
			}
			_logPanel?.AddLog($"给 {((val2 != null) ? val2.Name : null) ?? friendId.ToString()} 投喂: {((Item)food).Name} x{quantity} (-{num:F0})");
		}
	}

	internal Dictionary<string, double> CollectFoodStats(Food food)
	{
		Dictionary<string, double> dictionary = new Dictionary<string, double>();
		if (food.Exp > 0)
		{
			dictionary["exp"] = food.Exp;
		}
		if (food.StrengthFood > 0.0)
		{
			dictionary["strength_food"] = food.StrengthFood;
		}
		if (food.StrengthDrink > 0.0)
		{
			dictionary["strength_drink"] = food.StrengthDrink;
		}
		if (food.Strength > 0.0)
		{
			dictionary["strength"] = food.Strength;
		}
		if (food.Feeling > 0.0)
		{
			dictionary["feeling"] = food.Feeling;
		}
		if (food.Health > 0.0)
		{
			dictionary["health"] = food.Health;
		}
		if (food.Likability > 0.0)
		{
			dictionary["likability"] = food.Likability;
		}
		return dictionary;
	}

	private void ApplyLocalFeed(Food food, int quantity)
	{
		for (int i = 0; i < quantity; i++)
		{
			base.MW.Core.Save.EatFood((IFood)(object)food);
		}
		base.MW.DisplayFoodAnimation(food.GetGraph(), (ImageSource)((Item)food).ImageSource);
	}

	public void SendInteract(ulong? friendId, Interact kind)
	{
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		if (_mpWin == null)
		{
			_logPanel?.AddLog("未连接到服务器");
			return;
		}
		IMPFriend val = ((!friendId.HasValue) ? null : _mpWin.Friends?.FirstOrDefault((Func<IMPFriend, bool>)((IMPFriend f) => f.FriendID == friendId)));
		if (val == null)
		{
			_logPanel?.AddLog("好友不存在");
			return;
		}
		if (val.InConvenience())
		{
			_logPanel?.AddLog(val.Name + " 正忙，无法互动");
			return;
		}
		_mpWin.SendMessage(val.FriendID, MakeMPMsg<Interact>(3, kind));
		_logPanel?.AddLog("对 " + val.Name + GetInteractName(kind));
	}

	private static string GetInteractName(Interact k)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Expected I4, but got Unknown
		return (int)k switch
		{
			0 => "摸了摸头", 
			1 => "摸了摸身子", 
			2 => "捏了捏脸", 
			_ => "互动了", 
		};
	}

	private static MPMessage MakeMPMsg<T>(int type, T content)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		MPMessage result = default(MPMessage);
		((MPMessage)(ref result)).Type = type;
		((MPMessage)(ref result)).SetContent((object)content);
		return result;
	}

	public double GetMaxAmount()
	{
		return Math.Min(double.MaxValue, 5000 + base.MW.Core.Save.Level * 100);
	}
}
