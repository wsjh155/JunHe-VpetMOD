using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.QQBody;

/// <summary>
/// 桌宠侧桥接插件：把 QQ 机器人(AstrBot) 的对话接到桌宠身上，
/// 让桌宠替机器人"说话"、做动作，并把桌宠自己的碎碎念回传给机器人。
/// 通信走本机 HTTP，Python 端轮询/推送，不需要外网。
/// </summary>
public class QQBodyPlugin : MainPlugin
{
    // ⚠️ 必须与 mod 的 info.lps 里 vupmod 名完全一致，
    // 否则 VPet 的 mod 列表不会显示本插件的「设置」按钮（匹配逻辑见 winGameSetting.cs）。
    public override string PluginName => "QQBody桥接";

    /// <summary>默认监听端口，Python 端要与此一致</summary>
    public const int DefaultPort = 52816;

    /// <summary>聊天接口在 VPet「自定义聊天接口」下拉框里显示的名字</summary>
    public const string TalkAPIName = "瑾觅 QQ 机器人";

    /// <summary>功能开关（可在「MOD 配置 → QQBody桥接 → 设置」里改）</summary>
    public class ModConfig
    {
        /// <summary>机器人生成回复时，桌宠是否播放「直播/工作」动画</summary>
        public bool WorkAnim { get; set; } = true;
        /// <summary>生成过程中是否飘出 token 光团</summary>
        public bool TokenFly { get; set; } = true;
        /// <summary>右上角是否显示累计 token 计数徽标</summary>
        public bool TokenCounter { get; set; } = true;
        /// <summary>是否弹命中/未命中彩色气泡</summary>
        public bool Bubble { get; set; } = true;
        /// <summary>是否把聊天框接口注册进 VPet（关闭后需重启桌宠才从下拉框消失）</summary>
        public bool ChatApi { get; set; } = true;
    }

    public ModConfig Config { get; private set; } = new ModConfig();

