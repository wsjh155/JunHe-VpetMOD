using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Steamworks;
using Steamworks.Data;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.OnlineInteraction;

// ============ 数据模型 ============

/// <summary>大厅里的一个房间</summary>
public class RoomEntry
{
    public ulong Id;            // 桌号 = 访客表 Steam lobbyId (同一访客表唯一)
    public string Name = "";
    public string HostName = "";
    public ulong HostId;
    public int Players;
    public int Max;
    public bool FriendsOnly;
    public bool DiscoveredByFriend; // 通过好友查询发现(仅限好友房)
    public bool IsCurrent;          // 是否就是我当前所在的访客表
}

/// <summary>好友列表条目 (Steam好友 + 桌宠好友 + 联机历史 融合)</summary>
public class FriendEntry
{
    public ulong Id;
    public string Name = "";
    public bool Online;
    public bool PlayingThisGame;
    public bool InVisitorTable; // 在游戏自带访客表中
    public bool IsSteamFriend;  // 是Steam好友
    public bool IsBuddy;        // 桌宠好友(手动添加)
    public bool FromHistory;    // 仅来自联机历史
    public bool IsModUser;
    public long LastSeen;       // 上次联机时间(unix秒)
    public ulong? RoomId;     // 好友当前所在的mod房间 (Steam大厅Id, 内部使用)
    public string RoomName = "";
}

/// <summary>联机历史/桌宠好友条目 (持久化)</summary>
public class ContactEntry
{
    public ulong Id;
    public string Name = "";
    public long LastSeen;
    public bool Buddy; // 桌宠好友
}

/// <summary>房间成员</summary>
public class MemberEntry
{
    public ulong Id;
    public string Name = "";
    public bool IsHost;
}

// ============ Steam P2P 大厅服务 ============

/// <summary>
/// 基于Steam大厅 + Steam P2P 的联机服务 (无需自建服务器)
/// 公开房间 = Steam Public 大厅; 仅限好友房 = Steam FriendsOnly 大厅
/// </summary>
public class SteamLobbyService : IDisposable
{
    // 消息类型 (沿用MPMessage格式; 1/3/100/101为原有互动类型)
    public const int MSGTYPE_CHAT = 1;
    public const int MSGTYPE_INTERACT = 3;
    public const int MSGTYPE_REDPACKET = 100;
    public const int MSGTYPE_BATCHFEED = 101;
    public const int MSGTYPE_ROOM_QUERY = 110;   // 询问对方在哪个mod房间
    public const int MSGTYPE_ROOM_REPLY = 111;   // 回复: 房间信息
    public const int MSGTYPE_ADMIN = 200;        // 管理员指令
    public const int MSGTYPE_ADMIN_ACK = 201;    // 管理员指令回执
    public const int MSGTYPE_MOD_PRESENT = 112;  // 探测: 对方是否装有本mod
    public const int MSGTYPE_HUB_REGISTER = 113; // 客户端→hub: 报到/心跳 (内容=lobbyId|name)
    public const int MSGTYPE_HUB_REGISTRY = 114; // hub→客户端: 在线访客表注册表

    private const string DATA_TAG = "vpoi";          // 本mod房间标记
    private const string DATA_NAME = "vpoi_name";
    private const string DATA_HOST = "vpoi_host";
    private const string DATA_HOSTID = "vpoi_hostid";
    private const string DATA_VIS = "vpoi_vis";
    private const string DATA_MAX = "vpoi_max";

    private readonly OnlineInteraction _plugin;
    private readonly Dispatcher _ui;
    private Thread _p2pLoop;
    private volatile bool _running;

    // 联机历史/桌宠好友 (持久化到游戏设置)
    private const string SET_CONTACTS = "vpoi_contacts";
    private const string SET_MOD_FRIENDS = "vpoi_mod_friends";
    private const string SET_BLOCKED = "vpoi_blocked";   // 管理员屏蔽列表
    private readonly object _modFriendsLock = new();
    private readonly HashSet<ulong> _modFriends = new();
    private readonly object _blockedLock = new();
    private readonly HashSet<ulong> _blocked = new();
    private readonly object _contactLock = new();
    private long _lastContactSave;

    private Lobby _lobby;          // 当前所在房间 (Id=0 表示不在房间)
    private ulong _hostId;
    private string _roomName = "";
    private long _hubLastSeen;     // 客户端: 最近收到 hub 注册表的时间(unix秒); 判断是否走hub模式
    private readonly object _hubLock = new();
    private readonly Dictionary<ulong, HubEntry> _hubRegistry = new(); // 仅 hub(根管理员)用: SteamID -> 在线访客表
    private class HubEntry { public string Name = ""; public ulong LobbyId; public long LastSeen; }
    private System.Windows.Threading.DispatcherTimer _heartbeat;


    public bool SteamReady { get; private set; }
    public List<RoomEntry> PublicRooms { get; } = new();
    public List<RoomEntry> FriendRooms { get; } = new(); // 好友的仅限好友房
    public List<RoomEntry> ModRooms { get; } = new();    // 所有已知mod用户(含非好友)的访客表
    public List<RoomEntry> HubRooms { get; } = new();    // 中心服务器(hub)下发的全局访客表列表
    public bool IsHubActive => SelfId == AdminConfig.ROOT_ADMIN_ID || (NowUnix() - _hubLastSeen) < 60; // 根管理员本人就是中心节点; 客户端看最近60秒内是否收到过注册表
    public List<FriendEntry> Friends { get; } = new();
    public List<MemberEntry> Members { get; } = new();
    /// <summary>联机历史/桌宠好友 (跨会话持久化)</summary>
    public List<ContactEntry> Contacts { get; } = new();

    public SteamLobbyService(OnlineInteraction plugin)
    {
        _plugin = plugin;
        _ui = plugin.MW.Dispatcher;
        LoadContacts();
        LoadModFriends();
        LoadBlocked();
    }

