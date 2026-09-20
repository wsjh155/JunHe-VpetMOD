using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LinePutScript;
using LinePutScript.Localization.WPF;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.DayTrip;

public class DayTripPlugin : MainPlugin
{
	private MenuItem _menuItem;

	internal DayTripSetting _setting;

	private winDayTripSetting _winSetting;

	internal winDayTripMain _winMain;

	private DispatcherTimer _tripTimer;

	internal TripData _currentTrip;

	internal List<TripPhoto> _tripPhotos;

	private Random _rnd = new Random();

	private List<TripRecord> _tripHistory;

	private string _historyFilePath;

	private static readonly Dictionary<string, TripMode> Modes = new Dictionary<string, TripMode>
	{
		["taqing"] = new TripMode
		{
			Id = "taqing",
			Name = "\ud83c\udf3f 踏青远行",
			DurationHours = 2,
			Cost = 5000.0,
			StaminaChange = -0.4
		},
		["cema"] = new TripMode
		{
			Id = "cema",
			Name = "\ud83d\udc0e 策马疾行",
			DurationHours = 2,
			Cost = 20000.0,
			StaminaChange = -0.4
		},
		["xianyun"] = new TripMode
		{
			Id = "xianyun",
			Name = "☁\ufe0f 闲云野鹤",
			DurationHours = 8,
			Cost = 5000.0,
			StaminaChange = 0.4
		},
		["fengya"] = new TripMode
		{
			Id = "fengya",
			Name = "\ud83c\udfef 风雅云游",
			DurationHours = 8,
			Cost = 20000.0,
			StaminaChange = 0.4
		}
	};

	private string _cachedPhotoFolder;

	public override string PluginName => "DayTrip";

	public string PhotoFolder
	{
		get
		{
			if (_cachedPhotoFolder != null)
			{
				return _cachedPhotoFolder;
			}
			foreach (DirectoryInfo item in base.MW.MODPath)
			{
				if (item.Name == "一天特种兵旅行")
				{
					string text = Path.Combine(item.FullName, "image");
					if (Directory.Exists(text))
					{
						_cachedPhotoFolder = text;
						return text;
					}
				}
			}
			foreach (DirectoryInfo item2 in base.MW.MODPath)
			{
				string text2 = Path.Combine(item2.FullName, "一天特种兵旅行", "image");
				if (Directory.Exists(text2))
				{
					_cachedPhotoFolder = text2;
					return text2;
				}
			}
			_cachedPhotoFolder = Path.Combine(base.MW.MODPath[0].FullName, "image");
			return _cachedPhotoFolder;
		}
	}

	public string PhotoFolderAlt => PhotoFolder;

	private string HistoryFilePath
	{
		get
		{
			if (_historyFilePath != null)
			{
				return _historyFilePath;
			}
			foreach (DirectoryInfo item in base.MW.MODPath)
			{
				if (item.Name == "一天特种兵旅行")
				{
					_historyFilePath = Path.Combine(item.FullName, "trip_history.lps");
					return _historyFilePath;
				}
			}
			_historyFilePath = Path.Combine(base.MW.MODPath[0].FullName, "trip_history.lps");
			return _historyFilePath;
		}
	}

	private string FindFeelingsFile()
	{
		string text = Path.Combine(PhotoFolder, "feelings.lps");
		if (File.Exists(text))
		{
			return text;
		}
		foreach (DirectoryInfo item in base.MW.MODPath)
		{
			if (item.Name == "一天特种兵旅行")
			{
				string text2 = Path.Combine(item.FullName, "feelings_data.lps");
				if (File.Exists(text2))
				{
					return text2;
				}
			}
		}
		return text;
	}

	public string FindImageFile(string imageFile)
	{
		string text = Path.Combine(PhotoFolder, imageFile);
		if (File.Exists(text))
		{
			return text;
		}
		string text2 = Path.Combine(PhotoFolderAlt, imageFile);
		if (File.Exists(text2))
		{
			return text2;
		}
		return text;
	}

	public DayTripPlugin(IMainWindow mainwin)
		: base(mainwin)
	{
	}

