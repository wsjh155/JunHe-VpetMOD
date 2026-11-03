using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VPet.Plugin.OnlineInteraction
{
    /// <summary>
    /// 浏览器进程定位与回收（零依赖，不引入 System.Management）。
    ///
    /// 为什么需要它：Edge/Chrome 是「启动器 + browser 主进程 + 若干子进程」的多进程架构，
    /// Process.Start 拿到的往往只是启动器——它把命令行转交给已有的 browser 进程后立刻退出。
    /// 因此 Process.HasExited == true 完全不能说明浏览器关了：
    /// 之前按 PID 记录 + HasExited 判断，会把仍然占着 --user-data-dir 的无头实例当成「已退出」，
    /// 于是看门狗反复重启、可见窗口永远抢不到配置目录，用户看到的就是「点打开网页没反应」。
    ///
    /// 这里改为读目标进程的命令行（PEB → RTL_USER_PROCESS_PARAMETERS → CommandLine），
    /// 按 --user-data-dir 精确匹配，并用「命令行不含 --type=」识别主 browser 进程。
    /// </summary>
    internal static class BrowserProcs
    {
        public sealed class BrowserProc
        {
            public int Pid;
            /// <summary>主 browser 进程（命令行不含 --type=），杀掉它即可带走整棵树</summary>
            public bool IsMain;
            public bool Headless;
            public string CommandLine;

            public override string ToString()
            {
                string kind = IsMain ? "主进程" : "子进程";
                string mode = Headless ? "无头" : "可见";
                return kind + "/" + mode + " PID=" + Pid;
            }
        }

        private static readonly string[] ProcessNames = { "msedge", "chrome" };

        // ───────── 公开接口 ─────────

        /// <summary>列出所有使用该配置目录的浏览器进程（含子进程）</summary>
        public static List<BrowserProc> Find(string profileDir)
        {
            var list = new List<BrowserProc>();
            if (string.IsNullOrEmpty(profileDir)) return list;

            foreach (var name in ProcessNames)
            {
                Process[] arr;
                try { arr = Process.GetProcessesByName(name); }
                catch { continue; }

                foreach (var p in arr)
                {
                    int pid;
                    try { pid = p.Id; } catch { continue; }

                    string cl;
                    try { cl = GetCommandLine(pid); } catch { continue; }
                    if (string.IsNullOrEmpty(cl)) continue;
                    if (!ContainsPath(cl, profileDir)) continue;

                    list.Add(new BrowserProc
                    {
                        Pid = pid,
                        IsMain = cl.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) < 0,
                        Headless = cl.IndexOf("--headless", StringComparison.OrdinalIgnoreCase) >= 0,
                        CommandLine = cl
                    });
                }
            }
            return list;
        }

        /// <summary>是否已有主 browser 进程在跑（无头或可见都算）</summary>
        public static bool HasMain(string profileDir, out bool headless)
        {
            headless = false;
            var main = Find(profileDir).FirstOrDefault(x => x.IsMain);
            if (main == null) return false;
            headless = main.Headless;
            return true;
        }

        /// <summary>结束所有使用该配置目录的浏览器进程（主进程整树优先），返回实际处理数</summary>
        public static int KillAll(string profileDir, out bool anyMain)
        {
            var found = Find(profileDir);
            anyMain = found.Any(x => x.IsMain);

            int killed = 0;
            // 先杀主进程：Kill(true) 会带走渲染/GPU 等子进程，避免它们继续持有配置目录
            foreach (var b in found.OrderByDescending(x => x.IsMain))
            {
                if (KillTree(b.Pid)) killed++;
            }
            return killed;
        }

        /// <summary>
        /// 配置目录是否被浏览器独占（Chromium 用 LockFileEx 锁住 user-data-dir 下的 lockfile）。
        /// 这是比 PID 更可靠的「有没有实例在跑」判据。
        /// </summary>
        public static bool ProfileLocked(string profileDir)
        {
            if (string.IsNullOrEmpty(profileDir)) return false;
            string lockFile = Path.Combine(profileDir, "lockfile");
            try
            {
                if (!File.Exists(lockFile)) return false;
                using (new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None))
                    return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }

        /// <summary>没有主进程且 config 目录锁已释放</summary>
        public static bool IsIdle(string profileDir)
        {
            bool headless;
            if (HasMain(profileDir, out headless)) return false;
            return !ProfileLocked(profileDir);
        }

        /// <summary>等待配置目录真正空闲（浏览器退出后内核回收句柄需要一点时间）</summary>
        public static bool WaitIdle(string profileDir, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                if (IsIdle(profileDir)) return true;
                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(120);
            }
        }

        // ───────── 进程命令行的读取 ─────────

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr h, int cls,
            ref PROCESS_BASIC_INFORMATION pbi, int len, out int ret);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int PROCESS_VM_READ = 0x0010;

        /// <summary>读取指定进程的完整命令行；失败返回 null（权限不足/进程已退出）</summary>
        private static string GetCommandLine(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var pbi = new PROCESS_BASIC_INFORMATION();
                int len;
                if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out len) != 0) return null;
                if (pbi.PebBaseAddress == IntPtr.Zero) return null;

                var buf = new byte[8];
                IntPtr read;
                // PEB -> ProcessParameters（x64 固定偏移 0x20）
                if (!ReadProcessMemory(h, (IntPtr)(pbi.PebBaseAddress.ToInt64() + 0x20), buf, 8, out read)) return null;
                long pp = BitConverter.ToInt64(buf, 0);
                if (pp == 0) return null;

                // RTL_USER_PROCESS_PARAMETERS -> CommandLine（UNICODE_STRING，x64 偏移 0x70）
                var us = new byte[16];
                if (!ReadProcessMemory(h, (IntPtr)(pp + 0x70), us, 16, out read)) return null;
                int strLen = BitConverter.ToUInt16(us, 0);
                long strPtr = BitConverter.ToInt64(us, 8);
                if (strLen <= 0 || strLen > 0x8000 || strPtr == 0) return null;

                var bytes = new byte[strLen];
                if (!ReadProcessMemory(h, (IntPtr)strPtr, bytes, strLen, out read)) return null;
                return Encoding.Unicode.GetString(bytes);
            }
            catch { return null; }
            finally { CloseHandle(h); }
        }

        /// <summary>命令行里是否出现该目录（后接引号/空格/反斜杠/结尾，避免 a 与 a_ui 这种前缀误匹配）</summary>
        private static bool ContainsPath(string commandLine, string dir)
        {
            int idx = 0;
            while ((idx = commandLine.IndexOf(dir, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = idx + dir.Length;
                if (end >= commandLine.Length) return true;
                char c = commandLine[end];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return true;
                idx = end;
            }
            return false;
        }

        private static bool KillTree(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    // 防止 PID 被系统复用后误杀别的程序
                    if (name != "msedge" && name != "chrome") return false;
                    try { p.Kill(true); }
                    catch { try { p.Kill(); } catch { return false; } }
                    return true;
                }
            }
            catch { return false; }
        }
    }
}