    // ============ 联机历史/桌宠好友 (持久化) ============

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private void LoadContacts()
    {
        lock (_contactLock)
        {
            Contacts.Clear();
            string data = "";
            try { data = _plugin.MW.Set[SET_CONTACTS].Info ?? ""; } catch { }
            foreach (var e in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = e.Split('|');
                if (p.Length >= 1 && ulong.TryParse(p[0], out var id) && id != 0)
                {
                    Contacts.Add(new ContactEntry
                    {
                        Id = id,
                        Name = p.Length > 1 && p[1] != "" ? p[1] : id.ToString(),
                        LastSeen = p.Length > 2 && long.TryParse(p[2], out var ts) ? ts : 0,
                        Buddy = p.Length > 3 && p[3] == "1"
                    });
                }
            }
        }
    }

    /// <summary>把联机历史/桌宠好友写入游戏设置并保存</summary>
    public void SaveContacts()
    {
        string data;
        lock (_contactLock)
        {
            data = string.Join(";", Contacts
                .OrderByDescending(c => c.LastSeen)
                .Take(300)
                .Select(c => $"{c.Id}|{c.Name.Replace("|", "/").Replace(";", ",")}|{c.LastSeen}|{(c.Buddy ? "1" : "0")}"));
        }
        Ui(() =>
        {
            try
            {
                _plugin.MW.Set[SET_CONTACTS].Info = data;
                _plugin.MW.Save();
            }
            catch { }
        });
    }

    // ============ 装了本mod的Steam好友 (持久化) ============

    private void LoadModFriends()
    {
        lock (_modFriendsLock)
        {
            _modFriends.Clear();
            string data = "";
            try { data = _plugin.MW.Set[SET_MOD_FRIENDS].Info ?? ""; } catch { }
            foreach (var e in data.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (ulong.TryParse(e, out var id) && id != 0) _modFriends.Add(id);
            }
        }
    }

    private void SaveModFriends()
    {
        string data;
        lock (_modFriendsLock) { data = string.Join(";", _modFriends); }
        Ui(() =>
        {
            try { _plugin.MW.Set[SET_MOD_FRIENDS].Info = data; _plugin.MW.Save(); } catch { }
        });
    }

    // ============ 管理员屏蔽列表 (持久化) ============
    private void LoadBlocked()
    {
        lock (_blockedLock)
        {
            _blocked.Clear();
            try
            {
                var data = _plugin.MW?.Set[SET_BLOCKED]?.Info ?? "";
                if (!string.IsNullOrEmpty(data))
                    foreach (var s in data.Split(';'))
                        if (ulong.TryParse(s.Trim(), out var id)) _blocked.Add(id);
            }
            catch { }
        }
    }

    public void SaveBlocked()
    {
        string data;
        lock (_blockedLock) { data = string.Join(";", _blocked); }
        Ui(() =>
        {
            try { _plugin.MW.Set[SET_BLOCKED].Info = data; _plugin.MW.Save(); } catch { }
        });
    }

    /// <summary>该玩家是否被管理员屏蔽 (不会出现在大厅)</summary>
    public bool IsBlocked(ulong id)
    {
        lock (_blockedLock) return _blocked.Contains(id);
    }

    /// <summary>管理员: 屏蔽某玩家 (按SteamID), 使其不显示在大厅</summary>
    public void BlockPlayer(ulong id)
    {
        if (id == 0) return;
        bool changed;
        lock (_blockedLock) { changed = _blocked.Add(id); }
        if (changed) { SaveBlocked(); Ui(() => RoomsChanged?.Invoke()); }
    }

    /// <summary>管理员: 解除屏蔽某玩家</summary>
    public void UnblockPlayer(ulong id)
    {
        bool changed;
        lock (_blockedLock) { changed = _blocked.Remove(id); }
        if (changed) { SaveBlocked(); Ui(() => RoomsChanged?.Invoke()); }
    }

    /// <summary>当前屏蔽列表 (SteamID集合)</summary>
    public List<ulong> BlockedList
    {
        get { lock (_blockedLock) return _blocked.ToList(); }
    }

    /// <summary>我当前所在的访客表 lobbyId (0=没开访客表)。房间去重/加入保护都依赖它</summary>
    public ulong MyLobbyId { get; private set; }

    /// <summary>重新读取我当前所在的访客表 (UI 刷新与加入前都会调用)</summary>
    public ulong RefreshMyLobbyId()
    {
        try
        {
            var vt = OnlineInteraction.GetVisitorTableInfo(_plugin.MW);
            MyLobbyId = vt.HasValue ? vt.Value.lobbyId : 0;
        }
        catch { MyLobbyId = 0; }
        return MyLobbyId;
    }

    /// <summary>过滤掉被屏蔽玩家、自己、以及重复访客表后的可见房间列表</summary>
    public List<RoomEntry> GetVisibleRooms()
    {
        var list = new List<RoomEntry>();
        var seen = new HashSet<ulong>();
        ulong myLobby = RefreshMyLobbyId();
        // hub 模式优先: 最近60秒内收到过 hub 注册表 → 用全局列表
        bool hubActive = (NowUnix() - _hubLastSeen) < 60;
        IEnumerable<RoomEntry> source;
        if (hubActive)
        {
            source = HubRooms;
        }
        else // 兜底: P2P 发现 + Steam 大厅搜索
        {
            source = ModRooms.Concat(PublicRooms);
        }
        foreach (var r in source)
        {
            if (r == null || r.Id == 0) continue;      // 没开访客表
            if (IsBlocked(r.HostId)) continue;         // 已屏蔽
            // 同一访客表(lobbyId)只出现一次 —— 多人在同一张访客表时不再显示成多个房间
            if (!seen.Add(r.Id)) continue;
            r.IsCurrent = r.Id == myLobby;
            list.Add(r);
        }
        return list;
    }

    // ============ 联机网格 (每个人都是微型中心, 互相转发房间信息) ============
    /// <summary>向所有已知 peer (hub + Steam好友 + mod好友) 报到/心跳 (内容=本机访客表lobbyId|名字)</summary>
    public void RegisterWithHub()
    {
        if (!SteamReady) return;
        ulong myLobby = 0;
        try { var vt = OnlineInteraction.GetVisitorTableInfo(_plugin.MW); if (vt.HasValue) myLobby = vt.Value.lobbyId; } catch { }
        var msg = new MPMessage { Type = MSGTYPE_HUB_REGISTER };
        msg.SetContent(myLobby + "|" + SelfName);
        foreach (var pid in GetKnownPeers())
        {
            if (pid == SelfId) continue;
            try { SendP2P(pid, msg); } catch { }
        }
    }

