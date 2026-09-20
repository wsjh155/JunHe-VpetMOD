using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.BackpackTrade;

public class TradeWindow : Window
{
	private readonly IMainWindow _mw;

	private readonly BackpackTrade _plugin;

	private readonly List<Food> _allFoods;

	private string _currentCategory = "全部";

	private string _searchText = "";

	private bool _isBuyMode = true;

	private int _sortMode;

	private readonly List<(Food Food, int Count)> _cart = new List<(Food, int)>();

	private readonly HashSet<string> _favorites = new HashSet<string>();

	private readonly Dictionary<Item, int> _sellSel = new Dictionary<Item, int>();

	private WrapPanel _buyPanel;

	private WrapPanel _sellPanel;

	private WrapPanel _cartPanel;

	private ScrollViewer _buyScroll;

	private ScrollViewer _sellScroll;

	private ScrollViewer _cartScroll;

	private StackPanel _categoryPanel;

	private TextBlock _moneyLabel;

	private TextBlock _resultLabel;

	private TextBlock _statusLabel;

	private TextBox _searchBox;

	private ComboBox _sortCombo;

	private RadioButton _tabBuy;

	private RadioButton _tabSell;

	private RadioButton _tabCart;

	private Border _cartBar;

	private Border _sellBar;

	private TextBlock _cartLabel;

	private TextBlock _sellTotalLabel;

	private static readonly SolidColorBrush Blue = new SolidColorBrush(Color.FromRgb(91, 91, 214));

	private static readonly SolidColorBrush Green = new SolidColorBrush(Color.FromRgb(76, 175, 80));

	private static readonly SolidColorBrush Red = new SolidColorBrush(Color.FromRgb(229, 57, 53));

	private static readonly SolidColorBrush Gray = new SolidColorBrush(Color.FromRgb(153, 153, 153));

	private static readonly SolidColorBrush Dark = new SolidColorBrush(Color.FromRgb(51, 51, 51));

	private static readonly SolidColorBrush White = Brushes.White;

	private static readonly SolidColorBrush Bg = new SolidColorBrush(Color.FromRgb(240, 240, 245));

	private static readonly SolidColorBrush Sel = new SolidColorBrush(Color.FromRgb(232, 245, 233));

	private static readonly SolidColorBrush LtGray = new SolidColorBrush(Color.FromRgb(232, 232, 238));

	private static readonly SolidColorBrush BorderGray = new SolidColorBrush(Color.FromRgb(224, 224, 224));

	private static readonly SolidColorBrush FaBg = new SolidColorBrush(Color.FromRgb(250, 250, 250));

