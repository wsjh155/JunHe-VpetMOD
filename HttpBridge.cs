using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Plugin.QQBody;

/// <summary>
/// 极简本机 HTTP 服务，给 Python(AstrBot) 调用。
/// 只监听回环地址，不对外网开放。
/// </summary>
internal sealed class HttpBridge
{
    private readonly QQBodyPlugin _plugin;
    private readonly int _port;
    private HttpListener _listener;
    private CancellationTokenSource _cts;

    public bool IsRunning => _listener != null && _listener.IsListening;
    public string BoundPrefix { get; private set; }

    public HttpBridge(QQBodyPlugin plugin, int port)
    {
        _plugin = plugin;
        _port = port;
    }

    public void Start()
    {
        // Windows 下非管理员通常只能绑 localhost，两个都试
        string[] candidates =
        {
            $"http://127.0.0.1:{_port}/",
            $"http://localhost:{_port}/"
        };

        foreach (string prefix in candidates)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                _listener = listener;
                BoundPrefix = prefix;
                QQBodyPlugin.Log($"HTTP 桥接已监听 {prefix}");
                break;
            }
            catch (Exception ex)
            {
                QQBodyPlugin.Log($"监听 {prefix} 失败: {ex.Message}");
                try { listener.Close(); } catch { }
            }
        }

        if (_listener == null)
        {
            QQBodyPlugin.Log("HTTP 桥接启动失败：端口可能被占用，或需要提权");
            return;
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        Task.Run(() => AcceptLoop(token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_listener != null && _listener.IsListening) _listener.Stop();
            _listener?.Close();
        }
        catch { }
        _listener = null;
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                QQBodyPlugin.Log("GetContext 异常: " + ex.Message);
                await Task.Delay(300, token).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            string path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
            if (path == "") path = "/";

            string method = ctx.Request.HttpMethod.ToUpperInvariant();

            if (method == "GET" && (path == "/ping" || path == "/"))
            {
                WriteJson(ctx, 200, new { ok = true, service = "vpet-qqbody", pid = Environment.ProcessId });
                return;
            }

            if (method == "GET" && path == "/status")
            {
                WriteJson(ctx, 200, _plugin.BuildStatus());
                return;
            }

            // 功能开关状态：机器人端可以先查一遍，关掉的就不必再发请求
            if (method == "GET" && path == "/config")
            {
                var cfg = _plugin.Config;
                WriteJson(ctx, 200, new
                {
                    ok = true,
                    work_anim = cfg.WorkAnim,
                    token_fly = cfg.TokenFly,
                    token_counter = cfg.TokenCounter,
                    bubble = cfg.Bubble,
                    chat_api = cfg.ChatApi,
                });
                return;
            }

            if (method == "GET" && path == "/events")
            {
                int since = 0;
                string q = ctx.Request.QueryString["since"];
                if (!string.IsNullOrEmpty(q)) int.TryParse(q, out since);

                var list = _plugin.GetSaysSince(since);
                WriteJson(ctx, 200, new { ok = true, last = _plugin.LastSayId, count = list.Count, events = list });
                return;
            }

            if (method == "POST" && path == "/say")
            {
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                if (doc == null)
                {
                    WriteJson(ctx, 400, new { ok = false, error = "invalid json" });
                    return;
                }

                string text = GetString(doc.RootElement, "text");
                bool rnd = GetBool(doc.RootElement, "rnd");
                if (string.IsNullOrWhiteSpace(text))
                {
                    WriteJson(ctx, 400, new { ok = false, error = "text 为空" });
                    return;
                }

                _plugin.InjectSay(text, rnd);
                WriteJson(ctx, 200, new { ok = true });
                return;
            }

            if (method == "POST" && path == "/label")
            {
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                if (doc == null)
                {
                    WriteJson(ctx, 400, new { ok = false, error = "invalid json" });
                    return;
                }

                string text = GetString(doc.RootElement, "text");
                int ms = GetInt(doc.RootElement, "ms", 2000);
                _plugin.InjectLabel(text, ms);
                WriteJson(ctx, 200, new { ok = true });
                return;
            }

            if (method == "POST" && path == "/bubble")
            {
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                if (doc == null)
                {
                    WriteJson(ctx, 400, new { ok = false, error = "invalid json" });
                    return;
                }

                string text = GetString(doc.RootElement, "text");
                string color = GetString(doc.RootElement, "color") ?? "white";
                int ms = GetInt(doc.RootElement, "ms", 2000);
                if (string.IsNullOrWhiteSpace(text))
                {
                    WriteJson(ctx, 400, new { ok = false, error = "text 为空" });
                    return;
                }

                _plugin.ShowBubble(text, color, ms);
                WriteJson(ctx, 200, new { ok = true });
                return;
            }

            if (method == "POST" && path == "/action")
            {
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                if (doc == null)
                {
                    WriteJson(ctx, 400, new { ok = false, error = "invalid json" });
                    return;
                }

                string name = GetString(doc.RootElement, "name");
                bool done = _plugin.DoAction(name);
                WriteJson(ctx, done ? 200 : 400, new { ok = done, action = name });
                return;
            }

            if (method == "POST" && path == "/token")
            {
                // 真实 token 增量（机器人侧从 AstrBot 的 provider_stats 读出来后推过来）。
                // 累计/格式化在桌宠侧做：桌宠重启或设置页重置都会归零。
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                if (doc == null)
                {
                    WriteJson(ctx, 400, new { ok = false, error = "invalid json" });
                    return;
                }

                int delta = GetInt(doc.RootElement, "delta", 0);
                if (delta == 0) delta = GetInt(doc.RootElement, "total", 0);   // 兼容旧调用
                if (delta > 0) _plugin.AddTokenDelta(delta);
                if (GetBool(doc.RootElement, "fly")) _plugin.ShowTokenFly(delta);
                WriteJson(ctx, 200, new { ok = true, total = _plugin.TokenTotal });
                return;
            }

            if (method == "POST" && path == "/token/reset")
            {
                _plugin.ResetTokenTotal();
                WriteJson(ctx, 200, new { ok = true, total = 0 });
                return;
            }

            if (method == "POST" && path == "/fly")
            {
                // 只飞一个光团（数量由机器人侧决定节奏，球上不带数字）
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                int count = 1;
                if (doc != null) count = GetInt(doc.RootElement, "count", 1);
                count = Math.Max(1, Math.Min(5, count));
                for (int i = 0; i < count; i++) _plugin.ShowTokenFly(0);
                WriteJson(ctx, 200, new { ok = true, count });
                return;
            }

            if (method == "POST" && path == "/counter")
            {
                string body = await ReadBody(ctx).ConfigureAwait(false);
                using var doc = ParseJson(body);
                if (doc == null)
                {
                    WriteJson(ctx, 400, new { ok = false, error = "invalid json" });
                    return;
                }

                string text = GetString(doc.RootElement, "text") ?? "";
                _plugin.SetTokenCounter(text);
                WriteJson(ctx, 200, new { ok = true });
                return;
            }

            WriteJson(ctx, 404, new { ok = false, error = "not found", path = path });
        }
        catch (Exception ex)
        {
            QQBodyPlugin.Log("处理请求异常: " + ex);
            try { WriteJson(ctx, 500, new { ok = false, error = ex.Message }); } catch { }
        }
    }

    #region 工具

    private static async Task<string> ReadBody(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    private static string GetString(JsonElement root, string name)
    {
        try
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        catch { }
        return null;
    }

    private static bool GetBool(JsonElement root, string name)
    {
        try
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True) return true;
        }
        catch { }
        return false;
    }

    private static int GetInt(JsonElement root, string name, int def)
    {
        try
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
        }
        catch { }
        return def;
    }

    private static void WriteJson(HttpListenerContext ctx, int code, object payload)
    {
        byte[] buf = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        ctx.Response.OutputStream.Write(buf, 0, buf.Length);
        ctx.Response.OutputStream.Flush();
        ctx.Response.Close();
    }

    #endregion
}
