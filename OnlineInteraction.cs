using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Steamworks;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.OnlineInteraction;

public class OnlineInteraction : MainPlugin
{
    private IMPWindows? _mpWin;

    private TabItem? _tabItem;


    private TabItem? _lobbyTabItem;

    private TabItem? _adminTabItem;

    internal InteractionPanel? _panel;


    /// <summary>Steam P2P 大厅服务 (服务器联机, 独立于游戏自带访客表)</summary>
    internal SteamLobbyService Lobby { get; private set; }

    /// <summary>云端桥接: mod 内 127.0.0.1 本地服务, 由网页桥接端经此与 mod 通信</summary>
    internal CloudBridge Cloud { get; private set; }


    public override string PluginName => "VPet联机互动";

    public OnlineInteraction(IMainWindow mainwin)
        : base(mainwin)
    {
    }

    // ───────── 访客表 (winMutiPlayer) 反射桥接 ─────────
    // 桌宠访客表本质是一个 Steam Lobby (winMutiPlayer.lb); 游戏自建的「加入访客表」正是
    // new winMutiPlayer(this, lobbyId).Show()。本 mod 用反射复刻该流程以实现「点击加入访客表」,
    // 从而把 v1 废弃的 SteamMatchmaking 大厅层替换为原生访客表。
    private static Type _wmpTypeCache;
    private static string _wmpTypeFail;

