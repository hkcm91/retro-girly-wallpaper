using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace PetalOS
{
    static class Program
    {
        internal static string DataDirectory;
        internal static bool Smoke;
        [STAThread]
        static void Main(string[] args)
        {
            Smoke = args.Contains("--smoke-test");
            int index = Array.IndexOf(args, "--data-dir");
            DataDirectory = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetalOS");
            Directory.CreateDirectory(DataDirectory);
            bool created;
            using (var mutex = new Mutex(true, "Local\\PetalOS.Desktop." + (Smoke ? "Test" : "App"), out created))
            {
                if (!created) { try { using (var signal = EventWaitHandle.OpenExisting("Local\\PetalOS.ShowEditor")) signal.Set(); } catch { } return; }
                try
                {
                    try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { Desktop.SetProcessDPIAware(); }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (s, e) => Log(e.Exception.ToString());
                    Application.Run(new Host());
                }
                catch (Exception error)
                {
                    Log(error.ToString());
                    if (!Smoke) MessageBox.Show("Petal OS could not start. " + error.Message + "\n\nDetails: " + DataDirectory, "Petal OS", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.ExitCode = 1;
                }
            }
        }
        internal static void Log(string message) { try { File.AppendAllText(Path.Combine(DataDirectory, "petal.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch { } }
    }

    sealed class Host : ApplicationContext
    {
        internal readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        internal CoreWebView2Environment Environment;
        internal Editor Editor;
        readonly List<Wallpaper> wallpapers = new List<Wallpaper>();
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 300 };
        readonly System.Windows.Forms.Timer healthTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly EventWaitHandle showSignal;
        readonly RegisteredWaitHandle showWait;
        Dictionary<string, object> state;
        internal string StateJson { get { return state == null ? "null" : Json.Serialize(state); } }
        string statePath { get { return Path.Combine(Program.DataDirectory, "settings.json"); } }
        internal bool Exiting;
        bool busy;
        bool wanted;
        bool recovering;
        internal Host()
        {
            try { if (File.Exists(statePath)) state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath)); } catch (Exception error) { Program.Log("Settings load: " + error.Message); }
            Editor = new Editor(this);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Petal OS", null, (s, e) => ShowEditor());
            menu.Items.Add("Apply wallpaper", null, async (s, e) => await Apply());
            menu.Items.Add("Stop wallpaper", null, (s, e) => Stop());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit Petal OS", null, (s, e) => Shutdown());
            tray = new NotifyIcon { Icon = Editor.Icon, Text = "Petal OS · your little desktop", ContextMenuStrip = menu, Visible = !Program.Smoke };
            tray.DoubleClick += (s, e) => ShowEditor();
            saveTimer.Tick += (s, e) => Flush();
            healthTimer.Tick += async (s, e) =>
            {
                if (wanted && !busy && !recovering && wallpapers.Any(w => !Desktop.IsWindow(w.Handle) || !Desktop.IsWindow(w.DesktopParent)))
                { recovering = true; try { await Apply(); } finally { recovering = false; } }
            };
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PetalOS.ShowEditor");
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (s, t) => { if (!Exiting && Editor.IsHandleCreated) Editor.BeginInvoke(new Action(ShowEditor)); }, null, Timeout.Infinite, false);
            Editor.Shown += async (s, e) => await Initialize();
            Editor.Show();
        }
        async Task Initialize()
        {
            try
            {
                Environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDirectory, "WebView2"));
                await Editor.StartRenderer(Environment);
                Editor.Status("Ready · edit here, then Apply wallpaper");
                if (Program.Smoke) await SmokeTest();
            }
            catch (Exception error)
            {
                Program.Log("Startup: " + error);
                Editor.Status("Could not start renderer: " + error.Message);
                if (Program.Smoke) { File.WriteAllText(Path.Combine(Program.DataDirectory, "native-smoke.json"), Json.Serialize(new { passed = false, error = error.ToString() })); Shutdown(); }
                else MessageBox.Show(Editor, "Petal OS uses Microsoft Edge WebView2 to draw the wallpaper. If its runtime is missing, install it from https://developer.microsoft.com/microsoft-edge/webview2/ and reopen Petal OS.\n\n" + error.Message, "Petal OS renderer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        void DisplayChanged(object sender, EventArgs e)
        {
            if (!Exiting && Editor.IsHandleCreated) Editor.BeginInvoke(new Action(async () => { Editor.RefreshDisplays(); if (wanted) await Apply(); }));
        }
        internal void ShowEditor() { if (Exiting) return; Editor.Show(); Editor.WindowState = FormWindowState.Normal; Editor.Activate(); }
        internal void Receive(string raw)
        {
            if (raw.Length > 4 * 1024 * 1024) return;
            var message = Json.Deserialize<Dictionary<string, object>>(raw);
            object type, value;
            if (!message.TryGetValue("type", out type) || Convert.ToString(type) != "settings" || !message.TryGetValue("state", out value)) return;
            var incoming = value as Dictionary<string, object>;
            if (incoming == null || !incoming.ContainsKey("theme") || !incoming.ContainsKey("notes")) return;
            state = incoming;
            saveTimer.Stop(); saveTimer.Start();
            foreach (var wallpaper in wallpapers.ToArray()) wallpaper.PushState(StateJson);
        }
        internal void Flush()
        {
            saveTimer.Stop(); if (state == null) return;
            try
            {
                string temp = statePath + ".tmp"; File.WriteAllText(temp, StateJson);
                if (File.Exists(statePath)) File.Replace(temp, statePath, null); else File.Move(temp, statePath);
            }
            catch (Exception error) { Program.Log("Save: " + error.Message); Editor.Status("Could not save settings. Use Export settings for a backup."); }
        }
        internal async Task Apply()
        {
            if (busy || Exiting || Environment == null) return;
            busy = true;
            try
            {
                StopWindows();
                IntPtr parent = Desktop.FindWallpaperLayer();
                if (parent == IntPtr.Zero) throw new InvalidOperationException("Windows did not expose its wallpaper layer. Your original desktop is unchanged. Try again after signing out and in.");
                var screens = Editor.SelectedScreens;
                foreach (var screen in screens)
                {
                    var wallpaper = new Wallpaper(this, parent, screen.Bounds);
                    wallpapers.Add(wallpaper);
                    wallpaper.Show();
                    wallpaper.Attach();
                    await wallpaper.StartRenderer(Environment);
                }
                wanted = true; healthTimer.Start(); Editor.Status("Wallpaper running · " + wallpapers.Count + " display(s) · close this window to keep it in the tray");
            }
            catch (Exception error)
            {
                wanted = false; StopWindows(); Program.Log("Apply: " + error);
                Editor.Status("Wallpaper stopped · " + error.Message);
                if (Program.Smoke) throw;
            }
            finally { busy = false; }
        }
        void StopWindows() { foreach (var window in wallpapers.ToArray()) { try { window.Close(); window.Dispose(); } catch (Exception error) { Program.Log("Close: " + error.Message); } } wallpapers.Clear(); }
        internal void Stop() { wanted = false; healthTimer.Stop(); StopWindows(); Editor.Status("Wallpaper stopped · Windows shows your usual background"); }
        internal void Shutdown()
        {
            if (Exiting) return; Exiting = true;
            Stop(); Flush(); SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            saveTimer.Dispose(); healthTimer.Dispose(); tray.Visible = false; tray.Dispose();
            showWait.Unregister(null); showSignal.Dispose(); Editor.Close(); Editor.Dispose(); ExitThread();
        }
        async Task SmokeTest()
        {
            var checks = new List<string>();
            try
            {
                if (await Editor.Browser.ExecuteScriptAsync("document.querySelectorAll('.zone').length") != "4") throw new Exception("Editor did not render four zones");
                checks.Add("Own EXE initializes WebView2 and renders four zones");
                await Apply();
                if (wallpapers.Count == 0 || wallpapers.Any(w => Desktop.GetParent(w.Handle) != w.DesktopParent)) throw new Exception("Wallpaper was not attached to the desktop layer");
                checks.Add("Wallpaper HWND is parented to Explorer WorkerW behind desktop icons");
                if (wallpapers.Any(w => !Desktop.IsWindowVisible(w.Handle))) throw new Exception("Wallpaper window is not visible");
                checks.Add("Native wallpaper window is visible");
                if (await wallpapers[0].Browser.ExecuteScriptAsync("document.documentElement.scrollHeight <= innerHeight && document.documentElement.scrollWidth <= innerWidth") != "true") throw new Exception("Wallpaper exceeds the screen viewport");
                checks.Add("Native wallpaper fits the display without scrollbars");
                using (var image = File.Create(Path.Combine(Program.DataDirectory, "native-wallpaper.png")))
                    await wallpapers[0].Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                await Editor.Browser.ExecuteScriptAsync("document.getElementById('label4').value='native state verification';document.getElementById('label4').dispatchEvent(new Event('input',{bubbles:true}));");
                await Task.Delay(800);
                foreach (var wallpaper in wallpapers)
                    if (await wallpaper.Browser.ExecuteScriptAsync("document.getElementById('zone4-label').textContent") != "\"native state verification\"") throw new Exception("Editor-to-wallpaper live sync failed");
                checks.Add("Editor changes synchronize live to native wallpaper renderer");
                Flush();
                if (!File.ReadAllText(statePath).Contains("native state verification")) throw new Exception("Native settings file was not saved");
                checks.Add("Native settings persisted to disk");
                await Editor.Browser.ExecuteScriptAsync("document.getElementById('reset').click()");
                await Task.Delay(400); Flush();
                IntPtr[] handles = wallpapers.Select(w => w.Handle).ToArray(); Stop();
                if (handles.Any(Desktop.IsWindow)) throw new Exception("Wallpaper window survived Stop");
                checks.Add("Stop removes wallpaper HWNDs and restores the normal desktop");
                File.WriteAllText(Path.Combine(Program.DataDirectory, "native-smoke.json"), Json.Serialize(new { passed = true, checks = checks, os = System.Environment.OSVersion.ToString(), runtime = CoreWebView2Environment.GetAvailableBrowserVersionString() }));
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(Program.DataDirectory, "native-smoke.json"), Json.Serialize(new { passed = false, checks = checks, error = error.ToString() })); Program.Log("Smoke: " + error); System.Environment.ExitCode = 1; }
            finally { Shutdown(); }
        }
    }

    class RendererForm : Form
    {
        internal readonly WebView2 Browser;
        protected readonly Host Host;
        readonly bool wallpaperMode;
        internal RendererForm(Host host, bool wallpaper)
        {
            Host = host; wallpaperMode = wallpaper;
            Browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(246, 214, 224) };
            Controls.Add(Browser);
        }
        internal async Task StartRenderer(CoreWebView2Environment environment)
        {
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            string web = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
            if (!File.Exists(Path.Combine(web, "index.html"))) throw new FileNotFoundException("Keep the web folder beside PetalOS.exe.");
            core.SetVirtualHostNameToFolderMapping("petal.local", web, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (s, e) => { if (!e.Uri.StartsWith("https://petal.local/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                if (!e.IsUserInitiated) return;
                string target = e.Uri.StartsWith("https://petal.local/README.html", StringComparison.OrdinalIgnoreCase) ? Path.Combine(web, "README.html") : e.Uri;
                if (target.EndsWith("README.html") || e.Uri.StartsWith("https://") || e.Uri.StartsWith("http://")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            };
            core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (s, e) => { if (wallpaperMode) e.Cancel = true; };
            core.WebMessageReceived += (s, e) =>
            {
                if (wallpaperMode || !e.Source.StartsWith("https://petal.local/", StringComparison.OrdinalIgnoreCase)) return;
                try { Host.Receive(e.WebMessageAsJson); } catch (Exception error) { Program.Log("Message: " + error.Message); }
            };
            var loaded = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (s, e) => { core.NavigationCompleted -= handler; if (e.IsSuccess) loaded.TrySetResult(true); else loaded.TrySetException(new Exception("Page load: " + e.WebErrorStatus)); };
            core.NavigationCompleted += handler;
            core.Navigate("https://petal.local/index.html?host=petal&mode=" + (wallpaperMode ? "wallpaper" : "editor"));
            if (await Task.WhenAny(loaded.Task, Task.Delay(20000)) != loaded.Task) throw new TimeoutException("Renderer did not finish loading within 20 seconds.");
            await loaded.Task;
            await Browser.ExecuteScriptAsync("window.petalHostLoad(" + Host.StateJson + ")");
        }
        internal async void PushState(string json)
        {
            if (IsDisposed || Browser.CoreWebView2 == null) return;
            try { await Browser.ExecuteScriptAsync("window.petalHostLoad(" + json + ")"); } catch (Exception error) { Program.Log("Live update: " + error.Message); }
        }
    }
    sealed class Editor : RendererForm
    {
        readonly Label status;
        readonly ComboBox displays;
        internal Editor(Host host) : base(host, false)
        {
            Text = "Petal OS · Desktop Studio"; Width = 1150; Height = 850; MinimumSize = new Size(750, 620); StartPosition = FormStartPosition.CenterScreen;
            try { Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "icons", "heart.ico")); } catch { Icon = SystemIcons.Application; }
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(10), BackColor = Color.FromArgb(242, 182, 202), WrapContents = false };
            var apply = new Button { Text = "Apply wallpaper", AutoSize = true, Height = 30 };
            apply.Click += async (s, e) => await Host.Apply();
            var stop = new Button { Text = "Stop wallpaper", AutoSize = true, Height = 30 }; stop.Click += (s, e) => Host.Stop();
            var personalise = new Button { Text = "Personalise", AutoSize = true, Height = 30 }; personalise.Click += async (s, e) => { if (Browser.CoreWebView2 != null) await Browser.ExecuteScriptAsync("document.getElementById('settings').showModal()"); };
            displays = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Height = 30 };
            RefreshDisplays();
            var exit = new Button { Text = "Exit Petal OS", AutoSize = true, Height = 30 }; exit.Click += (s, e) => Host.Shutdown();
            toolbar.Controls.AddRange(new Control[] { apply, stop, personalise, displays, exit });
            status = new Label { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(10, 8, 0, 0), Text = "Starting Petal OS…", BackColor = Color.FromArgb(255, 247, 246) };
            Controls.Add(toolbar); Controls.Add(status); toolbar.BringToFront(); status.BringToFront();
            FormClosing += (s, e) => { if (!Host.Exiting) { e.Cancel = true; Hide(); } };
            if (Program.Smoke) { Opacity = 0; ShowInTaskbar = false; }
        }
        internal void RefreshDisplays()
        {
            string previous = Convert.ToString(displays.SelectedItem); displays.Items.Clear();
            foreach (var screen in Screen.AllScreens) displays.Items.Add(screen.DeviceName + (screen.Primary ? " (primary)" : ""));
            displays.Items.Add("All displays");
            int prior = displays.Items.IndexOf(previous);
            displays.SelectedIndex = prior >= 0 ? prior : Array.FindIndex(Screen.AllScreens, s => s.Primary);
            if (displays.SelectedIndex < 0) displays.SelectedIndex = 0;
        }
        internal Screen[] SelectedScreens { get { return displays.SelectedIndex >= Screen.AllScreens.Length ? Screen.AllScreens : new[] { Screen.AllScreens[Math.Max(0, displays.SelectedIndex)] }; } }
        internal void Status(string message) { status.Text = message; }
    }
    sealed class Wallpaper : RendererForm
    {
        internal IntPtr DesktopParent;
        readonly Rectangle screen;
        internal Wallpaper(Host host, IntPtr parent, Rectangle bounds) : base(host, true)
        {
            DesktopParent = parent; screen = bounds;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Bounds = bounds;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
        internal void Attach()
        {
            int style = Desktop.GetWindowLong(Handle, -16); Desktop.SetWindowLong(Handle, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
            Desktop.SetParent(Handle, DesktopParent);
            if (Desktop.GetParent(Handle) != DesktopParent) throw new InvalidOperationException("Windows refused the wallpaper attachment.");
            var point = new Desktop.Point { X = screen.Left, Y = screen.Top };
            Desktop.MapWindowPoints(IntPtr.Zero, DesktopParent, ref point, 1);
            if (!Desktop.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, screen.Width, screen.Height, 0x0010 | 0x0020 | 0x0040)) throw new InvalidOperationException("Windows could not place the wallpaper.");
        }
    }
    static class Desktop
    {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr child);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Point point, uint count);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        internal static IntPtr FindWallpaperLayer()
        {
            IntPtr progman = FindWindow("Progman", null), result;
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            // Explorer's WorkerW convention is undocumented; fail without covering icons
            // when a compatible background layer cannot be found.
            SendMessageTimeout(progman, 0x052c, IntPtr.Zero, IntPtr.Zero, 2, 1000, out result);
            IntPtr worker = IntPtr.Zero;
            EnumWindows((hwnd, unused) =>
            {
                if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                { worker = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null); if (worker != IntPtr.Zero) return false; }
                return true;
            }, IntPtr.Zero);
            if (worker == IntPtr.Zero) worker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (worker != IntPtr.Zero && FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return IntPtr.Zero;
            return worker;
        }
    }
}