    /// <summary>本机已知的对端集合: 中心服务器(根管理员) + 在线Steam好友 + 已知mod用户</summary>
    private IEnumerable<ulong> GetKnownPeers()
    {
        var set = new HashSet<ulong>();
        if (AdminConfig.ROOT_ADMIN_ID != 0) set.Add(AdminConfig.ROOT_ADMIN_ID);
        try { foreach (var f in SteamFriends.GetFriends()) if (f.IsOnline && !f.IsMe) set.Add(f.Id.Value); } catch { }
        lock (_modFriendsLock) { foreach (var id in _modFriends) set.Add(id); }
        return set;
    }

    /// <summary>hub: 构建全局注册表字符串 (仅含已开访客表且未被全局屏蔽者)</summary>
    private void BuildRegistry(out string content)
    {
        var sb = new System.Text.StringBuilder();
        lock (_hubLock)
        {
            foreach (var kv in _hubRegistry)
            {
                if (kv.Value.LobbyId == 0) continue;       // 没开访客表
                if (IsBlocked(kv.Key)) continue;           // 全局屏蔽
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('|').Append(kv.Value.LobbyId).Append('|').Append(kv.Value.Name ?? "");
            }
        }
        content = sb.ToString();
    }

    private void SendRegistryTo(ulong to)
    {
        BuildRegistry(out var content);
        var msg = new MPMessage { Type = MSGTYPE_HUB_REGISTRY };
        msg.SetContent(content);
        try { SendP2P(to, msg); } catch { }
    }

    /// <summary>向所有已知 peer 转发注册表 (exclude 为不转发给的对端, 避免回声)</summary>
    private void RelayRegistry(ulong exclude)
    {
        BuildRegistry(out var content);
        var msg = new MPMessage { Type = MSGTYPE_HUB_REGISTRY };
        msg.SetContent(content);
        foreach (var pid in GetKnownPeers())
        {
            if (pid == SelfId || pid == exclude) continue;
            try { SendP2P(pid, msg); } catch { }
        }
    }

    private void BroadcastRegistry() => RelayRegistry(0);

    /// <summary>把对端发来的注册表内容合并进本机注册表, 返回是否发生变化</summary>
    private bool MergeRegistry(string content)
    {
        bool changed = false;
        long now = NowUnix();
        lock (_hubLock)
        {
            foreach (var entry in content.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = entry.Split('|');
                if (p.Length >= 3 && ulong.TryParse(p[0], out var hid) && ulong.TryParse(p[1], out var lid))
                {
                    if (_hubRegistry.TryGetValue(hid, out var e))
                    {
                        if (e.LobbyId != lid || e.Name != p[2]) { e.LobbyId = lid; e.Name = p[2]; changed = true; }
                        e.LastSeen = now;
                    }
                    else { _hubRegistry[hid] = new HubEntry { Name = p[2], LobbyId = lid, LastSeen = now }; changed = true; }
                }
            }
        }
        return changed;
    }

    /// <summary>从本机注册表重建可见房间列表 (HubRooms)</summary>
    private void RebuildHubRooms()
    {
        var rooms = new List<RoomEntry>();
        lock (_hubLock)
        {
            // 关键: 多人可能同在"一张"访客表里 (同一个 lobbyId)。
            // 早期版本按"玩家SteamID"建条目, 导致两个人联机被显示成两个房间。
            // 这里改为按 lobbyId 分组合并: 同一张访客表 = 一个房间。
            var groups = _hubRegistry
                .Where(kv => kv.Value.LobbyId != 0 && !IsBlocked(kv.Key))
                .GroupBy(kv => kv.Value.LobbyId);
            foreach (var g in groups)
            {
                // 房主取组内最小 SteamID: 结果确定, 所有客户端看到同一房主
                var owner = g.OrderBy(kv => kv.Key).First();
                string ownerName = string.IsNullOrEmpty(owner.Value.Name)
                    ? owner.Key.ToString()
                    : owner.Value.Name;
                int people = g.Count();
                string suffix = people > 1 ? $" (共{people}人)" : "";
                rooms.Add(new RoomEntry
                {
                    Id = g.Key,                              // 桌号 = 访客表 lobbyId
                    Name = ownerName + " 的访客表" + suffix,
                    HostName = ownerName,
                    HostId = owner.Key,
                    Players = people,
                    FriendsOnly = false,
                    DiscoveredByFriend = false
                });
            }
        }
        HubRooms.Clear();
        foreach (var r in rooms) HubRooms.Add(r);
    }

    /// <summary>清理超时的注册表条目 (心跳超时60秒); 每个人都是微型中心, 都执行</summary>
    private void PruneHubRegistry()
    {
        long now = NowUnix();
        bool changed = false;
        lock (_hubLock)
        {
            var stale = _hubRegistry.Where(kv => now - kv.Value.LastSeen > 60).Select(kv => kv.Key).ToList();
            foreach (var k in stale) { _hubRegistry.Remove(k); changed = true; }
        }
        if (changed)
        {
            RelayRegistry(0);
            Ui(() => { RebuildHubRooms(); RoomsChanged?.Invoke(); });
        }
    }

    private void MarkModUser(ulong id, string name)
    {
        if (id == 0) return;
        bool added = false;
        lock (_modFriendsLock)
        {
            if (!_modFriends.Contains(id)) { _modFriends.Add(id); added = true; }
        }
        if (added) SaveModFriends();
        Ui(() =>
        {
            var fe = Friends.FirstOrDefault(x => x.Id == id);
            if (fe != null && !fe.IsModUser)
            {
                fe.IsModUser = true;
                FriendsChanged?.Invoke();
            }
        });
    }

    /// <summary>记录一位联机过的玩家 (自动: 同房成员/消息发送者; buddy=true表示添加为桌宠好友)</summary>
    public void RecordContact(ulong id, string name, bool buddy = false)
    {
        if (id == 0 || id == SelfId) return;
        bool changed = false;
        bool needSave;
        lock (_contactLock)
        {
            var c = Contacts.FirstOrDefault(x => x.Id == id);
            if (c == null)
            {
                if (Contacts.Count < 500)
                {
                    Contacts.Add(new ContactEntry { Id = id, Name = name ?? "", LastSeen = NowUnix(), Buddy = buddy });
                    changed = true;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(name) && name != c.Name && name != id.ToString()) { c.Name = name; changed = true; }
                c.LastSeen = NowUnix();
                if (buddy && !c.Buddy) { c.Buddy = true; changed = true; }
            }
            needSave = changed || NowUnix() - _lastContactSave > 60;
        }
        if (needSave)
        {
            _lastContactSave = NowUnix();
            SaveContacts();
        }
        if (changed) Ui(() => FriendsChanged?.Invoke());
    }

