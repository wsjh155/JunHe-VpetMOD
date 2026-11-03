using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Panuon.WPF.UI;
using System.Windows.Media.Effects;
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
    private TextBlock _statBlock;
    // v2 联机大厅 (桌宠访客表)
    private ListBox _roomList;
    private TextBox _roomIdInput;
    private TextBlock _roomStatusText;
    private TextBlock _myTableText;
    private TextBlock _visitorStateText;
    private ulong _myLobbyId;
    // 顶部 tab 栏

    // ── 配色 ──────────────────────────────────────────────
    private static readonly Color CAccent     = (Color)ColorConverter.ConvertFromString("#0096DC");
    private static readonly Color CAccentDark = (Color)ColorConverter.ConvertFromString("#0073B5");
    private static readonly Color CBg         = (Color)ColorConverter.ConvertFromString("#F4F7FB");
    private static readonly Color CCard       = Colors.White;
    private static readonly Color CText       = (Color)ColorConverter.ConvertFromString("#1F2937");
    private static readonly Color CMuted      = (Color)ColorConverter.ConvertFromString("#7A8BA0");
    private static readonly Color CBorder     = (Color)ColorConverter.ConvertFromString("#DDE3EC");
    private static readonly Color CDanger     = (Color)ColorConverter.ConvertFromString("#EF4444");
    private static readonly Color CHover      = (Color)ColorConverter.ConvertFromString("#DBEAFE");
    private static readonly Color CSelected   = (Color)ColorConverter.ConvertFromString("#BFDBFE");
    private static readonly Color CDangerLight= (Color)ColorConverter.ConvertFromString("#FEE2E2");
    private static readonly Color CAccentTint = (Color)ColorConverter.ConvertFromString("#EBF5FB");

    private static readonly SolidColorBrush AccentBrush      = new(CAccent);
    private static readonly SolidColorBrush AccentDarkBrush  = new(CAccentDark);
    private static readonly SolidColorBrush BgBrush          = new(CBg);
    private static readonly SolidColorBrush CardBrush        = new(CCard);
    private static readonly SolidColorBrush TextBrush        = new(CText);
    private static readonly SolidColorBrush MutedBrush       = new(CMuted);
    private static readonly SolidColorBrush BdrBrush = new(CBorder);
    private static readonly SolidColorBrush DangerBrush      = new(CDanger);
    private static readonly SolidColorBrush HoverBrush       = new(CHover);
    private static readonly SolidColorBrush SelectedBrush    = new(CSelected);
    private static readonly SolidColorBrush DangerLightBrush = new(CDangerLight);
    private static readonly SolidColorBrush AccentTintBrush  = new(CAccentTint);
    private static readonly SolidColorBrush WhiteBrush       = Brushes.White;

    // ── 圆角 & 阴影 ─────────────────────────────────────
    private static readonly CornerRadius R5  = new(5);
    private static readonly CornerRadius R6  = new(6);
    private static readonly CornerRadius R8  = new(8);
    private static readonly CornerRadius R10 = new(10);
    private static readonly DropShadowEffect CardShadow = new()
    {
        Color = Colors.Black,
        Opacity = 0.08,
        BlurRadius = 12,
        ShadowDepth = 2,
        Direction = 270
    };

    // ── 卡片容器 ─────────────────────────────────────────
    private static Border Card(UIElement child, Thickness? margin = null)
    {
        return new Border
        {
            Background = CardBrush,
            CornerRadius = R10,
            BorderBrush = BdrBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = margin ?? new Thickness(0, 0, 0, 12),
            Child = child,
            Effect = CardShadow
        };
    }

    // ── 小标题（蓝竖线） ────────────────────────────────
    private static FrameworkElement SectionTitle(string text)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var bar = new Border
        {
            Width = 4,
            Height = 16,
            Background = AccentBrush,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(bar, Dock.Left);
        dock.Children.Add(bar);
        dock.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return dock;
    }

    private static TextBlock Label(string text)
        => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = MutedBrush, Margin = new Thickness(0, 0, 6, 0) };

    // ── 主按钮 ────────────────────────────────────────────
    private Button PrimaryBtn(string text, Action click)
    {
        var b = new Button { Content = text, Cursor = Cursors.Hand };
        b.SetValue(ButtonHelper.CornerRadiusProperty, R6);
        b.SetValue(ButtonHelper.HoverBackgroundProperty, AccentDarkBrush);
        b.SetValue(ButtonHelper.HoverForegroundProperty, WhiteBrush);
        b.Background = AccentBrush;
        b.Foreground = WhiteBrush;
        b.BorderThickness = new Thickness(0);
        b.FontFamily = new FontFamily("Microsoft YaHei UI");
        b.FontSize = 13;
        b.FontWeight = FontWeights.SemiBold;
        b.Padding = new Thickness(12, 5, 12, 5);
        b.Click += (_, _) => click();
        return b;
    }

    // ── 次要按钮 ─────────────────────────────────────────
    private Button SecondaryBtn(string text, Action click)
    {
        var b = new Button { Content = text, Cursor = Cursors.Hand };
        b.SetValue(ButtonHelper.CornerRadiusProperty, R6);
        b.SetValue(ButtonHelper.HoverBackgroundProperty, HoverBrush);
        b.SetValue(ButtonHelper.HoverForegroundProperty, AccentBrush);
        b.Background = CardBrush;
        b.Foreground = TextBrush;
        b.BorderBrush = BdrBrush;
        b.BorderThickness = new Thickness(1);
        b.FontFamily = new FontFamily("Microsoft YaHei UI");
        b.FontSize = 13;
        b.Padding = new Thickness(12, 5, 12, 5);
        b.Click += (_, _) => click();
        return b;
    }

    // ── 小按钮 ────────────────────────────────────────────
    private Button SmallBtn(string text, Action click)
    {
        var b = new Button { Content = text, Cursor = Cursors.Hand };
        b.SetValue(ButtonHelper.CornerRadiusProperty, R5);
        b.SetValue(ButtonHelper.HoverBackgroundProperty, HoverBrush);
        b.SetValue(ButtonHelper.HoverForegroundProperty, AccentBrush);
        b.Background = CardBrush;
        b.Foreground = TextBrush;
        b.BorderBrush = BdrBrush;
        b.BorderThickness = new Thickness(1);
        b.FontFamily = new FontFamily("Microsoft YaHei UI");
        b.FontSize = 12;
        b.Padding = new Thickness(10, 4, 10, 4);
        b.Click += (_, _) => click();
        return b;
    }

    // ── 快捷数量按钮（迷你） ────────────────────────────
    private Button QtyBtn(string text, Action click)
    {
        var b = new Button { Content = text, Cursor = Cursors.Hand };
        b.SetValue(ButtonHelper.CornerRadiusProperty, R5);
        b.SetValue(ButtonHelper.HoverBackgroundProperty, AccentTintBrush);
        b.SetValue(ButtonHelper.HoverForegroundProperty, AccentBrush);
        b.Background = AccentTintBrush;
        b.Foreground = AccentBrush;
        b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BAE6FD"));
        b.BorderThickness = new Thickness(1);
        b.FontFamily = new FontFamily("Microsoft YaHei UI");
        b.FontSize = 12;
        b.FontWeight = FontWeights.SemiBold;
        b.Padding = new Thickness(8, 3, 8, 3);
        b.Click += (_, _) => click();
        return b;
    }

    // ── 统一文本框 ───────────────────────────────────────
    private TextBox InputBox(string text = "", double width = 0, TextAlignment align = TextAlignment.Left)
    {
        var t = new TextBox
        {
            Text = text,
            FontSize = 13,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = CardBrush,
            Foreground = TextBrush,
            BorderBrush = BdrBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 28
        };
        if (width > 0) t.Width = width;
        if (align != TextAlignment.Left) t.TextAlignment = align;
        t.SetValue(ButtonHelper.CornerRadiusProperty, R6);
        return t;
    }

    // ── 统一下拉框 ───────────────────────────────────────
    private ComboBox InputCombo(double width = 0)
    {
        var c = new ComboBox
        {
            FontSize = 13,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            Background = CardBrush,
            Foreground = TextBrush,
            BorderBrush = BdrBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 28
        };
        if (width > 0) c.Width = width;
        c.SetValue(ButtonHelper.CornerRadiusProperty, R6);
        return c;
    }

    // ── 统一列表 ─────────────────────────────────────────
    private ListBox CleanList(double? height = null)
    {
        var lb = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        if (height.HasValue) lb.Height = height.Value;
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 4)));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, CardBrush));
        itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, BdrBrush));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        itemStyle.Setters.Add(new Setter(ListBoxItemHelper.CornerRadiusProperty, R8));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, HoverBrush));
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        itemStyle.Triggers.Add(hover);
        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, SelectedBrush));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        itemStyle.Triggers.Add(selected);
        lb.ItemContainerStyle = itemStyle;
        return lb;
    }

    public InteractionPanel(OnlineInteraction plugin, IMPWindows mpWin)
    {
        _plugin = plugin;
        _mpWin = mpWin;
        Background = BgBrush;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        BuildUI();
    }

    private void BuildUI()
    {
        // ── 顶部 TabControl ──
        var tabs = new TabControl
        {
            Background = BgBrush,
            Margin = new Thickness(0),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        var tabStyle = new Style(typeof(TabItem));
        tabStyle.Setters.Add(new Setter(Control.FontSizeProperty, 16.0));
        tabStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        tabStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 10, 18, 10)));
        tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        tabStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        tabs.ItemContainerStyle = tabStyle;

        // 顺序: 互动 / 桌宠好友 / Steam好友 / 联机历史 (联机、管理已注入访客表原生侧栏)
        tabs.Items.Add(new TabItem { Header = "互动", Content = BuildInteractPage() });
        tabs.Items.Add(new TabItem { Header = "桌宠好友", Content = BuildBuddyTab() });
        tabs.Items.Add(new TabItem { Header = "Steam好友", Content = BuildSteamTab() });
        tabs.Items.Add(new TabItem { Header = "联机历史", Content = BuildHistoryTab() });

        Content = tabs;

        // 事件订阅与初始化
        if (_plugin.Lobby != null)
        {
            _plugin.Lobby.RoomsChanged += () => Dispatcher.Invoke((Action)RefreshRoomList);
            try { _plugin.Lobby.QueryFriendsRooms(); } catch { }
        }
        else
        {
            var lobbyWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            lobbyWait.Tick += (_, _) =>
            {
                if (_plugin.Lobby != null)
                {
                    _plugin.Lobby.RoomsChanged += () => Dispatcher.Invoke((Action)RefreshRoomList);
                    try { _plugin.Lobby.QueryFriendsRooms(); } catch { }
                    lobbyWait.Stop();
                    RefreshRoomList();
                }
            };
            lobbyWait.Start();
        }
        RefreshRoomList();

        // 延迟刷新（好友列表有时较慢）
        var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        retryTimer.Tick += (_, _) =>
        {
            if (_mpWin.Friends != null && _friendList.Items.Count == 0)
                RefreshFriendList();
        };
        retryTimer.Start();
    }

    // ── 「互动」页 (好友列表 + 快捷操作 + 批量投喂 + 管理员授权) ──
    private ScrollViewer BuildInteractPage()
    {
        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = BgBrush,
            Padding = new Thickness(12)
        };
        var sp = new StackPanel();

        // 好友列表卡片
        var friendCard = new StackPanel();
        friendCard.Children.Add(SectionTitle("在线好友"));
        _friendList = CleanList(100);
        _friendList.Loaded += (_, _) => RefreshFriendList();
        try { _mpWin.OnMemberJoined += delegate { Dispatcher.Invoke((Action)RefreshFriendList); }; } catch { }
        try { _mpWin.OnMemberLeave += delegate { Dispatcher.Invoke((Action)RefreshFriendList); }; } catch { }
        friendCard.Children.Add(_friendList);
        friendCard.Children.Add(new TextBlock
        {
            Text = "提示: 未选中好友则对所有人操作",
            Foreground = MutedBrush,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(Card(friendCard));

        // 快捷操作卡片
        var actionCard = new StackPanel();
        actionCard.Children.Add(SectionTitle("快捷操作"));
        var r1 = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        _greetInput = InputBox("你好呀~", 100);
        r1.Children.Add(_greetInput);
        r1.Children.Add(SmallBtn("打招呼", () => _plugin.SendGreet(GetSelectedFriendId(), _greetInput.Text)));
        _redInput = InputBox("100", 54, TextAlignment.Center);
        r1.Children.Add(_redInput);
        r1.Children.Add(SmallBtn("发红包", () =>
        {
            if (double.TryParse(_redInput.Text, out var amt) && amt > 0)
                _plugin.SendRedPacket(GetSelectedFriendId(), amt);
            else
                _plugin.Log("请输入有效金额");
        }));
        actionCard.Children.Add(r1);
        var r2 = new WrapPanel();
        r2.Children.Add(Label("互动:"));
        r2.Children.Add(SmallBtn("摸头", () => DoInteract(MPMessage.Interact.TouchHead)));
        r2.Children.Add(SmallBtn("摸身", () => DoInteract(MPMessage.Interact.TouchBody)));
        r2.Children.Add(SmallBtn("捏脸", () => DoInteract(MPMessage.Interact.TouchPinch)));
        actionCard.Children.Add(r2);
        sp.Children.Add(Card(actionCard));

        // 批量投喂卡片
        var feedCard = new StackPanel();
        feedCard.Children.Add(SectionTitle("批量投喂"));
        var foodRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        foodRow.Children.Add(Label("食物:"));
        _foodCombo = InputCombo(200);
        _foodCombo.Loaded += (_, _) => RefreshFoodList();
        foodRow.Children.Add(_foodCombo);
        feedCard.Children.Add(foodRow);
        var qtyRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        qtyRow.Children.Add(Label("数量:"));
        _amountInput = InputBox("1", 44, TextAlignment.Center);
        qtyRow.Children.Add(_amountInput);
        foreach (var (qty, label) in new(int qty, string label)[] { (1, "x1"), (5, "x5"), (10, "x10"), (50, "x50") })
        {
            qtyRow.Children.Add(QtyBtn(label, () => _amountInput.Text = qty.ToString()));
        }
        qtyRow.Children.Add(PrimaryBtn("投喂", () =>
        {
            var food = (_foodCombo.SelectedItem as ComboBoxItem)?.Tag as Food;
            if (food == null) { _plugin.Log("请先选择食物"); return; }
            int value = int.TryParse(_amountInput.Text, out var v) ? Math.Clamp(v, 1, 99) : 1;
            _plugin.SendBatchFeed(GetSelectedFriendId(), food, value);
        }));
        feedCard.Children.Add(qtyRow);
        _statBlock = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Text = "选择食物查看属性",
            TextWrapping = TextWrapping.Wrap
        };
        _foodCombo.SelectionChanged += (_, _) =>
        {
            var food = (_foodCombo.SelectedItem as ComboBoxItem)?.Tag as Food;
            if (food != null)
            {
                var list = new List<string>();
                if (food.Exp != 0) list.Add("Exp:" + (food.Exp >= 0 ? "+" : "") + food.Exp.ToString());
                if (food.StrengthFood != 0) list.Add("饱腹:" + (food.StrengthFood >= 0 ? "+" : "") + food.StrengthFood.ToString());
                if (food.StrengthDrink != 0) list.Add("饮品:" + (food.StrengthDrink >= 0 ? "+" : "") + food.StrengthDrink.ToString());
                if (food.Strength != 0) list.Add("体力:" + (food.Strength >= 0 ? "+" : "") + food.Strength.ToString());
                if (food.Feeling != 0) list.Add("心情:" + (food.Feeling >= 0 ? "+" : "") + food.Feeling.ToString());
                if (food.Health != 0) list.Add("生命:" + (food.Health >= 0 ? "+" : "") + food.Health.ToString());
                if (food.Likability != 0) list.Add("好感:" + (food.Likability >= 0 ? "+" : "") + food.Likability.ToString());
                _statBlock.Text = list.Count > 0 ? string.Join(" | ", list) : "无属性变化";
            }
        };
        feedCard.Children.Add(_statBlock);
        sp.Children.Add(Card(feedCard));

        if (AdminConfig.IsRootAdmin(_plugin.Lobby?.SelfId ?? 0UL))
        {
            var authCard = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var chk = new CheckBox { Content = "启用管理功能", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            chk.IsChecked = AdminConfig.IsAuthorized;
            chk.Checked += (_, _) => AdminConfig.SetAuthorized(true);
            chk.Unchecked += (_, _) => AdminConfig.SetAuthorized(false);
            authCard.Children.Add(chk);
            authCard.Children.Add(new TextBlock { Text = "勾选后左侧出现「管理」页签, 可授权/取消其他玩家。", Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(authCard);
        }
        root.Content = sp;
        return root;
    }

    // ── 「联机大厅」页 (我的桌号 + Steam房间 + 云端房间 + 排行榜 + 网页入口, 已合并原「云端」页) ──
    public UIElement BuildLobbyTab()
    {
        // 内容较多(两个房间来源 + 排行榜), 整页滚动, 保证小窗口下也都能看到
        var root = new ScrollViewer
        {
            Background = BgBrush,
            Padding = new Thickness(12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var card = new Border
        {
            Background = CardBrush,
            CornerRadius = R10,
            BorderBrush = BdrBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14),
            Effect = CardShadow
        };
        var inner = new StackPanel();

        // ── 标题 + 我的桌号 ──
        inner.Children.Add(SectionTitle("联机大厅 (Steam 大厅 + 云端房间)"));

        var myRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        _myTableText = new TextBlock
        {
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush,
            Text = "你还没有打开访客表",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        DockPanel.SetDock(_myTableText, Dock.Left);
        myRow.Children.Add(_myTableText);
        _visitorStateText = new TextBlock
        {
            FontSize = 11, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(_visitorStateText, Dock.Right);
        myRow.Children.Add(_visitorStateText);
        inner.Children.Add(myRow);

        // ── 按钮条: 用 WrapPanel 自动换行，窗口再窄也不会被裁掉/挤在一起 ──
        // (原先是 DockPanel 连续 Right 停靠 4 个按钮, DockPanel 不换行, 窄窗口下会被裁切)
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };

        // 1) 刷新大厅 (Steam)
        var refreshRoomsBtn = SmallBtn("刷新大厅", DoRefreshRooms);
        refreshRoomsBtn.ToolTip = "向 Steam 搜索其他装了本 mod 玩家的访客表";
        refreshRoomsBtn.Margin = new Thickness(0, 0, 6, 6);
        bar.Children.Add(refreshRoomsBtn);

        // 3) 刷新云端
        var refreshCloudBtn = SmallBtn("刷新云端", () => { RenderCloudRooms(); RenderBoard(); });
        refreshCloudBtn.ToolTip = "刷新云端房间列表与排行榜 (数据由云桥网页推送)";
        refreshCloudBtn.Margin = new Thickness(0, 0, 6, 6);
        bar.Children.Add(refreshCloudBtn);

        // 4) 重置访客表
        var resetBtn = SmallBtn("重置访客表", ResetVisitorTable);
        resetBtn.ToolTip = "强制退出当前大厅并关闭访客表窗口。\n若点「创建」提示「已经有加入了一个访客表」但看不到窗口，点这里即可恢复。";
        resetBtn.Margin = new Thickness(0, 0, 6, 6);
        bar.Children.Add(resetBtn);

        // 5) 打开云桥网页
        var openPageBtn = SmallBtn("打开云桥网页", OpenBridgePage);
        openPageBtn.ToolTip = "在浏览器打开「桌宠云桥」: 微信登录、上传/恢复整份存档、查看完整排行榜";
        openPageBtn.Margin = new Thickness(0, 0, 0, 6);
        bar.Children.Add(openPageBtn);

        inner.Children.Add(bar);

        // ── 状态文字单独一行，自动换行，不与按钮抢宽度 ──
        _roomStatusText = new TextBlock
        {
            Text = "点「刷新大厅」发现其他 mod 玩家的访客表",
            Foreground = MutedBrush, FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        inner.Children.Add(_roomStatusText);

        // ── ① Steam 大厅房间 ──
        inner.Children.Add(GroupLabel("开放中的访客表 · Steam 大厅"));
        _roomList = CleanList(null);
        inner.Children.Add(new ScrollViewer
        {
            Height = 170,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _roomList
        });

        // ── ② 云端房间 ──
        var cloudHead = new DockPanel { Margin = new Thickness(0, 12, 0, 2) };
        var cloudTitle = GroupLabel("云端房间 · 云桥");
        cloudTitle.Margin = new Thickness(0);
        DockPanel.SetDock(cloudTitle, Dock.Left);
        cloudHead.Children.Add(cloudTitle);
        _cloudStatus = new TextBlock
        {
            Text = "等待云桥网页推送…", Foreground = MutedBrush, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        DockPanel.SetDock(_cloudStatus, Dock.Right);
        cloudHead.Children.Add(_cloudStatus);
        inner.Children.Add(cloudHead);

        _cloudPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _cloudScroll = new ScrollViewer
        {
            Height = 132,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _cloudPanel
        };
        inner.Children.Add(_cloudScroll);

        // ── ③ 等级排行榜(可折叠) ──
        _boardStatus = new TextBlock { Text = "", FontSize = 11, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var boardHead = new StackPanel { Orientation = Orientation.Horizontal };
        boardHead.Children.Add(new TextBlock { Text = "等级排行榜", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center });
        boardHead.Children.Add(_boardStatus);

        _boardPanel = new StackPanel();
        var boardScroll = new ScrollViewer
        {
            Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _boardPanel
        };
        var boardExpander = new Expander
        {
            Header = boardHead,
            IsExpanded = true,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = TextBrush,
            Content = boardScroll
        };
        inner.Children.Add(boardExpander);

        // ── ④ 按大厅ID加入 ──
        var joinRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        joinRow.Children.Add(new TextBlock { Text = "按大厅ID加入:", FontSize = 13, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _roomIdInput = new TextBox { FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Width = 200, Margin = new Thickness(0, 0, 8, 0) };
        joinRow.Children.Add(_roomIdInput);
        joinRow.Children.Add(SmallBtn("加入", DoJoinRoomById));
        inner.Children.Add(joinRow);

        // ── 底部说明 ──
        inner.Children.Add(new TextBlock
        {
            Text = "云存档(整份 .lps)与完整排行榜在云桥网页里操作；mod 不直连云端，由网页经 127.0.0.1 把房间/榜单同步到这里。",
            Foreground = MutedBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });

        card.Child = inner;
        root.Content = card;

        // 订阅云端推送
        if (_plugin.Cloud != null)
        {
            _plugin.Cloud.RoomsChanged -= OnCloudRoomsChanged;
            _plugin.Cloud.RoomsChanged += OnCloudRoomsChanged;
            _plugin.Cloud.BoardChanged -= OnCloudBoardChanged;
            _plugin.Cloud.BoardChanged += OnCloudBoardChanged;
        }
        RenderCloudRooms();
        RenderBoard();
        // 进页即显示访客表实例状态(原先只在点「刷新大厅」后才填充, 首屏是空的)
        try { UpdateVisitorState(); } catch { }

        // 打开联机页时顺手把访客表窗口设为可拉伸并放大(默认窗口偏小会截断内容)
        try { OnlineInteraction.MakeVisitorTableResizable(_plugin.MW); } catch { }
        return root;
    }

    /// <summary>打开云桥网页(微信扫码登录 → 云存档 / 完整排行榜)</summary>
    private void OpenBridgePage()
    {
        if (_plugin.Cloud != null) { _plugin.Cloud.OpenBridgePage(); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CloudBridge.BridgeUrl) { UseShellExecute = true }); }
        catch (Exception ex) { _plugin.Log("打开云桥网页失败: " + ex.Message); }
    }

    /// <summary>分区小标题</summary>
    private TextBlock GroupLabel(string text)
    {
        return new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 3)
        };
    }

    /// <summary>显示访客表实例状态(是否存在/是否可见), 用于提示"卡住无法创建"的情况</summary>
    internal void UpdateVisitorState()
    {
        if (_visitorStateText == null) return;
        var st = OnlineInteraction.GetVisitorTableWindowState(_plugin.MW);
        if (st == null) { _visitorStateText.Text = ""; return; }
        if (!st.Value.exists)
        {
            _visitorStateText.Text = "访客表: 未打开";
            _visitorStateText.Foreground = MutedBrush;
        }
        else if (st.Value.visible)
        {
            _visitorStateText.Text = "访客表: 已打开";
            _visitorStateText.Foreground = MutedBrush;
        }
        else
        {
            _visitorStateText.Text = "访客表: 实例还在但窗口不可见 → 点「重置访客表」";
            _visitorStateText.Foreground = AccentBrush;
        }
    }

    /// <summary>强制释放访客表(退出大厅 + 关窗 + 清空游戏字段), 解决"无法再创建"的卡死状态</summary>
    internal void ResetVisitorTable()
    {
        var st = OnlineInteraction.GetVisitorTableWindowState(_plugin.MW);
        if (st != null && !st.Value.exists)
        {
            _plugin.Log("[联机] 当前没有访客表实例, 无需重置");
            UpdateVisitorState();
            return;
        }
        var r = MessageBox.Show(
            "将强制退出当前访客表(离开房间并关闭窗口)。\n如果你正和别人联机, 这会让对方看到你离开。\n\n确定要重置吗?",
            "重置访客表", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        bool ok = OnlineInteraction.ForceCloseVisitorTable(_plugin.MW);
        _plugin.Log(ok
            ? "[联机] 已重置访客表: 已退出大厅并关闭窗口, 现在可以重新「创建」"
            : "[联机] 重置未执行(没有可释放的访客表)");
        UpdateVisitorState();
    }

    // ───────── 云端数据展示区 (已合并进「联机大厅」页, 数据由云桥网页推送) ─────────
    private TextBlock _cloudStatus;
    private WrapPanel _cloudPanel;
    private ScrollViewer _cloudScroll;
    private StackPanel _boardPanel;
    private TextBlock _boardStatus;

    private void OnCloudRoomsChanged()
    {
        if (_cloudPanel == null) return;
        _plugin.MW?.Main?.Dispatcher?.Invoke(RenderCloudRooms);
    }

    private void OnCloudBoardChanged()
    {
        if (_boardPanel == null) return;
        _plugin.MW?.Main?.Dispatcher?.Invoke(RenderBoard);
    }

    private void RenderBoard()
    {
        if (_boardPanel == null) return;
        _boardPanel.Children.Clear();
        var board = _plugin.Cloud?.Board ?? new List<CloudEntry>();
        if (board.Count == 0)
        {
            if (_boardStatus != null) _boardStatus.Text = "暂无数据";
            _boardPanel.Children.Add(new TextBlock
            {
                Text = "登录云桥网页后，你的等级/转生/金钱会自动上榜。",
                FontSize = 12, Foreground = MutedBrush, Margin = new Thickness(2, 4, 0, 0)
            });
            return;
        }
        if (_boardStatus != null) _boardStatus.Text = $"共 {board.Count} 位玩家";
        int rank = 1;
        foreach (var e in board.Take(20))
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var left = new TextBlock
            {
                Text = $"#{rank}  {e.nickname}{(string.IsNullOrEmpty(e.pet_name) ? "" : " · " + e.pet_name)}",
                FontSize = 12, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            DockPanel.SetDock(left, Dock.Left);
            row.Children.Add(left);
            var right = new TextBlock
            {
                Text = $"Lv {e.level}" + (e.level_max > 0 ? $" ({e.level_max} 转)" : "") + $" ｜ {FormatMoney(e.money)}",
                FontSize = 12, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            _boardPanel.Children.Add(row);
            rank++;
        }
    }

    private static string FormatMoney(long m)
    {
        if (m >= 100000000L) return (m / 100000000.0).ToString("0.##") + " 亿";
        if (m >= 10000L) return (m / 10000.0).ToString("0.##") + " 万";
        return m.ToString();
    }

    private void RenderCloudRooms()
    {
        if (_cloudPanel == null) return;
        _cloudPanel.Children.Clear();
        var rooms = _plugin.Cloud?.Rooms ?? new List<CloudRoom>();
        if (rooms.Count == 0)
        {
            _cloudStatus.Text = "暂无开放房间。登录云桥网页并开房后，这里会显示所有开放中的访客表。";
            return;
        }
        _cloudStatus.Text = $"当前共 {rooms.Count} 个开放房间";
        foreach (var r in rooms)
        {
            var c = new Border
            {
                Background = CardBrush, CornerRadius = R8, BorderBrush = BdrBrush,
                BorderThickness = new Thickness(1), Margin = new Thickness(4),
                Padding = new Thickness(10, 8, 10, 8), Width = 200
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = (r.host_name ?? "某人") + " 的访客表", FontWeight = FontWeights.Bold, FontSize = 13, Foreground = TextBrush });
            if (!string.IsNullOrEmpty(r.pet_name))
                sp.Children.Add(new TextBlock { Text = "宠物: " + r.pet_name, FontSize = 12, Foreground = MutedBrush, Margin = new Thickness(0, 2, 0, 0) });
            if (!string.IsNullOrEmpty(r.region))
                sp.Children.Add(new TextBlock { Text = "地区: " + r.region, FontSize = 12, Foreground = MutedBrush });
            sp.Children.Add(new TextBlock { Text = "桌号: " + r.lobby_id, FontSize = 11, Foreground = MutedBrush, Margin = new Thickness(0, 2, 0, 0) });
            var join = SmallBtn("加入", () =>
            {
                if (ulong.TryParse(r.lobby_id, out var id) && id != 0)
                    OnlineInteraction.JoinVisitorTable(_plugin.MW, id);
            });
            join.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(join);
            c.Child = sp;
            _cloudPanel.Children.Add(c);
        }
    }

    internal void RefreshFriendList()
    {
        if (_friendList == null) return;
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
                Content = friend.Name + " (ID:" + friend.FriendID + ")",
                Tag = friend
            });
        }
    }

    internal void RefreshFoodList()
    {
        if (_foodCombo == null) return;
        if (((MainPlugin)_plugin).MW?.Foods == null) return;
        _foodCombo.Items.Clear();
        foreach (Food food in ((MainPlugin)_plugin).MW.Foods)
        {
            var stats = _plugin.CollectFoodStats(food);
            string suffix = stats.Count > 0 ? " (+" + stats.Count + "项)" : "";
            _foodCombo.Items.Add(new ComboBoxItem { Content = food.Name + suffix, Tag = food });
        }
        if (_foodCombo.Items.Count > 0) _foodCombo.SelectedIndex = 0;
    }

    private void DoInteract(MPMessage.Interact kind)
    {
        var id = GetSelectedFriendId();
        if (!id.HasValue)
            _plugin.Log("请先选择一个好朋友");
        else
            _plugin.SendInteract(id.Value, kind);
    }

    private ulong? GetSelectedFriendId()
    {
        if (_friendList?.SelectedItem is ListBoxItem { Tag: IMPFriend f })
            return f.FriendID;
        return null;
    }
    // v2 联机大厅 (桌宠访客表)
    private void RefreshRoomList()
    {
        try { OnlineInteraction.DumpReflectionDiag(_plugin.MW); } catch { }
        try { UpdateVisitorState(); } catch { }
        if (_myTableText != null)
        {
            var info = OnlineInteraction.GetVisitorTableInfo(_plugin.MW);
            if (info.HasValue)
            {
                _myLobbyId = info.Value.lobbyId;
                _myTableText.Text = "我的桌号: " + info.Value.lobbyId + "  (" + info.Value.members + " 人)";
            }
            else
            {
                _myLobbyId = 0;
                var diag = OnlineInteraction.ReflectDiagnostics();
                var vErr = OnlineInteraction.GetVisitorError();
                if (diag.StartsWith("winMutiPlayer 已定位"))
                    _myTableText.Text = "未读到访客表: " + (vErr ?? "游戏未开访客表");
                else
                    _myTableText.Text = "反射定位失败: " + diag;
            }
        }
        if (_roomList == null) return;
        _roomList.Items.Clear();
        var lobby = _plugin.Lobby;
        if (lobby == null)
        {
            _roomList.Items.Add("未连接 Steam");
            if (_roomStatusText != null) _roomStatusText.Text = "未连接 Steam";
            return;
        }
        bool canAdmin = AdminConfig.CanShowAdminUI(_plugin.Lobby?.SelfId ?? 0UL);
        var rooms = lobby.GetVisibleRooms();
        if (rooms.Count == 0)
        {
            _roomList.Items.Add("还没发现其他 mod 玩家的访客表。点「刷新大厅」, 并确认对方已打开访客表 (双方都装了本 mod)。");
        }
        else
        {
            foreach (var r in rooms)
            {
                // 两行布局: 第一行 房间名+房主, 第二行 桌号 + 按钮(自动换行, 不会被挤出可视区)
                var item = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                string curTag = r.IsCurrent ? "  [当前所在]" : "";
                item.Children.Add(new TextBlock
                {
                    Text = r.Name + "  房主:" + r.HostName,
                    FontSize = 13,
                    Foreground = TextBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var line2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                line2.Children.Add(new TextBlock
                {
                    Text = "桌号:" + r.Id + curTag,
                    FontSize = 12,
                    Foreground = MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 4)
                });
                if (r.IsCurrent)
                {
                    line2.Children.Add(new TextBlock
                    {
                        Text = "已在此",
                        Foreground = MutedBrush,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
                else
                {
                    line2.Children.Add(SmallBtn("加入", () => JoinVisitorTable(r.Id)));
                }
                if (canAdmin)
                {
                    var blocked = lobby.IsBlocked(r.HostId);
                    line2.Children.Add(SmallBtn(blocked ? "解除" : "屏蔽", () =>
                    {
                        if (blocked) lobby.UnblockPlayer(r.HostId);
                        else lobby.BlockPlayer(r.HostId);
                        RefreshRoomList();
                    }));
                }
                item.Children.Add(line2);
                _roomList.Items.Add(item);
            }
        }
        if (_roomStatusText != null)
        {
            string hubTag;
            if (lobby.SelfId == AdminConfig.ROOT_ADMIN_ID) hubTag = " [中心节点]";
            else if (lobby.IsHubActive) hubTag = " [网格模式]";
            else hubTag = " [P2P模式]";
            _roomStatusText.Text = "mod 玩家访客表: " + rooms.Count + hubTag
                + (canAdmin && lobby.BlockedList.Count > 0 ? "  (已屏蔽 " + lobby.BlockedList.Count + " 人)" : "");
        }
    }

    private void DoRefreshRooms()
    {
        var lobby = _plugin.Lobby;
        if (lobby == null) return;
        try { lobby.QueryFriendsRooms(); } catch { }
    }

    private void DoJoinRoomById()
    {
        var raw = _roomIdInput?.Text?.Trim();
        // 仅接受合法 SteamID64 (>= 76561190000000000), 避免对无意义数值发起 Steam 大厅搜索
        if (string.IsNullOrEmpty(raw) || !ulong.TryParse(raw, out var id) || id < 76561190000000000UL) return;
        JoinVisitorTable(id);
    }

    private void JoinVisitorTable(ulong lobbyId)
    {
        try
        {
            // 已经在这张访客表里 → 直接返回, 不重复创建窗口
            if (_plugin.Lobby != null)
            {
                _plugin.Lobby.RefreshMyLobbyId();
                if (_plugin.Lobby.MyLobbyId == lobbyId) return;
            }
            OnlineInteraction.JoinVisitorTable(_plugin.MW, lobbyId);
        }
        catch (Exception ex) { System.Windows.MessageBox.Show("加入失败: " + ex.Message); }
        try { _plugin.Lobby?.RefreshMyLobbyId(); } catch { }
        RefreshRoomList();
    }

    // ── v2: 多页签 + 管理 ────────────────────────────────
    private ListBox _buddyList;
    private ListBox _steamList;
    private ListBox _historyList;
    private ListBox _adminList;
    private TextBox _adminIdInput;

    private ScrollViewer BuildBuddyTab()
    {
        var sv = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = BgBrush,
            Padding = new Thickness(12)
        };
        var sp = new StackPanel();
        sp.Children.Add(SectionTitle("桌宠好友"));
        _buddyList = CleanList(null);
        sp.Children.Add(_buddyList);
        sp.Children.Add(new TextBlock
        {
            Text = "桌宠好友独立于 Steam 好友, 长期保留。在「Steam好友」或「联机历史」中点击「加为桌宠好友」即可添加。",
            Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        sv.Content = sp;
        RefreshBuddyList();
        if (_plugin.Lobby != null) _plugin.Lobby.FriendsChanged += () => Dispatcher.Invoke((Action)RefreshBuddyList);
        return sv;
    }

    private ScrollViewer BuildSteamTab()
    {
        var sv = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = BgBrush,
            Padding = new Thickness(12)
        };
        var sp = new StackPanel();
        sp.Children.Add(SectionTitle("Steam 好友 (导入来源)"));
        sp.Children.Add(new TextBlock
        {
            Text = "仅显示运行过本 mod 的 Steam 好友。点击「加为桌宠好友」即可导入。",
            Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _steamList = CleanList(null);
        sp.Children.Add(_steamList);
        sp.Children.Add(new TextBlock
        {
            Text = "提示: 若好友未显示「已安装」, 可能是其尚未启动本 mod 或 Steam 状态未刷新。",
            Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        sv.Content = sp;
        RefreshSteamList();
        if (_plugin.Lobby != null) _plugin.Lobby.FriendsChanged += () => Dispatcher.Invoke((Action)RefreshSteamList);
        return sv;
    }

    private ScrollViewer BuildHistoryTab()
    {
        var sv = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = BgBrush,
            Padding = new Thickness(12)
        };
        var sp = new StackPanel();
        sp.Children.Add(SectionTitle("联机历史"));
        sp.Children.Add(new TextBlock
        {
            Text = "所有联机过的玩家, 按最近联机时间倒序。可一键加为桌宠好友。",
            Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _historyList = CleanList(null);
        sp.Children.Add(_historyList);
        sv.Content = sp;
        RefreshHistoryList();
        if (_plugin.Lobby != null) _plugin.Lobby.FriendsChanged += () => Dispatcher.Invoke((Action)RefreshHistoryList);
        return sv;
    }

    public ScrollViewer BuildAdminTab()
    {
        var sv = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = BgBrush,
            Padding = new Thickness(12)
        };
        var sp = new StackPanel();
        sp.Children.Add(SectionTitle("本 mod 管理员"));
        sp.Children.Add(new TextBlock
        {
            Text = "你是本 mod 的最高权限管理员(君赫)。可在此授权/取消其他玩家。\n授权名单保存在本地游戏设置 vpoi_admins。",
            Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _adminList = CleanList(null);
        sp.Children.Add(_adminList);
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _adminIdInput = InputBox("输入玩家 SteamID64", 220);
        row.Children.Add(_adminIdInput);
        row.Children.Add(PrimaryBtn("授权为管理员", () =>
        {
            if (ulong.TryParse(_adminIdInput.Text, out var id) && id > 76561190000000000)
                AdminConfig.AddAdmin(id);
            else
                _plugin.Log("无效的 SteamID64");
        }));
        sp.Children.Add(row);
        sv.Content = sp;
        RefreshAdminList();
        return sv;
    }

    private ListBoxItem MakeBuddyRow(FriendEntry f)
    {
        var item = new ListBoxItem();
        var dock = new DockPanel { LastChildFill = true };
        var nameTb = new TextBlock
        {
            Text = f.Name + "  (ID:" + (f.Id) + ")",
            VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = TextBrush
        };
        DockPanel.SetDock(nameTb, Dock.Left);
        dock.Children.Add(nameTb);
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        bool canAdmin = AdminConfig.CanShowAdminUI(_plugin.Lobby?.SelfId ?? 0UL);
        if (canAdmin)
        {
            if (AdminConfig.IsAdmin(f.Id))
                panel.Children.Add(SmallBtn("取消管理员", () => AdminConfig.RemoveAdmin(f.Id)));
            else
                panel.Children.Add(SmallBtn("设为管理员", () => AdminConfig.AddAdmin(f.Id)));
        }
        panel.Children.Add(SmallBtn("取消好友", () => _plugin.Lobby?.RemoveBuddy(f.Id)));
        dock.Children.Add(panel);
        item.Content = dock;
        item.Tag = f;
        return item;
    }

    private ListBoxItem MakeSteamRow(FriendEntry f)
    {
        var item = new ListBoxItem();
        var dock = new DockPanel { LastChildFill = true };
        var nameTb = new TextBlock
        {
            Text = f.Name + "  (ID:" + (f.Id) + ")",
            VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = TextBrush
        };
        DockPanel.SetDock(nameTb, Dock.Left);
        dock.Children.Add(nameTb);
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (f.IsModUser)
            panel.Children.Add(SmallBtn("加为桌宠好友", () => _plugin.Lobby?.AddBuddy(f.Id, f.Name)));
        else
            panel.Children.Add(new TextBlock { Text = "未安装本 mod", Foreground = MutedBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        dock.Children.Add(panel);
        item.Content = dock;
        item.Tag = f;
        return item;
    }

    private ListBoxItem MakeHistoryRow(ContactEntry c)
    {
        var item = new ListBoxItem();
        var dock = new DockPanel { LastChildFill = true };
        var nameTb = new TextBlock
        {
            Text = c.Name + "  (最近: " + FormatTime(c.LastSeen) + ")",
            VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = TextBrush
        };
        DockPanel.SetDock(nameTb, Dock.Left);
        dock.Children.Add(nameTb);
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(SmallBtn("加为桌宠好友", () => _plugin.Lobby?.AddBuddy(c.Id, c.Name)));
        dock.Children.Add(panel);
        item.Content = dock;
        item.Tag = c;
        return item;
    }

    private void RefreshBuddyList()
    {
        if (_buddyList == null) return;
        _buddyList.Items.Clear();
        var buddies = _plugin.Lobby?.GetBuddies() ?? new List<FriendEntry>();
        if (buddies.Count == 0) { _buddyList.Items.Add("暂无桌宠好友"); return; }
        foreach (var f in buddies) _buddyList.Items.Add(MakeBuddyRow(f));
    }

    private void RefreshSteamList()
    {
        if (_steamList == null) return;
        _steamList.Items.Clear();
        var friends = _plugin.Lobby?.Friends ?? new List<FriendEntry>();
        var steam = friends.Where(f => f.IsSteamFriend).ToList();
        if (steam.Count == 0) { _steamList.Items.Add("暂无 Steam 好友 / 未刷新"); return; }
        foreach (var f in steam) _steamList.Items.Add(MakeSteamRow(f));
    }

    private void RefreshHistoryList()
    {
        if (_historyList == null) return;
        _historyList.Items.Clear();
        var hist = _plugin.Lobby?.GetHistory() ?? new List<ContactEntry>();
        if (hist.Count == 0) { _historyList.Items.Add("暂无联机历史"); return; }
        foreach (var c in hist) _historyList.Items.Add(MakeHistoryRow(c));
    }

    private void RefreshAdminList()
    {
        if (_adminList == null) return;
        _adminList.Items.Clear();
        foreach (var id in AdminConfig.AdminList)
        {
            var item = new ListBoxItem();
            var dock = new DockPanel { LastChildFill = true };
            var tb = new TextBlock
            {
                Text = (id == AdminConfig.ROOT_ADMIN_ID ? "君赫 (根管理员)" : id.ToString()),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = TextBrush
            };
            DockPanel.SetDock(tb, Dock.Left);
            dock.Children.Add(tb);
            var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            if (id != AdminConfig.ROOT_ADMIN_ID)
                panel.Children.Add(SmallBtn("取消管理员", () => AdminConfig.RemoveAdmin(id)));
            dock.Children.Add(panel);
            item.Content = dock;
            item.Tag = id;
            _adminList.Items.Add(item);
        }
    }

    private static string FormatTime(long unix)
    {
        if (unix <= 0) return "未知";
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm");
        }
        catch { return "未知"; }
    }
}
