using DotRas;
using Looog.Logging;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace PPPoEFucker
{
    class Program
    {
        private static string format = "yyyy-MM-dd HH:mm:ss";
        private static string entryName = string.Empty;
        private static string userName = string.Empty;
        private static string pwd = string.Empty;
        private static int switchTime = 0;
        private static List<string> historyIps = null;
        private static Logger logger = null;
        private static Thread thread_Fuck = null;

        static void Main(string[] args)
        {
            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Process.GetCurrentProcess().ProcessName + ".exe");
            int WINDOW_HANDLER = UnsafeNativeMethods.FindWindow(null, appPath);
            IntPtr CLOSE_MENU = UnsafeNativeMethods.GetSystemMenu((IntPtr)WINDOW_HANDLER, IntPtr.Zero);
            int SC_CLOSE = 0xF060;
            UnsafeNativeMethods.RemoveMenu(CLOSE_MENU, SC_CLOSE, 0x0);

            ConsoleKeyInfo cki;
            Console.TreatControlCAsInput = true;

            try
            {
                logger = new Logger();
                logger.Initialize("PPPoEFucker");
                logger.Info("[{0}] The PPPoE broadband dialing starting...!", DateTime.Now.ToString(format));
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                historyIps = new List<string>();
                entryName = ConfigurationManager.AppSettings["entryName"].ToString().Trim();
                userName = ConfigurationManager.AppSettings["userName"].ToString().Trim();
                pwd = ConfigurationManager.AppSettings["pwd"].ToString().Trim();
                switchTime = int.Parse(ConfigurationManager.AppSettings["switchTime"].ToString().Trim());

                if (string.IsNullOrWhiteSpace(entryName))
                {
                    Console.WriteLine("'entryName' in config file cannot be empty.");
                    throw new Exception("'entryName' in config file cannot be empty");
                }
                else if (string.IsNullOrWhiteSpace(userName))
                {
                    Console.WriteLine("'userName' in config file cannot be empty.");
                    throw new Exception("'userName' in config file cannot be empty");
                }
                else if (string.IsNullOrWhiteSpace(pwd))
                {
                    Console.WriteLine("'pwd' in config file cannot be empty.");
                    throw new Exception("'pwd' in config file cannot be empty");
                }

                thread_Fuck = new Thread(Run);
                thread_Fuck.Name = "thread_Fuck";
                thread_Fuck.IsBackground = true;
                thread_Fuck.Start();

                do
                {
                    cki = Console.ReadKey();
                    if ((cki.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        if ((cki.Modifiers & ConsoleModifiers.Alt) != 0)
                        {
                            if (cki.Key.ToString().ToLower().Equals("e"))
                            {
                                logger.Info("[{0}] The process is exited by the user pressing [Ctrl + Alt + e]", DateTime.Now.ToString(format));
                                Environment.Exit(0);
                            }
                        }
                    }

                    Thread.Sleep(5000);
                } while (true);
            }
            catch (Exception ex)
            {
                logger.Error("[{0}] {1}\n{2}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
            }

            Console.ReadKey();
        }

        private static void Run()
        {
            try
            {
                long loop = 0;
                while (true)
                {
                    ChangeIP();

                    logger.Info("[{0}] 等待{1}秒后挂断PPPoE并重连...", DateTime.Now.ToString(format), switchTime);
                    loop++;
                    logger.Info("[{0}] --------------程序已经轮询 {1} 次挂断/重连操作--------------\n", DateTime.Now.ToString(format), loop);
                    Thread.Sleep(switchTime * 1000);
                }
            }
            catch (Exception ex)
            {
                logger.Error("[{0}] {1}\n{2}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
            }
        }

        /// <summary>
        /// 通过挂断再拨号建立连接来改变IP
        /// </summary>
        private static void ChangeIP()
        {
            logger.Info("[{0}] 正在执行切换IP操作...", DateTime.Now.ToString(format));

        HANDUPCON:
            string oldIpAddress = string.Empty;
            RasConnection oldConn = null;
            GetIPAddress(out oldIpAddress, out oldConn);
            if (oldConn != null)
            {
                entryName = oldConn.EntryName;
                int tryHangup = 0;
                try
                {
                    tryHangup++;
                    logger.Info("[{0}] 开始挂断连接...", DateTime.Now.ToString(format));
                    oldConn.HangUp(5 * 1000);
                    Thread.Sleep(2000);
                    RasConnection con = RasConnection.GetActiveConnectionById(oldConn.EntryId);
                    if (con != null)
                    {
                        if (tryHangup >= 10)
                        {
                            try
                            {
                                logger.Critical("[{0}] 尝试挂断连接已经超过 {0} 次,全部失败!", DateTime.Now.ToString(format), tryHangup);
                            }
                            catch (Exception ex)
                            {
                                logger.Error("[{0}] {1}", DateTime.Now.ToString(format), ex.Message);
                            }

                            return;
                        }

                        logger.Info("[{0}] 挂断连接失败,尝试重新挂断...", DateTime.Now.ToString(format));
                        Thread.Sleep(500);
                        goto HANDUPCON;
                    }
                    logger.Info("[{0}] 成功挂断连接！\n", DateTime.Now.ToString(format));
                }
                catch (Exception ex)
                {
                    logger.Error("[{0}] 挂断连接过程中出现异常:{1}\n{2}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
                    Thread.Sleep(1000);
                    return;
                }
            }

        CHANGEIP:
            int tryConn = 0;
            string newAddresses = string.Empty;
            try
            {
                tryConn++;
                logger.Info("[{0}] 开始尝试建立拨号连接...", DateTime.Now.ToString(format));
                RasDialer rs = new RasDialer();
                rs.EntryName = entryName;
                rs.PhoneBookPath = RasPhoneBook.GetPhoneBookPath(RasPhoneBookType.AllUsers);
                rs.Credentials = new NetworkCredential(userName, pwd);
                rs.Dial();
                rs.Dispose();
                logger.Info("[{0}] 已经成功建立连接!", DateTime.Now.ToString(format));

                var conn = GetRasConnection();
                if (conn != null)
                {
                    var ipInfo = (RasIPInfo)conn.GetProjectionInfo(RasProjectionType.IP);
                    if (ipInfo != null && ipInfo.IPAddress != null)
                    {
                        newAddresses = ipInfo.IPAddress.ToString().Trim();
                        logger.Info("[{0}] 当前获得新建连接的 IP: {1}\n", DateTime.Now.ToString(format), newAddresses);
                    }
                    conn = null;
                }
                conn = null;

            }
            catch (Exception ex)
            {
                if (tryConn >= 10)
                {
                    logger.Info("[{0}] 尝试建立拨号连接已经超过 {0} 次,全部失败!", DateTime.Now.ToString(format), tryConn);
                    return;
                }
                logger.Error("[{0}] 建立拨号连接失败:{1}\n{2}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
                logger.Info("[{0}] 将尝试重新建立拨号连接...", DateTime.Now.ToString(format));
                Thread.Sleep(1000);
                goto CHANGEIP;
            }


            oldConn = null;
            try
            {
                if (oldIpAddress == newAddresses)
                {
                    logger.Info("[{0}] 当前IP和上次重复,重新拨号切换...", DateTime.Now.ToString(format));
                    Thread.Sleep(2000);
                    goto HANDUPCON;
                }
                else
                {
                    if (historyIps.Contains(newAddresses))
                    {
                        // 1  2  3  4  5  1
                        logger.Info("[{0}] 当前IP已存在历史IP列表中,重新拨号...", DateTime.Now.ToString(format));
                        Thread.Sleep(2000);
                        goto HANDUPCON;
                    }

                    if (historyIps.Count >= 5)
                    {
                        historyIps.RemoveAt(0);
                        historyIps.Add(newAddresses);
                    }
                    else
                    {
                        historyIps.Add(newAddresses);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error("[{0}] 在检查IP是否重复时出现错误:{1}\n{2}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
            }
        }

        private static void GetIPAddress(out string ipAddress, out RasConnection oldConn)
        {
            logger.Info("[{0}] 尝试获取当前宽带连接信息...", DateTime.Now.ToString(format));

            oldConn = null;
            ipAddress = string.Empty;
            var conns = RasConnection.GetActiveConnections();
            if (conns == null)
            {
                logger.Info("[{0}] 当前没有获得有效宽带连接.");
                return;
            }

            if (conns.Count == 0)
            {
                logger.Info("[{0}] 当前获得{1}个有效宽带连接.", DateTime.Now.ToString(format), conns.Count);
                return;
            }

            logger.Info("[{0}] 当前获得{1}个有效宽带连接.", DateTime.Now.ToString(format), conns.Count);
            foreach (var conn in conns)
            {
                if (conn.Device.DeviceType.ToString().ToLower().Equals("pppoe"))
                {
                    oldConn = conn;
                    break;
                }
            }

            if (oldConn != null)
            {
                try
                {
                    RasIPInfo ipInfo = (RasIPInfo)oldConn.GetProjectionInfo(RasProjectionType.IP);
                    if (ipInfo != null && ipInfo.IPAddress != null)
                    {
                        ipAddress = ipInfo.IPAddress.ToString().Trim();
                        logger.Info("[{0}] 当前获得有效宽带连接 IP: {1}\tEntryName: {2}\n", DateTime.Now.ToString(format), ipAddress, oldConn.EntryName);
                    }
                    else
                    {
                        logger.Info("[{0}] *********当前没有获得有效宽带连接*********", DateTime.Now.ToString(format));
                    }
                    ipInfo = null;

                    #region MyRegion
                    ////var obj = oldConn.GetProjectionInfo(RasProjectionType.IP);
                    ////var obj1 = oldConn.GetProjectionInfo(RasProjectionType.Amb);
                    ////var obj2 = oldConn.GetProjectionInfo(RasProjectionType.Ccp);
                    ////var obj3 = oldConn.GetProjectionInfo(RasProjectionType.IPv6);
                    ////var obj4 = oldConn.GetProjectionInfo(RasProjectionType.Ipx);
                    ////var obj5 = oldConn.GetProjectionInfo(RasProjectionType.Lcp);
                    ////var obj6 = oldConn.GetProjectionInfo(RasProjectionType.Nbf);
                    ////var obj7 = oldConn.GetProjectionInfo(RasProjectionType.Slip);
                    #endregion
                }
                catch (Exception ex)
                {
                    logger.Info("[{0}] 获取宽带连接信息时出错:{1}\n{2}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
                }
            }

            conns = null;
        }

        private static RasConnection GetRasConnection()
        {
            RasConnection conn = null;
            try
            {
                var conns = RasConnection.GetActiveConnections();
                if (conns != null && conns.Count > 0)
                {
                    conn = conns[0];
                }
            }
            catch (Exception ex)
            {
                logger.Error("[{0}] 获取RasConnection对象信息出错.{0}\n{1}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
            }

            return conn;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            logger.Error("[{0}] Main-Uncaught exception:{0}\n{1}", DateTime.Now.ToString(format), ex.Message, ex.StackTrace);
        }
    }

    /// <summary>
    /// P/Invoke Methods
    /// </summary>
    sealed class UnsafeNativeMethods
    {
        #region P/Invoke win32

        [DllImport("user32.dll ", EntryPoint = "FindWindow", CharSet = CharSet.Unicode, ThrowOnUnmappableChar = true)]
        internal static extern int FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll ", EntryPoint = "GetSystemMenu")]
        internal static extern IntPtr GetSystemMenu(IntPtr hWnd, IntPtr bRevert);

        [DllImport("user32.dll ", EntryPoint = "RemoveMenu")]
        internal static extern int RemoveMenu(IntPtr hMenu, int nPos, int flags);
        #endregion
    }
}