    /// <summary>添加桌宠好友 (mod内好友, 不依赖Steam好友关系)</summary>
    public void AddBuddy(ulong id, string name = "")
    {
        RecordContact(id, string.IsNullOrEmpty(name) ? id.ToString() : name, buddy: true);
        SaveContacts();
        Ui(() =>
        {
            Notify?.Invoke($"已添加桌宠好友: {name} ({id})");
            RefreshFriends();
        });
    }

    /// <summary>移除桌宠好友 (同时从历史中删除)</summary>
    public void RemoveBuddy(ulong id)
    {
        lock (_contactLock)
        {
            Contacts.RemoveAll(c => c.Id == id);
        }
        SaveContacts();
        Ui(() =>
        {
            Notify?.Invoke("已移除: " + id);
            RefreshFriends();
        });
    }
    // ============ 联机历史 / 好友查询 ============

    /// <summary>联机历史 (按最近联机时间倒序)</summary>
    public List<ContactEntry> GetHistory()
    {
        lock (_contactLock)
        {
            return Contacts.OrderByDescending(c => c.LastSeen).ToList();
        }
    }

    /// <summary>当前桌宠好友 (IsBuddy)</summary>
    public List<FriendEntry> GetBuddies()
    {
        return Friends.Where(f => f.IsBuddy).ToList();
    }

    public bool IsInRoom => _lobby.Id.Value != 0;
    public bool IsHost => IsInRoom && SelfId == _hostId;
    public ulong SelfId => SteamClient.IsValid ? SteamClient.SteamId.Value : 0;
    public string SelfName => _plugin.MW.Core.Save.Name;
    public string RoomName => _roomName;
    /// <summary>当前房间的房主SteamID (即房间号/访客表固定ID)</summary>
    public ulong RoomHostId => IsInRoom ? _hostId : 0;

    // ============ 事件 (均在UI线程触发) ============

    /// <summary>房间列表变化 (公开+好友房)</summary>
    public event Action RoomsChanged;
    /// <summary>当前房间状态变化 (加入/退出/成员变化/被踢)</summary>
    public event Action RoomChanged;
    /// <summary>好友列表变化</summary>
    public event Action FriendsChanged;
    /// <summary>收到互动消息 (发送者id, 发送者名字, 消息) → 插件共享处理</summary>
    public event Action<ulong, string, MPMessage> InteractionReceived;
    /// <summary>收到房间聊天 (发送者名字, 内容)</summary>
    public event Action<string, string> ChatReceived;
    /// <summary>通知/日志消息</summary>
    public event Action<string> Notify;
    /// <summary>管理员回执 (仅管理员本地关心): 指令, 详情</summary>
    public event Action<string, string> AdminAck;

    // ============ 启动/停止 ============