    /// <summary>配置文件放在 mod 根目录（plugin 的上一级），卸载插件不会丢</summary>
    public static string ConfigPath
    {
        get
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(QQBodyPlugin).Assembly.Location);
                if (string.IsNullOrEmpty(dir)) return "config.json";
                string modDir = Path.GetFullPath(Path.Combine(dir, ".."));
                return Path.Combine(modDir, "config.json");
            }
            catch
            {
                return "config.json";
            }
        }
    }

    private void LoadConfig()
    {
        try
        {
            string path = ConfigPath;
            if (!File.Exists(path))
            {
                SaveConfig();   // 首次运行写出默认配置，方便用户直接改文件
                return;
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            var cfg = JsonSerializer.Deserialize<ModConfig>(json);
            if (cfg != null) Config = cfg;
            Log($"配置已加载: {json.Replace("\r", " ").Replace("\n", " ")}");
        }
        catch (Exception ex)
        {
            Config = new ModConfig();
            Log("配置加载失败，使用默认值: " + ex.Message);
        }
    }

    public void SaveConfig()
    {
        try
        {
            string json = JsonSerializer.Serialize(Config,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            Log("配置已保存: " + ConfigPath);
        }
        catch (Exception ex)
        {
            Log("配置保存失败: " + ex.Message);
        }
    }

    private HttpBridge _bridge;
    private readonly object _lock = new object();

    /// <summary>桌宠说过的话（供 Python 轮询拉取）</summary>
    private readonly List<SayRecord> _says = new List<SayRecord>();
    private int _saySeq = 0;

    /// <summary>最近一次由 QQ 注入的文本，用于过滤 OnSay 回环</summary>
    private string _lastInjected;
    private DateTime _lastInjectedAt = DateTime.MinValue;

    /// <summary>右上角 token 计数徽标（持久元素，实时刷新数字）</summary>
    private Border _tokenCounter;
    private TextBlock _tokenCounterText;

    /// <summary>
    /// 本次桌宠运行期间累计消耗的真实 token。
    /// 数字由机器人侧从 AstrBot 的 provider_stats 表读出增量后推过来，
    /// 累计/格式化在桌宠这边做：桌宠一重启这个字段就归零，
    /// 设置页里点「重置累计」也会立刻清零。
    /// </summary>
    private long _tokenTotal = 0;

    public QQBodyPlugin(IMainWindow mainwin) : base(mainwin) { }

    #region 日志

    private static string _logPath;

    public static string LogPath
    {
        get
        {
            if (_logPath == null)
            {
                try
                {
                    string dir = Path.GetDirectoryName(typeof(QQBodyPlugin).Assembly.Location);
                    _logPath = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "qqbody.log");
                }
                catch
                {
                    _logPath = "qqbody.log";
                }
            }
            return _logPath;
        }
    }

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { /* 日志失败不能影响游戏 */ }
    }

    #endregion

    #region 生命周期

    public override void LoadPlugin()
    {
        Log($"LoadPlugin 开始，版本 {typeof(QQBodyPlugin).Assembly.GetName().Version}");
        LoadConfig();
        // 聊天框接口必须在这里注册：VPet 的「设置」窗口（winGameSetting）
        // 是在 LoadPlugin 之后、GameLoaded 之前构造的，下拉框只在构造时读一次
        // MW.TalkAPI。放到 GameLoaded 注册会晚一步，下拉框里永远看不到本接口。
        RegisterTalkAPI();
    }

    /// <summary>
    /// 把聊天框接口注册进 VPet 的「自定义聊天接口」下拉框。幂等，可重复调用。
    /// </summary>
    private void RegisterTalkAPI()
    {
        try
        {
            if (MW == null) return;
            if (!Config.ChatApi)
            {
                Log("配置里已关闭聊天框接口，跳过注册");
                return;
            }
            // 已注册过就不重复添加（LoadPlugin / GameLoaded 都会调一次）
            if (MW.TalkAPI.Exists(x => x.APIName == TalkAPIName)) return;
            MW.TalkAPI.Add(new QQBodyTalkAPI(this));
            Log("聊天框接口已注册 -> " + TalkAPIName);
        }
        catch (Exception ex)
        {
            Log("注册聊天框接口失败: " + ex);
        }
    }

    public override void GameLoaded()
    {
        try
        {
            MW.Main.OnSay += OnPetSay;
            _bridge = new HttpBridge(this, DefaultPort);
            _bridge.Start();
            Log($"GameLoaded 完成，HTTP 桥接 {( _bridge.IsRunning ? "已启动 -> " + _bridge.BoundPrefix : "启动失败")}");

            // 兜底：万一 LoadPlugin 时注册失败（比如资源还没就绪），这里再试一次
            RegisterTalkAPI();
        }
        catch (Exception ex)
        {
            Log("GameLoaded 异常: " + ex);
        }
    }

    public override void EndGame()
    {
        try
        {
            if (MW?.Main != null) MW.Main.OnSay -= OnPetSay;
            _bridge?.Stop();
            Log("EndGame，桥接已关闭");
        }
        catch (Exception ex)
        {
            Log("EndGame 异常: " + ex);
        }
    }

    /// <summary>
    /// MOD 配置页入口：VPet 在 mod 列表里点「设置」时调用。
    /// 注意：只有重写了本方法的插件才会显示「设置」按钮（见 winGameSetting.cs）。
    /// </summary>
    public override void Setting()
    {
        try
        {
            MW.Dispatcher.Invoke(() => ShowSettingWindow());
        }
        catch (Exception ex)
        {
            Log("打开设置窗口失败: " + ex);
        }
    }

    private void ShowSettingWindow()
    {
        var gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x66));
        gold.Freeze();
        var fg = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xF2));
        fg.Freeze();
        var dim = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xAA));
        dim.Freeze();
        var panelBg = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x30));
        panelBg.Freeze();

        var win = new Window
        {
            Title = "QQBody 桌宠桥接 · 设置",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x26)),
            Foreground = fg,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
        };

        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = "功能开关",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = gold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        root.Children.Add(new TextBlock
        {
            Text = "改完点「保存」立即生效，不用重启桌宠。",
            FontSize = 11.5,
            Foreground = dim,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        });

        CheckBox AddSwitch(string label, string desc, bool value)
        {
            var cb = new CheckBox
            {
                IsChecked = value,
                Content = label,
                FontSize = 13,
                Foreground = fg,
                Margin = new Thickness(0, 10, 0, 0),
            };
            root.Children.Add(cb);
            root.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = dim,
                Margin = new Thickness(22, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return cb;
        }

        var cbAnim = AddSwitch("生成时播放「直播」动画",
            "机器人开始思考时，桌宠切到工作/直播动作表示在干活。", Config.WorkAnim);
        var cbFly = AddSwitch("飘 token 光团",
            "生成过程中约每 2 秒飞出一小团光球（纯视觉效果，球上不带数字）。", Config.TokenFly);
        var cbCounter = AddSwitch("右上角显示累计 token 计数",
            "数字来自 AstrBot 自带的用量统计（provider_stats，仪表盘同源），是本次桌宠运行期间的累计值；"
            + "重启桌宠或点下方「重置」即清零。≤4 字符自动缩写（如 1.5k / 1.2m）。", Config.TokenCounter);
        var cbBubble = AddSwitch("弹命中 / 未命中气泡",
            "结束时弹缓存命中（蓝）与未命中（红）气泡，以及底部总结气泡。", Config.Bubble);
        var cbChat = AddSwitch("注册聊天框接口",
            "关闭后 VPet「自定义聊天接口」下拉框不再出现本接口（下次启动生效）。", Config.ChatApi);

        // ---- 累计 token：显示当前值 + 一键重置 ----
        var tokenRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var tokenLabel = new TextBlock
        {
            Text = $"本次累计：{TokenTotal} token",
            FontSize = 12,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var btnResetToken = new Button
        {
            Content = "重置为 0",
            Width = 84,
            Height = 26,
            Foreground = fg,
            Background = panelBg,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x56)),
            FontSize = 11.5,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btnResetToken.Click += (s, e) =>
        {
            ResetTokenTotal();
            tokenLabel.Text = "本次累计：0 token";
        };
        tokenRow.Children.Add(tokenLabel);
        tokenRow.Children.Add(btnResetToken);
        root.Children.Add(tokenRow);
        root.Children.Add(new TextBlock
        {
            Text = "重启桌宠也会自动清零；数值不写入配置文件。",
            FontSize = 11,
            Foreground = dim,
            Margin = new Thickness(0, 3, 0, 0),
        });

        root.Children.Add(new Border
        {
            Background = panelBg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 16, 0, 0),
            Child = new TextBlock
            {
                Text = $"HTTP 桥接端口：{DefaultPort}\n配置文件：{ConfigPath}",
                FontSize = 10.5,
                Foreground = dim,
                TextWrapping = TextWrapping.Wrap,
            },
        });

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var btnSave = new Button
        {
            Content = "保存",
            Width = 92,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x26)),
            Background = gold,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btnSave.Click += (s, e) =>
        {
            Config.WorkAnim = cbAnim.IsChecked == true;
            Config.TokenFly = cbFly.IsChecked == true;
            Config.TokenCounter = cbCounter.IsChecked == true;
            Config.Bubble = cbBubble.IsChecked == true;
            Config.ChatApi = cbChat.IsChecked == true;
            SaveConfig();
            if (!Config.TokenCounter) SetTokenCounter("");   // 关掉计数立刻清理残留徽标
            else RefreshTokenCounter();                      // 重新打开时按当前累计显示
            win.Close();
        };

        var btnCancel = new Button
        {
            Content = "取消",
            Width = 92,
            Height = 30,
            Foreground = fg,
            Background = panelBg,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btnCancel.Click += (s, e) => win.Close();

        btns.Children.Add(btnSave);
        btns.Children.Add(btnCancel);
        root.Children.Add(btns);

        win.Content = root;
        win.Show();
    }

    #endregion

    #region 桌宠说话 -> 事件队列

    private void OnPetSay(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            // 过滤我们自己注入的话，避免 QQ -> 桌宠 -> QQ 无限回环
            if (_lastInjected != null && _lastInjected == text &&
                (DateTime.UtcNow - _lastInjectedAt).TotalSeconds < 3.0)
            {
                return;
            }

            _saySeq++;
            _says.Add(new SayRecord { Id = _saySeq, Text = text, Time = DateTime.Now.ToString("HH:mm:ss") });
            if (_says.Count > 100) _says.RemoveRange(0, _says.Count - 100);
        }
    }

    /// <summary>取 id 大于 sinceId 的桌宠发言</summary>
    public List<SayRecord> GetSaysSince(int sinceId)
    {
        lock (_lock)
        {
            var result = new List<SayRecord>();
            foreach (var s in _says)
            {
                if (s.Id > sinceId) result.Add(s);
            }
            return result;
        }
    }

    public int LastSayId
    {
        get { lock (_lock) { return _saySeq; } }
    }

    #endregion

    #region 供 HTTP 调用

    /// <summary>让桌宠替机器人说话</summary>
    public void InjectSay(string text, bool rnd = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            _lastInjected = text;
            _lastInjectedAt = DateTime.UtcNow;
        }

        if (rnd) MW.Main.SayRnd(text, force: true);
        else MW.Main.Say(text, null, false, null);
    }

    /// <summary>飘一行小字（不打断当前动画）</summary>
    public void InjectLabel(string text, int ms = 2000)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        MW.Main.LabelDisplayShow(text, ms);
    }

    /// <summary>
    /// 弹一个自定义气泡：向上飘 + 淡出，可指定文字颜色。
    /// color: "blue" | "red" | 其它(白)。多个气泡互不覆盖，各飘各的。
    /// </summary>
    public void ShowBubble(string text, string color = "white", int ms = 2000)
    {
        if (!Config.Bubble) return;      // 设置页可关闭气泡
        if (string.IsNullOrWhiteSpace(text)) return;

        Brush fg = color switch
        {
            "blue" => new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xFF)),
            "red" => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
            _ => new SolidColorBrush(Colors.White),
        };
        // 冻结画刷，避免跨线程问题
        fg.Freeze();

        try
        {
            MW.Dispatcher.Invoke(() =>
            {
                var host = MW.MGHost;
                if (host == null) return;

                // 随机水平偏移，避免连续气泡完全重叠
                double dx = (new Random()).Next(-70, 71);

                var bubble = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1C, 0x1C, 0x26)),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(11, 5, 11, 5),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(dx, 46, 0, 0),
                    RenderTransform = new TranslateTransform(0, 0),
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = fg,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                    },
                };

                host.Children.Add(bubble);

                int dur = Math.Max(600, ms);
                var translate = (TranslateTransform)bubble.RenderTransform;

                var animY = new DoubleAnimation(0, -90, TimeSpan.FromMilliseconds(dur));
                var animO = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(dur))
                {
                    BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, dur - 700)),
                };

                bubble.BeginAnimation(UIElement.OpacityProperty, animO);
                translate.BeginAnimation(TranslateTransform.YProperty, animY);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(dur + 150) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    try { host.Children.Remove(bubble); } catch { }
                };
                timer.Start();
            });
        }
        catch (Exception ex)
        {
            Log("ShowBubble 异常: " + ex);
        }
    }

    /// <summary>
    /// token 飞走动画：一小团金色光球（不带数字）从桌宠身上向上飞走并淡出，
    /// 体现「实时消耗」的纯视觉特效；累计数字由左下角计数徽标承担。
    /// </summary>
    public void ShowTokenFly(int total, string color = "gold")
    {
        if (!Config.TokenFly) return;    // 设置页可关闭光团
        try
        {
            MW.Dispatcher.Invoke(() =>
            {
                var host = MW.MGHost;
                if (host == null) return;

                var rnd = new Random();
                double dx = rnd.Next(-55, 56);    // 水平起始偏移，避免完全重叠
                double drift = rnd.Next(-45, 46); // 飞行途中的横向漂移

                var fly = new System.Windows.Shapes.Ellipse
                {
                    Width = 22,
                    Height = 22,
                    Fill = new RadialGradientBrush(
                        Color.FromRgb(0xFF, 0xF5, 0xD0),
                        Color.FromRgb(0xFF, 0xC0, 0x3C)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xD9, 0x66)),
                    StrokeThickness = 1,
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0xFF, 0xD9, 0x66),
                        BlurRadius = 14,
                        ShadowDepth = 0,
                        Opacity = 0.9,
                    },
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(dx, 56, 0, 0),
                    RenderTransform = new TranslateTransform(0, 0),
                };

                host.Children.Add(fly);

                // 飞行更舒缓：全程约 2 秒，最后 0.8 秒才淡出，前段清晰可见
                int dur = 2000;
                var translate = (TranslateTransform)fly.RenderTransform;

                // 向上飞 + 横向漂移 + 淡出
                var animY = new DoubleAnimation(0, -150, TimeSpan.FromMilliseconds(dur));
                var animX = new DoubleAnimation(0, drift, TimeSpan.FromMilliseconds(dur));
                var animO = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(dur))
                {
                    BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, dur - 800)),
                };

                fly.BeginAnimation(UIElement.OpacityProperty, animO);
                translate.BeginAnimation(TranslateTransform.XProperty, animX);
                translate.BeginAnimation(TranslateTransform.YProperty, animY);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(dur + 150) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    try { host.Children.Remove(fly); } catch { }
                };
                timer.Start();
            });
        }
        catch (Exception ex)
        {
            Log("ShowTokenFly 异常: " + ex);
        }
    }

    /// <summary>
    /// 左下角实时 token 计数：一个持久停留的小徽标，实时刷新累计花费数字。
    /// text 为空则移除徽标。数字由 Python 端格式化成 ≤4 字符（如 1.5k / 12m）。
    /// </summary>
    public void SetTokenCounter(string text)
    {
        try
        {
            // 设置页关掉计数时当成「清除」处理，顺手把已有徽标移除，避免残留
            if (!Config.TokenCounter) text = "";
            MW.Dispatcher.Invoke(() =>
            {
                var host = MW.MGHost;
                if (host == null) return;

                // 传空 = 清除徽标
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (_tokenCounter != null)
                    {
                        try { host.Children.Remove(_tokenCounter); } catch { }
                        _tokenCounter = null;
                        _tokenCounterText = null;
                    }
                    return;
                }

                if (_tokenCounter == null)
                {
                    _tokenCounterText = new TextBlock
                    {
                        Text = "",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x66)),
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                    };
                    _tokenCounter = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1C, 0x1C, 0x26)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xD9, 0x66)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(9, 3, 9, 3),
                        // 右上角：桌宠身体居中偏下，右下/左下都是说话文字区，
                        // 放右上角才不会被气泡文字压住
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 52, 6, 0),
                        Child = _tokenCounterText,
                    };
                    host.Children.Add(_tokenCounter);
                }

                _tokenCounterText.Text = "⚡" + text;
            });
        }
        catch (Exception ex)
        {
            Log("SetTokenCounter 异常: " + ex);
        }
    }

    /// <summary>当前累计 token（只读，供 /status 与设置页显示）</summary>
    public long TokenTotal => Interlocked.Read(ref _tokenTotal);

    /// <summary>
    /// 加一笔真实 token（机器人侧从 provider_stats 读到的增量）。
    /// 累计值是「本次桌宠运行」的累计：进程重启自然归零，不落盘。
    /// </summary>
    public void AddTokenDelta(int delta)
    {
        if (delta <= 0) return;
        long now = Interlocked.Add(ref _tokenTotal, delta);
        Log($"[token] +{delta} => 累计 {now}");
        RefreshTokenCounter();
    }

    /// <summary>把累计清零（设置页的「重置」按钮、/桌宠 重置 都走这里）</summary>
    public void ResetTokenTotal()
    {
        Interlocked.Exchange(ref _tokenTotal, 0);
        Log("[token] 累计已清零");
        RefreshTokenCounter();
    }

    /// <summary>按当前累计值刷新右上角徽标（调用方不必自己格式化）</summary>
    public void RefreshTokenCounter()
    {
        if (!Config.TokenCounter)
        {
            SetTokenCounter("");
            return;
        }
        SetTokenCounter(FmtToken(TokenTotal));
    }

    /// <summary>把 token 数压成 ≤4 字符：999 / 1.5k / 12k / 1.2m / 12m</summary>
    public static string FmtToken(long n)
    {
        if (n < 0) n = 0;
        if (n < 1000) return n.ToString(CultureInfo.InvariantCulture);
        if (n < 10000)
        {
            string s = (n / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
            return s.Length <= 4 ? s : (n / 1000).ToString(CultureInfo.InvariantCulture) + "k";
        }
        if (n < 1000000) return (n / 1000).ToString(CultureInfo.InvariantCulture) + "k";
        if (n < 10000000)
        {
            string s = (n / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + "m";
            return s.Length <= 4 ? s : (n / 1000000).ToString(CultureInfo.InvariantCulture) + "m";
        }
        return (n / 1000000).ToString(CultureInfo.InvariantCulture) + "m";
    }

    /// <summary>执行一个动作</summary>
    public bool DoAction(string name)
    {
        if (!Config.WorkAnim) return false;   // 设置页可关闭「直播/工作」动画
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "touch_head":
            case "摸头":
                MW.Main.DisplayToTouchHead();
                return true;

            case "touch_body":
            case "摸身":
                MW.Main.DisplayToTouchBody();
                return true;

            case "sleep":
            case "睡觉":
                MW.Main.DisplaySleep(true);
                return true;

            case "idel":
            case "idle":
            case "待机":
                MW.Main.DisplayToIdel_StateONE();
                return true;

            case "work":
            case "工作":
                // 动画名因角色而异：依次尝试 workone/work，都找不到退到 think，再退到静默
                TryWorkAnim("workone", "work");
                return true;

            case "stream":
            case "live":
            case "直播":
            case "直播工作":
                // 「直播」在核心角色叫 worktwo，其它角色可能没有，做降级链
                TryWorkAnim("worktwo", "workone", "work");
                return true;

            case "raised":
            case "举起":
                MW.Main.DisplayRaised();
                return true;

            case "normal":
            case "恢复":
                MW.Main.DisplayToNomal();
                return true;

            default:
                return false;
        }
    }

    /// <summary>安全播放循环动画：动画不存在时返回 false，不触发桌宠「未找到动画」累积。</summary>
    private bool TryDisplayBLooping(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        try
        {
            var list = MW.Core.Graph.FindGraphs(name, GraphInfo.AnimatType.B_Loop, MW.Core.Save.Mode);
            if (list != null && list.Count > 0)
            {
                MW.Main.DisplayBLoopingForce(name);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"TryDisplayBLooping({name}) 异常: {ex.Message}");
        }
        return false;
    }

    /// <summary>依次尝试一组工作动画，全都没有则退到 think（几乎每个角色都有），再退到静默。</summary>
    private bool TryWorkAnim(params string[] candidates)
    {
        if (candidates != null)
        {
            foreach (var name in candidates)
            {
                if (TryDisplayBLooping(name)) return true;
            }
        }
        return TryDisplayBLooping("think");
    }

    /// <summary>当前状态快照</summary>
    public object BuildStatus()
    {
        var save = MW.Core.Save;
        return new
        {
            ok = true,
            name = save.Name,
            money = Math.Round(save.Money, 2),
            level = save.Level,
            strength = Math.Round(save.Strength, 1),
            strength_max = Math.Round(save.StrengthMax, 1),
            feeling = Math.Round(save.Feeling, 1),
            feeling_max = Math.Round(save.FeelingMax, 1),
            health = Math.Round(save.Health, 1),
            likability = Math.Round(save.Likability, 1),
            mode = save.Mode.ToString(),
            state = MW.Main.State.ToString(),
            working = MW.Main.IsWorking,
            token_total = TokenTotal,
            bridge = "vpet-qqbody",
            version = typeof(QQBodyPlugin).Assembly.GetName().Version?.ToString()
        };
    }

    #endregion
}

