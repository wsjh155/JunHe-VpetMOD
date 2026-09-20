using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.OnlineInteraction;

public class InteractionPanel : UserControl
{
	private readonly OnlineInteraction _plugin;

	private readonly IMPWindows _mpWin;

	private ListBox _friendList;

	private ComboBox _foodCombo;

	private TextBox _greetInput;

	private TextBox _redInput;

	private TextBox _amountInput;

	private static readonly SolidColorBrush BorderBrushColor = new SolidColorBrush(Color.FromRgb(200, 200, 200));

	private static readonly SolidColorBrush HeaderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 220));

	public InteractionPanel(OnlineInteraction plugin, IMPWindows mpWin)
	{
		_plugin = plugin;
		_mpWin = mpWin;
		base.Background = Brushes.White;
		base.FontFamily = new FontFamily("Microsoft YaHei UI");
		BuildUI();
	}

	private void BuildUI()
	{
		//IL_0849: Unknown result type (might be due to invalid IL or missing references)
		//IL_084e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0862: Unknown result type (might be due to invalid IL or missing references)
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Padding = new Thickness(10.0)
		};
		object obj = (scrollViewer.Content = new StackPanel());
		StackPanel stackPanel2 = (StackPanel)obj;
		stackPanel2.Children.Add(CreateSectionHeader("在线好友"));
		_friendList = new ListBox
		{
			Height = 80.0,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
			FontSize = 13.0,
			BorderBrush = BorderBrushColor,
			BorderThickness = new Thickness(1.0)
		};
		_friendList.Loaded += delegate
		{
			RefreshFriendList();
		};
		try
		{
			_mpWin.OnMemberJoined += delegate
			{
				((DispatcherObject)this).Dispatcher.Invoke((Action)RefreshFriendList);
			};
		}
		catch
		{
		}
		try
		{
			_mpWin.OnMemberLeave += delegate
			{
				((DispatcherObject)this).Dispatcher.Invoke((Action)RefreshFriendList);
			};
		}
		catch
		{
		}
		stackPanel2.Children.Add(_friendList);
		stackPanel2.Children.Add(new TextBlock
		{
			Text = "提示: 未选中好友则对所有人操作",
			Foreground = Brushes.Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			TextWrapping = TextWrapping.Wrap
		});
		stackPanel2.Children.Add(CreateSectionHeader("快捷操作"));
		StackPanel stackPanel3 = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		};
		StackPanel stackPanel4 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		_greetInput = new TextBox
		{
			Text = "你好呀~",
			Width = 100.0,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		stackPanel4.Children.Add(_greetInput);
		stackPanel4.Children.Add(CreateBtn("打招呼", delegate
		{
			_plugin.SendGreet(GetSelectedFriendId(), _greetInput.Text);
		}));
		_redInput = new TextBox
		{
			Text = "100",
			Width = 50.0,
			FontSize = 13.0,
			Margin = new Thickness(6.0, 0.0, 4.0, 0.0),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		stackPanel4.Children.Add(_redInput);
		stackPanel4.Children.Add(CreateBtn("发红包", delegate
		{
			if (double.TryParse(_redInput.Text, out var result2) && result2 > 0.0)
			{
				_plugin.SendRedPacket(GetSelectedFriendId(), result2);
			}
			else
			{
				_plugin._logPanel?.AddLog("请输入有效金额");
			}
		}));
		stackPanel3.Children.Add(stackPanel4);
		StackPanel stackPanel5 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel5.Children.Add(new TextBlock
		{
			Text = "互动:",
			VerticalAlignment = VerticalAlignment.Center,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0)
		});
		stackPanel5.Children.Add(CreateBtn("摸头", delegate
		{
			DoInteract((Interact)0);
		}));
		stackPanel5.Children.Add(CreateBtn("摸身", delegate
		{
			DoInteract((Interact)1);
		}));
		stackPanel5.Children.Add(CreateBtn("捏脸", delegate
		{
			DoInteract((Interact)2);
		}));
		stackPanel3.Children.Add(stackPanel5);
		stackPanel2.Children.Add(stackPanel3);
		stackPanel2.Children.Add(CreateSectionHeader("批量投喂"));
		StackPanel stackPanel6 = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		StackPanel stackPanel7 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		stackPanel7.Children.Add(new TextBlock
		{
			Text = "食物:",
			VerticalAlignment = VerticalAlignment.Center,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0)
		});
		_foodCombo = new ComboBox
		{
			Width = 200.0,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 6.0, 0.0)
		};
		_foodCombo.Loaded += delegate
		{
			RefreshFoodList();
		};
		stackPanel7.Children.Add(_foodCombo);
		stackPanel6.Children.Add(stackPanel7);
		StackPanel stackPanel8 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel8.Children.Add(new TextBlock
		{
			Text = "数量:",
			VerticalAlignment = VerticalAlignment.Center,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0)
		});
		_amountInput = new TextBox
		{
			Text = "1",
			Width = 40.0,
			FontSize = 13.0,
			TextAlignment = TextAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center
		};
		stackPanel8.Children.Add(_amountInput);
		(int, string)[] array = new(int, string)[4]
		{
			(1, "x1"),
			(5, "x5"),
			(10, "x10"),
			(50, "x50")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (qty, text) = array[i];
			stackPanel8.Children.Add(CreateSmallBtn(text, delegate
			{
				_amountInput.Text = qty.ToString();
			}));
		}
		stackPanel8.Children.Add(CreateBtn("投喂", delegate
		{
			//IL_002e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Expected O, but got Unknown
			object obj5 = (_foodCombo.SelectedItem as ComboBoxItem)?.Tag;
			Food val3 = (Food)((obj5 is Food) ? obj5 : null);
			if (val3 == null)
			{
				_plugin._logPanel?.AddLog("请先选择食物");
			}
			else
			{
				int value = ((!int.TryParse(_amountInput.Text, out var result)) ? 1 : result);
				value = Math.Clamp(value, 1, 99);
				_plugin.SendBatchFeed(GetSelectedFriendId(), val3, value);
			}
		}));
		stackPanel6.Children.Add(stackPanel8);
		TextBlock statBlock = new TextBlock
		{
			Foreground = Brushes.Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Text = "选择食物查看属性",
			TextWrapping = TextWrapping.Wrap
		};
		_foodCombo.SelectionChanged += delegate
		{
			//IL_002e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Expected O, but got Unknown
			object obj4 = (_foodCombo.SelectedItem as ComboBoxItem)?.Tag;
			Food val2 = (Food)((obj4 is Food) ? obj4 : null);
			if (val2 != null)
			{
				List<string> list = new List<string>();
				if (val2.Exp != 0)
				{
					list.Add($"Exp:{val2.Exp:+0;-0;0}");
				}
				if (val2.StrengthFood != 0.0)
				{
					list.Add($"饱腹:{val2.StrengthFood:+0;-0;0}");
				}
				if (val2.StrengthDrink != 0.0)
				{
					list.Add($"饮品:{val2.StrengthDrink:+0;-0;0}");
				}
				if (val2.Strength != 0.0)
				{
					list.Add($"体力:{val2.Strength:+0;-0;0}");
				}
				if (val2.Feeling != 0.0)
				{
					list.Add($"心情:{val2.Feeling:+0;-0;0}");
				}
				if (val2.Health != 0.0)
				{
					list.Add($"生命:{val2.Health:+0;-0;0}");
				}
				if (val2.Likability != 0.0)
				{
					list.Add($"好感:{val2.Likability:+0;-0;0}");
				}
				statBlock.Text = ((list.Count > 0) ? string.Join(" | ", list) : "无属性变化");
			}
		};
		stackPanel6.Children.Add(statBlock);
		stackPanel2.Children.Add(stackPanel6);
		base.Content = scrollViewer;
		DispatcherTimer val = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		val.Tick += delegate
		{
			if (_mpWin.Friends != null && _friendList.Items.Count == 0)
			{
				RefreshFriendList();
			}
		};
		val.Start();
	}

	private TextBlock CreateSectionHeader(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = 15.0,
			FontWeight = FontWeights.Bold,
			Foreground = HeaderBrush,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
	}

	private Button CreateBtn(string text, Action click)
	{
		Button button = new Button();
		button.Content = text;
		button.Margin = new Thickness(2.0);
		button.Padding = new Thickness(8.0, 3.0, 8.0, 3.0);
		button.FontSize = 13.0;
		button.Cursor = Cursors.Hand;
		button.Click += delegate
		{
			click();
		};
		return button;
	}

	private Button CreateSmallBtn(string text, Action click)
	{
		Button button = new Button();
		button.Content = text;
		button.Margin = new Thickness(2.0);
		button.Padding = new Thickness(4.0, 1.0, 4.0, 1.0);
		button.FontSize = 12.0;
		button.Cursor = Cursors.Hand;
		button.Click += delegate
		{
			click();
		};
		return button;
	}

	internal void RefreshFriendList()
	{
		if (_friendList == null)
		{
			return;
		}
		_friendList.Items.Clear();
		if (_mpWin.Friends == null || !_mpWin.Friends.Any())
		{
			_friendList.Items.Add("暂无在线好友");
			return;
		}
		foreach (IMPFriend friend in _mpWin.Friends)
		{
			_friendList.Items.Add(new ListBoxItem
			{
				Content = $"{friend.Name} (ID:{friend.FriendID})",
				Tag = friend
			});
		}
	}

	internal void RefreshFoodList()
	{
		if (_foodCombo == null)
		{
			return;
		}
		OnlineInteraction plugin = _plugin;
		object obj;
		if (plugin == null)
		{
			obj = null;
		}
		else
		{
			IMainWindow mW = ((MainPlugin)plugin).MW;
			obj = ((mW != null) ? mW.Foods : null);
		}
		if (obj == null)
		{
			return;
		}
		_foodCombo.Items.Clear();
		foreach (Food food in ((MainPlugin)_plugin).MW.Foods)
		{
			Dictionary<string, double> dictionary = _plugin.CollectFoodStats(food);
			string text = ((dictionary.Count > 0) ? $" (+{dictionary.Count}项)" : "");
			_foodCombo.Items.Add(new ComboBoxItem
			{
				Content = ((Item)food).Name + text,
				Tag = food
			});
		}
		if (_foodCombo.Items.Count > 0)
		{
			_foodCombo.SelectedIndex = 0;
		}
	}

	private void DoInteract(Interact kind)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		ulong? selectedFriendId = GetSelectedFriendId();
		if (!selectedFriendId.HasValue)
		{
			_plugin._logPanel?.AddLog("请先选择一个好友");
		}
		else
		{
			_plugin.SendInteract(selectedFriendId, kind);
		}
	}

	private ulong? GetSelectedFriendId()
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected O, but got Unknown
		if (_friendList?.SelectedItem is ListBoxItem { Tag: var tag })
		{
			IMPFriend val = (IMPFriend)((tag is IMPFriend) ? tag : null);
			if (val != null)
			{
				return val.FriendID;
			}
		}
		return null;
	}
}