	public override void LoadPlugin()
	{
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected O, but got Unknown
		_setting = new DayTripSetting(null);
		try
		{
			ILine val = base.MW.Set["DayTrip"];
			if (val != null)
			{
				if (((IGetOBJ<ISub>)(object)val)[(gint)"tripDuration"] != 0)
				{
					_setting.TripDurationSeconds = ((IGetOBJ<ISub>)(object)val)[(gint)"tripDuration"];
				}
				if ((double)((IGetOBJ<ISub>)(object)val)[(gint)"tripCost"] != 0.0)
				{
					_setting.TripCost = ((IGetOBJ<ISub>)(object)val)[(gint)"tripCost"];
				}
			}
		}
		catch
		{
		}
		LoadTripPhotos();
		LoadTripHistory();
		_menuItem = new MenuItem
		{
			Header = LocalizeCore.Translate("特种兵旅行"),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			Visibility = Visibility.Visible
		};
		_menuItem.Click += delegate
		{
			ShowMainWindow();
		};
		_tripTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_tripTimer.Tick += TripTimer_Tick;
	}

	private void LoadTripPhotos()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		_tripPhotos = new List<TripPhoto>();
		string path = FindFeelingsFile();
		if (!File.Exists(path))
		{
			return;
		}
		foreach (ILine item2 in (LpsDocument<List<ILine>>)new LPS(File.ReadAllText(path, Encoding.UTF8)))
		{
			TripPhoto obj = new TripPhoto
			{
				ImageFile = ((ISub)item2).Name,
				FeelingText = ((ISub)item2).Info
			};
			ISub obj2 = item2.Find("tags");
			obj.Tags = ((obj2 != null) ? obj2.Info : null) ?? "";
			TripPhoto item = obj;
			_tripPhotos.Add(item);
		}
	}

	private void LoadTripHistory()
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected O, but got Unknown
		_tripHistory = new List<TripRecord>();
		try
		{
			if (!File.Exists(HistoryFilePath))
			{
				return;
			}
			foreach (ILine item in (LpsDocument<List<ILine>>)new LPS(File.ReadAllText(HistoryFilePath, Encoding.UTF8)))
			{
				_tripHistory.Add(TripRecord.FromLine(item));
			}
		}
		catch
		{
		}
	}

	private void SaveTripHistory()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected O, but got Unknown
		try
		{
			LPS val = new LPS();
			foreach (TripRecord item in _tripHistory)
			{
				((LpsDocument<List<ILine>>)(object)val).Add(item.ToLine());
			}
			File.WriteAllText(HistoryFilePath, ((object)val).ToString(), Encoding.UTF8);
		}
		catch
		{
		}
	}

	public List<TripRecord> GetTripHistory()
	{
		if (_tripHistory == null)
		{
			LoadTripHistory();
		}
		return _tripHistory;
	}

	public override void LoadDIY()
	{
		base.MW.Main.ToolBar.MenuDIY.Items.Add(_menuItem);
	}

	public void ShowMainWindow()
	{
		if (_winMain == null || !_winMain.IsLoaded)
		{
			_winMain = new winDayTripMain(this);
			_winMain.Show();
		}
		else
		{
			_winMain.Activate();
		}
	}

	public void StartTrip(string modeId)
	{
		if (_currentTrip != null && _currentTrip.IsTraveling)
		{
			ShowMainWindow();
		}
		else if (Modes.ContainsKey(modeId))
		{
			TripMode tripMode = Modes[modeId];
			double money = base.MW.GameSavesData.GameSave.Money;
			if (money < tripMode.Cost)
			{
				base.MW.Main.MsgBar.Show("桌宠", $"旅行费{tripMode.Cost}元，你只有{money:F0}元，攒攒钱再来吧！", (string)null, (UIElement)null);
				return;
			}
			GameSave_VPet gameSave = base.MW.GameSavesData.GameSave;
			gameSave.Money -= tripMode.Cost;
			int num = tripMode.DurationHours * 3600;
			_currentTrip = new TripData
			{
				StartTime = DateTime.Now,
				EndTime = DateTime.Now.AddSeconds(num),
				IsTraveling = true,
				ModeId = modeId,
				ModeName = tripMode.Name
			};
			base.MW.GameSavesData.SetString("DayTrip_Status", "Traveling");
			base.MW.GameSavesData.SetString("DayTrip_Mode", modeId);
			base.MW.GameSavesData.SetDateTime("DayTrip_EndTime", _currentTrip.EndTime);
			base.MW.Main.MsgBar.Show("桌宠", $"花了{tripMode.Cost}元买票，{tripMode.Name}出发啦！等我回来～", (string)null, (UIElement)null);
			_menuItem.Header = LocalizeCore.Translate("旅行中...");
			_tripTimer.Start();
			ShowMainWindow();
			_winMain.RefreshUI();
		}
	}

	private void TripTimer_Tick(object? sender, EventArgs e)
	{
		if (_currentTrip == null || !_currentTrip.IsTraveling)
		{
			_tripTimer.Stop();
			return;
		}
		TimeSpan timeSpan = _currentTrip.EndTime - DateTime.Now;
		if (timeSpan.TotalSeconds > 0.0)
		{
			_menuItem.Header = LocalizeCore.Translate($"特种兵旅行 ({(int)timeSpan.TotalSeconds}秒)");
		}
		if (DateTime.Now >= _currentTrip.EndTime)
		{
			_tripTimer.Stop();
			CompleteTrip();
		}
		if (_winMain != null && _winMain.IsLoaded)
		{
			_winMain.RefreshUI();
		}
	}

	private void CompleteTrip()
	{
		if (_currentTrip == null)
		{
			return;
		}
		string text = _currentTrip.ModeId ?? "taqing";
		TripMode valueOrDefault = Modes.GetValueOrDefault(text, Modes["taqing"]);
		TripPhoto randomPhoto = GetRandomPhoto();
		int num = _rnd.Next(10000, 100001);
		double staminaChange = valueOrDefault.StaminaChange;
		List<string> list = new List<string>();
		try
		{
			double strength = base.MW.GameSavesData.GameSave.Strength;
			if (staminaChange < 0.0)
			{
				double num2 = strength * Math.Abs(staminaChange);
				base.MW.GameSavesData.GameSave.StrengthChange(0.0 - num2);
				list.Add($"体力-{Math.Abs(staminaChange) * 100.0:F0}%");
			}
			else
			{
				double num3 = strength * staminaChange;
				base.MW.GameSavesData.GameSave.StrengthChange(num3);
				list.Add($"体力+{staminaChange * 100.0:F0}%");
			}
		}
		catch
		{
			list.Add((staminaChange < 0.0) ? "体力-40%" : "体力+40%");
		}
		string[] attrNames = new string[4] { "Likability", "StrengthFood", "StrengthDrink", "Feeling" };
		string[] attrDisplay = new string[4] { "好感度", "饱腹度", "口渴度", "心情" };
		List<string> collection = ApplyRandomAttributes(attrNames, attrDisplay, 2, 0.2);
		list.AddRange(collection);
		string text2 = $"\ud83c\udf89 经验+{num:N0}  " + string.Join("  ", list);
		try
		{
			GameSave_VPet gameSave = base.MW.GameSavesData.GameSave;
			gameSave.Exp += (double)Math.Min(num, int.MaxValue);
		}
		catch
		{
		}
		try
		{
			if (randomPhoto != null)
			{
				base.MW.Main.MsgBar.Show("桌宠", randomPhoto.FeelingText + "\n" + text2, (string)null, (UIElement)null);
			}
			else
			{
				base.MW.Main.MsgBar.Show("桌宠", "旅行回来啦！\n" + text2, (string)null, (UIElement)null);
			}
		}
		catch
		{
		}
		TripRecord tripRecord = new TripRecord
		{
			StartTime = _currentTrip.StartTime,
			EndTime = DateTime.Now,
			ModeId = text,
			ModeName = valueOrDefault.Name,
			FeelingText = (randomPhoto?.FeelingText ?? "旅行回来了"),
			ImageFile = (randomPhoto?.ImageFile ?? ""),
			RewardText = text2
		};
		_tripHistory.Add(tripRecord);
		SaveTripHistory();
		if (_winMain != null && _winMain.IsLoaded)
		{
			try
			{
				_winMain.ShowTripResult(randomPhoto, text2, tripRecord);
			}
			catch (Exception ex)
			{
				try
				{
					base.MW.Main.MsgBar.Show("旅行", "显示结果窗口出错: " + ex.Message, (string)null, (UIElement)null);
				}
				catch
				{
				}
			}
		}
		_currentTrip.IsTraveling = false;
		_currentTrip = null;
		base.MW.GameSavesData.SetString("DayTrip_Status", "Idle");
		base.MW.GameSavesData.SetString("DayTrip_Mode", "");
		base.MW.GameSavesData.SetDateTime("DayTrip_EndTime", DateTime.MinValue);
		_menuItem.Header = LocalizeCore.Translate("特种兵旅行");
	}

	private List<string> ApplyRandomAttributes(string[] attrNames, string[] attrDisplay, int count, double percentage)
	{
		List<string> list = new List<string>();
		List<int> list2 = (from _ in Enumerable.Range(0, attrNames.Length)
			orderby _rnd.Next()
			select _).Take(count).ToList();
		GameSave_VPet gameSave = base.MW.GameSavesData.GameSave;
		foreach (int item in list2)
		{
			try
			{
				PropertyInfo property = ((object)gameSave).GetType().GetProperty(attrNames[item]);
				if (!(property == null))
				{
					double num = Convert.ToDouble(property.GetValue(gameSave));
					double num2 = num * percentage * _rnd.NextDouble();
					if (num2 < 1.0)
					{
						num2 = 1.0;
					}
					MethodInfo method = ((object)gameSave).GetType().GetMethod(attrNames[item] + "Change");
					if (method != null)
					{
						method.Invoke(gameSave, new object[1] { num2 });
					}
					else
					{
						property.SetValue(gameSave, Convert.ChangeType(num + num2, property.PropertyType));
					}
					list.Add($"{attrDisplay[item]}+{num2:F0}");
				}
			}
			catch
			{
				list.Add(attrDisplay[item] + "+?");
			}
		}
		return list;
	}

	private TripPhoto? GetRandomPhoto()
	{
		if (_tripPhotos == null || _tripPhotos.Count == 0)
		{
			LoadTripPhotos();
		}
		if (_tripPhotos.Count == 0)
		{
			return null;
		}
		return _tripPhotos[_rnd.Next(_tripPhotos.Count)];
	}

	public override void Save()
	{
		if (_currentTrip != null && _currentTrip.IsTraveling)
		{
			base.MW.GameSavesData.SetString("DayTrip_Status", "Traveling");
			base.MW.GameSavesData.SetString("DayTrip_Mode", _currentTrip.ModeId ?? "taqing");
			base.MW.GameSavesData.SetDateTime("DayTrip_EndTime", _currentTrip.EndTime);
		}
	}

	public override void GameLoaded()
	{
		if (base.MW.GameSavesData.GetString("DayTrip_Status", "Idle") == "Traveling")
		{
			DateTime dateTime = base.MW.GameSavesData.GetDateTime("DayTrip_EndTime", DateTime.MinValue);
			string @string = base.MW.GameSavesData.GetString("DayTrip_Mode", "taqing");
			if (dateTime > DateTime.Now)
			{
				TripMode valueOrDefault = Modes.GetValueOrDefault(@string, Modes["taqing"]);
				_currentTrip = new TripData
				{
					StartTime = dateTime.AddHours(-valueOrDefault.DurationHours),
					EndTime = dateTime,
					IsTraveling = true,
					ModeId = @string,
					ModeName = valueOrDefault.Name
				};
				_menuItem.Header = LocalizeCore.Translate("旅行中...");
				_tripTimer.Start();
			}
			else
			{
				CompleteTrip();
			}
		}
	}

	public override void Setting()
	{
		if (_winSetting == null || !_winSetting.IsLoaded)
		{
			_winSetting = new winDayTripSetting(this);
			_winSetting.Show();
		}
		else
		{
			_winSetting.Activate();
		}
	}
}
