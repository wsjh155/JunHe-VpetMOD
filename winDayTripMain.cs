using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.DayTrip;

public class winDayTripMain : Window, IComponentConnector
{
	private DayTripPlugin _plugin;

	private DispatcherTimer _uiTimer;

	private List<TripPhoto> _albumQueue = new List<TripPhoto>();

	private int _albumIndex = -1;

	private List<TripRecord> _historyList = new List<TripRecord>();

	private int _historyIndex = -1;

	private bool _browsingHistory;

	internal TextBlock StatusText;

	internal TextBlock CountdownText;

	internal Image PhotoImage;

	internal Viewbox GifHost;

	internal TextBlock NoPhotoText;

	internal TextBlock FeelingText;

	internal Border RewardBorder;

	internal TextBlock RewardText;

	internal UniformGrid ModeGrid;

	internal Button BtnTaqing;

	internal Button BtnCema;

	internal Button BtnXianyun;

	internal Button BtnFengya;

	internal Button AlbumButton;

	internal Button HistoryButton;

	internal Button SettingButton;

	private bool _contentLoaded;

	public winDayTripMain(DayTripPlugin plugin)
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		InitializeComponent();
		_plugin = plugin;
		_uiTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_uiTimer.Tick += UiTimer_Tick;
		base.Closed += delegate
		{
			_uiTimer.Stop();
		};
		RefreshUI();
		_uiTimer.Start();
	}

	private void UiTimer_Tick(object? sender, EventArgs e)
	{
		RefreshUI();
	}

	public void RefreshUI()
	{
		TripData currentTrip = _plugin._currentTrip;
		bool flag = currentTrip?.IsTraveling ?? false;
		BtnTaqing.IsEnabled = !flag;
		BtnCema.IsEnabled = !flag;
		BtnXianyun.IsEnabled = !flag;
		BtnFengya.IsEnabled = !flag;
		if (flag)
		{
			TimeSpan timeSpan = currentTrip.EndTime - DateTime.Now;
			if (timeSpan.TotalSeconds > 0.0)
			{
				StatusText.Text = "\ud83c\udfd6\ufe0f " + currentTrip.ModeName + "中...";
				int num = (int)timeSpan.TotalMinutes;
				CountdownText.Text = ((num > 0) ? $"{num}分{timeSpan.Seconds}秒后回来" : $"{timeSpan.Seconds}秒后回来");
				CountdownText.Visibility = Visibility.Visible;
			}
			else
			{
				StatusText.Text = "\ud83c\udf89 旅行回来了！";
				CountdownText.Visibility = Visibility.Collapsed;
			}
		}
		else if (!_browsingHistory)
		{
			StatusText.Text = "选择出行方式！";
			CountdownText.Visibility = Visibility.Collapsed;
		}
	}

	public void ShowTripResult(TripPhoto? photo, string rewardText, TripRecord record)
	{
		try
		{
			_browsingHistory = false;
			StatusText.Text = "\ud83c\udf89 旅行归来！";
			CountdownText.Visibility = Visibility.Collapsed;
			if (photo != null)
			{
				FeelingText.Text = photo.FeelingText ?? "(无感受文字)";
				LoadPhoto(photo);
			}
			else
			{
				FeelingText.Text = "旅行回来了，不过这次没拍到什么照片…";
			}
			if (!string.IsNullOrEmpty(rewardText))
			{
				RewardText.Text = rewardText;
				RewardBorder.Visibility = Visibility.Visible;
			}
		}
		catch (Exception ex)
		{
			FeelingText.Text = "显示旅行结果时出错: " + ex.Message;
		}
	}

	private void LoadPhoto(TripPhoto photo)
	{
		string text = null;
		try
		{
			text = _plugin.FindImageFile(photo.ImageFile);
			if (!File.Exists(text))
			{
				NoPhotoText.Text = "\ud83d\udcf8 找不到图片: " + photo.ImageFile;
				NoPhotoText.Visibility = Visibility.Visible;
				return;
			}
			byte[] array = File.ReadAllBytes(text);
			if (array.Length == 0)
			{
				NoPhotoText.Text = "\ud83d\udcf8 图片文件为空";
				NoPhotoText.Visibility = Visibility.Visible;
			}
			else
			{
				LoadStaticImage(array);
			}
		}
		catch (Exception ex)
		{
			NoPhotoText.Text = "\ud83d\udcf8 图片加载失败: " + (text ?? "null") + "\n" + ex.Message;
			NoPhotoText.Visibility = Visibility.Visible;
			PhotoImage.Visibility = Visibility.Collapsed;
		}
	}

	private void LoadPhotoByFile(string imageFile)
	{
		string text = null;
		try
		{
			text = _plugin.FindImageFile(imageFile);
			if (!File.Exists(text))
			{
				NoPhotoText.Text = "\ud83d\udcf8 找不到图片: " + imageFile;
				NoPhotoText.Visibility = Visibility.Visible;
				return;
			}
			byte[] array = File.ReadAllBytes(text);
			if (array.Length == 0)
			{
				NoPhotoText.Text = "\ud83d\udcf8 图片文件为空";
				NoPhotoText.Visibility = Visibility.Visible;
			}
			else
			{
				LoadStaticImage(array);
			}
		}
		catch (Exception ex)
		{
			NoPhotoText.Text = "\ud83d\udcf8 图片加载失败: " + ex.Message;
			NoPhotoText.Visibility = Visibility.Visible;
			PhotoImage.Visibility = Visibility.Collapsed;
		}
	}

	private void LoadStaticImage(byte[] data)
	{
		NoPhotoText.Visibility = Visibility.Collapsed;
		GifHost.Visibility = Visibility.Collapsed;
		PhotoImage.Visibility = Visibility.Visible;
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
		MemoryStream memoryStream = (MemoryStream)(bitmapImage.StreamSource = new MemoryStream(data));
		bitmapImage.EndInit();
		((Freezable)bitmapImage).Freeze();
		memoryStream.Dispose();
		if (bitmapImage.PixelWidth > 0 && bitmapImage.PixelHeight > 0)
		{
			PhotoImage.Source = bitmapImage;
			return;
		}
		NoPhotoText.Text = "\ud83d\udcf8 图片解码失败（像素宽高为0）";
		NoPhotoText.Visibility = Visibility.Visible;
		PhotoImage.Visibility = Visibility.Collapsed;
	}

	private void BtnTaqing_Click(object sender, RoutedEventArgs e)
	{
		_plugin.StartTrip("taqing");
	}

	private void BtnCema_Click(object sender, RoutedEventArgs e)
	{
		_plugin.StartTrip("cema");
	}

	private void BtnXianyun_Click(object sender, RoutedEventArgs e)
	{
		_plugin.StartTrip("xianyun");
	}

	private void BtnFengya_Click(object sender, RoutedEventArgs e)
	{
		_plugin.StartTrip("fengya");
	}

	private void AlbumButton_Click(object sender, RoutedEventArgs e)
	{
		List<TripPhoto> tripPhotos = _plugin._tripPhotos;
		if (tripPhotos == null || tripPhotos.Count == 0)
		{
			FeelingText.Text = "还没有旅行照片呢，先去旅行吧！";
			return;
		}
		_browsingHistory = false;
		if (_albumQueue == null || _albumQueue.Count != tripPhotos.Count)
		{
			_albumQueue = new List<TripPhoto>(tripPhotos);
			_albumIndex = -1;
		}
		_albumIndex = (_albumIndex + 1) % _albumQueue.Count;
		TripPhoto tripPhoto = _albumQueue[_albumIndex];
		FeelingText.Text = tripPhoto.FeelingText;
		RewardBorder.Visibility = Visibility.Collapsed;
		StatusText.Text = $"\ud83d\udcf7 相册 ({_albumIndex + 1}/{_albumQueue.Count})";
		LoadPhoto(tripPhoto);
	}

	private void HistoryButton_Click(object sender, RoutedEventArgs e)
	{
		_historyList = _plugin.GetTripHistory();
		if (_historyList == null || _historyList.Count == 0)
		{
			FeelingText.Text = "还没有旅行记录，先出发一趟吧！";
			return;
		}
		_browsingHistory = true;
		_historyIndex = (_historyIndex + 1) % _historyList.Count;
		ShowHistoryRecord(_historyList[_historyIndex]);
	}

	private void ShowHistoryRecord(TripRecord record)
	{
		StatusText.Text = $"\ud83d\udcd6 旅行记录 ({_historyIndex + 1}/{_historyList.Count})";
		CountdownText.Visibility = Visibility.Collapsed;
		FeelingText.Text = $"{record.ModeName} · {record.StartTime:MM/dd HH:mm}→{record.EndTime:HH:mm}\n{record.FeelingText}";
		RewardText.Text = record.RewardText;
		RewardBorder.Visibility = Visibility.Visible;
		if (!string.IsNullOrEmpty(record.ImageFile))
		{
			LoadPhotoByFile(record.ImageFile);
		}
	}

	private void SettingButton_Click(object sender, RoutedEventArgs e)
	{
		((MainPlugin)_plugin).Setting();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.1.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/VPet.Plugin.DayTrip;component/windaytripmain.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.1.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			StatusText = (TextBlock)target;
			break;
		case 2:
			CountdownText = (TextBlock)target;
			break;
		case 3:
			PhotoImage = (Image)target;
			break;
		case 4:
			GifHost = (Viewbox)target;
			break;
		case 5:
			NoPhotoText = (TextBlock)target;
			break;
		case 6:
			FeelingText = (TextBlock)target;
			break;
		case 7:
			RewardBorder = (Border)target;
			break;
		case 8:
			RewardText = (TextBlock)target;
			break;
		case 9:
			ModeGrid = (UniformGrid)target;
			break;
		case 10:
			BtnTaqing = (Button)target;
			BtnTaqing.Click += BtnTaqing_Click;
			break;
		case 11:
			BtnCema = (Button)target;
			BtnCema.Click += BtnCema_Click;
			break;
		case 12:
			BtnXianyun = (Button)target;
			BtnXianyun.Click += BtnXianyun_Click;
			break;
		case 13:
			BtnFengya = (Button)target;
			BtnFengya.Click += BtnFengya_Click;
			break;
		case 14:
			AlbumButton = (Button)target;
			AlbumButton.Click += AlbumButton_Click;
			break;
		case 15:
			HistoryButton = (Button)target;
			HistoryButton.Click += HistoryButton_Click;
			break;
		case 16:
			SettingButton = (Button)target;
			SettingButton.Click += SettingButton_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
