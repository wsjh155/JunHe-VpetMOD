using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using LinePutScript;
using VPet_Simulator.Windows.Interface;
using Steamworks;

namespace VPet.Plugin.OnlineInteraction
{
    /// <summary>
    /// 云端桥接：mod 内起的 127.0.0.1 本地 HTTP 服务。
    /// 网页桥接端(WorkBuddy 托管)通过 localhost 与 mod 通信，由网页端负责真正的云端读写
    /// (规避云服务 Origin 校验), mod 永不直连云端。
    /// 传输层使用 TcpListener（裸套接字），不依赖 http.sys，普通用户权限即可绑定 127.0.0.1。
    ///
    /// 云存档：以「整个 .lps 存档文件」为单位上传/下载。
    /// 恢复采用内存还原（解析存档 → 回写当前存档对象与背包 → MW.Save()），
    /// 因此无需重启游戏；解析失败时回退为写入 Saves 目录。
    /// </summary>
    public class CloudBridge
    {
        public const int Port = 27551;

        /// <summary>已发布的云桥页面（合法浏览器 Origin，云端 SDK 由它承载）</summary>
        /// <summary>
        /// 云桥页面的分享码。云端对应用启用了访问校验: 不带 sharecode 会返回「链接不完整」，
        /// 因此必须带上该参数，否则后台无头浏览器也加载不出页面，云端功能全部失效。
        /// </summary>
        public const string BridgeShareCode = "PKDQBM3ZNG";

        public const string BridgeUrl = "https://vpet-cloud-bridge.app.workbuddy.host/?sharecode=" + BridgeShareCode;

        private readonly OnlineInteraction _plugin;
        private TcpListener _tcp;
        private Thread _thread;
        private Timer _watchdog;
        /// <summary>防止连点「打开云桥网页」时多个线程互相清理对方刚启动的窗口(0=空闲,1=进行中)</summary>
        private int _openingPage;
        /// <summary>可见窗口的保护期截止时刻(UTC ticks)。启动/验证期间看门狗必须让路,
        /// 否则它会在空档里抢先拉起无头实例把配置目录锁走。</summary>
        private long _uiHoldUntilTicks;

        /// <summary>网页端推送过来的云端房间列表(仅用于 mod 内「云端房间」页展示+加入)</summary>
        public List<CloudRoom> Rooms { get; private set; } = new List<CloudRoom>();
        public event Action RoomsChanged;

        /// <summary>网页端推送过来的排行榜(等级榜)</summary>
        public List<CloudEntry> Board { get; private set; } = new List<CloudEntry>();
        public event Action BoardChanged;

        public bool Running => _tcp != null;

        private static string LocalDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VPet");