public class SayRecord
{
    public int Id { get; set; }
    public string Text { get; set; }
    public string Time { get; set; }
}

/// <summary>
/// VPet 聊天框接口：把桌宠聊天框输入的每一句话转发给 AstrBot 机器人，
/// 机器人回复后由桌宠显示。对应 VPet 聊天设置里的「自定义聊天接口」。
/// </summary>
public class QQBodyTalkAPI : TalkBox
{
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(180),
    };

    private readonly QQBodyPlugin _plugin;

    public QQBodyTalkAPI(QQBodyPlugin plugin) : base(plugin)
    {
        _plugin = plugin;
    }

    public override string APIName => QQBodyPlugin.TalkAPIName;

    public override void Responded(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        DisplayThink();
        _ = HandleChatAsync(content.Trim());
    }

    private async Task HandleChatAsync(string content)
    {
        try
        {
            string reply = await RequestChatAsync(content).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                DisplayThinkToSayRnd(reply);
            }
        }
        catch (Exception ex)
        {
            QQBodyPlugin.Log("聊天请求异常: " + ex.Message);
            DisplayThinkToSayRnd("（呜……连不上机器人，稍后再试试吧）");
        }
    }

    private async Task<string> RequestChatAsync(string content)
    {
        var payload = JsonSerializer.Serialize(new { text = content });
        var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:52817/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(req).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            QQBodyPlugin.Log($"聊天接口返回 {(int)resp.StatusCode}: {body}");
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("reply", out var r))
                return r.GetString();
        }
        catch { }
        return null;
    }

    public override void Setting()
    {
        // 无需设置：机器人人设/记忆都在 AstrBot 侧管理
    }
}
