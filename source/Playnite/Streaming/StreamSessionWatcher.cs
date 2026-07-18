using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Playnite.SDK;

namespace Playnite.Streaming
{
    /// <summary>
    /// 通过轮询本机 UDP 端口占用情况来检测 Sunshine 串流会话的开始与结束。
    /// 串流建立时 sunshine.exe 会占用 UDP 48000 端口传输视频，会话结束后端口释放。
    /// 只在状态发生变化的那一刻触发回调（不会每次轮询都触发）。
    /// </summary>
    public class StreamSessionWatcher : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // Sunshine 视频流默认 UDP 端口。
        public const int DefaultStreamPort = 48000;

        private readonly int streamPort;
        private readonly int pollIntervalMs;
        private Timer timer;
        private bool streaming;
        private bool disposed;
        private readonly object stateLock = new object();

        /// <summary>串流会话开始时触发（从"无"到"有"的那一刻）。</summary>
        public event EventHandler StreamStarted;

        /// <summary>串流会话结束时触发（从"有"到"无"的那一刻）。</summary>
        public event EventHandler StreamEnded;

        public StreamSessionWatcher(int streamPort = DefaultStreamPort, int pollIntervalMs = 3000)
        {
            this.streamPort = streamPort;
            this.pollIntervalMs = pollIntervalMs;
        }

        public void Start()
        {
            if (timer != null)
            {
                return;
            }

            logger.Info($"Starting stream session watcher on UDP port {streamPort}, interval {pollIntervalMs}ms.");
            timer = new Timer(_ => Poll(), null, 0, pollIntervalMs);
        }

        public void Stop()
        {
            timer?.Dispose();
            timer = null;
        }

        private void Poll()
        {
            bool portInUse;
            try
            {
                portInUse = IsPortOwnedBySunshine(streamPort);
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to poll UDP port state for stream detection.");
                return;
            }

            bool started = false;
            bool ended = false;
            lock (stateLock)
            {
                if (portInUse && !streaming)
                {
                    streaming = true;
                    started = true;
                }
                else if (!portInUse && streaming)
                {
                    streaming = false;
                    ended = true;
                }
            }

            if (started)
            {
                logger.Info("Sunshine stream session started (UDP port in use).");
                StreamStarted?.Invoke(this, EventArgs.Empty);
            }
            else if (ended)
            {
                logger.Info("Sunshine stream session ended (UDP port released).");
                StreamEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 查询指定 UDP 端口是否被名为 sunshine 的进程占用。
        /// </summary>
        public static bool IsPortOwnedBySunshine(int port)
        {
            foreach (var entry in GetUdpTableWithPid())
            {
                if (entry.Port != port)
                {
                    continue;
                }

                try
                {
                    var proc = Process.GetProcessById(entry.Pid);
                    if (string.Equals(proc.ProcessName, "sunshine", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // 进程可能刚好退出，忽略。
                }
            }

            return false;
        }

        /// <summary>
        /// 在常见安装位置查找 sunshine.exe，找不到返回 null。
        /// </summary>
        public static string FindSunshineExe()
        {
            // 1. 若 sunshine.exe 正在运行，直接取它的路径。
            try
            {
                var running = Process.GetProcessesByName("sunshine").FirstOrDefault();
                if (running != null)
                {
                    var path = running.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // 访问其他进程模块可能因权限失败，忽略，继续走常见路径。
            }

            // 2. 常见安装路径。
            var candidates = new List<string>();
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            })
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                candidates.Add(Path.Combine(root, "Sunshine", "sunshine.exe"));
                candidates.Add(Path.Combine(root, "Sunshine", "tools", "sunshine.exe"));
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        #region iphlpapi P/Invoke

        private struct UdpRow
        {
            public int Port;
            public int Pid;
        }

        private const int AF_INET = 2;
        private const int UDP_TABLE_OWNER_PID = 1;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int tableClass,
            int reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort; // 网络字节序，存在低位两个字节里
            public uint owningPid;
        }

        private static IEnumerable<UdpRow> GetUdpTableWithPid()
        {
            var rows = new List<UdpRow>();
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (size == 0)
            {
                return rows;
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedUdpTable(buffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
                if (result != 0)
                {
                    return rows;
                }

                var count = Marshal.ReadInt32(buffer);
                var rowPtr = IntPtr.Add(buffer, 4);
                var rowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                for (var i = 0; i < count; i++)
                {
                    var row = (MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_UDPROW_OWNER_PID));
                    // localPort 是网络字节序，取低两字节还原成主机端口号。
                    var port = (int)((row.localPort & 0xFF) << 8 | (row.localPort >> 8) & 0xFF);
                    rows.Add(new UdpRow { Port = port, Pid = (int)row.owningPid });
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return rows;
        }

        #endregion

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
        }
    }
}