        private static string LogPath => Path.Combine(LocalDir, "cloudbridge.log");
        private static string AuthFlag => Path.Combine(LocalDir, "cloudbridge_authed.flag");
        private static string ProfileDir
        {
            get
            {
                // 按「加载了本程序集的 mod 目录名」隔离浏览器配置目录，
                // 避免同时含本 DLL 的多个 mod（如联机互动 / 君赫自用 / 自家银行）抢同一个浏览器实例。
                // 仅 1145141_OnlineInteraction 部署启用此隔离；其余 mod 仍用旧 cloudbridge_profile。
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location ?? "";
                string modName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(loc)));
                if (string.IsNullOrEmpty(modName)) modName = "default";
                return Path.Combine(LocalDir, "cloudbridge_profile_" + modName);
            }
        }
        /// <summary>浏览器 PID 清单仅作历史兼容保留，判活/清理已改为按命令行匹配（见 BrowserProcs）</summary>
        private static string PidFile => Path.Combine(LocalDir, "cloudbridge_browser_pids.txt");

        /// <summary>页面是否已经成功登录过一次（决定后台浏览器用无头还是可见模式）</summary>
        public bool HasSession
        {
            get { try { return File.Exists(AuthFlag); } catch { return false; } }
        }

        public CloudBridge(OnlineInteraction plugin) { _plugin = plugin; }

        public void Start()
        {
            try
            {
                _tcp = new TcpListener(IPAddress.Loopback, Port);
                _tcp.Start();
                _thread = new Thread(Loop) { IsBackground = true, Name = "CloudBridge" };
                _thread.Start();
                Log($"本地服务已启动 http://127.0.0.1:{Port}/ (TcpListener, 无需管理员权限)");
                _plugin.Log($"[云端桥接] 本地服务已启动 http://127.0.0.1:{Port}/");
                StartBrowser();
                StartWatchdog();
            }
            catch (Exception ex)
            {
                Log($"启动失败: {ex}");
                _plugin.Log($"[云端桥接] 启动失败(端口可能被占用或权限不足): {ex.Message}");
            }
        }

        public void Stop()
        {
            try { _tcp?.Stop(); } catch { }
            _tcp = null;
            try { _watchdog?.Dispose(); } catch { }
            _watchdog = null;
            // 整棵进程树结束：只 Kill() 根进程会留下渲染进程继续占着 user-data-dir
            KillBrowsers("退出游戏");
        }

        /// <summary>
        /// 结束本 mod 启动过的所有浏览器并等待 user-data-dir 锁释放。
        /// 背景：Edge/Chrome 同一 user-data-dir 只允许一个实例。若残留一个无头实例，
        /// 再启动「可见」进程时它只会把 URL 转交给那个无头实例然后自己退出 ——
        /// 用户看到的现象就是「点了打开网页没反应」。
        /// 定位方式不能靠 PID/Process.HasExited（拿到的常是转交后即退出的启动器进程），
        /// 改为按命令行里的 --user-data-dir 匹配（BrowserProcs）。
        /// </summary>
        private void KillBrowsers(string reason)
        {
            try
            {
                bool anyMain;
                int killed = BrowserProcs.KillAll(ProfileDir, out anyMain);
                bool freed = BrowserProcs.WaitIdle(ProfileDir, 5000);
                if (killed > 0 || anyMain || !freed)
                    Log($"{reason}: 结束 {killed} 个浏览器进程，配置目录{(freed ? "已释放" : "仍被占用")}");
            }
            catch (Exception ex)
            {
                Log($"{reason}: 清理浏览器失败 {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(PidFile)) File.Delete(PidFile); } catch { }
            }
        }

        /// <summary>看门狗：后台浏览器意外退出时自动重启（仅在已登录后启用）</summary>
        private void StartWatchdog()
        {
            try
            {
                _watchdog?.Dispose();
                _watchdog = new Timer(_ =>
                {
                    try
                    {
                        if (!HasSession) return;
                        // 用户刚点了「打开云桥网页」，可见窗口还在启动/验证中，别去抢配置目录
                        if (Interlocked.Read(ref _uiHoldUntilTicks) > DateTime.UtcNow.Ticks) return;
                        // 用命令行判活，而不是 Process.HasExited（后者会误判为已退出）
                        bool headless;
                        if (BrowserProcs.HasMain(ProfileDir, out headless)) return;
                        Log("云桥页面已不在运行，重新拉起后台浏览器");
                        StartBrowser();
                    }
                    catch { }
                }, null, 30000, 30000);
            }
            catch { }
        }

        /// <summary>在浏览器里打开云桥页面（首次微信登录、或用手机扫码时用）。
        /// 用与后台一致的受管配置目录可见启动，登录态才能被后台无头浏览器复用。</summary>
        public void OpenBridgePage()
        {
            // 连点保护：正在进行中就直接忽略，避免下一次点击把刚启动的窗口杀掉
            if (Interlocked.CompareExchange(ref _openingPage, 1, 0) != 0) return;

            // 先给自己 60 秒保护期，看门狗在这段时间内不许拉起无头实例抢配置目录
            Interlocked.Exchange(ref _uiHoldUntilTicks, DateTime.UtcNow.AddSeconds(60).Ticks);

            // 清理残留实例 + 等锁释放会短暂阻塞，放到后台线程，避免卡住游戏 UI
            var t = new Thread(() =>
            {
                try
                {
                    KillBrowsers("打开云桥网页前清理");

                    string exe = FindBrowser();
                    if (exe == null)
                    {
                        Log("未找到 Edge/Chrome，改用系统默认浏览器打开");
                        Process.Start(new ProcessStartInfo(BridgeUrl) { UseShellExecute = true });
                        return;
                    }
                    var psi = new ProcessStartInfo(exe);
                    psi.ArgumentList.Add("--no-first-run");
                    psi.ArgumentList.Add("--no-default-browser-check");
                    psi.ArgumentList.Add("--disable-extensions");
                    psi.ArgumentList.Add("--user-data-dir=" + ProfileDir);
                    psi.ArgumentList.Add(BridgeUrl);
                    psi.UseShellExecute = false;
                    Process.Start(psi);

                    // 判活必须看命令行：Process.Start 返回的进程往往是把命令转交出去后立刻退出的启动器
                    bool ok = false;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 6000)
                    {
                        bool headless;
                        if (BrowserProcs.HasMain(ProfileDir, out headless) && !headless) { ok = true; break; }
                        Thread.Sleep(250);
                    }

                    if (ok)
                    {
                        // 窗口已出现，延长保护期；用户关掉窗口后看门狗会自然接管
                        Interlocked.Exchange(ref _uiHoldUntilTicks, DateTime.UtcNow.AddSeconds(90).Ticks);
                        Log("已打开云桥页面(可见窗口)");
                    }
                    else
                    {
                        Log("可见窗口未出现（配置目录被占用），改用系统默认浏览器兜底");
                        try { Process.Start(new ProcessStartInfo(BridgeUrl) { UseShellExecute = true }); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log("打开云桥页面失败: " + ex.Message);
                    try { Process.Start(new ProcessStartInfo(BridgeUrl) { UseShellExecute = true }); } catch { }
                }
                finally
                {
                    Interlocked.Exchange(ref _openingPage, 0);
                }
            })
            { IsBackground = true, Name = "CloudBridge-OpenUI" };
            t.Start();
        }

        /// <summary>
        /// 后台启动云桥页面（无头）。
        /// 云端 SDK 只能在真实浏览器上下文里运行（服务端校验 Origin），
        /// 因此由 mod 静默拉起一个 Edge/Chrome 承载该页面。
        /// 只有登录过一次之后才自动启动；未登录时不再弹窗打扰，改为用户点「打开云桥网页」。
        /// </summary>
        public void StartBrowser()
        {
            try
            {
                // 已有实例（无头或用户开着的可见窗口）就不要再起一个：同配置目录会互相转交 URL
                bool headless;
                if (BrowserProcs.HasMain(ProfileDir, out headless))
                {
                    Log($"云桥页面已在运行（{(headless ? "无头" : "可见窗口")}），跳过重复启动");
                    return;
                }
                if (!HasSession)
                {
                    Log("尚未登录云桥，跳过后台自动启动（点「打开云桥网页」扫码登录一次即可）");
                    return;
                }
                string exe = FindBrowser();
                if (exe == null)
                {
                    Log("未找到 Edge/Chrome，无法启动后台云桥页面（云端功能不可用，可用「打开云桥网页」按钮）");
                    return;
                }
                var psi = new ProcessStartInfo(exe);
                psi.ArgumentList.Add("--headless=new");
                psi.ArgumentList.Add("--disable-gpu");
                psi.ArgumentList.Add("--no-first-run");
                psi.ArgumentList.Add("--no-default-browser-check");
                psi.ArgumentList.Add("--disable-extensions");
                psi.ArgumentList.Add("--mute-audio");
                psi.ArgumentList.Add("--user-data-dir=" + ProfileDir);
                psi.ArgumentList.Add(BridgeUrl);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                Log($"已启动云桥页面(无头) {exe}");
            }
            catch (Exception ex)
            {
                Log("启动浏览器失败: " + ex.Message);
            }
        }

        /// <summary>探测本机 Edge / Chrome 可执行文件路径</summary>
        private static string FindBrowser()
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(pf86)) candidates.Add(Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"));
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"));
            if (!string.IsNullOrEmpty(pf86)) candidates.Add(Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"));
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                candidates.Add(Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"));
                candidates.Add(Path.Combine(local, "Microsoft", "Edge", "Application", "msedge.exe"));
            }
            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return null;
        }

        // ───────── 存档目录 ─────────

        /// <summary>VPet 存档目录（优先 exe 同级 Saves，其次 AppData/VPet/Saves）</summary>
        public static string SavesDir()
        {
            var list = new List<string>();
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir)) list.Add(Path.Combine(baseDir, "Saves"));
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) list.Add(Path.Combine(Path.GetDirectoryName(exe) ?? "", "Saves"));
            }
            catch { }
            list.Add(Path.Combine(LocalDir, "Saves"));
            foreach (var d in list)
            {
                try { if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d; } catch { }
            }
            return list.FirstOrDefault() ?? "";
        }

        private static List<FileInfo> ListSaveFiles(int max = 80)
        {
            var result = new List<FileInfo>();
            try
            {
                var dir = SavesDir();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return result;
                result = new DirectoryInfo(dir).GetFiles("*.lps")
                    .Where(f => f.Name.IndexOf(".backup_", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(max).ToList();
            }
            catch { }
            return result;
        }

        private void Loop()
        {
            while (_tcp != null)
            {
                TcpClient client = null;
                try { client = _tcp.AcceptTcpClient(); }
                catch { break; }
                if (client == null) continue;
                var c = client;
                ThreadPool.QueueUserWorkItem(_ => { try { HandleClient(c); } catch { } finally { try { c.Close(); } catch { } } });
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    var ms = new MemoryStream();
                    var tmp = new byte[1024];
                    int headerEnd = -1;
                    while (headerEnd < 0 && ms.Length < 64 * 1024)
                    {
                        int n = ns.Read(tmp, 0, tmp.Length);
                        if (n <= 0) return;
                        ms.Write(tmp, 0, n);
                        headerEnd = Encoding.ASCII.GetString(ms.ToArray()).IndexOf("\r\n\r\n");
                    }
                    if (headerEnd < 0) return;
                    var all = ms.ToArray();
                    string headerText = Encoding.ASCII.GetString(all, 0, headerEnd);

                    var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;
                    var reqLine = lines[0].Split(' ');
                    if (reqLine.Length < 2) return;
                    string method = reqLine[0];
                    string rawUrl = reqLine[1];
                    int qIdx = rawUrl.IndexOf('?');
                    string path = (qIdx >= 0 ? rawUrl.Substring(0, qIdx) : rawUrl).ToLower();
                    string query = qIdx >= 0 ? rawUrl.Substring(qIdx + 1) : "";

                    int contentLength = 0;
                    foreach (var l in lines)
                    {
                        int c = l.IndexOf(':');
                        if (c > 0 && l.Substring(0, c).Trim().Equals("content-length", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(l.Substring(c + 1).Trim(), out contentLength);
                    }

                    string body = "";
                    if (contentLength > 0)
                    {
                        int bodyStart = headerEnd + 4;
                        var bodyBytes = new List<byte>();
                        if (all.Length > bodyStart) bodyBytes.AddRange(Slice(all, bodyStart, all.Length - bodyStart));
                        while (bodyBytes.Count < contentLength)
                        {
                            int n = ns.Read(tmp, 0, tmp.Length);
                            if (n <= 0) break;
                            bodyBytes.AddRange(Slice(tmp, 0, n));
                        }
                        int take = Math.Min(contentLength, bodyBytes.Count);
                        body = Encoding.UTF8.GetString(bodyBytes.ToArray(), 0, take);
                    }

                    if (method == "OPTIONS") { Send(ns, 200, ""); return; }

                    string respBody;
                    int status = 200;
                    switch (path)
                    {
                        case "/ping":
                            respBody = Json(new { ok = true, t = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                            break;
                        case "/state":
                            respBody = Json(GetState());
                            break;
                        case "/rooms":
                            if (method == "POST")
                            {
                                try
                                {
                                    Rooms = JsonSerializer.Deserialize<List<CloudRoom>>(body) ?? new List<CloudRoom>();
                                    RoomsChanged?.Invoke();
                                }
                                catch { Rooms = new List<CloudRoom>(); }
                                respBody = Json(new { ok = true });
                            }
                            else { status = 405; respBody = ""; }
                            break;
                        case "/join":
                            if (method == "POST")
                            {
                                var lid = ParseQuery(query)["lobbyid"] ?? "";
                                if (ulong.TryParse(lid, out var id) && id != 0)
                                {
                                    var mw = _plugin.MW;
                                    if (mw != null)
                                        mw.Main.Dispatcher.Invoke(() => OnlineInteraction.JoinVisitorTable(mw, id));
                                }
                                respBody = Json(new { ok = true });
                            }
                            else { status = 405; respBody = ""; }
                            break;
                        case "/leaderboard":
                            if (method == "POST")
                            {
                                try
                                {
                                    Board = JsonSerializer.Deserialize<List<CloudEntry>>(body) ?? new List<CloudEntry>();
                                    BoardChanged?.Invoke();
                                }
                                catch { Board = new List<CloudEntry>(); }
                                respBody = Json(new { ok = true });
                            }
                            else { status = 405; respBody = ""; }
                            break;
                        case "/authed":
                            if (method == "POST")
                            {
                                try
                                {
                                    Directory.CreateDirectory(LocalDir);
                                    File.WriteAllText(AuthFlag, DateTime.Now.ToString("o"));
                                    Log("页面回报已登录，后续后台浏览将使用无头模式");
                                }
                                catch { }
                                respBody = Json(new { ok = true });
                            }
                            else { status = 405; respBody = ""; }
                            break;
                        case "/saves":
                            {
                                var files = ListSaveFiles();
                                string dir = SavesDir();
                                string latest = files.Count > 0 ? files[0].Name : "";
                                respBody = Json(new
                                {
                                    ok = true,
                                    dir,
                                    latest,
                                    saves = files.Select(f => new
                                    {
                                        name = f.Name,
                                        size = f.Length,
                                        mtime = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                                        isLatest = f.Name == latest
                                    }).ToList()
                                });
                            }
                            break;
                        case "/savefile":
                            if (method == "GET")
                            {
                                var name = ParseQuery(query)["name"] ?? "";
                                var bytes = ReadSaveFile(name);
                                if (bytes == null) { status = 404; respBody = Json(new { ok = false, error = "存档不存在" }); }
                                else
                                {
                                    respBody = Json(new
                                    {
                                        ok = true,
                                        name,
                                        size = bytes.Length,
                                        base64 = Convert.ToBase64String(bytes)
                                    });
                                }
                            }
                            else { status = 405; respBody = ""; }
                            break;
                        case "/restorefile":
                            if (method == "POST")
                            {
                                RestoreRequest req = null;
                                try { req = JsonSerializer.Deserialize<RestoreRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { }
                                if (req == null || string.IsNullOrEmpty(req.base64))
                                {
                                    status = 400;
                                    respBody = Json(new { ok = false, error = "请求体缺少存档内容" });
                                }
                                else
                                {
                                    byte[] data;
                                    try { data = Convert.FromBase64String(req.base64); }
                                    catch { data = null; }
                                    if (data == null || data.Length == 0)
                                    {
                                        status = 400;
                                        respBody = Json(new { ok = false, error = "存档内容解码失败" });
                                    }
                                    else
                                    {
                                        var r = RestoreSave(req.name, data);
                                        respBody = Json(r);
                                    }
                                }
                            }
                            else { status = 405; respBody = ""; }
                            break;
                        default:
                            status = 404; respBody = "";
                            break;
                    }
                    Send(ns, status, respBody);
                }
            }
            catch { try { client.Close(); } catch { } }
        }

        private static byte[] Slice(byte[] src, int start, int len)
        {
            var o = new byte[len];
            Array.Copy(src, start, o, 0, len);
            return o;
        }

        private static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(q)) return d;
            foreach (var pair in q.Split('&'))
            {
                if (string.IsNullOrEmpty(pair)) continue;
                int eq = pair.IndexOf('=');
                if (eq < 0) d[pair] = "";
                else d[pair.Substring(0, eq)] = Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return d;
        }

        private static void Send(NetworkStream ns, int status, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
            var sb = new StringBuilder();
            string reason = status == 200 ? "OK" : status == 404 ? "Not Found"
                : status == 405 ? "Method Not Allowed" : status == 400 ? "Bad Request" : "Error";
            sb.Append($"HTTP/1.1 {status} {reason}\r\n");
            sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type, X-Requested-With\r\n");
            // Chrome/Edge 的 Private Network Access: 公网 https 页面访问 127.0.0.1 前
            // 会先发 OPTIONS 预检并带 Access-Control-Request-Private-Network, 不回这个头
            // 就被拦下来, 前端只看到 "Failed to fetch"
            sb.Append("Access-Control-Allow-Private-Network: true\r\n");
            sb.Append("Vary: Origin, Access-Control-Request-Private-Network\r\n");
            sb.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            sb.Append("Connection: close\r\n\r\n");
            var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(headerBytes, 0, headerBytes.Length);
            ns.Write(bodyBytes, 0, bodyBytes.Length);
            ns.Flush();
        }

        // ───────── 存档读写 ─────────

        private static byte[] ReadSaveFile(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return null;
                name = Path.GetFileName(name); // 防目录穿越
                var dir = SavesDir();
                if (string.IsNullOrEmpty(dir)) return null;
                var full = Path.Combine(dir, name);
                return File.Exists(full) ? File.ReadAllBytes(full) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 恢复存档。优先内存还原（立即生效），失败则回退写入 Saves 目录（需重启）。
        /// </summary>
        private object RestoreSave(string name, byte[] data)
        {
            string text;
            try { text = new UTF8Encoding(false).GetString(data); }
            catch { text = null; }

            // 动手前先备份当前最新存档，出问题可回滚
            string backup = "";
            try
            {
                var files = ListSaveFiles(1);
                if (files.Count > 0)
                {
                    backup = files[0].FullName + ".before_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(files[0].FullName, backup, true);
                }
            }
            catch { }

            string msg;
            string created = "";
            if (!string.IsNullOrEmpty(text) && TryApplyToMemory(text, out msg))
            {
                created = ListSaveFiles(1).FirstOrDefault()?.Name ?? "";
                Log($"存档已内存还原并保存: {created} (备份: {backup})");
                return new
                {
                    ok = true,
                    mode = "memory",
                    needRestart = false,
                    createdSave = created,
                    backup,
                    message = "已还原到当前存档并立即保存" + (string.IsNullOrEmpty(created) ? "" : $"（新存档 {created}）")
                };
            }

            // 回退：写入 Saves 目录，作为新存档文件
            try
            {
                var dir = SavesDir();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return new { ok = false, mode = "none", error = "未找到存档目录" };
                string outName = string.IsNullOrEmpty(name) ? "Save-cloud_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".lps"
                                                            : Path.GetFileName(name);
                var full = Path.Combine(dir, outName);
                if (File.Exists(full)) full = Path.Combine(dir, Path.GetFileNameWithoutExtension(outName) + "_restored_" + DateTime.Now.ToString("HHmmss") + ".lps");
                File.WriteAllBytes(full, data);
                Log($"存档已写入文件(需重启): {full}");
                return new
                {
                    ok = true,
                    mode = "file",
                    needRestart = true,
                    createdSave = Path.GetFileName(full),
                    backup,
                    message = "已写入存档文件，需重启 VPet 后生效"
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, mode = "none", error = "恢复失败: " + ex.Message };
            }
        }

        /// <summary>把存档文本还原到当前游戏内存并保存（等级/经验/金钱/转生/体力 + 背包物品）</summary>
        private bool TryApplyToMemory(string text, out string msg)
        {
            msg = "";
            try
            {
                var mw = _plugin.MW;
                var save = mw?.Core?.Save;
                if (mw == null || save == null) { msg = "游戏尚未加载存档"; return false; }

                var lps = new LPS(text);
                var vpetLine = lps.FindLine("vpet");
                if (vpetLine == null) { msg = "存档缺少 vpet 数据段"; return false; }

                var parsed = GameSave_VPet.Load(vpetLine);
                if (parsed == null) { msg = "vpet 数据段解析失败"; return false; }

                // 1) 核心数值：反射拷贝所有可写属性（Level/LevelMax/Money/Exp/Strength/Feeling/Health...）
                int copied = 0;
                var tgtType = save.GetType();
                foreach (var p in parsed.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        if (!p.CanRead || !p.CanWrite) continue;
                        var tp = tgtType.GetProperty(p.Name);
                        if (tp == null || !tp.CanWrite) continue;
                        object v = p.GetValue(parsed);
                        if (v == null) continue;
                        tp.SetValue(save, v);
                        copied++;
                    }
                    catch { }
                }

                // 2) 背包物品：item0..itemN
                var newItems = new List<Item>();
                int miss = 0;
                for (int i = 0; i < 300; i++)
                {
                    var line = lps.FindLine("item" + i);
                    if (line == null)
                    {
                        if (++miss >= 5) break;
                        continue;
                    }
                    miss = 0;
                    try
                    {
                        var it = Item.CreateItem(mw, line);
                        if (it != null) newItems.Add(it);
                    }
                    catch { }
                }

                // 3) 应用物品与保存（必须回到 UI 线程）
                string saveErr = "";
                mw.Main.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (mw.Items != null)
                        {
                            mw.Items.Clear();
                            foreach (var it in newItems) mw.Items.Add(it);
                        }
                        mw.Save();
                    }
                    catch (Exception ex) { saveErr = "保存失败: " + ex.Message; }
                });
                if (!string.IsNullOrEmpty(saveErr)) { msg = saveErr; return false; }

                // 4) 刷新背包 UI
                try
                {
                    var inv = mw.GetType().GetField("winInventory", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.GetValue(mw);
                    inv?.GetType().GetMethod("UpdateList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(inv, null);
                }
                catch { }

                if (!string.IsNullOrEmpty(msg)) return false;
                msg = $"已还原 {copied} 项数值、{newItems.Count} 件物品";                Log("内存还原成功: " + msg);
                return true;
            }
            catch (Exception ex)
            {
                msg = "内存还原失败: " + ex.Message;
                Log(msg);
                return false;
            }
        }

        private object GetState()
        {
            bool hosting = false;
            string lobbyId = "";
            string fixId = "";
            int members = 0;
            string steamId = _plugin.Lobby?.SelfId.ToString() ?? "";
            string petName = "";
            string hostName = "";
            string hostSteam = "";
            int level = 0, levelMax = 0;
            double money = 0, exp = 0;
            try
            {
                var save = _plugin.MW?.Core?.Save;
                petName = save?.Name ?? "";
                if (save != null)
                {
                    try { level = save.Level; } catch { }
                    try { money = save.Money; } catch { }
                    try { exp = save.Exp; } catch { }
                    try
                    {
                        var lm = save.GetType().GetProperty("LevelMax", BindingFlags.Public | BindingFlags.Instance);
                        if (lm != null) levelMax = Convert.ToInt32(lm.GetValue(save) ?? 0);
                    }
                    catch { }
                }
                try { hostName = SteamClient.Name ?? ""; } catch { hostName = ""; }
                if (string.IsNullOrEmpty(hostName)) hostName = petName;

                var wmpType = OnlineInteraction.GetWmpType(_plugin.MW);
                if (wmpType != null)
                {
                    var field = _plugin.MW.GetType().GetField("winMutiPlayer", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    var cur = field?.GetValue(_plugin.MW);
                    if (cur != null)
                    {
                        var lb = OnlineInteraction.GetLb(cur, wmpType);
                        if (lb != null)
                        {
                            var idObj = lb.GetType().GetProperty("Id")?.GetValue(lb);
                            ulong lid = OnlineInteraction.GetSteamIdValue(idObj);
                            if (lid != 0)
                            {
                                lobbyId = lid.ToString();
                                var ownerObj = lb.GetType().GetProperty("Owner")?.GetValue(lb);
                                var ownerIdObj = ownerObj?.GetType().GetProperty("Id")?.GetValue(ownerObj);
                                ulong owner = OnlineInteraction.GetSteamIdValue(ownerIdObj);
                                hosting = owner != 0 && owner == SteamClient.SteamId.Value;
                                hostSteam = owner != 0 ? owner.ToString() : "";
                                if (owner != 0)
                                {
                                    // 成员的视角要显示「房主是谁」, 自己当房主时才用自己
                                    try { var hn = new Friend(owner).Name; if (!string.IsNullOrEmpty(hn)) hostName = hn; } catch { }
                                }
                                try
                                {
                                    var mcProp = lb.GetType().GetProperty("MemberCount");
                                    if (mcProp != null) members = Convert.ToInt32(mcProp.GetValue(lb) ?? 0);
                                }
                                catch { }
                            }
                        }
                    }
                }
                // 固定 ID: 游戏访客表窗口里显示的短号 (V255082), 由 report.exlb.net 分配
                fixId = OnlineInteraction.GetFixedId(_plugin.MW);
                if (string.IsNullOrEmpty(hostName)) hostName = hosting ? (string.IsNullOrEmpty(SteamClient.Name) ? petName : SteamClient.Name) : petName;
            }
            catch (Exception ex) { Log("GetState 异常: " + ex); }

            // 本机视野内所有可加入的访客表: Steam P2P 发现 + 好友/hub 下发 + 大厅搜索
            // 网页会把这批整体登记到云端, 这样没开网页的房主、以及非房主玩家的视角
            // 也能把房间目录补全(不用每个人自己登录才登记)。
            var discovered = new List<object>();
            bool tableOpen = false;
            try
            {
                var st = OnlineInteraction.GetVisitorTableWindowState(_plugin.MW);
                tableOpen = st.HasValue && st.Value.exists;
            }
            catch { }
            try
            {
                var vis = _plugin.Lobby?.GetVisibleRooms();
                if (vis != null)
                {
                    foreach (var r in vis)
                    {
                        if (r == null || r.Id == 0) continue;
                        discovered.Add(new
                        {
                            lobbyId = r.Id.ToString(),
                            hostName = r.HostName ?? "",
                            petName = r.Name ?? "",
                            hostSteam = r.HostId != 0 ? r.HostId.ToString() : "",
                            members = r.Players,
                            maxMembers = r.Max,
                            isCurrent = r.IsCurrent,
                            friendsOnly = r.FriendsOnly
                        });
                        if (discovered.Count >= 80) break;
                    }
                }
            }
            catch (Exception ex) { Log("读取可见房间失败: " + ex.Message); }

            var saves = ListSaveFiles(1);
            return new
            {
                connected = true,
                hosting,
                lobbyId,
                fixId,
                members,
                steamId,
                hostSteam,
                petName,
                hostName,
                level,
                levelMax,
                money,
                exp,
                authed = HasSession,
                tableOpen,
                discovered,
                saveDir = SavesDir(),
                latestSave = saves.Count > 0 ? saves[0].Name : ""
            };
        }

        private static string Json(object obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        private static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(LocalDir);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n");
            }
            catch { }
        }
    }

    public class CloudRoom
    {
        public string lobby_id { get; set; } = "";
        public string fix_id { get; set; } = "";       // 游戏固定 ID (V255082)
        public string host_steam { get; set; } = "";
        public string host_name { get; set; } = "";
        public string pet_name { get; set; } = "";
        public string region { get; set; } = "";
        public int members { get; set; } = 0;
        public int level { get; set; } = 0;
        public int level_max { get; set; } = 0;
    }

    public class RestoreRequest
    {
        public string name { get; set; } = "";
        public string base64 { get; set; } = "";
    }

    /// <summary>排行榜条目（等级榜：等级 → 转生 → 金钱）</summary>
    public class CloudEntry
    {
        public string nickname { get; set; } = "";
        public string pet_name { get; set; } = "";
        public int level { get; set; } = 0;
        public int level_max { get; set; } = 0;
        public long money { get; set; } = 0;
    }
}
