using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace QuickLaunch
{
    internal enum LogLevel
    {
        INFO,
        OK,
        WARN,
        ERR,
        WAIT
    }

    /// <summary>
    /// 日志：内存队列（界面每 150ms 取一次，绝不卡界面）+ 后台线程写文件
    /// logs\launch-yyyy-MM-dd.log，按天分文件，自动清理 30 天前的旧日志。
    /// </summary>
    internal static class Logging
    {
        private const int KeepDays = 30;
        private const int RingSize = 400;

        private static readonly object QGate = new object();
        private static readonly Queue<LogItem> Queue = new Queue<LogItem>(256);
        private static readonly LogItem[] Ring = new LogItem[RingSize];
        private static int _ringAt;
        private static int _ringCount;

        private static Thread _writer;
        private static volatile bool _running;
        private static DateTime _fileDay = DateTime.MinValue;
        private static string _filePath = "";

        private static int _errCount;
        private static int _warnCount;
        private static int _total;

        /// <summary>同时把日志打到控制台（--scan 自检模式用）。</summary>
        public static bool ConsoleMirror;

        public static string FilePath { get { return _filePath; } }
        public static string LogFolder { get { return Helper.LogDir; } }
        public static int ErrorCount { get { return _errCount; } }
        public static int WarnCount { get { return _warnCount; } }
        public static int TotalCount { get { return _total; } }

        public sealed class LogItem
        {
            public DateTime Time;
            public LogLevel Level;
            public string Message = "";
        }

        // ------------------------------------------------------------- 生命周期

        public static void Init()
        {
            if (_running) return;
            _running = true;
            try
            {
                _filePath = Path.Combine(Helper.LogDir, "launch-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                _fileDay = DateTime.Now.Date;
            }
            catch { _filePath = ""; }

            _writer = new Thread(WriterLoop);
            _writer.IsBackground = true;
            _writer.Name = "QuickLaunch-LogWriter";
            _writer.Start();

            Write(LogLevel.INFO, "===== QuickLaunch 启动 =====");
            Write(LogLevel.INFO, "日志文件：" + _filePath);
            Write(LogLevel.INFO, "系统：" + Environment.OSVersion.VersionString + " / CLR " + Environment.Version + " / " + (IntPtr.Size * 8) + " 位");
        }

        public static void Shutdown()
        {
            if (!_running) return;
            Write(LogLevel.INFO, "===== QuickLaunch 退出，日志结束 =====");
            lock (QGate)
            {
                _running = false;
                Monitor.PulseAll(QGate);
            }
            try { if (_writer != null) _writer.Join(1500); }
            catch { }
            CleanOldLogs();
        }

        // ---------------------------------------------------------------- 写入

        public static void Write(LogLevel lv, string msg)
        {
            var it = new LogItem();
            it.Time = DateTime.Now;
            it.Level = lv;
            it.Message = msg == null ? "" : msg;

            lock (QGate)
            {
                Queue.Enqueue(it);
                _total++;
                if (lv == LogLevel.ERR) _errCount++;
                if (lv == LogLevel.WARN) _warnCount++;
                Monitor.Pulse(QGate);
            }

            if (ConsoleMirror)
            {
                Console.WriteLine("{0} [{1,-4}] {2}", it.Time.ToString("HH:mm:ss.fff"), lv, it.Message);
            }
        }

        public static void Info(string m) { Write(LogLevel.INFO, m); }
        public static void Ok(string m) { Write(LogLevel.OK, m); }
        public static void Warn(string m) { Write(LogLevel.WARN, m); }
        public static void Err(string m) { Write(LogLevel.ERR, m); }

        public static void Exception(string where, Exception ex)
        {
            Write(LogLevel.ERR, where + " 抛异常：" + ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace)) Write(LogLevel.ERR, ex.StackTrace);
        }

        // ------------------------------------------------------------ 界面取用

        /// <summary>界面定时器调用：把队列里的新日志搬进环形缓冲，返回这一批。</summary>
        public static List<LogItem> TakeNew()
        {
            var batch = new List<LogItem>();
            lock (QGate)
            {
                while (Queue.Count > 0)
                {
                    LogItem it = Queue.Dequeue();
                    Ring[_ringAt] = it;
                    _ringAt = (_ringAt + 1) % RingSize;
                    if (_ringCount < RingSize) _ringCount++;
                    batch.Add(it);
                }
            }
            return batch;
        }

        /// <summary>取最近 n 条（重新灌满界面用）。</summary>
        public static List<LogItem> Recent(int n)
        {
            var list = new List<LogItem>();
            int take = Math.Min(n, _ringCount);
            int start = (_ringAt - take + RingSize * 2) % RingSize;
            for (int i = 0; i < take; i++)
                list.Add(Ring[(start + i) % RingSize]);
            return list;
        }

        public static void ResetCounters()
        {
            _errCount = 0; _warnCount = 0; _total = 0;
        }

        // ------------------------------------------------------------ 文件线程

        private static void WriterLoop()
        {
            var pending = new List<LogItem>();
            while (true)
            {
                lock (QGate)
                {
                    while (_running && Queue.Count == 0)
                        Monitor.Wait(QGate, 400);
                    while (Queue.Count > 0)
                        pending.Add(Queue.Dequeue());
                    if (!_running && pending.Count == 0) break;
                }

                if (pending.Count > 0)
                {
                    FlushToFile(pending);
                    pending.Clear();
                }
            }
            FlushToFile(pending);
        }

        private static void FlushToFile(List<LogItem> items)
        {
            if (items.Count == 0) return;
            try
            {
                // 跨天换文件
                if (DateTime.Now.Date != _fileDay)
                {
                    _fileDay = DateTime.Now.Date;
                    _filePath = Path.Combine(Helper.LogDir, "launch-" + _fileDay.ToString("yyyy-MM-dd") + ".log");
                }

                var sb = new StringBuilder();
                foreach (LogItem it in items)
                {
                    sb.Append(it.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                      .Append(" [").Append(it.Level.ToString().PadRight(4)).Append("] ")
                      .Append(it.Message.Replace("\r\n", " ").Replace("\n", " "))
                      .Append("\r\n");
                }
                File.AppendAllText(_filePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // 磁盘满/文件被占用时不能反过来拖垮程序，静默丢弃这一批
            }
        }

        private static void CleanOldLogs()
        {
            try
            {
                string dir = Helper.LogDir;
                DateTime limit = DateTime.Now.AddDays(-KeepDays);
                foreach (string f in Directory.GetFiles(dir, "launch-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < limit) File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