    internal static Type GetWmpType(IMainWindow mw)
    {
        if (_wmpTypeCache != null) return _wmpTypeCache;
        _wmpTypeFail = null;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                var t = types.FirstOrDefault(x => x != null && x.Name == "winMutiPlayer");
                if (t != null) { _wmpTypeCache = t; return t; }
            }
            _wmpTypeFail = "在所有已加载程序集中未找到 winMutiPlayer 类型";
        }
        catch (Exception ex) { _wmpTypeFail = ex.Message; }
        return null;
    }

    internal static object GetLb(object instance, Type type)
    {
        var f = type.GetField("lb", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (f != null) return f.GetValue(instance);
        var p = type.GetProperty("lb", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (p != null) return p.GetValue(instance);
        return null;
    }

    // 从 Steamworks.SteamId 提取 UInt64.Value
    internal static ulong GetSteamIdValue(object steamId)
    {
        if (steamId == null) return 0;
        // SteamId 是结构体 (ref): 直接 cast 或 GetField("Value")
        if (steamId is ulong u) return u;
        var f = steamId.GetType().GetField("Value", BindingFlags.Public | BindingFlags.Instance);
        if (f != null && f.GetValue(steamId) is ulong uv) return uv;
        return 0;
    }

    /// <summary>反射诊断信息 (供 UI 在失败时显示)</summary>
    internal static string ReflectDiagnostics()
    {
        if (_wmpTypeCache != null) return "winMutiPlayer 已定位: " + _wmpTypeCache.FullName;
        return _wmpTypeFail ?? "未知反射错误";
    }

    /// <summary>刷新反射诊断(仅更新内存诊断字符串, 不写磁盘), 排查 winMutiPlayer 定位失败时调用</summary>
    internal static void DumpReflectionDiag(IMainWindow mw)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== VPet mod 反射诊断 @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            var mwType = mw.GetType();
            sb.AppendLine("MW type     : " + mwType.FullName);
            sb.AppendLine("MW assembly : " + mwType.Assembly.GetName().Name + "  (" + mwType.Assembly.Location + ")");
            var f = mwType.GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            sb.AppendLine("MW.winMutiPlayer field : " + (f == null ? "NULL" : f.FieldType.FullName));
            var cur = f?.GetValue(mw);
            sb.AppendLine("MW.winMutiPlayer value (null?=未开表) : " + (cur == null ? "NULL" : "non-null: " + cur.GetType().Name + " instance"));
            if (cur != null)
            {
                var lb = GetLb(cur, cur.GetType());
                sb.AppendLine("lb value (null?=还没建Lobby) : " + (lb == null ? "NULL" : "non-null: " + lb.GetType().Name));
                if (lb != null)
                {
                    var idObj = lb.GetType().GetProperty("Id")?.GetValue(lb);
                    sb.AppendLine("Lobby.Id raw : " + (idObj == null ? "NULL" : idObj.GetType().Name + " instance"));
                    if (idObj != null) sb.AppendLine("SteamId.Value (UInt64) : " + GetSteamIdValue(idObj));
                    var mc = lb.GetType().GetProperty("MemberCount")?.GetValue(lb);
                    sb.AppendLine("MemberCount : " + (mc ?? "N/A"));
                }
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = FilterTypes(ex.Types); }
                var hit = System.Array.Find(types, t => t != null && t.Name == "winMutiPlayer");
                if (hit != null) sb.AppendLine("FOUND winMutiPlayer in [" + asm.GetName().Name + "] -> " + hit.FullName);
            }
            var wmp = GetWmpType(mw);
            sb.AppendLine("GetWmpType  : " + (wmp == null ? "NULL (" + (_wmpTypeFail ?? "") + ")" : wmp.FullName));
            if (wmp != null)
            {
                var lbF = wmp.GetField("lb", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var lbP = wmp.GetProperty("lb", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                sb.AppendLine("  lb field : " + (lbF == null ? "NULL" : lbF.FieldType.FullName) + " | lb prop : " + (lbP == null ? "NULL" : lbP.PropertyType.FullName));
                foreach (var c in wmp.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
                    sb.AppendLine("  ctor(" + string.Join(",", System.Array.ConvertAll(c.GetParameters(), p => p.ParameterType.Name)) + ")");
            }
            // 诊断信息不再写磁盘; 失败时 UI 通过 ReflectDiagnostics() 显示原因
        }
        catch (Exception ex)
        {
            try { _wmpTypeFail = ex.Message; } catch { }
        }
    }

    private static Type[] FilterTypes(Type[] types) {
        var list = new System.Collections.Generic.List<Type>();
        foreach (var t in types) if (t != null) list.Add(t);
        return list.ToArray();
    }

    /// <summary>获取当前打开的访客表的 Steam 大厅ID 与人数 (未开房返回 null)</summary>
    internal static (ulong lobbyId, int members)? GetVisitorTableInfo(IMainWindow mw)
    {
        _visitorError = null;
        try
        {
            var wmpType = GetWmpType(mw);
            if (wmpType == null) return null;
            var field = mw.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var cur = field?.GetValue(mw);
            if (cur == null) { _visitorError = "winMutiPlayer 字段为 null (游戏未开访客表, 或 MW 实例不对)"; return null; }
            var lb = GetLb(cur, wmpType);
            if (lb == null) { _visitorError = "lb 字段/property 为 null (访客表实例未初始化)"; return null; }

            // Lobby.Id 返回 SteamId 结构体 → SteamId.Value (UInt64 field)
            var idObj = lb.GetType().GetProperty("Id")?.GetValue(lb);
            ulong lid = GetSteamIdValue(idObj);
            if (lid == 0) { _visitorError = "lobby Id 为 0 (lb 还没建 Lobby?)"; return null; }

            var mcProp = lb.GetType().GetProperty("MemberCount");
            int members = mcProp == null ? 1 : (int)mcProp.GetValue(lb);
            return (lid, members);
        }
        catch (Exception ex)
        {
            _visitorError = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    private static string _visitorError;
    internal static string GetVisitorError() => _visitorError;

    /// <summary>本账号当前是否打开了访客表</summary>
    internal static bool IsVisitorTableOpen(IMainWindow mw)
    {
        try
        {
            var wmpType = GetWmpType(mw);
            if (wmpType == null) return false;
            var field = mw.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(mw) != null;
        }
        catch { return false; }
    }

    /// <summary>退出当前访客表(如有)并加入指定 lobbyId 的访客表 —— 复刻游戏「加入访客表」</summary>
    internal static void JoinVisitorTable(IMainWindow mw, ulong lobbyId)
    {
        if (lobbyId == 0) throw new ArgumentException("lobbyId 不能为 0");
        var wmpType = GetWmpType(mw);
        if (wmpType == null) throw new InvalidOperationException("找不到 winMutiPlayer 类型: " + ReflectDiagnostics());
        var mainType = mw.GetType();
        var field = mainType.GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        // 已经在同一张访客表里 → 什么都不做。
        // (旧版没这个判断: 列表里两个条目指向同一 lobby 时, 点两次会反复 Close+重建,
        //  用户就会看到自己"同时加入了两个房间")
        var already = GetVisitorTableInfo(mw);
        if (already.HasValue && already.Value.lobbyId == lobbyId) return;
        var cur = field?.GetValue(mw);
        if (cur != null)
        {
            var lb = GetLb(cur, wmpType);
            lb?.GetType().GetMethod("Leave", Type.EmptyTypes)?.Invoke(lb, null);
            try
            {
                var f = wmpType.GetField("lb", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (f != null && !f.IsInitOnly) f.SetValue(cur, Activator.CreateInstance(f.FieldType));
            }
            catch { }
            cur.GetType().GetMethod("Close", Type.EmptyTypes)?.Invoke(cur, null);
            // 彻底断开旧实例引用, 保证同一时刻只存在一个访客表窗口
            try { field?.SetValue(mw, null); } catch { }
        }

        // 找任何 ctor(MainWindow, X) — X 可以是 ulong/SteamId/Nullable<SteamId>/ulong?
        // 从 ctor 参数动态拿 SteamId 类型, 避免 assembly 身份不一致导致匹配失败
        System.Reflection.ConstructorInfo ctor = null;
        foreach (var c in wmpType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            var ps = c.GetParameters();
            if (ps.Length == 2 && ps[0].ParameterType == mainType) { ctor = c; break; }
        }
        if (ctor == null)
        {
            var sb = new System.Text.StringBuilder("找不到 ctor(MainWindow, X)。所有 ctor 签名: ");
            foreach (var c in wmpType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
            {
                sb.Append("\n  ctor(");
                var ps = c.GetParameters();
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ps[i].ParameterType.Name);
                }
                sb.Append(")");
            }
            throw new InvalidOperationException(sb.ToString());
        }

        // 把 ulong lobbyId 转成 ctor 期望的第二参数
        var p1 = ctor.GetParameters()[1].ParameterType;
        Type steamIdType;
        if (p1.IsGenericType && p1.GetGenericTypeDefinition() == typeof(Nullable<>)) steamIdType = p1.GenericTypeArguments[0];
        else if (p1 == typeof(ulong) || p1 == typeof(ulong?)) steamIdType = null;
        else steamIdType = p1;
        object secondArg;
        if (steamIdType == null)
        {
            secondArg = lobbyId;
        }
        else
        {
            try
            {
                var sidCtor = steamIdType.GetConstructor(new[] { typeof(ulong) });
                if (sidCtor != null) secondArg = sidCtor.Invoke(new object[] { lobbyId });
                else secondArg = Convert.ChangeType(lobbyId, steamIdType);
            }
            catch { secondArg = Convert.ChangeType(lobbyId, steamIdType); }
        }

        var win = ctor.Invoke(new object[] { mw, secondArg });
        // 把新窗口写回 MainWindow.winMutiPlayer, 保证字段指向唯一当前实例
        try { field?.SetValue(mw, win); } catch { }
        var w = (System.Windows.Window)win;
        try
        {
            w.Show();
        }
        catch (Exception ex)
        {
            // 加入失败时 ctor 内部会 Close(), 之后再 Show 会抛异常;
            // 必须把字段清空, 否则游戏会一直认为"已经有加入了一个访客表", 再也无法创建
            try { field?.SetValue(mw, null); } catch { }
            throw new InvalidOperationException("访客表窗口无法显示: " + ex.Message, ex);
        }
    }

    /// <summary>取访客表窗口状态: (是否存在实例, 窗口是否可见)。null 表示反射失败</summary>
    internal static (bool exists, bool visible)? GetVisitorTableWindowState(IMainWindow mw)
    {
        try
        {
            var field = mw.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var cur = field?.GetValue(mw);
            if (cur == null) return (false, false);
            bool visible = false;
            if (cur is System.Windows.Window w)
                visible = w.IsVisible;   // 最小化也算可见(WPF 中 IsVisible 为 true), 只有从未 Show 或已关闭才是 false
            return (true, visible);
        }
        catch { return null; }
    }

    /// <summary>
    /// 强制释放访客表: 退出 Steam 大厅 → 关闭窗口 → 清空 MainWindow.winMutiPlayer 字段。
    /// 用于「窗口没真正关掉/被隐藏, 点创建却提示已经有加入了一个访客表」的卡死状态。
    /// </summary>
    internal static bool ForceCloseVisitorTable(IMainWindow mw)
    {
        try
        {
            var wmpType = GetWmpType(mw);
            var field = mw.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var cur = field?.GetValue(mw);
            if (cur == null)
            {
                try { field?.SetValue(mw, null); } catch { }
                return false;
            }

            // 1) 退出 Steam 大厅
            try
            {
                var lb = wmpType != null ? GetLb(cur, wmpType) : null;
                lb?.GetType().GetMethod("Leave", Type.EmptyTypes)?.Invoke(lb, null);
            }
            catch { }

            // 2) 把 lb 重置为 default(Lobby): 游戏 Window_Closing 用 lb.Equals(default) 判断是否弹「确定关闭」框
            try
            {
                var lbField = wmpType?.GetField("lb", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (lbField != null && !lbField.IsInitOnly)
                    lbField.SetValue(cur, Activator.CreateInstance(lbField.FieldType));
            }
            catch { }

            // 3) 关窗 + 清字段(关键, 不依赖 Closed 事件是否触发)
            try { (cur as System.Windows.Window)?.Close(); } catch { }
            try { field?.SetValue(mw, null); } catch { }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 【已停用】访客表窗口拉伸。
    /// 该窗口是 WindowStyle=None + SizeToContent=WidthAndHeight + ResizeMode=NoResize 的无边框窗,
    /// 强行拉伸/接管 WM_NCHITTEST 会和游戏自身的布局逻辑冲突, 导致桌宠卡死。
    /// 因此这里保持为空操作, 联机页改为自适应小尺寸布局。
    /// </summary>
    internal static void MakeWindowResizable(System.Windows.Window win)
    {
    }

    /// <summary>【已停用】见 MakeWindowResizable 说明; 保留空方法以免影响调用方</summary>
    internal static void MakeVisitorTableResizable(IMainWindow mw)
    {
    }

    /// <summary>取访客表原生「消息日志」的 TextBox (winMutiPlayer.tbLog)</summary>
    internal static System.Windows.Controls.TextBox? GetGameLogBox(IMainWindow mw)
    {
        try
        {
            var wmpType = GetWmpType(mw);
            if (wmpType == null) return null;
            var field = mw.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var cur = field?.GetValue(mw);
            if (cur == null) return null;
            var f = cur.GetType().GetField("tbLog", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            return f?.GetValue(cur) as System.Windows.Controls.TextBox;
        }
        catch { return null; }
    }

    /// <summary>写日志到游戏自带的「消息日志」页 (原「互动日志」已并入该页)</summary>
    internal void Log(string msg)
    {
        try
        {
            var box = GetGameLogBox(MW);
            if (box == null) return;
            box.Dispatcher.Invoke(() =>
            {
                box.AppendText($"[{DateTime.Now:HH:mm:ss}]{msg}\n");
                box.ScrollToEnd();
            });
        }
        catch { }
    }

    /// <summary>标记自己的访客表为 mod 房间 (设置 DATA_TAG, 让 Steam 大厅搜索能发现非好友的 mod 用户)</summary>
    internal static void MarkMyVisitorTable(IMainWindow mw)
    {
        try
        {
            var wmpType = GetWmpType(mw);
            if (wmpType == null) return;
            var field = mw.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var cur = field?.GetValue(mw);
            if (cur == null) return;
            var lb = GetLb(cur, wmpType);
            if (lb == null) return;
            var setData = lb.GetType().GetMethod("SetData", new[] { typeof(string), typeof(string) });
            setData?.Invoke(lb, new object[] { "vpoi", "1" });
        }
        catch { }
    }

    public override void LoadPlugin()
    {
        // 游戏自带访客表联机 (原功能保留)
        MW.MutiPlayerHandle += OnMutiPlayerStart;
    }

    // 云存档改为「整份 .lps 存档文件」的上传/还原，逻辑集中在 CloudBridge（BackpackSnapshot 已废弃）

    public override void GameLoaded()
    {
        // 启动 Steam P2P 大厅服务
        AdminConfig.Init(this);
        Lobby = new SteamLobbyService(this);
        Lobby.InteractionReceived += OnLobbyInteraction;
        Lobby.AdminAck += OnAdminAck;
        Lobby.Start();
        // 云端桥接: 本地服务 (网页桥接端通过它读写云端 + 推送房间列表)
        Cloud = new CloudBridge(this);
        Cloud.Start();
    }

    public override void EndGame()
    {
        Cloud?.Stop();
        Lobby?.Dispose();
    }

    /// <summary>MOD设置按钮 → 打开联机大厅</summary>
    public override void Setting()
    {
        MW.Dispatcher.Invoke(() =>
        {
            System.Windows.MessageBox.Show(
                "本 mod 已集成到桌宠访客表。\n\n打开桌宠访客表，切换到「互动」页签即可使用：联机互动 / 桌宠好友 / Steam好友 / 联机历史" + (AdminConfig.CanShowAdminUI(Lobby?.SelfId ?? 0) ? " / 管理" : "") + " 功能。",
                "VPet联机互动");
        });
    }

    // ==================== 大厅(Steam P2P) 消息处理 ====================

    private void OnLobbyInteraction(ulong fromId, string fromName, MPMessage msg)
    {
        switch (msg.Type)
        {
            case SteamLobbyService.MSGTYPE_CHAT:
            {
                var chat = msg.GetContent<MPMessage.Chat>();
                if (string.IsNullOrEmpty(chat.Content)) return;
                string chatText = chat.Content;
                if (chatText.StartsWith("[GREET]"))
                {
                    Log(fromName + " 打招呼: " + chatText.Substring(7).Trim());
                }
                else
                {
                    Log(fromName + ": " + chatText);
                }
                break;
            }
            case SteamLobbyService.MSGTYPE_INTERACT:
            {
                var interact = msg.GetContent<MPMessage.Interact>();
                ApplyInteract(fromName, (int)interact);
                break;
            }
            case SteamLobbyService.MSGTYPE_REDPACKET:
            {
                var rp = msg.GetContent<RedPacketData>();
                if (rp == null) return;
                ApplyRedPacket(fromName ?? rp.SenderName, rp.Amount);
                break;
            }
            case SteamLobbyService.MSGTYPE_BATCHFEED:
            {
                var bf = msg.GetContent<BatchFeedData>();
                if (bf == null) return;
                var safe = new BatchFeedData
                {
                    FoodName = bf.FoodName,
                    Quantity = bf.Quantity,
                    Price = bf.Price,
                    SenderName = bf.SenderName,
                    RawStats = new Dictionary<string, double>(bf.RawStats)
                };
                ApplyBatchFeed(safe, fromName ?? safe.SenderName);
                break;
            }
        }
    }


    private void OnAdminAck(string cmd, string detail)
    {
        Log($"[管理回执] {cmd}: {detail}");
    }

    // ==================== Steam 访客表联机 (原功能保留) ====================

    private void OnMutiPlayerStart(IMPWindows mpWin)
    {
        _mpWin = mpWin;
        _mpWin.ReceivedMessage += OnReceivedMessage;
        _mpWin.ClosingMutiPlayer += OnClosingMutiPlayer;
        // 访客表开启 → 刷新大厅好友列表的「访客」标记
        Lobby?.RefreshFriends();
        MW.Main.Dispatcher.BeginInvoke((Action)delegate
        {
            // 全程 try/catch: 插件 UI 出问题也不能让访客表窗口变残废
            try
            {
                _panel = new InteractionPanel(this, _mpWin);
                _tabItem = new TabItem
                {
                    Header = "互动",
                    Content = _panel
                };
                _mpWin.TabControl.Items.Add(_tabItem);
                // 互动日志已并入游戏自带的「消息日志」页, 不再单独建页签
                // 联机大厅(已合并云端房间/排行榜) / 管理 注入访客表原生左侧 tab 栏
                try
                {
                    _lobbyTabItem = new TabItem { Header = "联机大厅", Content = _panel.BuildLobbyTab() };
                    _mpWin.TabControl.Items.Add(_lobbyTabItem);
                }
                catch (Exception ex)
                {
                    Log("[联机] 联机大厅页构建失败: " + ex.Message);
                }
                if (AdminConfig.CanShowAdminUI(Lobby?.SelfId ?? 0UL))
                {
                    try
                    {
                        _adminTabItem = new TabItem { Header = "管理", Content = _panel.BuildAdminTab() };
                        _mpWin.TabControl.Items.Add(_adminTabItem);
                    }
                    catch (Exception ex)
                    {
                        Log("[联机] 管理页构建失败: " + ex.Message);
                    }
                }
                AdminConfig.AdminsChanged += OnAdminsChangedForVisitorTable;
            }
            catch (Exception ex)
            {
                Log("[联机] 访客表页签注入失败: " + ex.Message);
            }
        }, Array.Empty<object>());
    }

    private void OnAdminsChangedForVisitorTable()
    {
        if (_mpWin == null || _panel == null) return;
        MW.Main.Dispatcher.BeginInvoke((Action)delegate
        {
            bool show = AdminConfig.CanShowAdminUI(Lobby?.SelfId ?? 0UL);
            if (show && _adminTabItem == null)
            {
                _adminTabItem = new TabItem { Header = "管理", Content = _panel.BuildAdminTab() };
                _mpWin.TabControl.Items.Add(_adminTabItem);
            }
            else if (!show && _adminTabItem != null)
            {
                _mpWin.TabControl.Items.Remove(_adminTabItem);
                _adminTabItem = null;
            }
        }, Array.Empty<object>());
    }

    private void OnClosingMutiPlayer()
    {
        if (_mpWin != null)
        {
            _mpWin.ReceivedMessage -= OnReceivedMessage;
        }
        MW.Main.Dispatcher.BeginInvoke((Action)delegate
        {
            TabItem[] array = new TabItem[3] { _tabItem, _lobbyTabItem, _adminTabItem };
            foreach (TabItem tabItem in array)
            {
                if (tabItem?.Parent is System.Windows.Controls.ItemsControl itemsControl)
                {
                    itemsControl.Items.Remove(tabItem);
                }
            }
            AdminConfig.AdminsChanged -= OnAdminsChangedForVisitorTable;
        }, Array.Empty<object>());
        _mpWin = null;
        _tabItem = null;
            _lobbyTabItem = null;
            _adminTabItem = null;
        _panel = null;
        // 访客表关闭 → 清除「访客」标记
        Lobby?.RefreshFriends();
    }

    /// <summary>游戏自带访客表中的好友ID集合 (访客表窗口打开时有效)</summary>
    internal IEnumerable<ulong> GetVisitorFriendIds()
    {
        if (_mpWin?.Friends == null) yield break;
        foreach (var f in _mpWin.Friends)
        {
            if (f.FriendID != 0) yield return f.FriendID;
        }
    }

    private void OnReceivedMessage(ulong friendId, MPMessage msg)
    {
        switch (msg.Type)
        {
            case 1:
            {
                var chat = msg.GetContent<MPMessage.Chat>();
                if (!string.IsNullOrEmpty(chat.Content))
                {
                    MW.Main.Dispatcher.BeginInvoke((Action)delegate
                    {
                        HandleChatMessage(friendId, chat);
                    }, Array.Empty<object>());
                }
                break;
            }
            case 3:
            {
                var interact = msg.GetContent<MPMessage.Interact>();
                MW.Main.Dispatcher.BeginInvoke((Action)delegate
                {
                    string name = ResolveSteamFriendName(friendId);
                    ApplyInteract(name, (int)interact);
                }, Array.Empty<object>());
                break;
            }
            case 100:
            {
                var content2 = msg.GetContent<RedPacketData>();
                if (content2 == null) break;
                var safe2 = new RedPacketData
                {
                    Amount = content2.Amount,
                    SenderName = content2.SenderName,
                    Timestamp = content2.Timestamp
                };
                MW.Main.Dispatcher.BeginInvoke((Action)delegate
                {
                    string senderName = ResolveSteamFriendName(friendId) ?? safe2.SenderName;
                    ApplyRedPacket(senderName, safe2.Amount);
                }, Array.Empty<object>());
                break;
            }
            case 101:
            {
                var content = msg.GetContent<BatchFeedData>();
                if (content == null) break;
                var safe = new BatchFeedData
                {
                    FoodName = content.FoodName,
                    Quantity = content.Quantity,
                    Price = content.Price,
                    SenderName = content.SenderName,
                    RawStats = new Dictionary<string, double>(content.RawStats)
                };
                MW.Main.Dispatcher.BeginInvoke((Action)delegate
                {
                    ApplyBatchFeed(safe, ResolveSteamFriendName(friendId) ?? safe.SenderName);
                }, Array.Empty<object>());
                break;
            }
        }
    }

    private string ResolveSteamFriendName(ulong friendId)
    {
        IMPWindows mpWin = _mpWin;
        IMPFriend friend = mpWin?.Friends?.FirstOrDefault(f => f.FriendID == friendId);
        return friend?.Name ?? friendId.ToString();
    }

    // ==================== 共享: 互动应用 (访客表/大厅两条通道共用) ====================

    internal void ApplyInteract(string senderName, int interactKind)
    {
        switch (interactKind)
        {
            case 0:
                MW.Main.DisplayToTouchHead();
                LogToActive(senderName + " 摸了摸你的头");
                break;
            case 1:
                MW.Main.DisplayToTouchBody();
                LogToActive(senderName + " 摸了摸你的身子");
                break;
            case 2:
                MW.Main.DisplayToTouchHead();
                LogToActive(senderName + " 捏了捏你的脸");
                break;
        }
    }

    internal void ApplyRedPacket(string senderName, double amount)
    {
        if (amount > 0.0)
        {
            MW.Core.Save.Money += amount;
            LogToActive($"收到 {senderName} 的红包: +{amount:F0} 金币");
        }
    }

    internal void HandleChatMessage(ulong friendId, MPMessage.Chat chat)
    {
        string name = ResolveSteamFriendName(friendId);
        if (chat.Content.StartsWith("[GREET]"))
        {
            string greet = chat.Content.Substring(7).Trim();
            Log(name + " 打招呼: " + greet);
            IMPFriend friend = _mpWin?.Friends?.FirstOrDefault(f => f.FriendID == friendId);
            friend?.DisplayMessage(chat);
        }
        else if (chat.Content.StartsWith("[REDPACKET]"))
        {
            string[] parts = chat.Content.Substring(10).Split('|');
            if (parts.Length != 0 && double.TryParse(parts[0], out var amount) && amount > 0.0)
            {
                MW.Core.Save.Money += amount;
                Log($"收到 {name} 的红包: +{amount:F0} 金币");
            }
        }
        else
        {
            Log(name + ": " + chat.Content);
        }
    }

    internal void ApplyBatchFeed(BatchFeedData bf, string senderName)
    {
        for (int i = 0; i < bf.Quantity; i++)
        {
            Food food = MW.Foods?.FirstOrDefault(f => f.Name == bf.FoodName);
            if (food != null)
            {
                MW.Core.Save.EatFood(food);
                if (i == 0)
                {
                    MW.DisplayFoodAnimation(food.GetGraph(), food.ImageSource);
                }
            }
            else
            {
                ApplyRawStats(bf.RawStats, bf.FoodName, i == 0);
            }
        }
        LogToActive($"收到 {senderName} 的投喂: {bf.FoodName} x{bf.Quantity}");
    }

    internal void ApplyRawStats(Dictionary<string, double> stats, string foodName, bool logFirst)
    {
        IGameSave save = MW.Core.Save;
        bool applied = false;
        if (stats.TryGetValue("exp", out var exp) && exp > 0.0) { save.Exp += exp; applied = true; }
        if (stats.TryGetValue("strength_food", out var sf) && sf > 0.0) { save.StrengthFood += sf; applied = true; }
        if (stats.TryGetValue("strength_drink", out var sd) && sd > 0.0) { save.StrengthDrink += sd; applied = true; }
        if (stats.TryGetValue("strength", out var st) && st > 0.0) { save.Strength += st; applied = true; }
        if (stats.TryGetValue("feeling", out var fe) && fe > 0.0) { save.Feeling += fe; applied = true; }
        if (stats.TryGetValue("health", out var he) && he > 0.0) { save.Health += he; applied = true; }
        if (stats.TryGetValue("likability", out var li) && li > 0.0) { save.Likability += li; applied = true; }
        if (logFirst && applied)
        {
            LogToActive("\"" + foodName + "\" 物品未找到，属性已直接加成");
        }
    }

    /// <summary>输出到当前活跃的日志 (大厅日志优先, 其次访客表日志)</summary>
    internal void LogToActive(string msg)
    {
        Log(msg);
        Log(msg);
    }

    // ==================== Steam 访客表: 发送 (原功能保留) ====================

    public void SendGreet(ulong? friendId, string message)
    {
        if (_mpWin == null)
        {
            Log("未连接到服务器");
            return;
        }
        if (!friendId.HasValue)
        {
            var content = new MPMessage.Chat
            {
                Content = "[GREET] " + message,
                ChatType = MPMessage.Chat.Type.Public,
                SendName = MW.Core.Save.Name
            };
            _mpWin.SendMessageALL(MakeMPMsg(1, content));
            Log("向大家打招呼: " + message);
            return;
        }
        IMPFriend friend = _mpWin.Friends?.FirstOrDefault(f => f.FriendID == friendId);
        if (friend == null)
        {
            Log("好友不存在");
            return;
        }
        var content2 = new MPMessage.Chat
        {
            Content = "[GREET] " + message,
            ChatType = MPMessage.Chat.Type.Private,
            SendName = MW.Core.Save.Name,
            ToName = friend.Name
        };
        _mpWin.SendMessage(friendId.Value, MakeMPMsg(1, content2));
        Log("向 " + friend.Name + " 打招呼: " + message);
    }

    public void SendRedPacket(ulong? friendId, double amount)
    {
        if (_mpWin == null)
        {
            Log("未连接到服务器");
            return;
        }
        if (amount <= 0.0)
        {
            Log("红包金额必须大于0");
            return;
        }
        double maxAmount = GetMaxAmount();
        if (amount > maxAmount)
        {
            Log($"单次最多 {maxAmount:F0} (Lv.{MW.Core.Save.Level})");
            return;
        }
        int count = _mpWin.Friends?.Count() ?? 0;
        double total = friendId.HasValue ? amount : amount * count;
        if (MW.Core.Save.Money < total)
        {
            Log($"金币不足! 需要 {total:F0}，当前 {MW.Core.Save.Money:F0}");
            return;
        }
        MW.Core.Save.Money -= total;
        var content = new RedPacketData
        {
            Amount = amount,
            SenderName = MW.Core.Save.Name,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        MPMessage msg = MakeMPMsg(100, content);
        if (!friendId.HasValue)
        {
            foreach (IMPFriend friend in _mpWin.Friends ?? Enumerable.Empty<IMPFriend>())
            {
                _mpWin.SendMessage(friend.FriendID, msg);
            }
            Log($"给大家发红包 (每人 {amount:F0}，共{count}人，共-{total:F0})");
        }
        else
        {
            IMPFriend target = _mpWin.Friends?.FirstOrDefault(f => f.FriendID == friendId);
            _mpWin.SendMessage(friendId.Value, msg);
            Log($"给 {target?.Name ?? friendId.ToString()} 发红包 (-{amount:F0})");
        }
    }

    public void SendBatchFeed(ulong? friendId, Food food, int quantity)
    {
        if (_mpWin == null)
        {
            Log("未连接到服务器");
            return;
        }
        if (quantity <= 0)
        {
            Log("投喂数量必须大于0");
            return;
        }
        quantity = Math.Min(quantity, 99);
        double cost = food.Price * quantity;
        int count = _mpWin.Friends?.Count() ?? 0;
        double total = friendId.HasValue ? cost : cost * count;
        if (MW.Core.Save.Money < total)
        {
            Log($"金币不足! 需要 {total:F0}，当前 {MW.Core.Save.Money:F0}");
            return;
        }
        MW.Core.Save.Money -= total;
        var content = new BatchFeedData
        {
            FoodName = food.Name,
            Quantity = quantity,
            Price = food.Price,
            SenderName = MW.Core.Save.Name,
            RawStats = CollectFoodStats(food)
        };
        MPMessage msg = MakeMPMsg(101, content);
        if (!friendId.HasValue)
        {
            foreach (IMPFriend friend in _mpWin.Friends ?? Enumerable.Empty<IMPFriend>())
            {
                _mpWin.SendMessage(friend.FriendID, msg);
            }
            Log($"向大家投喂: {food.Name} x{quantity} x{count}人 (-{total:F0})");
        }
        else
        {
            IMPFriend target = _mpWin.Friends?.FirstOrDefault(f => f.FriendID == friendId);
            _mpWin.SendMessage(friendId.Value, msg);
            Log($"给 {target?.Name ?? friendId.ToString()} 投喂: {food.Name} x{quantity} (-{cost:F0})");
        }
    }

    public void SendInteract(ulong? friendId, MPMessage.Interact kind)
    {
        if (_mpWin == null)
        {
            Log("未连接到服务器");
            return;
        }
        IMPFriend friend = (!friendId.HasValue) ? null : _mpWin.Friends?.FirstOrDefault(f => f.FriendID == friendId);
        if (friend == null)
        {
            Log("好友不存在");
            return;
        }
        _mpWin.SendMessage(friend.FriendID, MakeMPMsg<MPMessage.Interact>(3, kind));
        Log("对 " + friend.Name + GetInteractName(kind));
    }

    // ==================== 通用工具 ====================

    internal Dictionary<string, double> CollectFoodStats(Food food)
    {
        var dict = new Dictionary<string, double>();
        if (food.Exp > 0) dict["exp"] = food.Exp;
        if (food.StrengthFood > 0.0) dict["strength_food"] = food.StrengthFood;
        if (food.StrengthDrink > 0.0) dict["strength_drink"] = food.StrengthDrink;
        if (food.Strength > 0.0) dict["strength"] = food.Strength;
        if (food.Feeling > 0.0) dict["feeling"] = food.Feeling;
        if (food.Health > 0.0) dict["health"] = food.Health;
        if (food.Likability > 0.0) dict["likability"] = food.Likability;
        return dict;
    }

    internal static MPMessage MakeMPMsg<T>(int type, T content)
    {
        MPMessage result = default;
        result.Type = type;
        result.SetContent(content);
        return result;
    }

    internal static string GetInteractName(MPMessage.Interact k)
    {
        return (int)k switch
        {
            0 => "摸了摸头",
            1 => "摸了摸身子",
            2 => "捏了捏脸",
            _ => "互动了",
        };
    }

    public double GetMaxAmount()
    {
        return Math.Min(double.MaxValue, 5000 + MW.Core.Save.Level * 100);
    }
}


