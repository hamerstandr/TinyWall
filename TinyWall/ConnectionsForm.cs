﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using pylorak.Windows;
using pylorak.Windows.NetStat;

namespace pylorak.TinyWall
{
    internal partial class ConnectionsForm : Form
    {
        private readonly TinyWallController Controller;
        private readonly AsyncIconScanner IconScanner;
        private readonly Size IconSize = new((int)Math.Round(16 * Utils.DpiScalingFactor), (int)Math.Round(16 * Utils.DpiScalingFactor));
        private bool EnableListUpdate = false;

        internal ConnectionsForm(TinyWallController ctrl)
        {
            InitializeComponent();
            Utils.SetRightToLeft(this);
            this.IconList.ImageSize = IconSize;
            this.Icon = Resources.Icons.firewall;
            this.Controller = ctrl;

            const string TEMP_ICON_KEY = "generic-executable";
            this.IconList.Images.Add(TEMP_ICON_KEY, Utils.GetIconContained(".exe", IconSize.Width, IconSize.Height));
            this.IconList.Images.Add("store", Resources.Icons.store);
            this.IconList.Images.Add("system", Resources.Icons.windows_small);
            this.IconList.Images.Add("network-drive", Resources.Icons.network_drive_small);
            this.IconScanner = new AsyncIconScanner((ListViewItem li) => { return (li.Tag as ProcessInfo)!.Path; }, IconList.Images.IndexOfKey(TEMP_ICON_KEY));
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private static string GetPathFromPidCached(Dictionary<uint, string> cache, uint pid)
        {
            if (cache.TryGetValue(pid, out string path))
                return path;
            else
            {
                string ret = Utils.GetPathOfProcessUseTwService(pid, GlobalInstances.Controller);
                cache.Add(pid, ret);
                return ret;
            }
        }

        private void UpdateList()
        {
            if (!EnableListUpdate)
            {
                return;
            }

            IconScanner.CancelScan();
            var fwLogRequest = GlobalInstances.Controller.BeginReadFwLog();

            var uwpPackages = new UwpPackage();
            var itemColl = new List<ListViewItem>();
            var procCache = new Dictionary<uint, string>();
            var servicePids = new ServicePidMap();

            // Retrieve IP tables while waiting for log entries

            DateTime now = DateTime.Now;
            TcpTable tcpTable = NetStat.GetExtendedTcp4Table(false);
            foreach (TcpRow tcpRow in tcpTable)
            {
                if ((chkShowListen.Checked && (tcpRow.State == TcpState.Listen))
                  || (chkShowActive.Checked && (tcpRow.State != TcpState.Listen)))
                {
                    var path = GetPathFromPidCached(procCache, tcpRow.ProcessId);
                    var pi = ProcessInfo.Create(tcpRow.ProcessId, path, uwpPackages, servicePids);
                    ConstructListItem(itemColl, pi, "TCP", tcpRow.LocalEndPoint, tcpRow.RemoteEndPoint, tcpRow.State.ToString(), now, RuleDirection.Invalid);
                }
            }
            tcpTable = NetStat.GetExtendedTcp6Table(false);
            foreach (TcpRow tcpRow in tcpTable)
            {
                if ((chkShowListen.Checked && (tcpRow.State == TcpState.Listen))
                 || (chkShowActive.Checked && (tcpRow.State != TcpState.Listen)))
                {
                    var path = GetPathFromPidCached(procCache, tcpRow.ProcessId);
                    var pi = ProcessInfo.Create(tcpRow.ProcessId, path, uwpPackages, servicePids);
                    ConstructListItem(itemColl, pi, "TCP", tcpRow.LocalEndPoint, tcpRow.RemoteEndPoint, tcpRow.State.ToString(), now, RuleDirection.Invalid);
                }
            }

            if (chkShowListen.Checked)
            {
                var dummyEP = new IPEndPoint(0, 0);
                var udpTable = NetStat.GetExtendedUdp4Table(false);
                foreach (UdpRow udpRow in udpTable)
                {
                    var path = GetPathFromPidCached(procCache, udpRow.ProcessId);
                    var pi = ProcessInfo.Create(udpRow.ProcessId, path, uwpPackages, servicePids);
                    ConstructListItem(itemColl, pi, "UDP", udpRow.LocalEndPoint, dummyEP, "Listen", now, RuleDirection.Invalid);
                }
                udpTable = NetStat.GetExtendedUdp6Table(false);
                foreach (UdpRow udpRow in udpTable)
                {
                    var path = GetPathFromPidCached(procCache, udpRow.ProcessId);
                    var pi = ProcessInfo.Create(udpRow.ProcessId, path, uwpPackages, servicePids);
                    ConstructListItem(itemColl, pi, "UDP", udpRow.LocalEndPoint, dummyEP, "Listen", now, RuleDirection.Invalid);
                }
            }

            // Finished reading tables, continues with log processing
            var fwLog = pylorak.TinyWall.Controller.EndReadFwLog(fwLogRequest.Response);

            // Show log entries if requested by user
            if (chkShowBlocked.Checked)
            {
                // Try to resolve PIDs heuristically
                var ProcessPathInfoMap = new Dictionary<string, List<ProcessSnapshotEntry>>();
                foreach (var p in ProcessManager.CreateToolhelp32SnapshotExtended())
                {
                    if (string.IsNullOrEmpty(p.ImagePath))
                        continue;

                    var key = p.ImagePath.ToLowerInvariant();
                    if (!ProcessPathInfoMap.ContainsKey(key))
                        ProcessPathInfoMap.Add(key, new List<ProcessSnapshotEntry>());
                    ProcessPathInfoMap[key].Add(p);
                }

                foreach (var e in fwLog)
                {
                    if (e.AppPath is null) continue;

                    var key = e.AppPath.ToLowerInvariant();
                    if (!ProcessPathInfoMap.ContainsKey(key))
                        continue;

                    var p = ProcessPathInfoMap[key];
                    if ((p.Count == 1) && (p[0].CreationTime < e.Timestamp.ToFileTime()))
                        e.ProcessId = p[0].ProcessId;
                }

                var filteredLog = new List<FirewallLogEntry>();
                TimeSpan refSpan = TimeSpan.FromMinutes(5);
                for (int i = 0; i < fwLog.Length; ++i)
                {
                    FirewallLogEntry newEntry = fwLog[i];

                    // Ignore log entries older than refSpan
                    TimeSpan span = now - newEntry.Timestamp;
                    if (span > refSpan)
                        continue;

                    switch (newEntry.Event)
                    {
                        case EventLogEvent.ALLOWED_LISTEN:
                        case EventLogEvent.ALLOWED_CONNECTION:
                        case EventLogEvent.ALLOWED_LOCAL_BIND:
                        case EventLogEvent.ALLOWED:
                            {
                                newEntry.Event = EventLogEvent.ALLOWED;
                                break;
                            }
                        case EventLogEvent.BLOCKED_LISTEN:
                        case EventLogEvent.BLOCKED_CONNECTION:
                        case EventLogEvent.BLOCKED_LOCAL_BIND:
                        case EventLogEvent.BLOCKED_PACKET:
                        case EventLogEvent.BLOCKED:
                            {
                                bool matchFound = false;
                                newEntry.Event = EventLogEvent.BLOCKED;

                                for (int j = 0; j < filteredLog.Count; ++j)
                                {
                                    FirewallLogEntry oldEntry = filteredLog[j];
                                    if (oldEntry.Equals(newEntry, false))
                                    {
                                        matchFound = true;
                                        oldEntry.Timestamp = newEntry.Timestamp;
                                        break;
                                    }
                                }

                                if (!matchFound)
                                    filteredLog.Add(newEntry);
                                break;
                            }
                    }
                }

                for (int i = 0; i < filteredLog.Count; ++i)
                {
                    FirewallLogEntry entry = filteredLog[i];

                    // Correct path capitalization
                    // TODO: Do this in the service, and minimize overhead. Right now if GetExactPath() fails,
                    // for example due to missing file system privileges, capitalization will not be corrected.
                    // The service has much more privileges, so doing this in the service would allow more paths
                    // to be corrected.
                    entry.AppPath = Utils.GetExactPath(entry.AppPath);

                    var pi = ProcessInfo.Create(entry.ProcessId, entry.AppPath ?? string.Empty, entry.PackageId, uwpPackages, servicePids);
                    ConstructListItem(itemColl, pi, entry.Protocol.ToString(), new IPEndPoint(IPAddress.Parse(entry.LocalIp), entry.LocalPort), new IPEndPoint(IPAddress.Parse(entry.RemoteIp), entry.RemotePort), "Blocked", entry.Timestamp, entry.Direction);
                }
            }

            // Add items to list
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(itemColl.ToArray());
            list.EndUpdate();

            // Load process icons asynchronously
            IconScanner.Rescan(itemColl, list, IconList);
        }

        private void ConstructListItem(List<ListViewItem> itemColl, ProcessInfo e, string protocol, IPEndPoint localEP, IPEndPoint remoteEP, string state, DateTime ts, RuleDirection dir)
        {
            try
            {
                // Construct list item
                string name = e.Package.HasValue ? e.Package.Value.Name : System.IO.Path.GetFileName(e.Path);
                string title = (e.Pid != 0) ? $"{name} ({e.Pid})" : $"{name}";
                ListViewItem li = new(title);
                li.Tag = e;
                li.ToolTipText = e.Path;

                // Add icon
                if (e.Package.HasValue)
                {
                    li.ImageIndex = IconList.Images.IndexOfKey("store");
                }
                else if (e.Path == "System")
                {
                    li.ImageIndex = IconList.Images.IndexOfKey("system");
                }
                else if (NetworkPath.IsNetworkPath(e.Path))
                {
                    li.ImageIndex = IconList.Images.IndexOfKey("network-drive");
                }
                else
                {
                    li.ImageIndex = IconList.Images.ContainsKey(e.Path) ? IconList.Images.IndexOfKey(e.Path) : IconScanner.TemporaryIconIdx;
                }

                if (e.Pid == 0)
                    li.SubItems.Add(string.Empty);
                else
                    li.SubItems.Add(string.Join(", ", e.Services.ToArray()));
                li.SubItems.Add(protocol);
                li.SubItems.Add(localEP.Port.ToString(CultureInfo.InvariantCulture).PadLeft(5));
                li.SubItems.Add(localEP.Address.ToString());
                li.SubItems.Add(remoteEP.Port.ToString(CultureInfo.InvariantCulture).PadLeft(5));
                li.SubItems.Add(remoteEP.Address.ToString());
                li.SubItems.Add(state);
                switch (dir)
                {
                    case RuleDirection.In:
                        li.SubItems.Add(Resources.Messages.TrafficIn);
                        break;
                    case RuleDirection.Out:
                        li.SubItems.Add(Resources.Messages.TrafficOut);
                        break;
                    default:
                        li.SubItems.Add(string.Empty);
                        break;
                }
                li.SubItems.Add(ts.ToString("yyyy/MM/dd HH:mm:ss"));
                itemColl.Add(li);
            }
            catch
            {
                // Most probably process ID has become invalid,
                // but we also catch other errors too.
                // Simply do not add item to the list.
                return;
            }
        }

        private void list_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            var oldSorter = (ListViewItemComparer)list.ListViewItemSorter;
            var newSorter = new ListViewItemComparer(e.Column);
            if ((oldSorter != null) && (oldSorter.Column == newSorter.Column))
                newSorter.Ascending = !oldSorter.Ascending;

            list.ListViewItemSorter = newSorter;
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void chkShowListen_CheckedChanged(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void chkShowBlocked_CheckedChanged(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void chkShowActive_CheckedChanged(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void ConnectionsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ActiveConfig.Controller.ConnFormWindowState = this.WindowState;
            if (this.WindowState == FormWindowState.Normal)
            {
                ActiveConfig.Controller.ConnFormWindowSize = this.Size;
                ActiveConfig.Controller.ConnFormWindowLoc = this.Location;
            }
            else
            {
                ActiveConfig.Controller.ConnFormWindowSize = this.RestoreBounds.Size;
                ActiveConfig.Controller.ConnFormWindowLoc = this.RestoreBounds.Location;
            }

            ActiveConfig.Controller.ConnFormShowConnections = this.chkShowActive.Checked;
            ActiveConfig.Controller.ConnFormShowOpenPorts = this.chkShowListen.Checked;
            ActiveConfig.Controller.ConnFormShowBlocked = this.chkShowBlocked.Checked;

            ActiveConfig.Controller.ConnFormColumnWidths.Clear();
            foreach (ColumnHeader col in list.Columns)
                ActiveConfig.Controller.ConnFormColumnWidths.Add((string)col.Tag, col.Width);

            ActiveConfig.Controller.Save();
            IconScanner.Dispose();
        }

        private void ConnectionsForm_Load(object sender, EventArgs e)
        {
            Utils.SetDoubleBuffering(list, true);
            list.ListViewItemSorter = new ListViewItemComparer(9, null, false);
            if (ActiveConfig.Controller.ConnFormWindowSize.Width != 0)
                this.Size = ActiveConfig.Controller.ConnFormWindowSize;
            if (ActiveConfig.Controller.ConnFormWindowLoc.X != 0)
            {
                this.Location = ActiveConfig.Controller.ConnFormWindowLoc;
                Utils.FixupFormPosition(this);
            }
            this.WindowState = ActiveConfig.Controller.ConnFormWindowState;
            this.chkShowActive.Checked = ActiveConfig.Controller.ConnFormShowConnections;
            this.chkShowListen.Checked = ActiveConfig.Controller.ConnFormShowOpenPorts;
            this.chkShowBlocked.Checked = ActiveConfig.Controller.ConnFormShowBlocked;

            foreach (ColumnHeader col in list.Columns)
            {
                if (ActiveConfig.Controller.ConnFormColumnWidths.TryGetValue((string)col.Tag, out int width))
                    col.Width = width;
            }

            EnableListUpdate = true;
            UpdateList();
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (list.SelectedIndices.Count < 1)
                e.Cancel = true;

            // Don't allow Kill if we don't have a PID
            bool hasPid = true;
            foreach (ListViewItem li in list.SelectedItems)
            {
                hasPid &= ((ProcessInfo)li.Tag).Pid != 0;
            }
            mnuCloseProcess.Enabled = hasPid;
        }

        private void mnuCloseProcess_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem li in list.SelectedItems)
            {
                var pi = (ProcessInfo)li.Tag;

                try
                {
                    using Process proc = Process.GetProcessById(unchecked((int)pi.Pid));
                    try
                    {
                        if (!proc.CloseMainWindow())
                            proc.Kill();

                        if (!proc.WaitForExit(5000))
                            throw new ApplicationException();
                        else
                            UpdateList();
                    }
                    catch (InvalidOperationException)
                    {
                        // The process has already exited. Fine, that's just what we want :)
                    }
                    catch
                    {
                        MessageBox.Show(this, string.Format(CultureInfo.CurrentCulture, Resources.Messages.CouldNotCloseProcess, proc.ProcessName, pi.Pid), Resources.Messages.TinyWall, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                }
                catch
                {
                    // The app has probably already quit. Leave it at that.
                }
            }
        }

        private void mnuUnblock_Click(object sender, EventArgs e)
        {
            if (!Controller.EnsureUnlockedServer())
                return;

            var selection = new List<ProcessInfo>();
            foreach (ListViewItem li in list.SelectedItems)
            {
                selection.Add((ProcessInfo)li.Tag);
            }
            Controller.WhitelistProcesses(selection);
        }

        private void mnuCopyRemoteAddress_Click(object sender, EventArgs e)
        {
            ListViewItem li = list.SelectedItems[0];
            string clipboardData = li.SubItems[6].Text;

            var dataObject = new DataObject();
            dataObject.SetData(DataFormats.UnicodeText, false, clipboardData);
            try
            {
                Clipboard.SetDataObject(dataObject, true, 20, 100);
            }
            catch
            {
                // Fail silently :(
            }
        }

        private void mnuVirusTotal_Click(object sender, EventArgs e)
        {
            try
            {
                ListViewItem li = list.SelectedItems[0];

                const string urlTemplate = @"https://www.virustotal.com/latest-scan/{0}";
                string hash = Hasher.HashFile(((ProcessInfo)li.Tag).Path);
                string url = string.Format(CultureInfo.InvariantCulture, urlTemplate, hash);
                Utils.StartProcess(url, string.Empty, false);
            }
            catch
            {
                MessageBox.Show(this, Resources.Messages.CannotGetPathOfProcess, Resources.Messages.TinyWall, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

            }
        }

        private void mnuProcessLibrary_Click(object sender, EventArgs e)
        {
            try
            {
                ListViewItem li = list.SelectedItems[0];

                const string urlTemplate = @"http://www.processlibrary.com/search/?q={0}";
                string filename = System.IO.Path.GetFileName(((ProcessInfo)li.Tag).Path);
                string url = string.Format(CultureInfo.InvariantCulture, urlTemplate, filename);
                Utils.StartProcess(url, string.Empty, false);
            }
            catch
            {
                MessageBox.Show(this, Resources.Messages.CannotGetPathOfProcess, Resources.Messages.TinyWall, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }
        private void mnuFileNameOnTheWeb_Click(object sender, EventArgs e)
        {
            try
            {
                ListViewItem li = list.SelectedItems[0];

                const string urlTemplate = @"www.google.com/search?q={0}";
                string filename = System.IO.Path.GetFileName(((ProcessInfo)li.Tag).Path);
                string url = string.Format(CultureInfo.InvariantCulture, urlTemplate, filename);
                Utils.StartProcess(url, string.Empty, false);
            }
            catch
            {
                MessageBox.Show(this, Resources.Messages.CannotGetPathOfProcess, Resources.Messages.TinyWall, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void mnuRemoteAddressOnTheWeb_Click(object sender, EventArgs e)
        {
            try
            {
                ListViewItem li = list.SelectedItems[0];

                const string urlTemplate = @"www.google.com/search?q={0}";
                string address = li.SubItems[6].Text;
                string url = string.Format(CultureInfo.InvariantCulture, urlTemplate, address);
                Utils.StartProcess(url, string.Empty, false);
            }
            catch
            {
                MessageBox.Show(this, Resources.Messages.CannotGetPathOfProcess, Resources.Messages.TinyWall, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void ConnectionsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.F5)
            {
                btnRefresh_Click(btnRefresh, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }
}
