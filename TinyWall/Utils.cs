﻿using System;
using System.Security;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Management;
using Microsoft.Win32;
using Microsoft.Samples;

namespace PKSoft
{
    internal static class Utils
    {
        [SuppressUnmanagedCodeSecurityAttribute]
        internal static class SafeNativeMethods
        {
            [DllImport("user32.dll")]
            internal static extern IntPtr WindowFromPoint(Point pt);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", SetLastError=true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsImmersiveProcess(IntPtr hProcess);

            [DllImport("shlwapi.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool PathIsNetworkPath(string pszPath);

            [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
            internal static extern uint DnsFlushResolverCache();

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out long ClientProcessId);

            [DllImport("Wer.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
            internal static extern void WerAddExcludedApplication(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pwzExeName,
                [MarshalAs(UnmanagedType.Bool)]
                bool bAllUsers
            );

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.U4)]
            internal static extern int GetLongPathName(
                [MarshalAs(UnmanagedType.LPTStr)]
                string lpszShortPath,
                [MarshalAs(UnmanagedType.LPTStr)]
                StringBuilder lpszLongPath,
                [MarshalAs(UnmanagedType.U4)]
                int cchBuffer
            );

            #region WNetGetUniversalName
            internal const int UNIVERSAL_NAME_INFO_LEVEL = 0x00000001;
            internal const int ERROR_MORE_DATA = 234;
            internal const int ERROR_NOT_CONNECTED = 2250;
            internal const int NOERROR = 0;

            [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.U4)]
            internal static extern int WNetGetUniversalName(
                string lpLocalPath,
                [MarshalAs(UnmanagedType.U4)] int dwInfoLevel,
                IntPtr lpBuffer,
                [MarshalAs(UnmanagedType.U4)] ref int lpBufferSize);
            #endregion

            #region IsMetroActive
            [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            internal interface IAppVisibility
            {
                HRESULT GetAppVisibilityOnMonitor([In] IntPtr hMonitor, [Out] out MONITOR_APP_VISIBILITY pMode);
                HRESULT IsLauncherVisible([Out] out bool pfVisible);
                HRESULT Advise([In] IAppVisibilityEvents pCallback, [Out] out int pdwCookie);
                HRESULT Unadvise([In] int dwCookie);
            }
            //...
            internal enum HRESULT : long
            {
                S_FALSE = 0x0001,
                S_OK = 0x0000,
                E_INVALIDARG = 0x80070057,
                E_OUTOFMEMORY = 0x8007000E
            }
            internal enum MONITOR_APP_VISIBILITY
            {
                MAV_UNKNOWN = 0,         // The mode for the monitor is unknown
                MAV_NO_APP_VISIBLE = 1,
                MAV_APP_VISIBLE = 2
            }
            [ComImport, Guid("6584CE6B-7D82-49C2-89C9-C6BC02BA8C38"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            internal interface IAppVisibilityEvents
            {
                HRESULT AppVisibilityOnMonitorChanged(
                    [In] IntPtr hMonitor,
                    [In] MONITOR_APP_VISIBILITY previousMode,
                    [In] MONITOR_APP_VISIBILITY currentMode);

                HRESULT LauncherVisibilityChange([In] bool currentVisibleState);
            }
            #endregion

            #region DoMouseRightClick
            [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, IntPtr dwExtraInfo);
            private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
            private const uint MOUSEEVENTF_LEFTUP = 0x04;
            private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
            private const uint MOUSEEVENTF_RIGHTUP = 0x10;
            internal static void DoMouseRightClick()
            {
                //Call the imported function with the cursor's current position
                uint X = (uint)System.Windows.Forms.Cursor.Position.X;
                uint Y = (uint)System.Windows.Forms.Cursor.Position.Y;
                mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, X, Y, 0, IntPtr.Zero);
            }
            #endregion
        }

        private static readonly Random _rng = new Random();

        /*
        internal static List<string> GetDNSServers()
        {
            List<string> servers = new List<string>();

            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int i = 0; i < interfaces.Length; ++i)
            {
                NetworkInterface iface = interfaces[i];
                IPAddressCollection dnscollection = iface.GetIPProperties().DnsAddresses;
                for (int j = 0; j < dnscollection.Count; ++j)
                    servers.Add(dnscollection[j].ToString());
            }

            return servers;
        }
        */

        internal static bool IsMetroActive(out bool success)
        { // http://stackoverflow.com/questions/12009999/imetromodeislaunchervisible-in-c-sharp-via-pinvoke

            success = false;
            try
            {
                Type tIAppVisibility = Type.GetTypeFromCLSID(new Guid("7E5FE3D9-985F-4908-91F9-EE19F9FD1514"));
                SafeNativeMethods.IAppVisibility appVisibility = (SafeNativeMethods.IAppVisibility)Activator.CreateInstance(tIAppVisibility);
                bool launcherVisible;
                if (SafeNativeMethods.HRESULT.S_OK == appVisibility.IsLauncherVisible(out launcherVisible))
                {
                    // launcherVisible flag is valid
                    success = true;

                    using (Process p = Process.GetProcessById(Utils.GetForegroundProcessPid()))
                    {
                        return launcherVisible || Utils.IsImmersiveProcess(p);
                    }
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        internal static void ShowToastNotif(string msg)
        {
            msg = msg.Replace("\n", "|");
            string args = string.Format(CultureInfo.InvariantCulture, "KPados.TinyWall.Controller \"{0}\"", msg);
            Utils.StartProcess(Path.Combine(Path.GetDirectoryName(TinyWall.Interface.Internal.Utils.ExecutablePath), "Toaster.exe"), args, false, true);
        }

        internal static int GetForegroundProcessPid()
        {
            IntPtr hwnd = SafeNativeMethods.GetForegroundWindow();
            SafeNativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
            return pid;
        }

        internal static bool IsImmersiveProcess(Process p)
        {
            return SafeNativeMethods.IsImmersiveProcess(p.Handle);
        }

        internal static string ProgramFilesx86()
        {
            if ((8 == IntPtr.Size) || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
                return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            else
                return Environment.GetEnvironmentVariable("ProgramFiles");
        }

        internal static void CompressDeflate(string inputFile, string outputFile)
        {
            // Get the stream of the source file.
            using (FileStream inFile = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            {
              // Create the compressed file.
              using (FileStream outFile = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
              {
                using (DeflateStream Compress = new DeflateStream(outFile, CompressionMode.Compress, true))
                {
                    // Copy the source file into
                    // the compression stream.
                    byte[] buffer = new byte[4096];
                    int numRead;
                    while ((numRead = inFile.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        Compress.Write(buffer, 0, numRead);
                    }
                }
              }
            }
        }

        internal static void DecompressDeflate(string inputFile, string outputFile)
        {
            // Create the decompressed file.
            using (FileStream outFile = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            {
              // Get the stream of the source file.
              using (FileStream inFile = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
              {
                using (DeflateStream Decompress = new DeflateStream(inFile, CompressionMode.Decompress, true))
                {
                    //Copy the decompression stream into the output file.
                    byte[] buffer = new byte[4096];
                    int numRead;
                    while ((numRead = Decompress.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        outFile.Write(buffer, 0, numRead);
                    }
                }
              }
            }
        }

        internal static string GetPathOfProcessUseTwService(int pid, TinyWall.Interface.Controller controller)
        {
            // Shortcut for special case
            if ((pid == 0) || (pid == 4))
                return "System";

            string ret = GetLongPathName(ProcessManager.GetProcessPath(pid));
            if (string.IsNullOrEmpty(ret))
                ret = controller.TryGetProcessPath(pid);

            return ret;
        }

        internal static string GetPathOfProcess(int pid)
        {
            // Shortcut for special case
            if ((pid == 0) || (pid == 4))
                return "System";

            string ret = GetLongPathName(ProcessManager.GetProcessPath(pid));
            if (string.IsNullOrEmpty(ret))
                ret = string.Empty;

            return ret;
        }

        internal static string GetExecutableUnderCursor(int x, int y, TinyWall.Interface.Controller controller)
        {
            // Get process id under cursor
            int ProcId;
            int dummy = SafeNativeMethods.GetWindowThreadProcessId(SafeNativeMethods.WindowFromPoint(new System.Drawing.Point(x, y)), out ProcId);

            // Get executable of process
            return Utils.GetPathOfProcessUseTwService(ProcId, controller);
        }

        /// <summary>
        /// Converts a short path to a long path.
        /// </summary>
        /// <param name="shortPath">A path that may contain short path elements (~1).</param>
        /// <returns>The long path. Null or empty if the input is null or empty. Returns the input path in case of error.</returns>
        internal static string GetLongPathName(string shortPath)
        {
            if (String.IsNullOrEmpty(shortPath))
            {
                return shortPath;
            }

            StringBuilder builder = new StringBuilder(255);
            int result = SafeNativeMethods.GetLongPathName(shortPath, builder, builder.Capacity);
            if ((result > 0) && (result < builder.Capacity))
            {
                return builder.ToString(0, result);
            }
            else
            {
                if (result > 0)
                {
                    builder = new StringBuilder(result);
                    result = SafeNativeMethods.GetLongPathName(shortPath, builder, builder.Capacity);
                    return builder.ToString(0, result);
                }
                else
                {
                    // Path not found or other error
                    return shortPath;
                }
            }
        }

        internal static string RandomString(int length)
        {
            const string chars = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            char[] buffer = new char[length];
            for (int i = 0; i < length; i++)
            {
                buffer[i] = chars[_rng.Next(chars.Length)];
            }
            return new string(buffer);
        }

        /*
        private static void PadToMultipleOf(ref byte[] src, int pad)
        {
            int len = (src.Length + pad - 1) / pad * pad;
            Array.Resize(ref src, len);
        }

        internal static byte[] ProtectString(string s)
        {
            byte[] rawString = Encoding.UTF8.GetBytes(s);
            PadToMultipleOf(ref rawString, 16);
            ProtectedMemory.Protect(rawString, MemoryProtectionScope.SameProcess);
            return rawString;
        }

        internal static string UnprotectString(byte[] raw)
        {
            byte[] copy = new byte[raw.Length];
            Array.Copy(raw, copy, raw.Length);
            ProtectedMemory.Unprotect(copy, MemoryProtectionScope.SameProcess);
            return Encoding.UTF8.GetString(copy);
        }
        */

        /*
        internal static void EncryptToStream(byte[] data, string key, string IV, Stream stream)
        {
            using (AesCryptoServiceProvider symmetricKey = new AesCryptoServiceProvider())
            {

                // It is reasonable to set encryption mode to Cipher Block Chaining
                // (CBC). Use default options for other symmetric key parameters.
                symmetricKey.Mode = CipherMode.CBC;
                symmetricKey.Key = Encoding.ASCII.GetBytes(key);
                symmetricKey.IV = Encoding.ASCII.GetBytes(IV);

                // Define cryptographic stream (always use Write mode for encryption).
                using (CryptoStream cryptoStream = new CryptoStream(stream, symmetricKey.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    // Start encrypting.
                    cryptoStream.Write(data, 0, data.Length);

                    // Finish encrypting.
                    cryptoStream.FlushFinalBlock();
                }
            }
        }

        internal static byte[] DecryptFromStream(int nBytes, Stream stream, string key, string IV)
        {
            byte[] data = new byte[nBytes];
            using (AesCryptoServiceProvider symmetricKey = new AesCryptoServiceProvider())
            {

                // It is reasonable to set encryption mode to Cipher Block Chaining
                // (CBC). Use default options for other symmetric key parameters.
                symmetricKey.Mode = CipherMode.CBC;
                symmetricKey.Key = Encoding.ASCII.GetBytes(key);
                symmetricKey.IV = Encoding.ASCII.GetBytes(IV);

                // Define cryptographic stream (always use Write mode for encryption).
                using (CryptoStream cryptoStream = new CryptoStream(stream, symmetricKey.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    // Start encrypting.
                    cryptoStream.Read(data, 0, data.Length);
                }

                return data;
            }
        }
        */

        internal static T DeepClone<T>(T obj)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Context = new StreamingContext(StreamingContextStates.Clone);
                formatter.Serialize(ms, obj);
                ms.Flush();
                ms.Position = 0;

                return (T)formatter.Deserialize(ms);
            }
        }

        internal static bool ArrayContains<T>(T[] arr, T val)
        {
            for (int i = 0; i < arr.Length; ++i)
            {
                if (arr[i].Equals(val))
                    return true;
            }

            return false;
        }

        internal static void RunAtStartup(string ApplicationName, string ApplicationPath)
        {
            using (RegistryKey runKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.FullControl))
            {
                if (string.IsNullOrEmpty(ApplicationPath))
                    runKey.DeleteValue(ApplicationName, false);
                else
                    runKey.SetValue(ApplicationName, ApplicationPath);
            }
        }

        internal static Process StartProcess(string path, string args, bool asAdmin, bool hideWindow = false)
        {
            ProcessStartInfo psi = new ProcessStartInfo(path, args);
            psi.WorkingDirectory = Path.GetDirectoryName(path);
            if (asAdmin)
            {
                psi.Verb = "runas";
                psi.UseShellExecute = true;
            }
            if (hideWindow)
                psi.WindowStyle = ProcessWindowStyle.Hidden;

            return Process.Start(psi);
        }

        internal static bool RunningAsAdmin()
        {
            WindowsIdentity wi = WindowsIdentity.GetCurrent();
            WindowsPrincipal wp = new WindowsPrincipal(wi);
            return wp.IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static Bitmap ScaleImage(Bitmap originalImage, float scaleX, float scaleY)
        {
            int newWidth = (int)Math.Round(originalImage.Width * scaleX);
            int newHeight = (int)Math.Round(originalImage.Height * scaleY);

            Bitmap newImage = new Bitmap(originalImage, newWidth, newHeight);
            try
            {
                using (Graphics g = Graphics.FromImage(newImage))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(originalImage, 0, 0, newImage.Width, newImage.Height);
                }

                return newImage;
            }
            catch
            {
                newImage.Dispose();
                throw;
            }
        }

        internal static Bitmap ResizeImage(Bitmap originalImage, int maxWidth, int maxHeight)
        {
            int newWidth = originalImage.Width;
            int newHeight = originalImage.Height;
            double aspectRatio = (double)originalImage.Width / (double)originalImage.Height;

            if (aspectRatio <= 1 && originalImage.Width > maxWidth)
            {
                newWidth = maxWidth;
                newHeight = (int)Math.Round(newWidth / aspectRatio);
            }
            else if (aspectRatio > 1 && originalImage.Height > maxHeight)
            {
                newHeight = maxHeight;
                newWidth = (int)Math.Round(newHeight * aspectRatio);
            }

            Bitmap newImage = new Bitmap(originalImage, newWidth, newHeight);
            try
            {
                using (Graphics g = Graphics.FromImage(newImage))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(originalImage, 0, 0, newImage.Width, newImage.Height);
                }

                return newImage;
            }
            catch
            {
                newImage.Dispose();
                throw;
            }
        }

        internal static Bitmap GetIconContained(string filePath, int targetWidth, int targetHeight)
        {
            IconTools.ShellIconSize icnSize = IconTools.ShellIconSize.LargeIcon;
            if ((targetHeight == 16) && (targetWidth == 16))
                icnSize = IconTools.ShellIconSize.SmallIcon;

            using (Icon icon = IconTools.GetIconForExtension(filePath, icnSize))
            {
                if ((icon.Width == targetWidth) && (icon.Height == targetHeight))
                {
                    return icon.ToBitmap();
                }
                if ((icon.Height > targetHeight) || (icon.Width > targetWidth))
                {
                    using (Bitmap bmp = icon.ToBitmap())
                    {
                        return Utils.ResizeImage(bmp, targetWidth, targetHeight);
                    }
                }
                else
                {
                    float scale = Math.Min((float)targetWidth / icon.Width, (float)targetHeight / icon.Height);
                    using (Bitmap bmp = icon.ToBitmap())
                    {
                        return Utils.ScaleImage(bmp, (int)Math.Round(scale*icon.Width), (int)Math.Round(scale * icon.Height));
                    }
                }
            }
        }

        private static float? _DpiScalingFactor;
        internal static float DpiScalingFactor
        {
            get
            {
                if (!_DpiScalingFactor.HasValue)
                {
                    using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    {
                        float dpiX = graphics.DpiX;
                        float dpiY = graphics.DpiY;
                        _DpiScalingFactor = dpiX / 96.0f;
                    }
                }

                return _DpiScalingFactor.Value;
            }
        }

        internal static void CenterControlInParent(Control control)
        {
            Control parent = control.Parent;

            control.Location = new Point(
                parent.Width / 2 - control.Width / 2,
                parent.Height / 2 - control.Height / 2
                );
        }

        internal static void Invoke(Control ctrl, MethodInvoker method)
        {
            if (ctrl.InvokeRequired)
                ctrl.Invoke(method);
            else
                method();
        }

        internal static void Invoke(SynchronizationContext syncCtx, SendOrPostCallback method)
        {
            if (syncCtx != null)
                syncCtx.Send(method, null);
        }

        internal static void SplitFirstLine(string str, out string firstLine, out string restLines)
        {
            string[] lines = str.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);

            firstLine = lines[0];
            restLines = null;

            if (lines.Length > 1)
            {
                restLines = lines[1];
                for (int i = 2; i < lines.Length; ++i)
                    restLines += Environment.NewLine + lines[i];
            }
        }

        internal static DialogResult ShowMessageBox(string msg, string title, TaskDialogCommonButtons buttons, TaskDialogIcon icon, IWin32Window parent = null)
        {
            string firstLine, contentLines;
            Utils.SplitFirstLine(msg, out firstLine, out contentLines);

            TaskDialog taskDialog = new TaskDialog();
            taskDialog.WindowTitle = title;
            taskDialog.MainInstruction = firstLine;
            taskDialog.CommonButtons = buttons;
            taskDialog.MainIcon = icon;
            taskDialog.Content = contentLines;
            if (parent==null)
                return (DialogResult)taskDialog.Show();
            else
                return (DialogResult)taskDialog.Show(parent);
        }

        private static Random RndGenerator = null;
        internal static int GetRandomNumber()
        {
            if (RndGenerator == null)
                RndGenerator = new Random();
            return RndGenerator.Next(0, int.MaxValue);
        }

        internal static Version TinyWallVersion { get; } = typeof(Utils).Assembly.GetName().Version;

        private static object logLocker = new object();
        internal static readonly string LOG_ID_SERVICE = "service";
        internal static readonly string LOG_ID_CLIENT = "client";
        internal static void LogCrash(Exception e, string logname)
        {
            Utils.Log(
                string.Join(
                    Environment.NewLine, new string[] {
                    $"TinyWall version: {Utils.TinyWallVersion}",
                    $"Windows version: {TinyWall.Interface.VersionInfo.WindowsVersionString}",
                    e.ToString()
                }),
                logname
            );
        }
        internal static void Log(string info, string logname)
        {
            lock (logLocker)
            {
                // First, remove deprecated log files if any is found
                // TODO: This can probably be removed in the future
                string[] old_logs = new string[] {
                    Path.Combine(Utils.AppDataPath, "errorlog")
                };
                foreach (string file in old_logs) {
                    try {
                        if (File.Exists(file))
                            File.Delete(file);
                    }
                    catch { }
                }

                // Name of the current log file
                string logfile = Path.Combine(Utils.AppDataPath, $"{logname}.log");

                // Only log if log file has not yet reached a certain size
                if (File.Exists(logfile))
                {
                    FileInfo fi = new FileInfo(logfile);
                    if (fi.Length > 512 * 1024)
                    {
                        // Truncate file back to zero
                        using (var fs = new FileStream(logfile, FileMode.Truncate, FileAccess.Write)) { }
                    }
                }

                // Do the logging
                using (StreamWriter sw = new StreamWriter(logfile, true, Encoding.UTF8))
                {
                    sw.WriteLine();
                    sw.WriteLine("------- " + DateTime.Now.ToString(CultureInfo.InvariantCulture) + " -------");
                    sw.WriteLine(info);
                    sw.WriteLine();
                }
            }
        }

        internal static void SetDoubleBuffering(Control control, bool enable)
        {
            try
            {
                var doubleBufferPropertyInfo = control.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                doubleBufferPropertyInfo.SetValue(control, enable, null);
            }
            catch { }
        }

        internal static void FlushDnsCache()
        {
            SafeNativeMethods.DnsFlushResolverCache();
        }

        internal static string AppDataPath
        {
            get
            {
#if DEBUG
                return Path.GetDirectoryName(TinyWall.Interface.Internal.Utils.ExecutablePath);
#else
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinyWall");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
#endif
            }
        }

        public static bool EqualsCaseInsensitive(string a, string b)
        {
            if (a == b)
                return true;

            return (a != null) && a.Equals(b, StringComparison.InvariantCultureIgnoreCase);
        }
    }

    public sealed class DevicePathMapper : IDisposable
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity]
        private static extern uint QueryDosDevice([In] string lpDeviceName, [Out] StringBuilder lpTargetPath, [In] int ucchMax);

        public struct DriveCache
        {
            public string DriverLetter;
            public string DevicePath;
        }

        private StringBuilder sbuilder = new StringBuilder(260);
        public DriveCache[] Cache { get; private set; } = BuildDriveCache();
        private ManagementEventWatcher DriveWatcher;

        public DevicePathMapper()
        {
            try
            {
                WqlEventQuery insertQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_LogicalDisk'");
                DriveWatcher = new ManagementEventWatcher(insertQuery);
                DriveWatcher.EventArrived += Watcher_EventArrived;
                DriveWatcher.Start();
            }
            catch { }
        }

        private static DriveCache[] BuildDriveCache()
        {
            var di = DriveInfo.GetDrives();
            var cache = new DriveCache[di.Length];
            for (int i = 0; i < cache.Length; ++i)
            {
                cache[i].DriverLetter = GetDriveLetter(di[i]);
                cache[i].DevicePath = GetDevicePath(di[i]);
            }
            return cache;
        }

        private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            RebuildCache();
        }

        public void RebuildCache()
        {
            Cache = BuildDriveCache();
        }

        public string FromNtPath(string devicePath)
        {
            var drives = Cache;

            foreach (var drive in drives)
            {
                if (devicePath.StartsWith(drive.DevicePath, StringComparison.InvariantCultureIgnoreCase))
                    return ReplaceFirst(devicePath, drive.DevicePath, drive.DriverLetter, sbuilder);
            }
            return devicePath;
        }

        private static string GetDevicePath(DriveInfo driveInfo)
        {
            var devicePathBuilder = new StringBuilder(128);
            string ret = QueryDosDevice(GetDriveLetter(driveInfo), devicePathBuilder, devicePathBuilder.Capacity + 1) != 0
                ? devicePathBuilder.ToString()
                : null;
            return ret;
        }

        private static string GetDriveLetter(DriveInfo driveInfo)
        {
            return driveInfo.Name.Substring(0, 2);
        }

        private static string ReplaceFirst(string text, string search, string replace, StringBuilder sb)
        {
            int pos = text.IndexOf(search, StringComparison.InvariantCultureIgnoreCase);
            if (pos < 0)
                return text;
            else
            {
                //return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
                int tmp = pos + search.Length;
                sb.Length = 0;
                sb.Append(text, 0, pos);
                sb.Append(replace);
                sb.Append(text, tmp, text.Length - tmp);
                return sb.ToString();
            }
        }

        public void Dispose()
        {
            DriveWatcher?.Dispose();
        }
    }
}
