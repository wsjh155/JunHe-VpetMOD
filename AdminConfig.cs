using System;
using System.Collections.Generic;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.OnlineInteraction;

/// <summary>
/// 管理员(授权)配置
/// 设计:
///   - ROOT_ADMIN_ID 为编译期根管理员(君赫的 SteamID), 固定且不可移除
///   - 运行时管理员名单持久化在游戏设置 vpoi_admins, 由根管理员通过界面增删
///   - 授权开关 vpoi_admin_authorized 默认关; 仅根管理员可开启, 开启后管理UI才显示
/// 注意: 公开仓库中 ROOT_ADMIN_ID 可留 0, 但那样无人能成为管理员
/// </summary>
public static class AdminConfig
{
    /// <summary>编译期根管理员 SteamID64 (君赫)</summary>
    public const ulong ROOT_ADMIN_ID = 76561199551895593UL;

    private const string SET_ADMINS = "vpoi_admins";
    private const string SET_AUTH = "vpoi_admin_authorized";

    private static OnlineInteraction _plugin;
    private static HashSet<ulong> _admins = new() { ROOT_ADMIN_ID };
    private static bool _authorized = false;
    private static bool _loaded = false;

    public static event Action AdminsChanged;

    /// <summary>在 GameLoaded 后调用一次, 绑定插件以访问游戏设置</summary>
    internal static void Init(OnlineInteraction plugin)
    {
        _plugin = plugin;
        if (!_loaded) Load();
    }

    private static void Load()
    {
        _loaded = true;
        try
        {
            var raw = _plugin?.MW?.Set[SET_ADMINS]?.Info ?? "";
            var set = new HashSet<ulong>();
            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (ulong.TryParse(part, out var id) && id != 0) set.Add(id);
            }
            if (!set.Contains(ROOT_ADMIN_ID)) set.Add(ROOT_ADMIN_ID);
            _admins = set;
        }
        catch
        {
            if (!_admins.Contains(ROOT_ADMIN_ID)) _admins.Add(ROOT_ADMIN_ID);
        }
        try { _authorized = (_plugin?.MW?.Set[SET_AUTH]?.Info ?? "0") == "1"; }
        catch { _authorized = false; }
    }

    private static void Save()
    {
        try
        {
            if (_plugin?.MW?.Set == null) return;
            _plugin.MW.Set[SET_ADMINS].Info = string.Join(";", _admins);
            _plugin.MW.Set[SET_AUTH].Info = _authorized ? "1" : "0";
            _plugin.MW.Save();
        }
        catch { }
    }

    /// <summary>某人是否根管理员(君赫)</summary>
    public static bool IsRootAdmin(ulong id) => id == ROOT_ADMIN_ID;

    /// <summary>某人是否在管理员名单中(运行时)</summary>
    public static bool IsAdmin(ulong id) => _admins.Contains(id);

    /// <summary>管理UI是否应显示: 仅根管理员且已授权</summary>
    public static bool CanShowAdminUI(ulong selfId) => selfId == ROOT_ADMIN_ID && _authorized;

    /// <summary>授权开关状态</summary>
    public static bool IsAuthorized => _authorized;

    /// <summary>切换授权(仅根管理员)</summary>
    public static void SetAuthorized(bool value)
    {
        if (!IsRootAdmin(_plugin?.Lobby?.SelfId ?? 0)) return;
        _authorized = value;
        Save();
        AdminsChanged?.Invoke();
    }

    /// <summary>添加管理员(根管理员专属, 界面按钮已做可见性限制)</summary>
    public static void AddAdmin(ulong id)
    {
        if (id == 0 || id == ROOT_ADMIN_ID) return;
        if (_admins.Add(id)) { Save(); AdminsChanged?.Invoke(); }
    }

    /// <summary>移除管理员(根管理员不可移除)</summary>
    public static void RemoveAdmin(ulong id)
    {
        if (id == ROOT_ADMIN_ID) return;
        if (_admins.Remove(id)) { Save(); AdminsChanged?.Invoke(); }
    }

    public static IReadOnlyCollection<ulong> AdminList => _admins;
}