	private static readonly SolidColorBrush Gold = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 213, 79));

	private static readonly SolidColorBrush Sep = new SolidColorBrush(Color.FromArgb(16, 0, 0, 0));

	public TradeWindow(IMainWindow mw, BackpackTrade plugin)
	{
		_mw = mw;
		_plugin = plugin;
		_allFoods = mw.Foods.ToList();
		LoadFavorites();
		base.Title = "跳蚤超市";
		base.Width = 900.0;
		base.Height = 580.0;
		base.MinWidth = 900.0;
		base.MinHeight = 580.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		base.Background = Bg;
		base.FontSize = 14.0;
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(220.0)
				},
				new ColumnDefinition()
			}
		};
		Border element = BuildLeft();
		grid.Children.Add(element);
		Grid.SetColumn(element, 0);
		Grid element2 = BuildRight();
		grid.Children.Add(element2);
		Grid.SetColumn(element2, 1);
		base.Content = grid;
		base.Loaded += delegate
		{
			RefreshBuy();
			RefreshSell();
			RefreshCartPanel();
			UpdateMoney();
		};
	}

	private Border BuildLeft()
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(8.0, 10.0, 8.0, 10.0)
		};
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition());
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "\ud83d\udce6 跳蚤超市",
			FontSize = 18.0,
			FontWeight = FontWeights.Bold,
			Foreground = Blue,
			Margin = new Thickness(4.0, 0.0, 0.0, 8.0)
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		_tabBuy = MkRadio("购买", chk: true, TabBuy);
		_tabSell = MkRadio("出售", chk: false, TabSell);
		_tabCart = MkRadio("购物车", chk: false, TabCart);
		stackPanel2.Children.Add(_tabBuy);
		stackPanel2.Children.Add(_tabSell);
		stackPanel2.Children.Add(_tabCart);
		stackPanel.Children.Add(stackPanel2);
		stackPanel.Children.Add(new Border
		{
			Background = Sep,
			Height = 1.0,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		});
		grid.Children.Add(stackPanel);
		Grid.SetRow(stackPanel, 0);
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		_categoryPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		_categoryPanel.Children.Add(new TextBlock
		{
			Text = "商品分类",
			FontSize = 13.0,
			Foreground = Gray,
			Margin = new Thickness(4.0, 0.0, 0.0, 6.0)
		});
		string[] array = new string[11]
		{
			"全部", "Food", "Meal", "Snack", "Drink", "Functional", "Drug", "Gift", "收藏", "每日礼包",
			"锦囊"
		};
		string[] array2 = new string[11]
		{
			"\ud83c\udf5c", "\ud83c\udf56", "\ud83c\udf5a", "\ud83c\udf7f", "\ud83e\udd64", "⚡", "\ud83d\udc8a", "\ud83c\udf81", "★", "每日礼包",
			"\ud83d\udc89"
		};
		string[] array3 = new string[11]
		{
			"全部", "食物", "正餐", "零食", "饮料", "功能性", "药品", "礼物", "收藏", "每日礼包",
			"锦囊"
		};
		for (int i = 0; i < array.Length; i++)
		{
			RadioButton radioButton = new RadioButton
			{
				Content = array2[i] + " " + array3[i],
				GroupName = "Cat",
				Tag = array[i],
				IsChecked = (i == 0),
				Margin = new Thickness(2.0, 1.0, 2.0, 1.0),
				Padding = new Thickness(12.0, 6.0, 12.0, 6.0),
				Cursor = Cursors.Hand,
				FontSize = 13.0
			};
			radioButton.Checked += CatChecked;
			_categoryPanel.Children.Add(radioButton);
		}
		scrollViewer.Content = _categoryPanel;
		grid.Children.Add(scrollViewer);
		Grid.SetRow(scrollViewer, 1);
		Border border = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(245, 245, 250)),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		StackPanel stackPanel3 = new StackPanel();
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "\ud83d\udcb0 金钱",
			FontSize = 12.0,
			Foreground = Gray
		});
		_moneyLabel = new TextBlock
		{
			Text = "¥ 0.00",
			FontSize = 18.0,
			FontWeight = FontWeights.Bold,
			Foreground = Blue
		};
		stackPanel3.Children.Add(_moneyLabel);
		border.Child = stackPanel3;
		grid.Children.Add(border);
		Grid.SetRow(border, 2);
		return new Border
		{
			Background = White,
			Child = grid
		};
	}

	private Grid BuildRight()
	{
		Grid obj = new Grid
		{
			Margin = new Thickness(6.0, 10.0, 10.0, 10.0),
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition(),
				new RowDefinition
				{
					Height = GridLength.Auto
				}
			}
		};
		Border border = new Border
		{
			Background = White,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		StackPanel stackPanel = new StackPanel();
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		_searchBox = new TextBox
		{
			VerticalAlignment = VerticalAlignment.Center,
			Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
			FontSize = 13.0,
			BorderThickness = new Thickness(1.0),
			BorderBrush = BorderGray,
			Background = FaBg
		};
		_searchBox.TextChanged += delegate
		{
			_searchText = _searchBox.Text.Trim().ToLower();
			if (_isBuyMode)
			{
				RefreshBuy();
			}
			else
			{
				RefreshSell();
			}
		};
		grid.Children.Add(_searchBox);
		_sortCombo = new ComboBox
		{
			FontSize = 12.0,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Padding = new Thickness(6.0, 2.0, 6.0, 2.0),
			MinWidth = 100.0
		};
		_sortCombo.Items.Add("默认排序");
		_sortCombo.Items.Add("名称 A→Z");
		_sortCombo.Items.Add("名称 Z→A");
		_sortCombo.Items.Add("价格 低→高");
		_sortCombo.Items.Add("价格 高→低");
		_sortCombo.SelectedIndex = 0;
		_sortCombo.SelectionChanged += delegate
		{
			_sortMode = _sortCombo.SelectedIndex;
			if (_isBuyMode)
			{
				RefreshBuy();
			}
			else
			{
				RefreshSell();
			}
		};
		Grid.SetColumn(_sortCombo, 1);
		grid.Children.Add(_sortCombo);
		_resultLabel = new TextBlock
		{
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = Gray,
			FontSize = 12.0,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		Grid.SetColumn(_resultLabel, 2);
		grid.Children.Add(_resultLabel);
		stackPanel.Children.Add(grid);
		_statusLabel = new TextBlock
		{
			Text = "就绪",
			Foreground = Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(_statusLabel);
		border.Child = stackPanel;
		obj.Children.Add(border);
		Grid.SetRow(border, 0);
		_buyPanel = new WrapPanel();
		_buyScroll = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = _buyPanel
		};
		obj.Children.Add(_buyScroll);
		Grid.SetRow(_buyScroll, 1);
		_sellPanel = new WrapPanel();
		_sellScroll = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = _sellPanel,
			Visibility = Visibility.Collapsed
		};
		obj.Children.Add(_sellScroll);
		Grid.SetRow(_sellScroll, 1);
		_cartPanel = new WrapPanel();
		_cartScroll = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = _cartPanel,
			Visibility = Visibility.Collapsed
		};
		obj.Children.Add(_cartScroll);
		Grid.SetRow(_cartScroll, 1);
		_cartBar = BuildCartBar();
		obj.Children.Add(_cartBar);
		Grid.SetRow(_cartBar, 2);
		_sellBar = BuildSellBar();
		obj.Children.Add(_sellBar);
		Grid.SetRow(_sellBar, 2);
		return obj;
	}

	private Border BuildCartBar()
	{
		Border obj = new Border
		{
			Background = Blue,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		_cartLabel = new TextBlock
		{
			Foreground = White,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(_cartLabel);
		Button button = new Button
		{
			Content = "\ud83d\udcb0 结算",
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(20.0, 6.0, 20.0, 6.0),
			Cursor = Cursors.Hand,
			Background = Gold,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += Checkout;
		Grid.SetColumn(button, 1);
		grid.Children.Add(button);
		obj.Child = grid;
		return obj;
	}

	private Border BuildSellBar()
	{
		Border obj = new Border
		{
			Background = Green,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		_sellTotalLabel = new TextBlock
		{
			Foreground = White,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(_sellTotalLabel);
		CheckBox checkBox = new CheckBox
		{
			Content = "全选",
			Foreground = White,
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			Cursor = Cursors.Hand
		};
		checkBox.Checked += SellAllChecked;
		checkBox.Unchecked += SellAllUnchecked;
		Grid.SetColumn(checkBox, 1);
		grid.Children.Add(checkBox);
		Button button = new Button
		{
			Content = "\ud83d\udcb0 一键出售",
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(20.0, 6.0, 20.0, 6.0),
			Cursor = Cursors.Hand,
			Background = Gold,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += SellAll;
		Grid.SetColumn(button, 2);
		grid.Children.Add(button);
		obj.Child = grid;
		return obj;
	}

	private void SellAllChecked(object s, RoutedEventArgs e)
	{
		try
		{
			foreach (KeyValuePair<Item, int> item in _sellSel.ToList())
			{
				_sellSel[item.Key] = item.Key.Count;
			}
			RefreshSell();
			UpdateSellBar();
			_statusLabel.Text = "已设定为最大数量";
		}
		catch (Exception ex)
		{
			_statusLabel.Text = "全选失败: " + ex.Message;
		}
	}

	private void SellAllUnchecked(object s, RoutedEventArgs e)
	{
		try
		{
			_sellSel.Clear();
			RefreshSell();
			UpdateSellBar();
			_statusLabel.Text = "已清空选择";
		}
		catch (Exception ex)
		{
			_statusLabel.Text = "清空失败: " + ex.Message;
		}
	}

	private void TabBuy(object s, RoutedEventArgs e)
	{
		_isBuyMode = true;
		_buyScroll.Visibility = Visibility.Visible;
		_sellScroll.Visibility = Visibility.Collapsed;
		_cartScroll.Visibility = Visibility.Collapsed;
		_categoryPanel.Visibility = Visibility.Visible;
		_cartBar.Visibility = ((_cart.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		_sellBar.Visibility = Visibility.Collapsed;
		RefreshBuy();
		UpdateMoney();
	}

	private void TabSell(object s, RoutedEventArgs e)
	{
		_isBuyMode = false;
		_buyScroll.Visibility = Visibility.Collapsed;
		_sellScroll.Visibility = Visibility.Visible;
		_cartScroll.Visibility = Visibility.Collapsed;
		_categoryPanel.Visibility = Visibility.Collapsed;
		_cartBar.Visibility = Visibility.Collapsed;
		_sellBar.Visibility = ((_sellSel.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		RefreshSell();
		UpdateMoney();
	}

	private void TabCart(object s, RoutedEventArgs e)
	{
		_isBuyMode = false;
		_buyScroll.Visibility = Visibility.Collapsed;
		_sellScroll.Visibility = Visibility.Collapsed;
		_cartScroll.Visibility = Visibility.Visible;
		_categoryPanel.Visibility = Visibility.Collapsed;
		_cartBar.Visibility = Visibility.Collapsed;
		_sellBar.Visibility = Visibility.Collapsed;
		RefreshCartPanel();
		UpdateMoney();
	}

	private void CatChecked(object s, RoutedEventArgs e)
	{
		if (s is RadioButton { Tag: string tag })
		{
			_currentCategory = tag;
			RefreshBuy();
		}
	}

	private IEnumerable<Food> SortFood(IEnumerable<Food> f)
	{
		return _sortMode switch
		{
			1 => f.OrderBy((Food x) => ((Item)x).TranslateName), 
			2 => f.OrderByDescending((Food x) => ((Item)x).TranslateName), 
			3 => f.OrderBy((Food x) => ((Item)x).Price), 
			4 => f.OrderByDescending((Food x) => ((Item)x).Price), 
			_ => f, 
		};
	}

	private IEnumerable<Item> SortItem(IEnumerable<Item> it)
	{
		return _sortMode switch
		{
			1 => it.OrderBy((Item x) => x.TranslateName), 
			2 => it.OrderByDescending((Item x) => x.TranslateName), 
			3 => it.OrderBy((Item x) => x.Price), 
			4 => it.OrderByDescending((Item x) => x.Price), 
			_ => it, 
		};
	}

	private void AddCart(Food food, int n)
	{
		if (n > 0)
		{
			int num = _cart.FindIndex(((Food Food, int Count) c) => c.Food == food);
			if (num >= 0)
			{
				_cart[num] = (food, _cart[num].Count + n);
			}
			else
			{
				_cart.Add((food, n));
			}
			UpdateCartBar();
		}
	}

	private void UpdateCartBar()
	{
		bool flag = _cart.Count > 0;
		_cartBar.Visibility = ((!flag || !_isBuyMode) ? Visibility.Collapsed : Visibility.Visible);
		if (flag)
		{
			double num = _cart.Sum(((Food Food, int Count) c) => _plugin.GetBuyPrice(c.Food) * (double)c.Count);
			int value = _cart.Sum(((Food Food, int Count) c) => c.Count);
			_cartLabel.Text = $"\ud83d\uded2 购物车: {value} 件，合计含税 ¥{FmtMoney(num * 1.01)}";
		}
	}

	private double CartTotalWithTax()
	{
		return _cart.Sum(((Food Food, int Count) c) => _plugin.GetBuyPrice(c.Food) * (double)c.Count) * 1.01;
	}

	private void LoadFavorites()
	{
		try
		{
			string @string = _mw.GameSavesData.GetString("BackpackTrade.Favorites", "");
			if (string.IsNullOrEmpty(@string))
			{
				return;
			}
			string[] array = @string.Split(',');
			foreach (string text in array)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					_favorites.Add(text.Trim());
				}
			}
		}
		catch
		{
		}
	}

	private void SaveFavorites()
	{
		try
		{
			_mw.GameSavesData.SetString("BackpackTrade.Favorites", string.Join(",", _favorites));
		}
		catch
		{
		}
	}

	private async void Checkout(object s, RoutedEventArgs e)
	{
		double num = CartTotalWithTax();
		if (_mw.Core.Save.Money < num)
		{
			_statusLabel.Text = "金钱不足！需要 ￥" + FmtMoney(num);
			_mw.Main.Say("金钱不足喵~ 需 ￥" + FmtMoney(num), (string)null, false, (string)null);
			return;
		}
		_statusLabel.Text = "结算中，请稍候...";
		await ((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
		{
			double num2 = 0.0;
			int num3 = 0;
			foreach (var item3 in _cart.ToList())
			{
				Food item = item3.Food;
				int item2 = item3.Count;
				double num4 = _plugin.GetBuyPrice(item) * (double)item2 * 1.01;
				if (_plugin.BuyItem(item, item2))
				{
					num2 += num4;
					num3 += item2;
				}
			}
			IGameSave save = _mw.Core.Save;
			save.Money -= num2;
			_cart.Clear();
			UpdateMoney();
			UpdateCartBar();
			RefreshCartPanel();
			RefreshSell();
			RefreshBuy();
			_statusLabel.Text = ((num3 > 0) ? $"结算完成！{num3} 件，￥{FmtMoney(num2)}" : "购物车为空");
			if (num3 > 0)
			{
				_mw.Main.Say($"结算完成！共 {num3} 件，花费 ￥{FmtMoney(num2)}~", (string)null, false, (string)null);
			}
		}, (DispatcherPriority)4);
	}

	private void SetSel(Item item, int n, Border card)
	{
		_sellSel[item] = Math.Min(n, item.Count);
		if (card != null)
		{
			card.Background = Sel;
		}
		UpdateSellBar();
	}

	private void Unsel(Item item, Border card)
	{
		_sellSel.Remove(item);
		if (card != null)
		{
			card.Background = White;
		}
		UpdateSellBar();
	}

	private void UpdateSellBar()
	{
		bool flag = _sellSel.Count > 0;
		_sellBar.Visibility = ((!flag || _isBuyMode) ? Visibility.Collapsed : Visibility.Visible);
		if (!flag)
		{
			return;
		}
		double num = 0.0;
		int num2 = 0;
		foreach (KeyValuePair<Item, int> item in _sellSel)
		{
			num += _plugin.GetSellPrice(item.Key) * 0.99 * (double)item.Value;
			num2 += item.Value;
		}
		_sellTotalLabel.Text = $"✅ 已选 {num2} 件，税后 ¥{FmtMoney(num)}";
	}

	private void SellAll(object s, RoutedEventArgs e)
	{
		double num = 0.0;
		int num2 = 0;
		foreach (KeyValuePair<Item, int> item in _sellSel.ToList())
		{
			if (_plugin.SellItem(item.Key, item.Value))
			{
				num += _plugin.GetSellPrice(item.Key) * 0.99 * (double)item.Value;
				num2 += item.Value;
			}
		}
		_sellSel.Clear();
		UpdateMoney();
		UpdateSellBar();
		RefreshSell();
		RefreshBuy();
		if (num2 > 0)
		{
			_statusLabel.Text = $"出售完成！{num2} 件，¥{FmtMoney(num)}";
			_mw.Main.Say($"出售完成！共 {num2} 件，获得 ¥{FmtMoney(num)}~", (string)null, false, (string)null);
		}
	}

	private void RefreshBuy()
	{
		try
		{
			_buyPanel.Children.Clear();
			int num = 0;
			if (_currentCategory == "每日礼包" || (_currentCategory == "全部" && string.IsNullOrEmpty(_searchText)))
			{
				_buyPanel.Children.Add(DailyCard());
				num++;
			}
			if (_currentCategory == "锦囊" || (_currentCategory == "全部" && string.IsNullOrEmpty(_searchText)))
			{
				_buyPanel.Children.Add(PouchCard());
				num++;
			}
			foreach (Food item in (_currentCategory == "每日礼包" || _currentCategory == "锦囊") ? new List<Food>() : SortFood(_allFoods.Where((Food f) => _plugin.IsTradeable(f) && CatMatch(f) && SearchMatch(f))).ToList())
			{
				_buyPanel.Children.Add(BuyCard(item));
				num++;
			}
			_resultLabel.Text = $"共 {num} 件";
			_statusLabel.Text = "就绪";
		}
		catch (Exception ex)
		{
			_statusLabel.Text = "刷新失败: " + ex.Message;
		}
	}

	private Border BuyCard(Food food)
	{
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		double v = _plugin.GetBuyPrice(food) * 1.01;
		Border obj = new Border
		{
			Background = White,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0),
			Margin = new Thickness(6.0),
			Width = 200.0,
			Effect = Shadow()
		};
		Grid grid = Grid4();
		TrySetImage(grid, food, 0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = ((Item)food).TranslateName,
			FontWeight = FontWeights.Bold,
			FontSize = 14.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Foreground = Dark
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = TypeName(food.Type),
			FontSize = 11.0,
			Foreground = Gray
		});
		SetRow(grid, stackPanel, 1);
		SetRow(grid, new TextBlock
		{
			Text = "含税 ¥" + FmtMoney(v),
			FontWeight = FontWeights.Bold,
			FontSize = 13.0,
			Foreground = Blue,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		}, 2);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		(TextBox, Button, Button) tuple = QtyCtrls(1, 99999);
		TextBox num = tuple.Item1;
		Button item = tuple.Item2;
		Button item2 = tuple.Item3;
		Button button = new Button
		{
			Content = "\ud83d\uded2",
			ToolTip = "加入购物车",
			Width = 32.0,
			Height = 28.0,
			FontSize = 14.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = Blue,
			Foreground = White,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += delegate
		{
			int num3 = ((!(num.Tag is int num2)) ? 1 : num2);
			AddCart(food, num3);
			_statusLabel.Text = $"已加购: {((Item)food).TranslateName} x{num3}";
		};
		stackPanel2.Children.Add(item);
		stackPanel2.Children.Add(num);
		stackPanel2.Children.Add(item2);
		stackPanel2.Children.Add(button);
		bool flag = _favorites.Contains(((Item)food).Name);
		Button favBtn = new Button
		{
			Content = (flag ? "⭐" : "☆"),
			ToolTip = (flag ? "取消收藏" : "收藏"),
			Width = 28.0,
			Height = 28.0,
			FontSize = 14.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = new SolidColorBrush(Colors.Transparent),
			Foreground = (flag ? Gold : Gray),
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(4.0, 0.0, 0.0, 0.0)
		};
		favBtn.Click += delegate
		{
			string name = ((Item)food).Name;
			if (_favorites.Contains(name))
			{
				_favorites.Remove(name);
				favBtn.Content = "☆";
				favBtn.ToolTip = "收藏";
				favBtn.Foreground = Gray;
			}
			else
			{
				_favorites.Add(name);
				favBtn.Content = "⭐";
				favBtn.ToolTip = "取消收藏";
				favBtn.Foreground = Gold;
			}
			SaveFavorites();
		};
		stackPanel2.Children.Add(favBtn);
		SetRow(grid, stackPanel2, 3);
		obj.Child = grid;
		return obj;
	}

	private Border DailyCard()
	{
		double v = 1010.0;
		Border obj = new Border
		{
			Background = White,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0),
			Margin = new Thickness(6.0),
			Width = 200.0,
			Effect = Shadow()
		};
		Grid grid = Grid4();
		SetRow(grid, new TextBlock
		{
			Text = "\ud83c\udf81",
			FontSize = 48.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		}, 0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "\ud83c\udf81 每日礼包",
			FontWeight = FontWeights.Bold,
			FontSize = 14.0,
			TextAlignment = TextAlignment.Center,
			Foreground = Dark
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "需要背包已有每日礼包才能购买！使用后随机获得3个道具！",
			FontSize = 11.0,
			Foreground = Gray,
			TextWrapping = TextWrapping.Wrap,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 2.0, 0.0, 6.0)
		});
		SetRow(grid, stackPanel, 1);
		SetRow(grid, new TextBlock
		{
			Text = "含税 ¥" + FmtMoney(v),
			FontWeight = FontWeights.Bold,
			FontSize = 13.0,
			Foreground = Blue,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		}, 2);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		(TextBox, Button, Button) tuple = QtyCtrls(1, 99999);
		TextBox num = tuple.Item1;
		Button item = tuple.Item2;
		Button item2 = tuple.Item3;
		Button button = new Button
		{
			Content = "购",
			ToolTip = "购买",
			Width = 32.0,
			Height = 28.0,
			FontSize = 14.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = Blue,
			Foreground = White,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += delegate
		{
			int num3 = ((!(num.Tag is int num2)) ? 1 : num2);
			BuyDailyGift(num3);
			_statusLabel.Text = $"已购买: 每日礼包 x{num3}";
		};
		stackPanel2.Children.Add(item);
		stackPanel2.Children.Add(num);
		stackPanel2.Children.Add(item2);
		stackPanel2.Children.Add(button);
		SetRow(grid, stackPanel2, 3);
		obj.Child = grid;
		return obj;
	}

	private Border PouchCard()
	{
		double v = 100.0 * 1.01;
		Border obj = new Border
		{
			Background = White,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0),
			Margin = new Thickness(6.0),
			Width = 200.0,
			Effect = Shadow()
		};
		Grid grid = Grid4();
		SetRow(grid, new TextBlock
		{
			Text = "\ud83d\udc89",
			FontSize = 48.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		}, 0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "\ud83d\udc89 锦囊",
			FontWeight = FontWeights.Bold,
			FontSize = 14.0,
			TextAlignment = TextAlignment.Center,
			Foreground = Dark
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "使用后随机获得3个道具！",
			FontSize = 11.0,
			Foreground = Gray,
			TextWrapping = TextWrapping.Wrap,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 2.0, 0.0, 6.0)
		});
		SetRow(grid, stackPanel, 1);
		SetRow(grid, new TextBlock
		{
			Text = "含税 ¥" + FmtMoney(v),
			FontWeight = FontWeights.Bold,
			FontSize = 13.0,
			Foreground = Blue,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		}, 2);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		(TextBox, Button, Button) tuple = QtyCtrls(1, 99999);
		TextBox num = tuple.Item1;
		Button item = tuple.Item2;
		Button item2 = tuple.Item3;
		Button button = new Button
		{
			Content = "购",
			ToolTip = "购买",
			Width = 32.0,
			Height = 28.0,
			FontSize = 14.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = Blue,
			Foreground = White,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += delegate
		{
			int num3 = ((!(num.Tag is int num2)) ? 1 : num2);
			BuyPouch(num3);
			_statusLabel.Text = $"已购买: 锦囊 x{num3}";
		};
		stackPanel2.Children.Add(item);
		stackPanel2.Children.Add(num);
		stackPanel2.Children.Add(item2);
		stackPanel2.Children.Add(button);
		SetRow(grid, stackPanel2, 3);
		obj.Child = grid;
		return obj;
	}

	private void BuyPouch(int count)
	{
		double num = 100.0 * (double)count * 1.01;
		if (_mw.Core.Save.Money < num)
		{
			_statusLabel.Text = "金钱不足！需要 " + FmtMoney(num);
			_mw.Main.Say("金钱不足喵~ 需 " + FmtMoney(num), (string)null, false, (string)null);
			return;
		}
		IGameSave save = _mw.Core.Save;
		save.Money -= num;
		GameSave_VPet gameSave = _mw.GameSavesData.GameSave;
		int moneylimit = Math.Min(20000, (50 * (gameSave.LevelMax + 1) + gameSave.Level + 1) * 50);
		List<Food> list = _mw.Foods.Where((Food f) => ((Item)f).Price > 10.0 && ((Item)f).Price < (double)moneylimit).ToList();
		if (list.Count == 0)
		{
			_statusLabel.Text = "没有可发放的道具";
			_mw.Main.Say("锦囊空空如也~ 没有可发放的道具喵~", (string)null, false, (string)null);
			return;
		}
		Random random = new Random();
		int num2 = 0;
		for (int i = 0; i < count; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				Food source = list[random.Next(list.Count)];
				Food val = _plugin.CloneFood(source);
				if (val != null)
				{
					_mw.ItemsAdd((Item)(object)val);
					num2++;
				}
			}
		}
		UpdateMoney();
		RefreshSell();
		RefreshBuy();
		_statusLabel.Text = $"购买成功！共发放 {num2} 个随机道具";
		_mw.Main.Say($"锦囊购买成功！共获得 {num2} 个随机道具~", (string)null, false, (string)null);
	}

	private void BuyDailyGift(int count)
	{
		try
		{
			double num = 1000.0 * (double)count * 1.01;
			if (_mw.Core.Save.Money < num)
			{
				_statusLabel.Text = "金钱不足！需要 ￥" + FmtMoney(num);
				_mw.Main.Say("金钱不足喵~ 需 ￥" + FmtMoney(num), (string)null, false, (string)null);
				return;
			}
			Item val = ((IEnumerable<Item>)_mw.Items).FirstOrDefault((Func<Item, bool>)((Item x) => x.Name == "每日礼包"));
			if (val == null)
			{
				_statusLabel.Text = "背包中没有每日礼包，无法购买";
				_mw.Main.Say("背包中没有每日礼包模板喵~ 无法购买", (string)null, false, (string)null);
				return;
			}
			IGameSave save = _mw.Core.Save;
			save.Money -= num;
			for (int i = 0; i < count; i++)
			{
				Item val2 = _plugin.CloneItem(val);
				if (val2 != null)
				{
					val2.Count = 1;
					_mw.ItemsAdd(val2);
				}
			}
			UpdateMoney();
			RefreshSell();
			RefreshBuy();
			_statusLabel.Text = $"购买成功！获得 {count} 个每日礼包";
			_mw.Main.Say($"购买每日礼包成功！获得 {count} 个礼包，使用后可获得随机道具~", (string)null, false, (string)null);
		}
		catch (Exception ex)
		{
			_statusLabel.Text = "购买失败: " + ex.Message;
		}
	}

	private void RefreshSell()
	{
		try
		{
			_sellPanel.Children.Clear();
			IEnumerable<Item> enumerable = _mw.Items.Where((Item i) => _plugin.IsTradeable(i));
			if (!string.IsNullOrEmpty(_searchText))
			{
				enumerable = enumerable.Where((Item i) => i.TranslateName.ToLower().Contains(_searchText));
			}
			foreach (Item item in SortItem(enumerable))
			{
				_sellPanel.Children.Add(SellCard(item));
			}
			_statusLabel.Text = "就绪";
		}
		catch (Exception ex)
		{
			_statusLabel.Text = "刷新失败: " + ex.Message;
		}
	}

	private Border SellCard(Item item)
	{
		double v = _plugin.GetSellPrice(item) * 0.99;
		bool flag = _sellSel.ContainsKey(item);
		Border card = new Border
		{
			Background = (flag ? Sel : White),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(10.0),
			Margin = new Thickness(6.0),
			Width = 200.0,
			Effect = Shadow()
		};
		Grid grid = Grid4();
		TrySetImage(grid, item, 0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = item.TranslateName,
			FontWeight = FontWeights.Bold,
			FontSize = 13.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"库存: {item.Count}",
			FontSize = 11.0,
			Foreground = Gray
		});
		SetRow(grid, stackPanel, 1);
		SetRow(grid, new TextBlock
		{
			Text = "税后 ¥" + FmtMoney(v) + "/个",
			FontWeight = FontWeights.Bold,
			FontSize = 12.0,
			Foreground = Green,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		}, 2);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		int value;
		int dflt = ((!flag || !_sellSel.TryGetValue(item, out value)) ? 1 : value);
		CheckBox chk = new CheckBox
		{
			IsChecked = flag,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0),
			Cursor = Cursors.Hand
		};
		(TextBox, Button, Button) tuple = QtyCtrls(dflt, item.Count);
		TextBox num = tuple.Item1;
		Button item2 = tuple.Item2;
		Button item3 = tuple.Item3;
		Button button = new Button
		{
			Content = "出售",
			FontSize = 11.0,
			Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
			Cursor = Cursors.Hand,
			Background = Green,
			Foreground = White,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += delegate
		{
			int count = ((!(num.Tag is int num3)) ? 1 : num3);
			if (_plugin.SellItem(item, count))
			{
				Unsel(item, card);
				UpdateMoney();
				UpdateSellBar();
				RefreshSell();
			}
		};
		chk.Checked += delegate
		{
			int n = ((!(num.Tag is int num2)) ? 1 : num2);
			SetSel(item, n, card);
		};
		chk.Unchecked += delegate
		{
			Unsel(item, card);
		};
		num.TextChanged += delegate
		{
			if (int.TryParse(num.Text, out var result) && result >= 1)
			{
				if (result > item.Count)
				{
					result = item.Count;
					num.Text = result.ToString();
				}
				num.Tag = result;
				if (chk.IsChecked.GetValueOrDefault())
				{
					SetSel(item, result, card);
				}
			}
		};
		stackPanel2.Children.Add(chk);
		stackPanel2.Children.Add(item2);
		stackPanel2.Children.Add(num);
		stackPanel2.Children.Add(item3);
		stackPanel2.Children.Add(button);
		SetRow(grid, stackPanel2, 3);
		card.Child = grid;
		return card;
	}

	private void RefreshCartPanel()
	{
		_cartPanel.Children.Clear();
		if (_cart.Count == 0)
		{
			_cartPanel.Children.Add(new TextBlock
			{
				Text = "\ud83d\uded2 购物车是空的~\n\n在「购买」页面点 \ud83d\uded2 加购商品",
				FontSize = 16.0,
				Foreground = Gray,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 60.0, 0.0, 0.0)
			});
			return;
		}
		for (int i = 0; i < _cart.Count; i++)
		{
			int idx = i;
			(Food, int) tuple = _cart[i];
			Food food = tuple.Item1;
			int item = tuple.Item2;
			double num = _plugin.GetBuyPrice(food) * 1.01;
			double lineTotal = num * (double)item;
			Border border = CartRow(((Item)food).TranslateName, item, num, lineTotal);
			Grid grid = (Grid)border.Child;
			StackPanel stackPanel = (StackPanel)grid.Children.Cast<UIElement>().First((UIElement c) => Grid.GetColumn(c) == 2);
			Button button = (Button)stackPanel.Children[0];
			Button obj = (Button)stackPanel.Children[2];
			TextBox qtyBox = (TextBox)stackPanel.Children[1];
			Tuple<TextBox, TextBlock> tuple2 = (Tuple<TextBox, TextBlock>)border.Tag;
			TextBlock totalBlock = tuple2.Item2;
			qtyBox.TextChanged += delegate
			{
				if (int.TryParse(qtyBox.Text, out var result) && result >= 1)
				{
					_cart[idx] = (food, result);
					double v4 = num * (double)result;
					totalBlock.Text = "¥" + FmtMoney(v4);
					UpdateCartBar();
				}
			};
			((Button)grid.Children.Cast<UIElement>().First((UIElement c) => Grid.GetColumn(c) == 4)).Click += delegate
			{
				_cart.RemoveAt(idx);
				UpdateCartBar();
				UpdateCartBar();
			};
			button.Click += delegate
			{
				if (_cart[idx].Count > 1)
				{
					_cart[idx] = (food, _cart[idx].Count - 1);
					qtyBox.Text = _cart[idx].Count.ToString();
					double v3 = num * (double)_cart[idx].Count;
					totalBlock.Text = "¥" + FmtMoney(v3);
					UpdateCartBar();
				}
			};
			obj.Click += delegate
			{
				_cart[idx] = (food, _cart[idx].Count + 1);
				qtyBox.Text = _cart[idx].Count.ToString();
				double v2 = num * (double)_cart[idx].Count;
				totalBlock.Text = "¥" + FmtMoney(v2);
				UpdateCartBar();
			};
			_cartPanel.Children.Add(border);
		}
		double v = CartTotalWithTax();
		Border border2 = new Border
		{
			Background = Blue,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(16.0, 10.0, 16.0, 10.0),
			Margin = new Thickness(6.0, 8.0, 6.0, 0.0)
		};
		Grid grid2 = new Grid();
		grid2.ColumnDefinitions.Add(new ColumnDefinition());
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid2.Children.Add(new TextBlock
		{
			Text = "合计",
			Foreground = White,
			FontSize = 15.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		});
		TextBlock element = new TextBlock
		{
			Text = "¥" + FmtMoney(v) + "（含税）",
			Foreground = Gold,
			FontSize = 18.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(element, 1);
		grid2.Children.Add(element);
		border2.Child = grid2;
		_cartPanel.Children.Add(border2);
		Button button2 = new Button
		{
			Content = "\ud83d\udcb0 结算购物车",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(30.0, 10.0, 30.0, 10.0),
			Margin = new Thickness(6.0, 12.0, 6.0, 0.0),
			Cursor = Cursors.Hand,
			Background = Gold,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		button2.Click += Checkout;
		_cartPanel.Children.Add(button2);
	}

	private Border CartRow(string name, int qty, double unitPrice, double lineTotal)
	{
		Border obj = new Border
		{
			Background = White,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Margin = new Thickness(6.0, 3.0, 6.0, 0.0)
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(2.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			},
			Children = { (UIElement)new TextBlock
			{
				Text = name,
				FontWeight = FontWeights.Bold,
				FontSize = 14.0,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			} }
		};
		TextBlock element = new TextBlock
		{
			Text = "¥" + FmtMoney(unitPrice) + "/个",
			Foreground = Blue,
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		Button element2 = new Button
		{
			Content = "−",
			Width = 26.0,
			Height = 26.0,
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = LtGray,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		TextBox textBox = new TextBox
		{
			Text = qty.ToString(),
			Width = 36.0,
			Height = 24.0,
			TextAlignment = TextAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(0.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = BorderGray,
			Margin = new Thickness(2.0, 0.0, 2.0, 0.0)
		};
		Button element3 = new Button
		{
			Content = "+",
			Width = 26.0,
			Height = 26.0,
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = LtGray,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		stackPanel.Children.Add(element2);
		stackPanel.Children.Add(textBox);
		stackPanel.Children.Add(element3);
		Grid.SetColumn(stackPanel, 2);
		grid.Children.Add(stackPanel);
		TextBlock textBlock = new TextBlock
		{
			Text = "¥" + FmtMoney(lineTotal),
			FontWeight = FontWeights.Bold,
			FontSize = 14.0,
			Foreground = Dark,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		Grid.SetColumn(textBlock, 3);
		grid.Children.Add(textBlock);
		Button element4 = new Button
		{
			Content = "✕",
			Width = 26.0,
			Height = 26.0,
			FontSize = 12.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = new SolidColorBrush(Color.FromRgb(253, 232, 232)),
			Foreground = Red,
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			ToolTip = "移除"
		};
		Grid.SetColumn(element4, 4);
		grid.Children.Add(element4);
		obj.Child = grid;
		obj.Tag = new Tuple<TextBox, TextBlock>(textBox, textBlock);
		return obj;
	}

	private Grid Grid4()
	{
		return new Grid
		{
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition(),
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				}
			}
		};
	}

	private void SetRow(Grid g, UIElement el, int r)
	{
		Grid.SetRow(el, r);
		g.Children.Add(el);
	}

	private RadioButton MkRadio(string text, bool chk, RoutedEventHandler handler)
	{
		RadioButton radioButton = new RadioButton();
		radioButton.Content = text;
		radioButton.IsChecked = chk;
		radioButton.FontWeight = FontWeights.Bold;
		radioButton.FontSize = 15.0;
		radioButton.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
		radioButton.Cursor = Cursors.Hand;
		radioButton.Checked += handler;
		return radioButton;
	}

	private (TextBox num, Button minus, Button plus) QtyCtrls(int dflt, int max)
	{
		Button button = new Button
		{
			Content = "−",
			Width = 28.0,
			Height = 28.0,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = LtGray,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		TextBox num = new TextBox
		{
			Text = dflt.ToString(),
			Width = 40.0,
			Height = 28.0,
			TextAlignment = TextAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			FontSize = 13.0,
			Padding = new Thickness(0.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = BorderGray,
			Tag = dflt
		};
		Button button2 = new Button
		{
			Content = "+",
			Width = 28.0,
			Height = 28.0,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Background = LtGray,
			Foreground = Dark,
			BorderThickness = new Thickness(0.0)
		};
		button.Click += delegate
		{
			if (int.TryParse(num.Text, out var result3) && result3 > 1)
			{
				result3--;
				num.Text = result3.ToString();
				num.Tag = result3;
			}
		};
		button2.Click += delegate
		{
			if (int.TryParse(num.Text, out var result2) && result2 < max)
			{
				result2++;
				num.Text = result2.ToString();
				num.Tag = result2;
			}
		};
		num.TextChanged += delegate
		{
			if (int.TryParse(num.Text, out var result) && result > 0 && result <= max)
			{
				num.Tag = result;
			}
			else if (!string.IsNullOrEmpty(num.Text))
			{
				num.Text = dflt.ToString();
			}
		};
		return (num: num, minus: button, plus: button2);
	}

	private void TrySetImage(Grid g, object obj, int row)
	{
		try
		{
			if (obj.GetType().GetProperty("ImageSource")?.GetValue(obj) is ImageSource source)
			{
				Image el = new Image
				{
					Height = 80.0,
					Width = 80.0,
					Stretch = Stretch.Uniform,
					Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
					Source = source
				};
				SetRow(g, el, row);
				return;
			}
		}
		catch
		{
		}
		SetRow(g, new TextBlock
		{
			Text = "\ud83d\udce6",
			FontSize = 40.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		}, row);
	}

	private static string FmtMoney(double v)
	{
		double num = Math.Abs(v);
		string value = ((v < 0.0) ? "-" : "");
		if (!(num >= 1000000000.0))
		{
			if (!(num >= 1000000.0))
			{
				if (num >= 10000.0)
				{
					return $"{value}{num / 1000.0:F1}K";
				}
				return $"{value}{num:F0}";
			}
			return $"{value}{num / 1000000.0:F2}M";
		}
		return $"{value}{num / 1000000000.0:F2}B";
	}

	private DropShadowEffect Shadow()
	{
		return new DropShadowEffect
		{
			BlurRadius = 6.0,
			ShadowDepth = 1.0,
			Opacity = 0.12
		};
	}

	private bool CatMatch(Food f)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		if (_currentCategory == "全部")
		{
			return true;
		}
		if (_currentCategory == "收藏")
		{
			return _favorites.Contains(((Item)f).Name);
		}
		return ((object)f.Type).ToString() == _currentCategory;
	}

	private bool SearchMatch(Food f)
	{
		if (!string.IsNullOrEmpty(_searchText))
		{
			return ((Item)f).TranslateName.ToLower().Contains(_searchText);
		}
		return true;
	}

	private string TypeName(FoodType t)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Expected I4, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		return (int)t switch
		{
			0 => "\ud83c\udf71 食物", 
			2 => "⭐ 正餐", 
			3 => "\ud83c\udf7f 零食", 
			4 => "\ud83e\udd64 饮料", 
			5 => "⚡ 功能性", 
			6 => "\ud83d\udc8a 药品", 
			7 => "\ud83c\udf81 礼物", 
			_ => ((object)t).ToString(), 
		};
	}

	private void UpdateMoney()
	{
		try
		{
			_moneyLabel.Text = "¥ " + FmtMoney(_mw.Core.Save.Money);
		}
		catch
		{
		}
	}
}