    public void Start()
    {
        SteamReady = SteamClient.IsValid;
        if (!SteamReady)
        {
            Ui(() => Notify?.Invoke("Steam未就绪, 联机功能不可用 (需要Steam版游戏)"));
            return;
        }
        if (_running) return;
        _running = true;

        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyMemberDisconnected += OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChanged;

        _p2pLoop = new Thread(P2PReceiveLoop) { IsBackground = true, Name = "VPOI-P2P" };
        _p2pLoop.Start();
        Ui(() => Notify?.Invoke($"Steam联机服务已启动 (ID:{SelfId})"));

        // 心跳: 客户端向hub报到(每15秒); hub清理超时条目(每20秒)
        _heartbeat = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _heartbeat.Tick += (s, e) =>
        {
            try { RegisterWithHub(); } catch { }
            try { PruneHubRegistry(); } catch { }
        };
        try { _heartbeat.Start(); } catch { }
        RegisterWithHub(); // 启动即报到一次
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected -= OnLobbyMemberLeave;
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChanged;
        }
        catch { }
        try { if (IsInRoom) _lobby.Leave(); } catch { }
        try { _heartbeat?.Stop(); } catch { }
        SaveContacts(); // 退出时保存联机历史
    }

    private void Ui(Action a) => _ui.BeginInvoke(a);

    // ============ P2P 收包循环 ============

    private void P2PReceiveLoop()
    {
        while (_running)
        {
            try
            {
                while (SteamNetworking.IsP2PPacketAvailable())
                {
                    var pkt = SteamNetworking.ReadP2PPacket();
                    if (pkt.HasValue)
                    {
                        HandleP2P(pkt.Value.SteamId.Value, pkt.Value.Data);
                    }
                }
            }
            catch { }
            Thread.Sleep(150);
        }
    }

    private void HandleP2P(ulong from, byte[] data)
    {
        // 防御: 拒绝空包或异常大的包, 防止恶意超大包耗尽资源
        if (data == null || data.Length == 0 || data.Length > 65536) return;
        MPMessage msg;
        try { msg = MPMessage.ConverTo(data); }
        catch { return; }
        string fromName = null;
        try { fromName = new Friend(from).Name; if (string.IsNullOrEmpty(fromName)) fromName = from.ToString(); }
        catch { fromName = from.ToString(); }

        switch (msg.Type)
        {
            case MSGTYPE_CHAT:
            {
                var chat = msg.GetContent<MPMessage.Chat>();
                if (string.IsNullOrEmpty(chat.Content)) return;
                RecordContact(from, fromName); // 记录互动过的玩家
                Ui(() => InteractionReceived?.Invoke(from, fromName, msg));
                break;
            }
            case MSGTYPE_MOD_PRESENT: // 探测: 对方装有本mod -> 回显并标记
                {
                    var echo = new MPMessage { Type = MSGTYPE_MOD_PRESENT };
                    SendP2P(from, echo);
                    MarkModUser(from, fromName);
                    break;
                }

            case MSGTYPE_HUB_REGISTER: // 报到/心跳: 每个微型中心都处理并转发
                {
                    var parts = (msg.GetContent() ?? "").Split('|');
                    ulong lobbyId = parts.Length > 0 && ulong.TryParse(parts[0], out var l) ? l : 0;
                    string name = parts.Length > 1 ? parts[1] : from.ToString();
                    bool changed = false;
                    lock (_hubLock)
                    {
                        if (_hubRegistry.TryGetValue(from, out var e))
                        {
                            if (e.LobbyId != lobbyId || e.Name != name) { e.LobbyId = lobbyId; e.Name = name; changed = true; }
                            e.LastSeen = NowUnix();
                        }
                        else { _hubRegistry[from] = new HubEntry { Name = name, LobbyId = lobbyId, LastSeen = NowUnix() }; changed = true; }
                    }
                    SendRegistryTo(from);          // 立即回给报到者本机已知列表
                    if (changed) RelayRegistry(from); // 变化则转发给其他微型中心 (回声排除报到者)
                    break;
                }

            case MSGTYPE_HUB_REGISTRY: // 微型中心发来的注册表 → 合并并继续转发
                {
                    _hubLastSeen = NowUnix();
                    var content = msg.GetContent() ?? "";
                    bool changed = MergeRegistry(content);
                    Ui(() =>
                    {
                        RebuildHubRooms();
                        RoomsChanged?.Invoke();
                    });
                    if (changed) RelayRegistry(from); // 有更新则转发给其他微型中心
                    break;
                }

            case MSGTYPE_INTERACT:
            case MSGTYPE_REDPACKET:
            case MSGTYPE_BATCHFEED:
                RecordContact(from, fromName); // 记录互动过的玩家
                Ui(() => InteractionReceived?.Invoke(from, fromName, msg));
                break;

            case MSGTYPE_ROOM_QUERY: // 询问我在哪个访客表 → 回复 (任何知道我SteamID的mod用户都可问, 不再限好友)
                {
                    var vt = OnlineInteraction.GetVisitorTableInfo(_plugin.MW);
                    if (vt.HasValue)
                    {
                        var reply = new MPMessage { Type = MSGTYPE_ROOM_REPLY };
                        reply.SetContent($"{vt.Value.lobbyId}|{SelfName}的访客表|{vt.Value.members}|member");
                        SendP2P(from, reply);
                    }
                    break;
                }

            case MSGTYPE_ROOM_REPLY: // 对方(任意mod用户)回复了房间信息
            {
                var parts = (msg.GetContent() ?? "").Split('|');
                if (parts.Length >= 2 && ulong.TryParse(parts[0], out var rid))
                {
                    Ui(() =>
                    {
                        var f = Friends.FirstOrDefault(x => x.Id == from);
                        if (f != null)
                        {
                            f.RoomId = rid;
                            f.RoomName = parts[1];
                            FriendsChanged?.Invoke();
                        }
                        // 加入统一mod房间列表(去重; 排除自己与已屏蔽)
                        if (rid != _lobby.Id.Value && !IsBlocked(from))
                        {
                            var existing = ModRooms.FirstOrDefault(r => r.Id == rid);
                            if (existing == null)
                            {
                                ModRooms.Add(new RoomEntry
                                {
                                    Id = rid,
                                    Name = parts[1],
                                    HostName = f?.Name ?? fromName,
                                    HostId = from,
                                    Players = parts.Length > 2 && int.TryParse(parts[2], out var n) ? n : 1,
                                    FriendsOnly = false,
                                    DiscoveredByFriend = f != null
                                });
                            }
                            else
                            {
                                existing.Name = parts[1];
                                existing.HostName = f?.Name ?? fromName;
                                if (parts.Length > 2 && int.TryParse(parts[2], out var n2)) existing.Players = n2;
                            }
                            RoomsChanged?.Invoke();
                        }
                    });
                }
                break;
            }

            case MSGTYPE_ADMIN: // 管理员指令 (所有mod客户端执行)
                HandleAdminCommand(from, fromName, msg.GetContent() ?? "");
                break;

            case MSGTYPE_ADMIN_ACK: // 管理员指令回执 (只有管理员关心)
                if (AdminConfig.IsAdmin(SelfId) && AdminConfig.IsAdmin(from))
                {
                    var parts = (msg.GetContent() ?? "").Split('|', 2);
                    Ui(() =>
                    {
                        AdminAck?.Invoke(parts.Length > 0 ? parts[0] : "", parts.Length > 1 ? parts[1] : "");
                    });
                }
                break;
        }
    }

    // ============ Steam 大厅事件 ============

    private void OnLobbyEntered(Lobby lobby)
    {
        if (lobby.Id.Value != _lobby.Id.Value) return; // 非本mod会话
        Ui(() => { RefreshMembers(); RoomChanged?.Invoke(); });
    }

    private void OnLobbyMemberJoined(Lobby lobby, Friend friend)
    {
        if (lobby.Id.Value != _lobby.Id.Value) return;
        Ui(() =>
        {
            RefreshMembers();
            RoomChanged?.Invoke();
            Notify?.Invoke($"{friend.Name} 加入了房间");
        });
    }

    private void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (lobby.Id.Value != _lobby.Id.Value) return;
        Ui(() =>
        {
            if (friend.Id.Value == _hostId && !IsHost)
            {
                // 房主退出 → 房间解散
                try { _lobby.Leave(); } catch { }
                _lobby = default;
                _hostId = 0;
                Members.Clear();
                Notify?.Invoke("房主已退出, 房间解散");
            }
            else
            {
                RefreshMembers();
                Notify?.Invoke($"{friend.Name} 离开了房间");
            }
            RoomChanged?.Invoke();
        });
    }

    private void OnLobbyDataChanged(Lobby lobby)
    {
        if (lobby.Id.Value != _lobby.Id.Value) return;
        // 房主踢人: kick 数据 = 被踢者ID (与游戏本体约定一致)
        var kick = lobby.GetData("kick");
        if (!string.IsNullOrEmpty(kick) && kick == SelfId.ToString())
        {
            Ui(() =>
            {
                try { _lobby.Leave(); } catch { }
                _lobby = default;
                _hostId = 0;
                Members.Clear();
                Notify?.Invoke("你已被移出房间");
                RoomChanged?.Invoke();
            });
            return;
        }
        Ui(() => { RefreshMembers(); RoomChanged?.Invoke(); });
    }

    // ============ 房间操作 ============

    /// <summary>创建房间 (公开 或 仅限好友)</summary>
    public async Task<bool> CreateRoomAsync(string name, bool friendsOnly, int max)
    {
        if (!SteamReady) return false;
        name = string.IsNullOrWhiteSpace(name) ? $"{SelfName}的房间" : name.Trim();
        if (name.Length > 30) name = name[..30];
        max = Math.Clamp(max, 2, 20);

        Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(max);
        if (lobby == null)
        {
            Ui(() => Notify?.Invoke("创建房间失败, 请检查网络或稍后再试"));
            return false;
        }
        _lobby = lobby.Value;
        _lobby.SetJoinable(true);
        if (friendsOnly) _lobby.SetFriendsOnly();
        else _lobby.SetPublic();
        _lobby.SetData(DATA_TAG, "1");
        _lobby.SetData(DATA_NAME, name);
        _lobby.SetData(DATA_HOST, SelfName);
        _lobby.SetData(DATA_HOSTID, SelfId.ToString());
        _lobby.SetData(DATA_VIS, friendsOnly ? "friends" : "public");
        _lobby.SetData(DATA_MAX, max.ToString());
        _lobby.SetMemberData("name", SelfName);
        _hostId = SelfId;
        _roomName = name;
        RefreshMembers();
        Ui(() =>
        {
            RoomChanged?.Invoke();
            // 房间号 = 房主的SteamID (与访客表固定ID一致)
            Notify?.Invoke($"房间已创建: {name} ({(friendsOnly ? "仅限好友" : "公开")}, 房间号:{SelfId})");
        });
        return true;
    }

    /// <summary>加入房间</summary>
    public async Task<bool> JoinRoomAsync(ulong lobbyId)
    {
        if (!SteamReady) return false;
        Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
        if (lobby == null || lobby.Value.Owner.Id.Value == 0)
        {
            Ui(() => Notify?.Invoke("加入房间失败 (房间可能已关闭, 或仅限好友房但你不是房主好友)"));
            return false;
        }
        if (lobby.Value.GetData(DATA_TAG) != "1")
        {
            try { lobby.Value.Leave(); } catch { }
            Ui(() => Notify?.Invoke("该房间不是VPet联机互动的房间"));
            return false;
        }
        _lobby = lobby.Value;
        _hostId = lobby.Value.Owner.Id.Value;
        _roomName = lobby.Value.GetData(DATA_NAME);
        if (string.IsNullOrEmpty(_roomName)) _roomName = "未命名房间";
        _lobby.SetMemberData("name", SelfName);
        RefreshMembers();
        Ui(() =>
        {
            RoomChanged?.Invoke();
            Notify?.Invoke($"已加入房间: {_roomName} (房主:{lobby.Value.Owner.Name})");
        });
        return true;
    }

    /// <summary>按房主访客表ID(SteamID64)加入房间 — 房间号与访客表固定ID一致</summary>
    public async Task<bool> JoinRoomByHostIdAsync(ulong hostId)
    {
        if (!SteamReady) return false;
        if (hostId == SelfId)
        {
            Ui(() => Notify?.Invoke("这是你自己的ID, 无法加入自己的房间"));
            return false;
        }
        // 1. 先在已缓存的房间列表里找 (公开房 + 好友的好友房)
        var cached = PublicRooms.FirstOrDefault(r => r.HostId == hostId)
                     ?? FriendRooms.FirstOrDefault(r => r.HostId == hostId);
        if (cached != null)
        {
            return await JoinRoomAsync(cached.Id);
        }
        // 2. Steam大厅搜索 (仅限公开房; 仅限好友房需先刷新让好友查询协议发现)
        try
        {
            var rooms = await SteamMatchmaking.LobbyList
                .WithKeyValue(DATA_TAG, "1")
                .WithKeyValue(DATA_HOSTID, hostId.ToString())
                .WithMaxResults(5)
                .RequestAsync();
            var lb = rooms?.FirstOrDefault(x => x.Id.Value != _lobby.Id.Value);
            if (lb.HasValue && lb.Value.Id.Value != 0)
            {
                return await JoinRoomAsync(lb.Value.Id.Value);
            }
        }
        catch { }
        Ui(() => Notify?.Invoke("未找到该玩家的房间 (对方可能未开房/房间为仅限好友且未被发现, 可先点「立即刷新」)"));
        return false;
    }

    /// <summary>退出房间</summary>
    public void LeaveRoom(bool silent = false)
    {
        if (!IsInRoom) return;
        try { _lobby.Leave(); } catch { }
        _lobby = default;
        _hostId = 0;
        Members.Clear();
        if (!silent)
        {
            Ui(() =>
            {
                RoomChanged?.Invoke();
                Notify?.Invoke("已离开房间");
            });
        }
        else Ui(() => RoomChanged?.Invoke());
    }

    /// <summary>刷新当前房间成员</summary>
    public void RefreshMembers()
    {
        Members.Clear();
        if (!IsInRoom) return;
        try
        {
            foreach (var f in _lobby.Members)
            {
                if (f.Id.Value == SelfId) continue;
                var name = MemberName(f);
                Members.Add(new MemberEntry
                {
                    Id = f.Id.Value,
                    Name = name,
                    IsHost = f.Id.Value == _hostId
                });
                RecordContact(f.Id.Value, name); // 自动记录同房玩家到联机历史
            }
        }
        catch { }
    }

    private string MemberName(Friend f)
    {
        var md = _lobby.GetMemberData(f, "name");
        if (!string.IsNullOrEmpty(md)) return md;
        return string.IsNullOrEmpty(f.Name) ? f.Id.Value.ToString() : f.Name;
    }

    /// <summary>刷新公开房间列表 (Steam大厅搜索) + 向所有已知mod用户查询其房间</summary>
    public async Task RefreshRoomsAsync()
    {
        if (!SteamReady) return;
        // 先确认自己当前在哪张访客表 (房间去重/加入保护都依赖它)
        RefreshMyLobbyId();
        // 向 hub 报到并拉取全局注册表 (优先发现机制)
        try { RegisterWithHub(); } catch { }
        // 标记自己的访客表, 让 Steam 大厅搜索能发现 (v2: 桌宠访客表)
        try { OnlineInteraction.MarkMyVisitorTable(_plugin.MW); } catch { }
        // 1. Steam公开大厅搜索
        try
        {
            var rooms = await SteamMatchmaking.LobbyList
                .WithKeyValue(DATA_TAG, "1")
                .WithMaxResults(50)
                .RequestAsync();
            if (rooms == null)
            {
                // RequestAsync 失败返回null (网络波动/Steam忙), 不算异常, 保持现有列表
                Ui(() => Notify?.Invoke("本次刷新无响应, 房间列表保持不变 (可稍后手动刷新)"));
            }
            else
            {
                Ui(() =>
                {
                    try
                    {
                        PublicRooms.Clear();
                        foreach (var lb in rooms)
                        {
                            if (lb.Id.Value == _lobby.Id.Value) continue;
                            PublicRooms.Add(new RoomEntry
                            {
                                Id = lb.Id.Value,
                                Name = lb.GetData(DATA_NAME),
                                HostName = lb.GetData(DATA_HOST),
                                HostId = ulong.TryParse(lb.GetData(DATA_HOSTID), out var hid) ? hid : lb.Owner.Id.Value,
                                Players = lb.MemberCount,
                                Max = lb.MaxMembers,
                                FriendsOnly = lb.GetData(DATA_VIS) == "friends"
                            });
                        }
                        RoomsChanged?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Notify?.Invoke("更新房间列表时出错: " + ex.Message);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Ui(() => Notify?.Invoke("刷新房间列表失败: " + ex.Message));
        }
        // 2. 向好友查询房间 (仅限好友房通过此途径发现)
        QueryFriendsRooms();
    }

    /// <summary>向所有已知mod用户(好友 + 历史/曾探测到的mod用户)发送房间查询, 发现其访客表</summary>
    public void QueryFriendsRooms()
    {
        if (!SteamReady) return;
        Ui(() => { ModRooms.Clear(); });
        var query = new MPMessage { Type = MSGTYPE_ROOM_QUERY };
        query.SetContent("q");
        var data = MPMessage.ConverTo(query);
        // 1. 所有在线 Steam 好友
        var friendIds = new HashSet<ulong>();
        foreach (var f in SteamFriends.GetFriends())
        {
            if (f.IsOnline && !f.IsMe)
            {
                friendIds.Add(f.Id.Value);
                try { SteamNetworking.SendP2PPacket(f.Id, data); } catch { }
            }
        }
        // 2. 已知 mod 用户(历史/曾探测) 中不是 Steam 好友的
        lock (_modFriendsLock)
        {
            foreach (var id in _modFriends)
            {
                if (id == SelfId || friendIds.Contains(id)) continue;
                try { SendP2P(id, query); } catch { }
            }
        }
        // 清空好友的在房间标记, 等待回复
        Ui(() =>
        {
            foreach (var fe in Friends) { fe.RoomId = null; fe.RoomName = ""; }
            FriendsChanged?.Invoke();
        });
    }

    /// <summary>刷新好友列表 (Steam好友 + 桌宠好友 + 联机历史 融合)</summary>
    public void RefreshFriends()
    {
        if (!SteamReady) return;
        var visitorIds = new HashSet<ulong>();
        try
        {
            foreach (var id in _plugin.GetVisitorFriendIds()) visitorIds.Add(id);
        }
        catch { }
        ContactEntry[] contacts;
        lock (_contactLock) { contacts = Contacts.ToArray(); }
        var contactMap = contacts.ToDictionary(c => c.Id, c => c);

        var list = new List<FriendEntry>();
        // 1. Steam好友 (自动导入)
        foreach (var f in SteamFriends.GetFriends())
        {
            var old = Friends.FirstOrDefault(x => x.Id == f.Id.Value);
            contactMap.TryGetValue(f.Id.Value, out var ct);
            list.Add(new FriendEntry
            {
                Id = f.Id.Value,
                Name = string.IsNullOrEmpty(f.Name) ? f.Id.Value.ToString() : f.Name,
                Online = f.IsOnline,
                PlayingThisGame = f.IsPlayingThisGame,
                InVisitorTable = visitorIds.Contains(f.Id.Value),
                IsSteamFriend = true,
                IsBuddy = ct?.Buddy ?? false,
                IsModUser = _modFriends.Contains(f.Id.Value),
                LastSeen = ct?.LastSeen ?? 0,
                RoomId = old?.RoomId,
                RoomName = old?.RoomName ?? ""
            });
        }
        // 2. 联机历史/桌宠好友 中非Steam好友的玩家
        foreach (var ct in contacts)
        {
            if (list.Any(x => x.Id == ct.Id)) continue;
            list.Add(new FriendEntry
            {
                Id = ct.Id,
                Name = string.IsNullOrEmpty(ct.Name) ? ct.Id.ToString() : ct.Name,
                Online = false,
                InVisitorTable = visitorIds.Contains(ct.Id),
                IsSteamFriend = false,
                IsBuddy = ct.Buddy,
                FromHistory = true,
                LastSeen = ct.LastSeen
            });
        }
        Ui(() =>
        {
            Friends.Clear();
            Friends.AddRange(list
                .OrderByDescending(x => x.IsBuddy)
                .ThenByDescending(x => x.InVisitorTable)
                .ThenByDescending(x => x.Online)
                .ThenBy(x => x.Name));
            FriendsChanged?.Invoke();
        });
        // 向 Steam 好友探测谁装了本 mod
        if (SteamReady)
        {
            var ping = MPMessage.ConverTo(new MPMessage { Type = MSGTYPE_MOD_PRESENT });
            var friendIds = new HashSet<ulong>();
            foreach (var f in SteamFriends.GetFriends())
            {
                if (f.IsOnline && !f.IsMe)
                {
                    friendIds.Add(f.Id.Value);
                    try { SteamNetworking.SendP2PPacket(f.Id, ping); } catch { }
                }
            }
            // 也向已知 mod 用户(非好友) 探测, 扩大发现面
            lock (_modFriendsLock)
            {
                foreach (var id in _modFriends)
                {
                    if (id == SelfId || friendIds.Contains(id)) continue;
                    try { SteamNetworking.SendP2PPacket(new SteamId { Value = id }, ping); } catch { }
                }
            }
        }
    }
    /// <summary>待处理的好友申请 (Steam)</summary>
    public List<(ulong Id, string Name)> GetPendingFriendRequests()
    {
        var list = new List<(ulong, string)>();
        if (!SteamReady) return list;
        foreach (var f in SteamFriends.GetFriendsRequestingFriendship())
        {
            list.Add((f.Id.Value, string.IsNullOrEmpty(f.Name) ? f.Id.Value.ToString() : f.Name));
        }
        return list;
    }

    /// <summary>打开Steam添加好友界面</summary>
    public void AddFriendBySteamId(ulong steamId)
    {
        try
        {
            SteamFriends.OpenUserOverlay(steamId, "friendadd");
            Ui(() => Notify?.Invoke("已打开Steam好友添加界面"));
        }
        catch (Exception ex)
        {
            Ui(() => Notify?.Invoke("打开Steam界面失败: " + ex.Message));
        }
    }

    // ============ 发送 ============

    private static void SendP2P(ulong to, MPMessage msg)
    {
        try { SteamNetworking.SendP2PPacket(to, MPMessage.ConverTo(msg)); }
        catch { }
    }

    /// <summary>发送互动消息给房间内某人 (0=所有人)</summary>
    public void SendRoom(ulong to, MPMessage msg)
    {
        if (!IsInRoom) return;
        if (to == 0)
        {
            foreach (var m in Members) SendP2P(m.Id, msg);
        }
        else
        {
            SendP2P(to, msg);
        }
    }

    /// <summary>发送房间聊天</summary>
    public void SendChat(string text)
    {
        if (!IsInRoom || string.IsNullOrWhiteSpace(text)) return;
        var chat = new MPMessage.Chat
        {
            Content = text,
            ChatType = MPMessage.Chat.Type.Public,
            SendName = SelfName
        };
        var msg = new MPMessage { Type = MSGTYPE_CHAT };
        msg.SetContent(chat);
        SendRoom(0, msg);
    }

    // ============ 房主管理 ============

    /// <summary>房主: 踢出房间成员 (写入kick数据, 所有mod客户端和游戏本体都会退出)</summary>
    public bool HostKick(ulong targetId)
    {
        if (!IsHost) return false;
        var ok = _lobby.SetData("kick", targetId.ToString());
        if (ok)
        {
            var name = Members.FirstOrDefault(m => m.Id == targetId)?.Name ?? targetId.ToString();
            Ui(() => Notify?.Invoke($"已将 {name} 移出房间"));
        }
        return ok;
    }

    /// <summary>房主: 解散房间 (退出后成员检测到房主离开自动解散)</summary>
    public void HostDisband()
    {
        if (!IsHost) return;
        var name = _roomName;
        try { _lobby.Leave(); } catch { }
        _lobby = default;
        _hostId = 0;
        Members.Clear();
        Ui(() =>
        {
            RoomChanged?.Invoke();
            Notify?.Invoke($"房间「{name}」已解散");
        });
    }

    // ============ 管理员指令 (仅AdminConfig.ADMIN_STEAMID有效) ============

    private void HandleAdminCommand(ulong from, string fromName, string content)
    {
        if (!AdminConfig.IsAdmin(from)) return; // 非管理员指令直接忽略
        var parts = content.Split('|');
        string cmd = parts.Length > 0 ? parts[0] : "";
        switch (cmd)
        {
            case "kick": // 管理员强制某人退房
            {
                if (parts.Length > 1 && ulong.TryParse(parts[1], out var target))
                {
                    if (target == SelfId && IsInRoom)
                    {
                        LeaveRoom(silent: true);
                        Ui(() => Notify?.Invoke($"管理员已将你移出房间「{_roomName}」"));
                        // 回执给管理员
                        var ack = new MPMessage { Type = MSGTYPE_ADMIN_ACK };
                        ack.SetContent($"kick|已将 {SelfName} ({SelfId}) 移出房间");
                        SendP2P(from, ack);
                    }
                }
                break;
            }
            case "disband": // 管理员解散当前房间 (需要管理员在房间内发送)
            {
                if (IsInRoom)
                {
                    var rn = _roomName;
                    LeaveRoom(silent: true);
                    Ui(() => Notify?.Invoke($"管理员解散了房间「{rn}」"));
                    var ack = new MPMessage { Type = MSGTYPE_ADMIN_ACK };
                    ack.SetContent($"disband|{SelfName}({SelfId}) 已按指令退出房间「{rn}」");
                    SendP2P(from, ack);
                }
                break;
            }
            case "announce": // 管理员公告 (发给房间内所有人)
            {
                var text = content.Length > 8 ? content[8..] : "";
                Ui(() => Notify?.Invoke($"[管理员公告] {text}"));
                var ack = new MPMessage { Type = MSGTYPE_ADMIN_ACK };
                ack.SetContent($"announce|{SelfName}({SelfId}) 已收到公告");
                SendP2P(from, ack);
                break;
            }
        }
    }

    // ============ 管理员本地操作 (管理级mod) ============

    /// <summary>管理员: 强制踢出房间内某玩家 (需与该玩家同房间或互为好友)</summary>
    public void AdminKick(ulong targetId)
    {
        if (!AdminConfig.IsAdmin(SelfId)) return;
        var msg = new MPMessage { Type = MSGTYPE_ADMIN };
        msg.SetContent($"kick|{targetId}");
        SendP2P(targetId, msg);
        Ui(() => Notify?.Invoke($"已发送管理员踢出指令 → {targetId}"));
    }

    /// <summary>管理员: 解散当前所在房间</summary>
    public void AdminDisband()
    {
        if (!AdminConfig.IsAdmin(SelfId) || !IsInRoom) return;
        var msg = new MPMessage { Type = MSGTYPE_ADMIN };
        msg.SetContent("disband");
        foreach (var m in Members) SendP2P(m.Id, msg);
        Ui(() => Notify?.Invoke("已发送管理员解散指令"));
    }

    /// <summary>管理员: 向当前房间发布公告</summary>
    public void AdminAnnounce(string text)
    {
        if (!AdminConfig.IsAdmin(SelfId) || !IsInRoom) return;
        var msg = new MPMessage { Type = MSGTYPE_ADMIN };
        msg.SetContent("announce|" + text);
        foreach (var m in Members) SendP2P(m.Id, msg);
        Ui(() => Notify?.Invoke("已发送管理员公告"));
    }

}
