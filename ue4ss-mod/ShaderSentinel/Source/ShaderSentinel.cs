// Oblivion Shader Sentinel
// Tray app that watches Oblivion Remastered for shader (PSO) compilation and
// shows a game-styled overlay while it runs, plus a "ready" notice when done.
// Detection is fully external: game process CPU load + write activity in the
// GPU driver shader caches. No injection, no game files touched.
//
// Build (no SDK needed, uses the .NET Framework compiler shipped with Windows):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe
//     /out:OblivionShaderSentinel.exe ShaderSentinel.cs
//
// Every run is run-once: wait for the game (up to 3 min), watch it, and exit
// by itself after the "Shaders Ready" notice, after a short "no compiling
// needed" notice if no compile starts within the first minute, or when the
// game closes. It never installs anything and only runs while the game does.
//
// Intended launch method: started on game launch by the companion UE4SS Lua
// mod (ue4ss\Mods\ShaderSentinel\Scripts\main.lua). Alternatively usable as a
// Steam launch wrapper:  "<path>\OblivionShaderSentinel.exe" %command%
// (non-flag arguments are launched as the game command first).
//
// Flags: --demo previews the overlay ~20 s and exits.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ShaderSentinel
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool demo = false;
            List<string> cmd = new List<string>();
            foreach (string a in args)
            {
                if (a == "--demo") demo = true;
                else if (a.StartsWith("--")) { /* ignore unknown flags */ }
                else cmd.Add(a);
            }

            // Wrapper mode: Steam hands us the real game command via %command%.
            // Launch it first, no matter what — the game must start even if
            // another sentinel instance is somehow already running.
            if (cmd.Count > 0)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = cmd[0];
                    psi.Arguments = JoinArgs(cmd, 1);
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = Path.GetDirectoryName(cmd[0]);
                    Process.Start(psi);
                }
                catch { }
            }

            bool createdNew;
            Mutex mutex = new Mutex(true, "OblivionShaderSentinel", out createdNew);
            // Only one watcher instance; a --demo run may coexist with it.
            if (!createdNew && !demo) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp(demo));
            GC.KeepAlive(mutex);
        }

        static string JoinArgs(List<string> a, int start)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = start; i < a.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a[i].IndexOf(' ') >= 0) sb.Append('"').Append(a[i]).Append('"');
                else sb.Append(a[i]);
            }
            return sb.ToString();
        }
    }

    class TrayApp : ApplicationContext
    {
        const string GameProc = "OblivionRemastered-Win64-Shipping";
        const double CpuThreshold = 0.20;   // fraction of total CPU that counts as "working hard"
                                            // (cache writes are the primary signal; this only
                                            // confirms the game is active, since D3DSCache is
                                            // shared with other applications)
        const int WriteRecentSec = 12;      // cache write within this window = active
        const int EnterSamples = 2;         // consecutive hot samples before showing overlay
        const int ExitQuietSec = 20;        // no writes this long + calm CPU = finished
        const int ExitQuietMaxSec = 75;     // no writes this long = finished regardless of CPU
        const double CpuCalmFrac = 0.25;    // "calm" CPU for the quick done path (the driver
                                            // can buffer compiled shaders and pause writes for
                                            // ~45 s mid-compile, but CPU stays pinned)
        const int DoneShowSec = 8;          // how long the "ready" notice stays up
        const int GameWaitSec = 180;        // give up if the game never appears
        const int NoCompileCheckSec = 20;   // no compile by then -> notice, then exit

        NotifyIcon tray;
        OverlayForm overlay;
        System.Windows.Forms.Timer poll;
        List<string> cacheDirs = new List<string>();

        int gamePid = -1;
        TimeSpan lastCpu;
        DateTime lastSample = DateTime.MinValue;

        int hotStreak = 0;
        DateTime lastWriteSeen = DateTime.MinValue;
        DateTime compileStart;
        DateTime doneAt;
        string state = "watching";          // watching -> compiling -> done -> watching

        bool demoMode;
        bool demoFromCli;
        DateTime demoStart;

        bool onceMode;
        DateTime appStart = DateTime.UtcNow;
        DateTime gameFirstSeen = DateTime.MinValue;
        bool sawCompile = false;

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        public TrayApp(bool demo)
        {
            demoMode = demo;
            demoFromCli = demo;
            onceMode = !demo;

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates = new string[] {
                Path.Combine(local, "NVIDIA\\DXCache"),
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, "AMD\\DxCache"),
                Path.Combine(local, "Intel\\ShaderCache")
            };
            foreach (string d in candidates)
                if (Directory.Exists(d)) cacheDirs.Add(d);

            overlay = new OverlayForm();

            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "Oblivion Shader Sentinel \u2014 watching";
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Preview overlay", null, OnDemoClick);
            menu.Items.Add("Exit", null, OnExitClick);
            tray.ContextMenuStrip = menu;
            tray.Visible = !demo;

            poll = new System.Windows.Forms.Timer();
            poll.Interval = demo ? 250 : 2000;
            poll.Tick += OnPoll;
            poll.Start();
            if (demo) demoStart = DateTime.UtcNow;
        }

        void OnDemoClick(object s, EventArgs e)
        {
            demoMode = true;
            demoFromCli = false;
            demoStart = DateTime.UtcNow;
            poll.Interval = 250;
        }

        void OnExitClick(object s, EventArgs e)
        {
            Quit();
        }

        void Quit()
        {
            poll.Stop();
            overlay.HideOverlay();
            tray.Visible = false;
            ExitThread();
        }


        void OnPoll(object s, EventArgs e)
        {
            try
            {
                if (demoMode) { DemoTick(); return; }
                WatchTick();
            }
            catch { /* game exited mid-sample, cache dir vanished, etc. */ }
        }

        void DemoTick()
        {
            double t = (DateTime.UtcNow - demoStart).TotalSeconds;
            Rectangle r = GetTargetRect(FindGame());
            if (t < 14)
            {
                overlay.ShowCompiling(r);
                overlay.UpdateCompiling(TimeSpan.FromSeconds(47 + t), r);
            }
            else if (t < 20)
            {
                overlay.ShowDone(TimeSpan.FromSeconds(61), r);
            }
            else
            {
                overlay.HideOverlay();
                if (demoFromCli) { ExitThread(); return; }
                demoMode = false;
                state = "watching";
                hotStreak = 0;
                poll.Interval = 2000;
            }
        }

        void WatchTick()
        {
            Process p = FindGame();
            if (p == null)
            {
                gamePid = -1;
                hotStreak = 0;
                if (state != "watching") { state = "watching"; overlay.HideOverlay(); }
                if (onceMode)
                {
                    // Game exited, or never appeared within the wait window.
                    if (gameFirstSeen != DateTime.MinValue) { Quit(); return; }
                    if ((DateTime.UtcNow - appStart).TotalSeconds > GameWaitSec) { Quit(); return; }
                }
                tray.Text = "Oblivion Shader Sentinel \u2014 waiting for game";
                return;
            }
            if (gameFirstSeen == DateTime.MinValue) gameFirstSeen = DateTime.UtcNow;

            double cpuFrac = SampleCpu(p);
            if (CacheWrittenWithin(WriteRecentSec)) lastWriteSeen = DateTime.UtcNow;
            bool recentWrite = (DateTime.UtcNow - lastWriteSeen).TotalSeconds <= WriteRecentSec;

            if (state == "watching")
            {
                tray.Text = "Oblivion Shader Sentinel \u2014 watching";
                // No-compile notice: only once the window has passed AND nothing
                // is mid-detection (no fresh cache writes, no partial streak).
                if (onceMode && !sawCompile && hotStreak == 0 && !recentWrite &&
                    (DateTime.UtcNow - gameFirstSeen).TotalSeconds > NoCompileCheckSec)
                {
                    state = "done";
                    doneAt = DateTime.UtcNow;
                    overlay.ShowNoCompile(GetTargetRect(p));
                    p.Dispose();
                    return;
                }
                if (recentWrite && cpuFrac >= CpuThreshold) hotStreak++; else hotStreak = 0;
                if (hotStreak >= EnterSamples)
                {
                    state = "compiling";
                    sawCompile = true;
                    compileStart = DateTime.UtcNow.AddSeconds(-EnterSamples * poll.Interval / 1000);
                    overlay.ShowCompiling(GetTargetRect(p));
                }
            }
            else if (state == "compiling")
            {
                tray.Text = "Oblivion Shader Sentinel \u2014 compiling shaders";
                overlay.UpdateCompiling(DateTime.UtcNow - compileStart, GetTargetRect(p));
                double quiet = (DateTime.UtcNow - lastWriteSeen).TotalSeconds;
                if ((quiet > ExitQuietSec && cpuFrac <= CpuCalmFrac) || quiet > ExitQuietMaxSec)
                {
                    state = "done";
                    doneAt = DateTime.UtcNow;
                    overlay.ShowDone(DateTime.UtcNow - compileStart, GetTargetRect(p));
                }
            }
            else if (state == "done")
            {
                if ((DateTime.UtcNow - doneAt).TotalSeconds > DoneShowSec)
                {
                    overlay.HideOverlay();
                    if (onceMode) { p.Dispose(); Quit(); return; }
                    state = "watching";
                    hotStreak = 0;
                }
            }
            p.Dispose();
        }

        Process FindGame()
        {
            Process[] all = Process.GetProcessesByName(GameProc);
            Process keep = null;
            foreach (Process p in all)
            {
                if (keep == null) keep = p; else p.Dispose();
            }
            return keep;
        }

        double SampleCpu(Process p)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan cpu = p.TotalProcessorTime;
            double frac = 0;
            if (p.Id == gamePid && lastSample != DateTime.MinValue)
            {
                double dt = (now - lastSample).TotalSeconds;
                if (dt > 0.5)
                    frac = (cpu - lastCpu).TotalSeconds / dt / Environment.ProcessorCount;
            }
            gamePid = p.Id;
            lastCpu = cpu;
            lastSample = now;
            return frac;
        }

        bool CacheWrittenWithin(int seconds)
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-seconds);
            foreach (string dir in cacheDirs)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { if (File.GetLastWriteTimeUtc(f) >= cutoff) return true; }
                        catch { }
                    }
                }
                catch { }
            }
            return false;
        }

        Rectangle GetTargetRect(Process p)
        {
            try
            {
                if (p != null && p.MainWindowHandle != IntPtr.Zero)
                {
                    RECT r;
                    if (GetWindowRect(p.MainWindowHandle, out r) && r.Right - r.Left > 200)
                        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                }
            }
            catch { }
            return Screen.PrimaryScreen.WorkingArea;
        }
    }

    class OverlayForm : Form
    {
        // Palette sampled from the game's menu style: near-black warm panel,
        // antique gold borders and text, parchment-tan secondary text.
        static readonly Color PanelFill = Color.FromArgb(18, 15, 11);
        static readonly Color GoldBright = Color.FromArgb(227, 200, 126);
        static readonly Color GoldBorder = Color.FromArgb(186, 155, 92);
        static readonly Color GoldDim = Color.FromArgb(150, 128, 82);
        static readonly Color TanSub = Color.FromArgb(168, 149, 108);

        System.Windows.Forms.Timer anim;
        int tick;
        bool done;
        string doneSub = "";
        TimeSpan elapsed;
        Font titleFont;
        Font subFont;
        Font glyphFont;

        const int WS_EX_TOOLWINDOW = 0x80;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_LAYERED = 0x80000;
        const int WS_EX_NOACTIVATE = 0x8000000;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(400, 92);
            BackColor = PanelFill;
            Opacity = 0.97;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            titleFont = MakeSerif(14.5f, FontStyle.Regular);
            subFont = MakeSerif(9.5f, FontStyle.Italic);
            glyphFont = MakeSerif(15f, FontStyle.Regular);

            anim = new System.Windows.Forms.Timer();
            anim.Interval = 160;
            anim.Tick += delegate(object s, EventArgs e) { tick++; Invalidate(); };
        }

        static Font MakeSerif(float size, FontStyle style)
        {
            string[] prefs = new string[] { "Palatino Linotype", "Georgia", "Times New Roman" };
            foreach (string name in prefs)
            {
                try
                {
                    Font f = new Font(name, size, style);
                    if (f.Name == name) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericSerif, size, style);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (GraphicsPath p = RoundedRect(new Rectangle(0, 0, Width, Height), 8))
                Region = new Region(p);
        }

        public void ShowCompiling(Rectangle target)
        {
            done = false;
            Place(target);
            if (!Visible) Show();
            anim.Start();
        }

        public void UpdateCompiling(TimeSpan t, Rectangle target)
        {
            elapsed = t;
            Place(target);
            Invalidate();
        }

        public void ShowDone(TimeSpan t, Rectangle target)
        {
            done = true;
            elapsed = t;
            doneSub = string.Format("completed in {0}:{1:00}  ·  safe to enter Cyrodiil",
                (int)t.TotalMinutes, t.Seconds);
            Place(target);
            if (!Visible) Show();
            Invalidate();
        }

        public void ShowNoCompile(Rectangle target)
        {
            done = true;
            doneSub = "no compiling needed  ·  safe to enter Cyrodiil";
            Place(target);
            if (!Visible) Show();
            Invalidate();
        }

        public void HideOverlay()
        {
            anim.Stop();
            Hide();
        }

        void Place(Rectangle target)
        {
            int x = target.Right - Width - 28;
            int y = target.Bottom - Height - 28;
            Location = new Point(x, y);
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Panel with the game's double-line gold border
            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRect(outer, 8))
            using (SolidBrush fill = new SolidBrush(PanelFill))
            using (Pen border = new Pen(GoldBorder, 1.6f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            Rectangle inner = new Rectangle(4, 4, Width - 9, Height - 9);
            using (GraphicsPath path = RoundedRect(inner, 6))
            using (Pen thin = new Pen(Color.FromArgb(110, GoldDim), 1f))
            {
                g.DrawPath(thin, path);
            }

            // Four-pointed star glyph; pulses while compiling, steady when done
            int glyphAlpha = 255;
            if (!done)
                glyphAlpha = 150 + (int)(100 * Math.Sin(tick * 0.55));
            if (glyphAlpha < 60) glyphAlpha = 60;
            if (glyphAlpha > 255) glyphAlpha = 255;
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(glyphAlpha, GoldBright)))
                g.DrawString("\u2726", glyphFont, gb, 18, 26);

            string title;
            string sub;
            string time = string.Format("{0}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
            if (done)
            {
                title = "Shaders Ready";
                sub = doneSub;
            }
            else
            {
                string dots = new string('.', (tick / 3) % 4);
                title = "Compiling Shaders" + dots;
                sub = string.Format("elapsed {0}  \u00b7  best to wait at the menu", time);
            }

            using (SolidBrush tb = new SolidBrush(GoldBright))
                g.DrawString(title, titleFont, tb, 48, 18);
            using (SolidBrush sb = new SolidBrush(TanSub))
                g.DrawString(sub, subFont, sb, 50, 52);
        }
    }
}
