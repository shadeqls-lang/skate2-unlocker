// s2 loader — главное окно
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace S2Loader
{
    class MainForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        const int W = 1200, H = 740;
        static readonly RectangleF Content = new RectangleF(320, 30, 850, 680);
        const float X0 = 338, CW = 814;           // внутренняя колонка карточки
        static readonly string[] Pages = { "play", "saves", "outfits", "tools", "setup", "about" };
        static readonly string[] PageHints = {
            "pick a save and an outfit, then drop in",
            "slots: separate save copies, the selected one goes into the game",
            "skater looks: save, swap, share",
            "edit the selected save",
            "game and save paths",
            "credits, licenses, contacts" };

        // ------------------------------------------------------------ состояние
        Config cfg;
        Library lib;
        int page;
        float pageFade = 1;
        string hover;
        string hoverHint;
        string status = "";
        Color statusColor = Pal.Cream;
        int tick;
        int bgIndex, bgWait = 720;
        float bgFade;
        const string TelegramUrl = "https://t.me/emopunk";
        const string DiscordUrl = "https://discord.gg/VzGdZZ5H8";

        float savesScroll, outfitsScroll;
        RectangleF listArea = new RectangleF(X0 - 8, 96, CW + 16, 504);

        Process game;
        bool gameRunning;
        DateTime gameStarted;
        string runningSlot;

        List<string> foundSaveDirs = new List<string>();
        Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>();

        // всплывашка
        string toastTitle = "", toastText = "";
        int toastKind, toastLife;
        float toastT;

        class Zone { public RectangleF R; public string Id; public bool On; public string Hint; }
        List<Zone> zones = new List<Zone>();

        TextBox txtMoney, txtNick;
        Timer timer;

        Font fNav, fTitle, fHead, fChip, fBand, fKey, fSmallP, fBig;
        Font b11, b12, b13, b14, b15, b16b, b20b;

        public MainForm(string dataRoot)
        {
            Text = "s2 loader";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            BackColor = Color.Black;
            KeyPreview = true;
            AllowDrop = true;
            Opacity = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Directory.CreateDirectory(dataRoot);
            cfg = Config.Load(Path.Combine(dataRoot, "config.ini"));
            lib = new Library(dataRoot, cfg);

            fNav = Res.Presto(32); fTitle = Res.Presto(28); fHead = Res.Presto(19); fChip = Res.Presto(16);
            fBand = Res.Presto(58); fKey = Res.Presto(14); fSmallP = Res.Presto(13); fBig = Res.Presto(40);
            b11 = Res.Body(11, false); b12 = Res.Body(12, false); b13 = Res.Body(13, false); b14 = Res.Body(14, false);
            b15 = Res.Body(15, false); b16b = Res.Body(16, true); b20b = Res.Body(20, true);

            txtMoney = MakeBox(8, true);
            txtMoney.SetBounds((int)X0 + 36, 140, 170, 24);
            txtNick = MakeBox(SaveFile.NickMax, false);
            txtNick.SetBounds((int)X0 + 16, 216, 300, 24);

            DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] f = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (f != null) foreach (string x in f) DropFile(x);
            };

            timer = new Timer();
            timer.Interval = 16;
            timer.Tick += delegate { Animate(); };
            timer.Start();

            Reload();
            if (!cfg.GameOk || !cfg.SaveDirOk)
            {
                page = 4;
                SetStatus("set the game exe and the SKATER.P folder first", Pal.Warn);
            }
            else SetStatus("ready. Enter - play, 1-6 - tabs, Esc - quit", Pal.Cream);
            SyncBoxes();
        }

        TextBox MakeBox(int max, bool digits)
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.BackColor = Pal.Paper;
            t.ForeColor = Pal.Ink;
            t.Font = Res.Body(17, true);
            t.MaxLength = max;
            t.Visible = false;
            if (digits)
                t.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            t.TextChanged += delegate { Invalidate(); };
            Controls.Add(t);
            return t;
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
        }

        // ------------------------------------------------------------ данные
        void Reload()
        {
            try { lib.Refresh(); }
            catch (Exception ex) { SetStatus("library error: " + ex.Message, Pal.Bad); }
            if (cfg.Slot.Length > 0 && lib.FindSlot(cfg.Slot) == null) cfg.Slot = "";
            if (cfg.Outfit.Length > 0 && lib.FindOutfit(cfg.Outfit) == null) cfg.Outfit = "";
            cfg.Save();
            ClampScroll();
            Invalidate();
        }

        SlotInfo CurrentSlot()
        {
            if (cfg.Slot.Length > 0)
            {
                SlotInfo s = lib.FindSlot(cfg.Slot);
                if (s != null) return s;
            }
            return lib.Slots.Count > 0 ? lib.Slots[0] : null;
        }

        string TargetFile()
        {
            SlotInfo s = CurrentSlot();
            return s != null ? s.File : "";
        }

        string TargetName()
        {
            SlotInfo s = CurrentSlot();
            if (s == null) return "—";
            return s.Live ? "game save" : s.Name;
        }

        void SyncBoxes()
        {
            bool tools = page == 3 && pageFade > 0.5f;
            SlotInfo s = CurrentSlot();
            bool ok = s != null && s.Valid;
            txtMoney.Visible = tools && ok && s.HasCareer;
            txtNick.Visible = tools && ok;
            if (ok && txtMoney.Tag as string != s.File + s.Modified.Ticks)
            {
                txtMoney.Text = s.Money.ToString();
                txtNick.Text = s.Nick;
                txtMoney.Tag = s.File + s.Modified.Ticks;
            }
        }

        // ------------------------------------------------------------ анимация
        void Animate()
        {
            tick++;
            bool dirty = false;
            if (Opacity < 1) Opacity = Math.Min(1.0, Opacity + 0.07);
            if (pageFade < 1) { pageFade = Math.Min(1, pageFade + 0.12f); dirty = true; if (pageFade >= 1) SyncBoxes(); }
            if (toastLife > 0) { toastLife--; if (toastT < 1) toastT = Math.Min(1, toastT + 0.09f); dirty = true; }
            else if (toastT > 0) { toastT = Math.Max(0, toastT - 0.07f); dirty = true; }
            if (gameRunning && tick % 4 == 0) dirty = true;
            if (bgFade > 0)
            {
                bgFade += 0.012f;
                if (bgFade >= 1) { bgFade = 0; bgIndex = 1 - bgIndex; bgWait = 720; }
                dirty = true;
            }
            else if (--bgWait <= 0) { bgFade = 0.001f; }
            if (dirty) Invalidate();
        }

        static float Ease(float t) { return 1 - (1 - t) * (1 - t) * (1 - t); }

        void GoPage(int p)
        {
            if (p == page) return;
            page = p;
            pageFade = 0;
            txtMoney.Visible = false; txtNick.Visible = false;
            hover = null;
            Invalidate();
        }

        // ------------------------------------------------------------ зоны
        void AddZone(RectangleF r, string id, bool on, string hint)
        {
            Zone z = new Zone(); z.R = r; z.Id = id; z.On = on; z.Hint = hint;
            zones.Add(z);
        }

        bool Hot(string id) { return hover == id; }

        Zone ZoneAt(Point p)
        {
            for (int i = zones.Count - 1; i >= 0; i--) if (zones[i].R.Contains(p)) return zones[i];
            return null;
        }

        // ------------------------------------------------------------ отрисовка
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            zones.Clear();

            DrawBackground(g);
            DrawWindowButtons(g);
            DrawNav(g);

            Gfx.Card(g, Content);
            string hint = PageHints[page];
            if (page == 3) hint = "save: " + TargetName();
            Gfx.Header(g, new RectangleF(X0, 46, CW, 38), Pages[page], fTitle, hint, b13);

            switch (page)
            {
                case 0: DrawPlay(g); break;
                case 1: DrawSaves(g); break;
                case 2: DrawOutfits(g); break;
                case 3: DrawTools(g); break;
                case 4: DrawSetup(g); break;
                case 5: DrawAbout(g); break;
            }
            DrawStatus(g);

            if (pageFade < 1)
                Gfx.Fill(g, new RectangleF(Content.X + 1, 88, Content.Width - 2, Content.Height - 60),
                    Color.FromArgb((int)(200 * (1 - Ease(pageFade))), 22, 17, 13));
            DrawToast(g);
        }

        void DrawBackground(Graphics g)
        {
            Bitmap cur = bgIndex == 0 ? Res.Bg1 : Res.Bg2;
            Bitmap next = bgIndex == 0 ? Res.Bg2 : Res.Bg1;
            g.DrawImage(cur, 0, 0, W, H);
            if (bgFade > 0)
            {
                float a = Ease(Math.Min(1, bgFade));
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = a;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(next, new Rectangle(0, 0, W, H), 0, 0, next.Width, next.Height, GraphicsUnit.Pixel, ia);
                }
            }
        }

        void DrawWindowButtons(Graphics g)
        {
            string[] ids = { "min", "close" };
            for (int i = 0; i < 2; i++)
            {
                RectangleF r = new RectangleF(W - 78 + i * 36, 8, 28, 22);
                bool hot = Hot(ids[i]);
                Gfx.Fill(g, r, hot ? (i == 1 ? Pal.BtnB : Pal.Orange) : Color.FromArgb(150, 20, 14, 10));
                if (i == 0) Gfx.Line(g, Pal.Cream, 2, r.X + 8, r.Y + 15, r.Right - 8, r.Y + 15);
                else
                {
                    Gfx.Line(g, Pal.Cream, 2, r.X + 9, r.Y + 6, r.Right - 9, r.Bottom - 6);
                    Gfx.Line(g, Pal.Cream, 2, r.Right - 9, r.Y + 6, r.X + 9, r.Bottom - 6);
                }
                AddZone(r, ids[i], true, i == 0 ? "minimize" : "quit");
            }
        }

        void DrawNav(Graphics g)
        {
            g.DrawImage(Res.Logo, 34, 34, Res.Logo.Width * 0.8f, Res.Logo.Height * 0.8f);

            float y = 150;
            for (int i = 0; i < Pages.Length; i++)
            {
                RectangleF r = new RectangleF(24, y, 274, 44);
                bool sel = page == i, hot = Hot("nav" + i);
                if (sel)
                {
                    Gfx.Strip(g, Res.MaroonTex, r);
                    Gfx.Fill(g, new RectangleF(r.X, r.Y, 6, r.Height), Pal.Orange);
                }
                else if (hot) Gfx.Fill(g, r, Color.FromArgb(60, 0, 0, 0));
                Color c = sel ? Color.White : (hot ? Pal.OrangeHi : Pal.CreamDim);
                Gfx.TextShadow(g, Pages[i], fNav, c, new RectangleF(r.X + 18, r.Y, r.Width - 30, r.Height + 4), Gfx.Left(true));
                Gfx.Text(g, (i + 1).ToString(), fSmallP, Gfx.A(c, 120), new RectangleF(r.X, r.Y, r.Width - 10, r.Height), Gfx.Right(true));
                AddZone(r, "nav" + i, true, PageHints[i]);
                y += 50;
            }

            // мини-сводка того, что уйдёт в игру
            float sy = 470;
            Gfx.Line(g, Color.FromArgb(70, 243, 232, 206), 1, 30, sy - 10, 292, sy - 10);
            Gfx.Text(g, "ride with", fSmallP, Pal.Dim, new RectangleF(30, sy, 260, 18), Gfx.Left(false));
            SlotInfo s = CurrentSlot();
            string slot = s == null ? "—" : (s.Live ? "game save" : s.Name);
            string outfit = cfg.Outfit.Length > 0 ? cfg.Outfit : "current look";
            Mini(g, sy + 22, "slot", slot);
            Mini(g, sy + 62, "outfit", outfit);
            string opts = "";
            if (cfg.OptMoney) opts += "max cash  ";
            if (cfg.OptUnlock) opts += cfg.OptDlc ? "all gear+dlc  " : "all gear  ";
            if (cfg.OptSync) opts += "sync";
            Gfx.Text(g, opts.Length > 0 ? opts : "no changes", fSmallP, Pal.Dim, new RectangleF(30, sy + 104, 270, 18), Gfx.Left(false));

            // клавиши
            float ky = 660;
            Gfx.PadButton(g, 42, ky, 20, "A", Pal.BtnA, true, fSmallP);
            Gfx.Text(g, "enter", fSmallP, Pal.Dim, new RectangleF(56, ky - 9, 60, 20), Gfx.Left(false));
            Gfx.PadButton(g, 118, ky, 20, "B", Pal.BtnB, true, fSmallP);
            Gfx.Text(g, "esc", fSmallP, Pal.Dim, new RectangleF(132, ky - 9, 60, 20), Gfx.Left(false));
            Gfx.Text(g, "s2 loader v1.1 · fan made, not affiliated with EA", b11, Gfx.A(Pal.Dim, 150), new RectangleF(30, 690, 280, 18), Gfx.Left(false));
        }

        void Mini(Graphics g, float y, string label, string value)
        {
            RectangleF r = new RectangleF(30, y, 262, 34);
            Gfx.Strip(g, Res.Paper, r);
            Gfx.Text(g, label, fSmallP, Pal.InkSoft, new RectangleF(r.X + 8, r.Y, 60, r.Height), Gfx.Left(true));
            UserText(g, value, 17, b14, Pal.Ink, new RectangleF(r.X + 64, r.Y, r.Width - 70, r.Height), Gfx.Left(true));
        }

        // пользовательская строка: шрифт игры, если хватает глифов
        void UserText(Graphics g, string s, float prestoPx, Font fallback, Color c, RectangleF r, StringFormat sf)
        {
            if (Res.PrestoCan(s))
                using (Font f = Res.Presto(prestoPx)) Gfx.Text(g, s, f, c, r, sf);
            else Gfx.Text(g, s, fallback, c, r, sf);
        }

        // бумажная кнопка-ярлык
        float Chip(Graphics g, float x, float y, float h, string label, string id, bool on, string hint)
        {
            SizeF sz = g.MeasureString(label, fChip);
            RectangleF r = new RectangleF(x, y, sz.Width + 18, h);
            bool hot = on && Hot(id);
            Color bg = !on ? Color.FromArgb(70, 243, 232, 206) : (hot ? Pal.OrangeHi : Pal.Paper);
            Gfx.Fill(g, r, bg);
            if (on) Gfx.Line(g, Color.FromArgb(90, 0, 0, 0), 1, r.X, r.Bottom, r.Right, r.Bottom);
            Gfx.Text(g, label, fChip, on ? Pal.Ink : Color.FromArgb(120, 36, 26, 18), new RectangleF(r.X, r.Y + 1, r.Width, r.Height), Gfx.Center());
            AddZone(r, id, on, hint);
            return r.Right + 8;
        }

        float ChipRight(Graphics g, float right, float y, float h, string label, string id, bool on, string hint)
        {
            SizeF sz = g.MeasureString(label, fChip);
            float x = right - sz.Width - 18;
            Chip(g, x, y, h, label, id, on, hint);
            return x - 8;
        }

        void Row(Graphics g, float y, string title, string right)
        {
            Gfx.Header(g, new RectangleF(X0, y, CW, 28), title, fHead, right, b12);
            Gfx.Strip(g, Res.Paper, new RectangleF(X0, y + 30, CW, 40));
        }

        // ============================================================ PLAY
        void DrawPlay(Graphics g)
        {
            SlotInfo s = CurrentSlot();

            // карточка сейва
            RectangleF sc = new RectangleF(X0, 100, 400, 252);
            Gfx.Header(g, new RectangleF(sc.X, sc.Y, sc.Width, 30), "save", fHead, s == null ? "" : (s.Live ? "from game folder" : "slot"), b12);
            string[] lab = { "slot", "skater", "cash", "gear" };
            string[] val = new string[4];
            val[0] = s == null ? "—" : (s.Live ? "game save" : s.Name);
            if (s != null && s.Valid)
            {
                val[1] = s.Nick.Length > 0 ? s.Nick : "no name";
                val[2] = s.HasCareer ? "$" + s.Money.ToString("N0") : "no career";
                val[3] = s.HasCareer ? string.Format("{0} / {1}{2}", s.Gear, SaveFile.GearTotal, s.Dlc ? "  + dlc" : "") : "—";
            }
            else
            {
                val[1] = s == null ? "—" : (s.Error.Length > 0 ? s.Error : "—");
                val[2] = "—"; val[3] = "—";
            }
            for (int i = 0; i < 4; i++)
            {
                RectangleF r = new RectangleF(sc.X, sc.Y + 36 + i * 44, sc.Width, 40);
                Gfx.Strip(g, Res.Paper, r);
                Gfx.Text(g, lab[i], fSmallP, Pal.InkSoft, new RectangleF(r.X + 10, r.Y, 70, r.Height), Gfx.Left(true));
                UserText(g, val[i], 20, b16b, Pal.Ink, new RectangleF(r.X + 80, r.Y, r.Width - 90, r.Height), Gfx.Left(true));
            }
            if (s != null && s.Valid && s.HasCareer)
                Gfx.Segments(g, new RectangleF(sc.X + 230, sc.Y + 36 + 3 * 44 + 14, 160, 12), 8, (float)s.Gear / SaveFile.GearTotal);
            ChipRight(g, sc.Right, sc.Bottom - 26, 24, "change", "go1", true, "pick another slot");

            // карточка образа
            RectangleF oc = new RectangleF(X0 + CW - 400, 100, 400, 252);
            OutfitEntry o = cfg.Outfit.Length > 0 ? lib.FindOutfit(cfg.Outfit) : null;
            Gfx.Header(g, new RectangleF(oc.X, oc.Y, oc.Width, 30), "outfit", fHead, o == null ? "keep current" : "", b12);
            RectangleF pic = new RectangleF(oc.X, oc.Y + 36, 150, 182);
            DrawOutfitPicture(g, pic, o);
            RectangleF info = new RectangleF(oc.X + 158, oc.Y + 36, oc.Width - 158, 182);
            Gfx.Strip(g, Res.Paper, new RectangleF(info.X, info.Y, info.Width, 40));
            UserText(g, o == null ? "current look" : o.Name, 20, b16b, Pal.Ink, new RectangleF(info.X + 10, info.Y, info.Width - 16, 40), Gfx.Left(true));
            string parts = o == null ? "the look already in the save goes in" :
                (o.Parts.Count > 0 ? string.Join(" · ", o.Parts.ToArray()) : "parts not recognized");
            Gfx.Text(g, parts, b13, Pal.Cream, new RectangleF(info.X + 4, info.Y + 50, info.Width - 8, 120), new StringFormat());
            ChipRight(g, oc.Right, oc.Bottom - 26, 24, "change", "go2", true, "pick an outfit");

            // опции перед запуском
            Gfx.Header(g, new RectangleF(X0, 368, CW, 30), "before you ride", fHead, "applied to the copy that goes into the game", b12);
            string[] ids = { "optMoney", "optUnlock", "optDlc", "optSync" };
            string[] txt = { "max cash ($9,999,999)", "unlock all gear (sponsors, HoM, pro)", "include DLC packs", "grab progress into slot after playing" };
            bool[] on = { cfg.OptMoney, cfg.OptUnlock, cfg.OptDlc, cfg.OptSync };
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, row = i / 2;
                RectangleF r = new RectangleF(X0 + col * 411, 404 + row * 46, 403, 40);
                bool en = i != 2 || cfg.OptUnlock;
                bool hot = Hot(ids[i]);
                Gfx.Strip(g, Res.Paper, r);
                if (hot) Gfx.Fill(g, r, Color.FromArgb(30, 232, 132, 42));
                Gfx.Check(g, new RectangleF(r.X + 12, r.Y + 11, 18, 18), on[i] && en, hot);
                Gfx.Text(g, txt[i], b14, en ? Pal.Ink : Pal.InkSoft, new RectangleF(r.X + 40, r.Y, r.Width - 46, r.Height), Gfx.Left(true));
                AddZone(r, ids[i], en, null);
            }

            // полоса PLAY
            RectangleF band = new RectangleF(X0, 512, CW, 84);
            string problem = PlayProblem();
            bool can = problem == null && !gameRunning;
            g.DrawImage(Res.Band, band, new RectangleF(0, 0, Math.Min(Res.Band.Width, band.Width), Res.Band.Height), GraphicsUnit.Pixel);
            if (!can && !gameRunning) Gfx.Fill(g, band, Color.FromArgb(150, 40, 30, 22));
            if (Hot("play") && can) Gfx.Fill(g, band, Color.FromArgb(40, 255, 250, 230));
            Gfx.PadButton(g, band.X + 44, band.Y + band.Height / 2, 42, "A", Pal.BtnA, can, fKey);
            string big = gameRunning ? "in game" : "play";
            Gfx.Text(g, big, fBand, Gfx.A(Pal.Ink, can || gameRunning ? 255 : 140), new RectangleF(band.X + 76, band.Y + 6, 300, band.Height), Gfx.Left(true));
            string side;
            if (gameRunning)
            {
                TimeSpan t = DateTime.Now - gameStarted;
                side = string.Format("in game {0}:{1:00}", (int)t.TotalMinutes, t.Seconds) + (cfg.OptSync && runningSlot != null ? "\nprogress goes back to the slot on exit" : "");
            }
            else side = problem ?? LaunchSummary();
            Gfx.Text(g, side, b14, Pal.Ink, new RectangleF(band.X + 330, band.Y + 8, band.Width - 350, band.Height - 16), Gfx.Right(true));
            if (gameRunning)
            {
                float a = (float)(0.5 + 0.5 * Math.Sin(tick * 0.12));
                Gfx.Fill(g, new RectangleF(band.X, band.Bottom - 4, band.Width * a, 4), Gfx.A(Pal.Ink, 120));
            }
            AddZone(band, "play", can, problem ?? "launch the game");

            // нижняя строка
            float by = 612;
            float x = X0;
            SlotInfo slot = CurrentSlot();
            bool slotPicked = slot != null && !slot.Live;
            x = Chip(g, x, by, 26, "grab progress", "grab", slotPicked && File.Exists(cfg.GameSave), "copy the current game save into the selected slot");
            x = Chip(g, x, by, 26, "game folder", "openGame", cfg.GameOk, "open game folder");
            x = Chip(g, x, by, 26, "save folder", "openSaves", cfg.SaveDirOk, "open the SKATER.P folder");
            if (gameRunning) Chip(g, x, by, 26, "sync now", "syncNow", runningSlot != null, "grab progress into the slot right now");
        }

        string PlayProblem()
        {
            if (!cfg.GameOk) return "set the game exe in setup";
            if (!cfg.SaveDirOk) return "set the save folder in setup";
            SlotInfo s = CurrentSlot();
            if (s == null) return "no save";
            if (!s.Live && !s.Valid) return "slot is broken: " + s.Error;
            if (s.Live && !File.Exists(cfg.GameSave) && (cfg.Outfit.Length > 0 || cfg.OptMoney || cfg.OptUnlock))
                return "no SKATER.P in game folder - nothing to apply edits to";
            if ((cfg.OptMoney || cfg.OptUnlock) && s.Valid && !s.HasCareer) return "no career in this save - untick the options";
            return null;
        }

        string LaunchSummary()
        {
            SlotInfo s = CurrentSlot();
            string a = s.Live ? "game save" : "slot '" + s.Name + "'";
            string b = cfg.Outfit.Length > 0 ? "outfit '" + cfg.Outfit + "'" : "current look";
            return a + "\n" + b;
        }

        void DrawOutfitPicture(Graphics g, RectangleF r, OutfitEntry o)
        {
            Gfx.Fill(g, r, Pal.Paper);
            RectangleF inner = new RectangleF(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10);
            Bitmap bmp = o != null ? Thumb(o, inner.Size) : null;
            if (bmp != null) g.DrawImage(bmp, inner);
            else
            {
                using (LinearGradientBrush b = new LinearGradientBrush(inner, Color.FromArgb(70, 62, 54), Color.FromArgb(28, 24, 20), 90f))
                    g.FillRectangle(b, inner);
                float s = Math.Min(inner.Width, inner.Height) * 0.62f;
                RectangleF ic = new RectangleF(inner.X + (inner.Width - s) / 2, inner.Y + (inner.Height - s) / 2, s, s);
                if (o == null) Gfx.IconDeck(g, ic, Pal.Cream, Color.FromArgb(50, 44, 38));
                else Gfx.IconTee(g, ic, Pal.Cream, Color.FromArgb(40, 34, 28));
            }
            Gfx.Brackets(g, r, Color.FromArgb(220, 255, 248, 235), 7, 1.3f, 3);
        }

        Bitmap Thumb(OutfitEntry o, SizeF size)
        {
            if (o.Image == null) return null;
            string key = o.Image + "|" + File.GetLastWriteTime(o.Image).Ticks + "|" + (int)size.Width + "x" + (int)size.Height;
            Bitmap b;
            if (thumbs.TryGetValue(key, out b)) return b;
            try
            {
                using (FileStream fs = new FileStream(o.Image, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image src = Image.FromStream(fs))
                {
                    int w = (int)size.Width, h = (int)size.Height;
                    b = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        float scale = Math.Max((float)w / src.Width, (float)h / src.Height);
                        float sw = src.Width * scale, sh = src.Height * scale;
                        g.DrawImage(src, (w - sw) / 2, (h - sh) / 2, sw, sh);
                    }
                }
            }
            catch { b = null; }
            thumbs[key] = b;
            return b;
        }

        // ============================================================ SAVES
        const float SlotRowH = 70;

        void DrawSaves(Graphics g)
        {
            GraphicsState st = g.Save();
            g.SetClip(listArea);
            float y = listArea.Y + 4 - savesScroll;
            for (int i = 0; i < lib.Slots.Count; i++)
            {
                SlotInfo s = lib.Slots[i];
                bool sel = s.Live ? cfg.Slot.Length == 0 : cfg.Slot == s.Name;
                string id = "slot" + i;
                bool hot = Hot(id);
                RectangleF head = new RectangleF(X0, y, CW - 88, 26);
                RectangleF paper = new RectangleF(X0, y + 28, CW - 88, 36);
                RectangleF thumb = new RectangleF(X0 + CW - 80, y, 80, 64);
                if (y + SlotRowH > listArea.Y && y < listArea.Bottom)
                {
                    string date = s.Modified.Year > 2000 ? s.Modified.ToString("dd.MM HH:mm") : "";
                    Gfx.Strip(g, Res.MaroonTex, head);
                    if (s.Live) Gfx.Text(g, "game save", fHead, Color.White, new RectangleF(head.X + 10, head.Y, head.Width - 20, head.Height + 2), Gfx.Left(true));
                    else UserText(g, s.Name, 19, b14, Color.White, new RectangleF(head.X + 10, head.Y, head.Width - 120, head.Height + 2), Gfx.Left(true));
                    Gfx.Text(g, s.Live ? "live file in game folder · " + date : date, b12, Pal.CreamDim, new RectangleF(head.X, head.Y, head.Width - 10, head.Height), Gfx.Right(true));
                    Gfx.Strip(g, Res.Paper, paper);
                    string line;
                    if (!s.Valid) line = s.Live && !cfg.SaveDirOk ? "save folder not set" : "unreadable: " + s.Error;
                    else
                    {
                        line = (s.Nick.Length > 0 ? s.Nick : "no name");
                        if (s.HasCareer) line += "   ·   $" + s.Money.ToString("N0") + "   ·   gear " + s.Gear + "/" + SaveFile.GearTotal + (s.Dlc ? " + dlc" : "");
                        else line += "   ·   career not started";
                    }
                    Gfx.Text(g, line, b15, s.Valid ? Pal.Ink : Pal.Bad, new RectangleF(paper.X + 12, paper.Y, paper.Width - 20, paper.Height), Gfx.Left(true));
                    if (hot && !sel) Gfx.Fill(g, RectangleF.Union(head, paper), Color.FromArgb(28, 232, 132, 42));

                    Gfx.Fill(g, thumb, Pal.Paper);
                    RectangleF ti = new RectangleF(thumb.X + 4, thumb.Y + 4, thumb.Width - 8, thumb.Height - 8);
                    using (LinearGradientBrush b = new LinearGradientBrush(ti, Color.FromArgb(70, 62, 54), Color.FromArgb(28, 24, 20), 90f))
                        g.FillRectangle(b, ti);
                    Gfx.IconDisk(g, new RectangleF(ti.X + 12, ti.Y + 6, ti.Width - 24, ti.Height - 12), Color.FromArgb(40, 36, 32), s.Live ? Pal.OrangeHi : Pal.Cream);
                    if (sel)
                    {
                        Gfx.Fill(g, new RectangleF(X0 - 10, y, 5, 64), Pal.Orange);
                        Gfx.Brackets(g, new RectangleF(X0, y, CW, 64), Pal.OrangeHi, 9, 2, 4);
                    }
                    AddZone(new RectangleF(X0, y, CW, 64), id, true, s.Live ? "play with the save that's in the game folder" : "select slot '" + s.Name + "'");
                }
                y += SlotRowH;
            }
            if (lib.Slots.Count <= 1)
                Gfx.Text(g, "no slots yet. 'grab from game' copies the current game save into a new slot,\n'import file' adds any SKATER.P from disk (or drop the file onto the window).",
                    b14, Pal.CreamDim, new RectangleF(X0, listArea.Y + SlotRowH + 16, CW, 60), new StringFormat());
            g.Restore(st);
            DrawScrollbar(g, savesScroll, ContentHeightSaves());

            float x = X0, by = 612;
            SlotInfo cur = CurrentSlot();
            bool slot = cur != null && !cur.Live;
            x = Chip(g, x, by, 26, "grab from game", "slotGrab", File.Exists(cfg.GameSave), "copy SKATER.P from the game folder into a new slot");
            x = Chip(g, x, by, 26, "import file", "slotImport", true, "add a slot from a SKATER.P file");
            x = Chip(g, x, by, 26, "duplicate", "slotDup", cur != null && cur.Valid, "copy the selected slot");
            x = Chip(g, x, by, 26, "rename", "slotRename", slot, "rename slot");
            x = Chip(g, x, by, 26, "export", "slotExport", cur != null && cur.Valid, "save SKATER.P somewhere");
            x = Chip(g, x, by, 26, "delete", "slotDelete", slot, "delete slot (a copy goes to backups)");
            Chip(g, x, by, 26, "folder", "slotFolder", true, "open slots folder");
        }

        float ContentHeightSaves() { return lib.Slots.Count * SlotRowH + 8; }

        void DrawScrollbar(Graphics g, float scroll, float contentH)
        {
            if (contentH <= listArea.Height) return;
            float trackX = listArea.Right - 2;
            Gfx.Fill(g, new RectangleF(trackX, listArea.Y, 3, listArea.Height), Color.FromArgb(40, 243, 232, 206));
            float h = listArea.Height * listArea.Height / contentH;
            float y = listArea.Y + (listArea.Height - h) * (scroll / (contentH - listArea.Height));
            Gfx.Fill(g, new RectangleF(trackX, y, 3, h), Pal.Orange);
        }

        void ClampScroll()
        {
            savesScroll = Math.Max(0, Math.Min(savesScroll, Math.Max(0, ContentHeightSaves() - listArea.Height)));
            outfitsScroll = Math.Max(0, Math.Min(outfitsScroll, Math.Max(0, ContentHeightOutfits() - listArea.Height)));
        }

        // ============================================================ OUTFITS
        const int OCols = 4;
        const float OCardW = 194, OCardH = 238, OGap = 12;

        float ContentHeightOutfits()
        {
            int n = lib.Outfits.Count + 1;
            int rows = (n + OCols - 1) / OCols;
            return rows * (OCardH + OGap) + 8;
        }

        void DrawOutfits(Graphics g)
        {
            GraphicsState st = g.Save();
            g.SetClip(listArea);
            int n = lib.Outfits.Count + 1;
            for (int i = 0; i < n; i++)
            {
                int col = i % OCols, row = i / OCols;
                RectangleF r = new RectangleF(X0 + col * (OCardW + OGap), listArea.Y + 6 + row * (OCardH + OGap) - outfitsScroll, OCardW, OCardH);
                if (r.Bottom < listArea.Y || r.Y > listArea.Bottom) continue;
                OutfitEntry o = i == 0 ? null : lib.Outfits[i - 1];
                bool sel = o == null ? cfg.Outfit.Length == 0 : cfg.Outfit == o.Name;
                string id = "fit" + i;
                bool hot = Hot(id);

                Gfx.Fill(g, r, Pal.Paper);
                RectangleF pic = new RectangleF(r.X + 6, r.Y + 6, r.Width - 12, 156);
                Bitmap bmp = o != null ? Thumb(o, pic.Size) : null;
                if (bmp != null) g.DrawImage(bmp, pic);
                else
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(pic, Color.FromArgb(70, 62, 54), Color.FromArgb(28, 24, 20), 90f))
                        g.FillRectangle(b, pic);
                    RectangleF ic = new RectangleF(pic.X + pic.Width / 2 - 52, pic.Y + 20, 104, 104);
                    if (o == null) Gfx.IconDeck(g, ic, Pal.Cream, Color.FromArgb(50, 44, 38));
                    else Gfx.IconTee(g, ic, Pal.Cream, Color.FromArgb(40, 34, 28));
                    if (o != null) Gfx.Text(g, "no photo", fSmallP, Gfx.A(Pal.Cream, 110), new RectangleF(pic.X, pic.Bottom - 22, pic.Width, 18), Gfx.Center());
                }
                RectangleF name = new RectangleF(r.X + 6, r.Y + 166, r.Width - 12, 28);
                Gfx.Strip(g, Res.MaroonTex, name);
                if (o == null) Gfx.Text(g, "keep current", fHead, Color.White, new RectangleF(name.X + 8, name.Y, name.Width - 10, name.Height + 2), Gfx.Left(true));
                else UserText(g, o.Name, 19, b14, Color.White, new RectangleF(name.X + 8, name.Y, name.Width - 10, name.Height + 2), Gfx.Left(true));
                string parts = o == null ? "the look in the save stays as is" :
                    (o.Parts.Count > 0 ? string.Join(" · ", o.Parts.ToArray()) : "parts not recognized");
                Gfx.Text(g, parts, b11, Pal.Ink, new RectangleF(r.X + 8, r.Y + 198, r.Width - 16, 36), new StringFormat());

                if (hot && !sel) Gfx.Fill(g, r, Color.FromArgb(26, 232, 132, 42));
                if (sel) Gfx.Brackets(g, r, Pal.OrangeHi, 12, 2.5f, 5);
                else Gfx.Brackets(g, r, Color.FromArgb(120, 255, 245, 230), 7, 1.2f, 3);
                AddZone(r, id, true, o == null ? "don't change the look on launch" : "select '" + o.Name + "' · double-click - wear it in the selected save now");
            }
            g.Restore(st);
            DrawScrollbar(g, outfitsScroll, ContentHeightOutfits());

            OutfitEntry cur = cfg.Outfit.Length > 0 ? lib.FindOutfit(cfg.Outfit) : null;
            SlotInfo slot = CurrentSlot();
            bool slotOk = slot != null && slot.Valid;
            float x = X0, by = 612;
            x = Chip(g, x, by, 26, "save from game", "fitFromGame", File.Exists(cfg.GameSave), "save the look from the game save");
            x = Chip(g, x, by, 26, "save from slot", "fitFromSlot", slotOk && !slot.Live, "save the look from the selected slot");
            x = Chip(g, x, by, 26, "apply now", "fitApply", cur != null && slotOk, "write the selected outfit into the selected save now");
            x = Chip(g, x, by, 26, "photo", "fitPhoto", cur != null, "attach a picture (in-game screenshot)");
            x = Chip(g, x, by, 26, "rename", "fitRename", cur != null, "rename outfit");
            x = Chip(g, x, by, 26, "export", "fitExport", cur != null, "save .s2fit to share");
            x = Chip(g, x, by, 26, "import", "fitImport", true, "add a .s2fit from a friend");
            Chip(g, x, by, 26, "delete", "fitDelete", cur != null, "delete outfit");
        }

        // ============================================================ TOOLS
        void DrawTools(Graphics g)
        {
            SlotInfo s = CurrentSlot();
            bool ok = s != null && s.Valid;
            bool career = ok && s.HasCareer;

            // деньги
            Row(g, 100, "cash", career ? "now $" + s.Money.ToString("N0") : "no career");
            Gfx.Text(g, "$", b20b, Pal.Ink, new RectangleF(X0 + 14, 130, 24, 40), Gfx.Left(true));
            float r = X0 + CW - 8;
            r = ChipRight(g, r, 137, 26, "apply", "cashApply", career, "write the amount into the save");
            r = ChipRight(g, r, 137, 26, "max", "cashMax", career, "$9,999,999");
            r = ChipRight(g, r, 137, 26, "1m", "cash1m", career, "$1,000,000");
            ChipRight(g, r, 137, 26, "100k", "cash100k", career, "$100,000");

            // ник
            Row(g, 176, "name", ok ? "up to " + SaveFile.NickMax + " characters, latin" : "");
            ChipRight(g, X0 + CW - 8, 213, 26, "apply", "nickApply", ok, "write the name");

            // шмотки
            int gear = career ? s.Gear : 0;
            Row(g, 252, "unlocks", career ? string.Format("{0} / {1}{2}", gear, SaveFile.GearTotal, s.Dlc ? " + dlc" : "") : "");
            if (career) Gfx.Segments(g, new RectangleF(X0 + 14, 296, 260, 14), 10, (float)gear / SaveFile.GearTotal);
            else Gfx.Text(g, "needs a started career", b14, Pal.InkSoft, new RectangleF(X0 + 14, 282, 300, 40), Gfx.Left(true));
            r = X0 + CW - 8;
            r = ChipRight(g, r, 289, 26, "unlock all", "gearAll", career, "sponsors, Hall of Meat, pro gear" + (cfg.OptDlc ? " and DLC" : ""));
            bool dhot = Hot("toolDlc");
            RectangleF dz = new RectangleF(r - 130, 284, 130, 36);
            Gfx.Check(g, new RectangleF(dz.X + 6, dz.Y + 9, 18, 18), cfg.OptDlc, dhot);
            Gfx.Text(g, "+ DLC packs", b14, Pal.Ink, new RectangleF(dz.X + 32, dz.Y, 100, dz.Height), Gfx.Left(true));
            AddZone(dz, "toolDlc", true, "First Gear and Throwback");

            // образ в сейве
            Row(g, 328, "outfit in save", "");
            string fitInfo = "—", crc = "—";
            if (ok) ToolInfo(s, out fitInfo, out crc);
            Gfx.Text(g, fitInfo, b14, Pal.Ink, new RectangleF(X0 + 14, 358, CW - 200, 40), Gfx.Left(true));
            ChipRight(g, X0 + CW - 8, 365, 26, "save as outfit", "toolFitSave", ok, "save the current skater look into the library");

            // файл
            Row(g, 404, "file", ok ? Path.GetFileName(Path.GetDirectoryName(s.File)) + "\\SKATER.P" : "");
            Gfx.Text(g, crc, b14, Pal.Ink, new RectangleF(X0 + 14, 434, CW - 380, 40), Gfx.Left(true));
            r = X0 + CW - 8;
            r = ChipRight(g, r, 441, 26, "backups", "openBackups", true, "open backups folder");
            r = ChipRight(g, r, 441, 26, "undo last", "undo", ok && lib.LatestBackup(BackupTag(s)) != null, "restore the last backup of this save");
            ChipRight(g, r, 441, 26, "fix crc", "fixCrc", ok, "recalculate the checksum");

            Gfx.Text(g, "before every write the old file goes to backups (last " + cfg.KeepBackups + ").\n" +
                        "edits here are written straight into the selected save. close the game first or it will overwrite the file.",
                b13, Pal.CreamDim, new RectangleF(X0, 500, CW, 50), new StringFormat());
        }

        string infoKey, infoFit, infoCrc;

        void ToolInfo(SlotInfo s, out string fit, out string crc)
        {
            string key = s.File + "|" + s.Modified.Ticks;
            if (key != infoKey)
            {
                infoKey = key;
                try
                {
                    SaveFile sf = SaveFile.Load(s.File);
                    infoFit = sf.HasOutfit ? string.Join(" · ", OutfitInfo.Parts(sf.GetOutfit()).ToArray()) : "look not found";
                    infoCrc = sf.CrcOk ? "checksum ok" : "checksum broken - hit fix crc";
                }
                catch (Exception ex) { infoFit = ex.Message; infoCrc = ex.Message; }
            }
            fit = infoFit; crc = infoCrc;
        }

        static string BackupTag(SlotInfo s) { return s.Live ? "game" : "slot_" + Library.SafeName(s.Name); }

        // ============================================================ SETUP
        void DrawSetup(Graphics g)
        {
            Row(g, 100, "game exe", cfg.GameOk ? "found" : "not set");
            PathText(g, 130, cfg.GameExe.Length > 0 ? cfg.GameExe : "pick the Skate 2 port .exe", cfg.GameOk);
            ChipRight(g, X0 + CW - 8, 137, 26, "browse", "setGame", true, "choose the game exe");

            Row(g, 176, "save folder", cfg.SaveDirOk ? (File.Exists(cfg.GameSave) ? "SKATER.P is there" : "no SKATER.P yet") : "not set");
            PathText(g, 206, cfg.SaveDir.Length > 0 ? cfg.SaveDir : "folder that contains SKATER.P", cfg.SaveDirOk);
            float r = X0 + CW - 8;
            r = ChipRight(g, r, 213, 26, "browse", "setSaves", true, "choose folder");
            ChipRight(g, r, 213, 26, "auto find", "findSaves", true, "search for SKATER.P near the game, in AppData and Documents");

            float y = 256;
            if (foundSaveDirs.Count > 0)
            {
                Gfx.Text(g, "found:", fSmallP, Pal.Dim, new RectangleF(X0, y, 100, 20), Gfx.Left(false));
                y += 22;
                for (int i = 0; i < foundSaveDirs.Count && i < 4; i++)
                {
                    RectangleF fr = new RectangleF(X0, y, CW, 26);
                    bool hot = Hot("found" + i);
                    Gfx.Fill(g, fr, hot ? Color.FromArgb(70, 232, 132, 42) : Color.FromArgb(40, 0, 0, 0));
                    Gfx.Text(g, foundSaveDirs[i], b13, Pal.Cream, new RectangleF(fr.X + 10, fr.Y, fr.Width - 20, fr.Height), Gfx.Left(true));
                    AddZone(fr, "found" + i, true, "use this folder");
                    y += 30;
                }
                y += 6;
            }

            float dy = Math.Max(y, 300);
            Row(g, dy, "library", "slots, outfits, backups");
            PathText(g, dy + 30, lib.Root, true);
            ChipRight(g, X0 + CW - 8, dy + 37, 26, "open", "openData", true, "open library folder");

            float cy = dy + 86;
            Gfx.Header(g, new RectangleF(X0, cy, CW, 28), "checklist", fHead, null, null);
            string[] items = {
                cfg.GameOk ? "game exe found" : "game exe not set",
                cfg.SaveDirOk ? "save folder exists" : "save folder not set",
                File.Exists(cfg.GameSave) ? "SKATER.P is in the game folder" : "no SKATER.P in the game folder (run the game and save)",
                lib.Slots.Count > 1 ? "slots in library: " + (lib.Slots.Count - 1) : "no slots yet - optional",
                "outfits: " + lib.Outfits.Count };
            bool[] good = { cfg.GameOk, cfg.SaveDirOk, File.Exists(cfg.GameSave), true, true };
            for (int i = 0; i < items.Length; i++)
            {
                float iy = cy + 34 + i * 24;
                using (SolidBrush b = new SolidBrush(good[i] ? Pal.Good : Pal.Bad)) g.FillEllipse(b, X0 + 4, iy + 6, 9, 9);
                Gfx.Text(g, items[i], b14, Pal.Cream, new RectangleF(X0 + 22, iy, CW - 30, 22), Gfx.Left(false));
            }

            float ky = cy + 34 + items.Length * 24 + 8;
            Gfx.Text(g, "keep backups: " + cfg.KeepBackups, b14, Pal.Cream, new RectangleF(X0, ky, 200, 26), Gfx.Left(true));
            float kx = Chip(g, X0 + 190, ky, 26, "-", "keepMinus", cfg.KeepBackups > 10, "fewer");
            Chip(g, kx, ky, 26, "+", "keepPlus", cfg.KeepBackups < 200, "more");
        }

        // ============================================================ ABOUT
        void DrawAbout(Graphics g)
        {
            Row(g, 100, "s2 loader", "v1.1");
            Gfx.Text(g, "save slots, outfits and unlocks for the Skate 2 PC port. made by emopunk.", b14, Pal.Ink,
                new RectangleF(X0 + 14, 130, CW - 28, 40), Gfx.Left(true));

            Gfx.Header(g, new RectangleF(X0, 186, CW, 28), "contact", fHead, "bugs, ideas, new versions", b12);
            ContactCard(g, new RectangleF(X0, 222, 403, 84), "tg", "telegram", "@emopunk", Color.FromArgb(42, 150, 214));
            ContactCard(g, new RectangleF(X0 + 411, 222, 403, 84), "dc", "discord", "join the server", Color.FromArgb(88, 101, 242));

            Gfx.Header(g, new RectangleF(X0, 324, CW, 28), "credits & licenses", fHead, null, null);
            string[] lines = {
                "free fan-made tool. not affiliated with, endorsed or sponsored by Electronic Arts / EA Black Box.",
                "Skate 2 and skate. are trademarks of Electronic Arts Inc. background screenshots (c) EA.",
                "Helvetica Presto typeface - rights belong to their respective owners.",
                "built on .NET Framework / Windows Forms (c) Microsoft.",
                "save format, checksum and unlock lists worked out from save files. no game code included.",
                "s2 loader (c) 2026 emopunk. free to share, not for sale. use at your own risk." };
            float y = 360;
            for (int i = 0; i < lines.Length; i++)
            {
                RectangleF r = new RectangleF(X0, y, CW, 34);
                Gfx.Strip(g, Res.Paper, r);
                Gfx.Fill(g, new RectangleF(r.X + 12, r.Y + 14, 6, 6), Pal.Maroon);
                Gfx.Text(g, lines[i], b13, Pal.Ink, new RectangleF(r.X + 26, r.Y, r.Width - 36, r.Height), Gfx.Left(true));
                y += 38;
            }
        }

        void ContactCard(Graphics g, RectangleF r, string id, string title, string sub, Color brand)
        {
            bool hot = Hot(id);
            Gfx.Strip(g, Res.Paper, r);
            if (hot) Gfx.Fill(g, r, Color.FromArgb(34, 232, 132, 42));
            RectangleF ic = new RectangleF(r.X + 10, r.Y + 10, r.Height - 20, r.Height - 20);
            Gfx.Fill(g, ic, brand);
            if (id == "tg")
            {
                PointF[] plane = {
                    new PointF(ic.X + ic.Width * 0.18f, ic.Y + ic.Height * 0.50f),
                    new PointF(ic.X + ic.Width * 0.82f, ic.Y + ic.Height * 0.24f),
                    new PointF(ic.X + ic.Width * 0.68f, ic.Y + ic.Height * 0.78f),
                    new PointF(ic.X + ic.Width * 0.46f, ic.Y + ic.Height * 0.62f) };
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPolygon(b, plane);
                Gfx.Line(g, brand, 2, ic.X + ic.Width * 0.46f, ic.Y + ic.Height * 0.62f, ic.X + ic.Width * 0.82f, ic.Y + ic.Height * 0.24f);
            }
            else
            {
                RectangleF face = new RectangleF(ic.X + ic.Width * 0.16f, ic.Y + ic.Height * 0.28f, ic.Width * 0.68f, ic.Height * 0.46f);
                using (GraphicsPath p = Gfx.Round(face, face.Height * 0.4f))
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                using (SolidBrush e = new SolidBrush(brand))
                {
                    g.FillEllipse(e, face.X + face.Width * 0.24f, face.Y + face.Height * 0.36f, face.Width * 0.16f, face.Height * 0.3f);
                    g.FillEllipse(e, face.X + face.Width * 0.60f, face.Y + face.Height * 0.36f, face.Width * 0.16f, face.Height * 0.3f);
                }
            }
            Gfx.Brackets(g, ic, Color.FromArgb(160, 36, 26, 18), 6, 1.2f, 3);
            Gfx.Text(g, title, fNav, Pal.Ink, new RectangleF(ic.Right + 16, r.Y + 6, r.Width - ic.Width - 40, 40), Gfx.Left(true));
            Gfx.Text(g, sub, b14, Pal.InkSoft, new RectangleF(ic.Right + 18, r.Y + 44, r.Width - ic.Width - 40, 28), Gfx.Left(true));
            Gfx.Text(g, "open >", fChip, hot ? Pal.Maroon : Pal.InkSoft, new RectangleF(r.X, r.Y, r.Width - 14, r.Height), Gfx.Right(true));
            AddZone(r, id, true, id == "tg" ? TelegramUrl : DiscordUrl);
        }

        void PathText(Graphics g, float y, string text, bool ok)
        {
            Gfx.Text(g, text, b14, ok ? Pal.Ink : Pal.InkSoft, new RectangleF(X0 + 14, y, CW - 250, 40), Gfx.Left(true));
        }

        // ============================================================ статус и всплывашка
        void DrawStatus(Graphics g)
        {
            string text = hoverHint ?? status;
            Color c = hoverHint != null ? Pal.CreamDim : statusColor;
            RectangleF r = new RectangleF(X0, 664, CW, 26);
            Gfx.Line(g, Color.FromArgb(50, 243, 232, 206), 1, X0, 656, X0 + CW, 656);
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r.X, r.Y + 9, 8, 8);
            Gfx.Text(g, text, b13, c, new RectangleF(r.X + 16, r.Y, r.Width - 16, r.Height), Gfx.Left(true));
        }

        void SetStatus(string text, Color c) { status = text; statusColor = c; Invalidate(); }

        void Toast(string title, string text, int kind)
        {
            toastTitle = title; toastText = text; toastKind = kind; toastLife = 220;
            if (toastT > 0.9f) toastT = 0.6f;
        }

        void DrawToast(Graphics g)
        {
            if (toastT <= 0.001f) return;
            float e = Ease(toastT);
            float w = 320, h = 100;
            RectangleF r = new RectangleF(W - (w + 26) * e, 40, w, h);
            Gfx.Fill(g, new RectangleF(r.X + 6, r.Y + 8, r.Width, r.Height), Color.FromArgb((int)(120 * e), 0, 0, 0));
            g.DrawImage(Res.Card, r, new RectangleF(0, 0, r.Width, r.Height), GraphicsUnit.Pixel);
            Gfx.Brackets(g, r, Color.FromArgb(220, 255, 245, 230), 8, 1.4f, 5);
            RectangleF head = new RectangleF(r.X + 8, r.Y + 8, r.Width - 92, 26);
            RectangleF paper = new RectangleF(r.X + 8, r.Y + 36, r.Width - 92, 56);
            RectangleF thumb = new RectangleF(r.Right - 78, r.Y + 8, 70, 84);
            Gfx.Strip(g, Res.MaroonTex, head);
            if (toastKind == 3) Gfx.Fill(g, head, Color.FromArgb(90, 255, 40, 20));
            Gfx.Text(g, toastTitle, fHead, Color.White, new RectangleF(head.X + 8, head.Y, head.Width - 10, head.Height + 2), Gfx.Left(true));
            Gfx.Strip(g, Res.Paper, paper);
            StringFormat sf = new StringFormat(); sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisWord;
            Gfx.Text(g, toastText, b13, Pal.Ink, new RectangleF(paper.X + 8, paper.Y, paper.Width - 12, paper.Height), sf);
            Gfx.Fill(g, thumb, Pal.Paper);
            RectangleF ti = new RectangleF(thumb.X + 4, thumb.Y + 4, thumb.Width - 8, thumb.Height - 8);
            Gfx.Fill(g, ti, toastKind == 1 ? Color.FromArgb(235, 228, 210) : Color.FromArgb(40, 36, 30));
            RectangleF ic = new RectangleF(ti.X + 4, ti.Y + 10, ti.Width - 8, ti.Width - 8);
            switch (toastKind)
            {
                case 0: Gfx.Text(g, "$", fBig, Pal.Cream, ti, Gfx.Center()); break;
                case 1: Gfx.IconTee(g, ic, Color.FromArgb(20, 18, 16), Pal.Cream); break;
                case 2: Gfx.IconDisk(g, ic, Color.FromArgb(40, 36, 32), Pal.Cream); break;
                case 3: Gfx.IconLock(g, ic, Pal.Bad); break;
                case 4: Gfx.IconPlay(g, ic, Pal.OrangeHi); break;
            }
        }

        // ============================================================ ввод
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Zone z = ZoneAt(e.Location);
            string h = z != null && z.On ? z.Id : null;
            string hint = z != null ? z.Hint : null;
            if (h != hover || hint != hoverHint)
            {
                hover = h; hoverHint = hint;
                Cursor = h != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = null; hoverHint = null; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Zone z = ZoneAt(e.Location);
            if (z == null)
            {
                if (e.Y < 120 || e.X < 310)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                }
                return;
            }
            if (!z.On) return;
            Do(z.Id, e.Clicks >= 2);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Zone z = ZoneAt(e.Location);
            if (z != null && z.On && z.Id.StartsWith("fit") && z.Id != "fit0" && char.IsDigit(z.Id[3])) Do("fitApply", false);
            if (z != null && z.On && z.Id.StartsWith("slot") && char.IsDigit(z.Id[4])) GoPage(0);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (page == 1) savesScroll -= e.Delta * 0.6f;
            else if (page == 2) outfitsScroll -= e.Delta * 0.6f;
            else return;
            ClampScroll();
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = ActiveControl is TextBox && ((TextBox)ActiveControl).Visible;
            if (keyData == Keys.Escape)
            {
                if (typing) { ActiveControl = null; Focus(); return true; }
                Close(); return true;
            }
            if (keyData == Keys.Enter)
            {
                if (typing && ActiveControl == txtMoney) { Do("cashApply", false); return true; }
                if (typing && ActiveControl == txtNick) { Do("nickApply", false); return true; }
                if (page == 0) { Do("play", false); return true; }
            }
            if (!typing)
            {
                if (keyData >= Keys.D1 && keyData <= Keys.D6) { GoPage(keyData - Keys.D1); return true; }
                if (keyData == Keys.F5) { Reload(); SetStatus("refreshed", Pal.Cream); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void DropFile(string f)
        {
            try
            {
                if (f.EndsWith(".s2fit", StringComparison.OrdinalIgnoreCase))
                {
                    string n = lib.AddOutfit(File.ReadAllBytes(f), Path.GetFileNameWithoutExtension(f));
                    Reload(); cfg.Outfit = n; cfg.Save(); GoPage(2);
                    Toast("New Outfit", "outfit '" + n + "' added", 1);
                }
                else if (Path.GetFileName(f).StartsWith("SKATER", StringComparison.OrdinalIgnoreCase))
                {
                    string n = lib.AddSlotFromFile(f, "imported");
                    Reload(); cfg.Slot = n; cfg.Save(); GoPage(1);
                    Toast("Save Added", "slot '" + n + "' added", 2);
                }
                else if (page == 2 && cfg.Outfit.Length > 0 && IsImage(f))
                {
                    OutfitEntry o = lib.FindOutfit(cfg.Outfit);
                    if (o != null) { lib.SetOutfitImage(o, f); thumbs.Clear(); Reload(); Toast("Photo", "photo attached to '" + o.Name + "'", 1); }
                }
                else SetStatus("unknown file: " + Path.GetFileName(f), Pal.Warn);
            }
            catch (Exception ex) { Fail(ex); }
        }

        static bool IsImage(string f)
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp";
        }

        void Fail(Exception ex)
        {
            SetStatus("error: " + ex.Message, Pal.Bad);
            Toast("Didn't work", ex.Message, 3);
        }

        // ============================================================ действия
        void Do(string id, bool dbl)
        {
            try { DoInner(id); }
            catch (Exception ex) { if (!IsDisposed) Fail(ex); }
            if (IsDisposed || Disposing) return;
            SyncBoxes();
            Invalidate();
        }

        void DoInner(string id)
        {
            if (id.StartsWith("nav")) { GoPage(int.Parse(id.Substring(3))); return; }
            if (id.StartsWith("slot") && id.Length > 4 && char.IsDigit(id[4]))
            {
                SlotInfo s = lib.Slots[int.Parse(id.Substring(4))];
                cfg.Slot = s.Live ? "" : s.Name; cfg.Save();
                SetStatus("selected: " + (s.Live ? "game save" : s.Name), Pal.Cream);
                return;
            }
            if (id.StartsWith("fit") && id.Length > 3 && char.IsDigit(id[3]))
            {
                int i = int.Parse(id.Substring(3));
                cfg.Outfit = i == 0 ? "" : lib.Outfits[i - 1].Name; cfg.Save();
                SetStatus(i == 0 ? "keeping the current look" : "outfit selected: '" + cfg.Outfit + "'", Pal.Cream);
                return;
            }
            if (id.StartsWith("found"))
            {
                cfg.SaveDir = foundSaveDirs[int.Parse(id.Substring(5))]; cfg.Save();
                foundSaveDirs.Clear(); Reload();
                SetStatus("save folder: " + cfg.SaveDir, Pal.Good);
                return;
            }

            switch (id)
            {
                case "close": Close(); return;
                case "min": WindowState = FormWindowState.Minimized; return;
                case "go1": GoPage(1); return;
                case "go2": GoPage(2); return;
                case "optMoney": cfg.OptMoney = !cfg.OptMoney; cfg.Save(); return;
                case "optUnlock": cfg.OptUnlock = !cfg.OptUnlock; cfg.Save(); return;
                case "optDlc":
                case "toolDlc": cfg.OptDlc = !cfg.OptDlc; cfg.Save(); return;
                case "optSync": cfg.OptSync = !cfg.OptSync; cfg.Save(); return;
                case "play": Play(); return;
                case "grab": Grab(); return;
                case "syncNow": SyncBack(true); return;
                case "openGame": OpenFolder(Path.GetDirectoryName(cfg.GameExe)); return;
                case "openSaves": OpenFolder(cfg.SaveDir); return;
                case "openData": OpenFolder(lib.Root); return;
                case "openBackups": OpenFolder(lib.BackupsDir); return;
                case "slotFolder": OpenFolder(lib.SavesDir); return;

                // ---- слоты
                case "slotGrab":
                {
                    string n = S2Dialog.Ask(this, "new slot", "slot name for the current game save:", NickOrDefault(cfg.GameSave, "slot"), 40);
                    if (n == null) return;
                    string name = lib.AddSlotFromFile(cfg.GameSave, n);
                    Reload(); cfg.Slot = name; cfg.Save();
                    Toast("Save Added", "slot '" + name + "' from game folder", 2);
                    return;
                }
                case "slotImport":
                {
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Title = "SKATER.P for a new slot";
                        d.Filter = "Skate 2 save|SKATER.P;SKATER*;*.P|All files|*.*";
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        string n = S2Dialog.Ask(this, "new slot", "slot name:", NickOrDefault(d.FileName, "imported"), 40);
                        if (n == null) return;
                        string name = lib.AddSlotFromFile(d.FileName, n);
                        Reload(); cfg.Slot = name; cfg.Save();
                        Toast("Save Added", "slot '" + name + "' added", 2);
                    }
                    return;
                }
                case "slotDup":
                {
                    SlotInfo s = CurrentSlot();
                    string n = S2Dialog.Ask(this, "duplicate", "copy name:", (s.Live ? "game" : s.Name) + " copy", 40);
                    if (n == null) return;
                    string name = lib.AddSlotFromFile(s.File, n);
                    Reload(); cfg.Slot = name; cfg.Save();
                    Toast("Save Added", "copy '" + name + "'", 2);
                    return;
                }
                case "slotRename":
                {
                    SlotInfo s = CurrentSlot();
                    string n = S2Dialog.Ask(this, "rename", "new slot name:", s.Name, 40);
                    if (n == null || n == s.Name) return;
                    lib.RenameSlot(s.Name, n);
                    cfg.Slot = Library.SafeName(n); Reload();
                    return;
                }
                case "slotExport":
                {
                    SlotInfo s = CurrentSlot();
                    using (SaveFileDialog d = new SaveFileDialog())
                    {
                        d.FileName = "SKATER.P";
                        d.Filter = "Skate 2 save|*.P|All files|*.*";
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        File.Copy(s.File, d.FileName, true);
                        SetStatus("saved: " + d.FileName, Pal.Good);
                    }
                    return;
                }
                case "slotDelete":
                {
                    SlotInfo s = CurrentSlot();
                    if (!S2Dialog.Confirm(this, "delete", "delete slot '" + s.Name + "'?\na copy stays in backups.")) return;
                    lib.DeleteSlot(s.Name);
                    cfg.Slot = ""; Reload();
                    Toast("Deleted", "slot deleted, copy kept in backups", 2);
                    return;
                }

                // ---- образы
                case "fitFromGame": SaveOutfitFrom(cfg.GameSave, "from game"); return;
                case "fitFromSlot": SaveOutfitFrom(CurrentSlot().File, CurrentSlot().Name); return;
                case "toolFitSave": SaveOutfitFrom(CurrentSlot().File, TargetName()); return;
                case "fitApply":
                {
                    OutfitEntry o = lib.FindOutfit(cfg.Outfit);
                    SlotInfo s = CurrentSlot();
                    if (o == null || s == null) return;
                    if (gameRunning && s.Live) throw new Exception("the game is running - close it before changing the look");
                    if (!S2Dialog.Confirm(this, "apply outfit", "wear '" + o.Name + "' in '" + TargetName() + "'?\nthe old save goes to backups.")) return;
                    EditTarget(delegate(SaveFile sf) { sf.ApplyOutfit(File.ReadAllBytes(o.File)); });
                    Toast("New Outfit", "'" + o.Name + "' on", 1);
                    return;
                }
                case "fitPhoto":
                {
                    OutfitEntry o = lib.FindOutfit(cfg.Outfit);
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Title = "outfit picture (in-game screenshot)";
                        d.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp";
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        lib.SetOutfitImage(o, d.FileName);
                        thumbs.Clear(); Reload();
                        Toast("Photo", "photo attached", 1);
                    }
                    return;
                }
                case "fitRename":
                {
                    OutfitEntry o = lib.FindOutfit(cfg.Outfit);
                    string n = S2Dialog.Ask(this, "rename", "new outfit name:", o.Name, 40);
                    if (n == null || n == o.Name) return;
                    lib.RenameOutfit(o, n);
                    thumbs.Clear();
                    cfg.Outfit = Library.SafeName(n); Reload();
                    return;
                }
                case "fitExport":
                {
                    OutfitEntry o = lib.FindOutfit(cfg.Outfit);
                    using (SaveFileDialog d = new SaveFileDialog())
                    {
                        d.FileName = o.Name + ".s2fit";
                        d.Filter = "s2 loader outfit|*.s2fit";
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        File.Copy(o.File, d.FileName, true);
                        if (o.Image != null) File.Copy(o.Image, Path.ChangeExtension(d.FileName, Path.GetExtension(o.Image)), true);
                        SetStatus("outfit saved: " + d.FileName, Pal.Good);
                    }
                    return;
                }
                case "fitImport":
                {
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Title = "outfit .s2fit";
                        d.Filter = "s2 loader outfit|*.s2fit";
                        d.Multiselect = true;
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        string last = null;
                        foreach (string f in d.FileNames)
                        {
                            last = lib.AddOutfit(File.ReadAllBytes(f), Path.GetFileNameWithoutExtension(f));
                            foreach (string ext in new string[] { ".png", ".jpg", ".jpeg", ".bmp" })
                            {
                                string img = Path.ChangeExtension(f, ext);
                                if (File.Exists(img)) { File.Copy(img, Path.Combine(lib.OutfitsDir, last + ext), true); break; }
                            }
                        }
                        Reload(); cfg.Outfit = last; cfg.Save();
                        Toast("New Outfit", d.FileNames.Length + " added", 1);
                    }
                    return;
                }
                case "fitDelete":
                {
                    OutfitEntry o = lib.FindOutfit(cfg.Outfit);
                    if (!S2Dialog.Confirm(this, "delete", "delete outfit '" + o.Name + "'?")) return;
                    lib.DeleteOutfit(o);
                    thumbs.Clear();
                    cfg.Outfit = ""; Reload();
                    return;
                }

                // ---- инструменты
                case "cash100k": txtMoney.Text = "100000"; txtMoney.Focus(); return;
                case "cash1m": txtMoney.Text = "1000000"; txtMoney.Focus(); return;
                case "cashMax": txtMoney.Text = "9999999"; txtMoney.Focus(); return;
                case "cashApply":
                {
                    uint v;
                    if (!uint.TryParse(txtMoney.Text, out v)) throw new Exception("enter the amount in digits");
                    if (v > SaveFile.MaxMoney) v = SaveFile.MaxMoney;
                    uint old = 0;
                    EditTarget(delegate(SaveFile sf) { old = sf.Money; sf.SetMoney(v); });
                    Toast("New Cash", string.Format("${0:N0} → ${1:N0}", old, v), 0);
                    return;
                }
                case "nickApply":
                {
                    string n = txtNick.Text;
                    EditTarget(delegate(SaveFile sf) { sf.SetNick(n); });
                    Toast("Name", "you are now " + n.Trim(), 2);
                    return;
                }
                case "gearAll":
                {
                    int added = 0;
                    EditTarget(delegate(SaveFile sf) { added = sf.UnlockAllGear(cfg.OptDlc); });
                    Toast("New Clothing", added > 0 ? "sets unlocked: " + added : "everything is already unlocked", 1);
                    return;
                }
                case "fixCrc":
                    EditTarget(delegate(SaveFile sf) { });
                    Toast("CRC Fixed", "checksum recalculated", 2);
                    return;
                case "undo":
                {
                    SlotInfo s = CurrentSlot();
                    string b = lib.LatestBackup(BackupTag(s));
                    if (b == null) return;
                    if (!S2Dialog.Confirm(this, "undo", "restore '" + TargetName() + "' to backup\n" + Path.GetFileName(b) + "?")) return;
                    File.Copy(b, s.File, true);
                    File.Delete(b);
                    Reload();
                    Toast("Restored", "previous version restored", 2);
                    return;
                }

                // ---- настройки
                case "setGame":
                {
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Title = "Skate 2 port exe";
                        d.Filter = "Programs|*.exe";
                        if (cfg.GameOk) d.InitialDirectory = Path.GetDirectoryName(cfg.GameExe);
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        cfg.GameExe = d.FileName; cfg.Save();
                        if (!cfg.SaveDirOk) FindSaves(true);
                        Reload();
                    }
                    return;
                }
                case "setSaves":
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        d.Description = "folder that contains SKATER.P";
                        if (cfg.SaveDirOk) d.SelectedPath = cfg.SaveDir;
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        cfg.SaveDir = d.SelectedPath; cfg.Save(); Reload();
                    }
                    return;
                }
                case "findSaves": FindSaves(false); return;
                case "tg": OpenUrl(TelegramUrl); return;
                case "dc": OpenUrl(DiscordUrl); return;
                case "keepMinus": cfg.KeepBackups = Math.Max(10, cfg.KeepBackups - 10); cfg.Save(); return;
                case "keepPlus": cfg.KeepBackups = Math.Min(200, cfg.KeepBackups + 10); cfg.Save(); return;
            }
        }

        delegate void SaveEdit(SaveFile sf);

        // правка выбранного сейва с бэкапом
        void EditTarget(SaveEdit edit)
        {
            SlotInfo s = CurrentSlot();
            if (s == null || !File.Exists(s.File)) throw new Exception("save not found");
            if (gameRunning && s.Live) throw new Exception("the game is running - close it or it will overwrite the save");
            SaveFile sf = SaveFile.Load(s.File);
            edit(sf);
            lib.Backup(s.File, BackupTag(s));
            sf.WriteTo(s.File);
            Reload();
            SetStatus("written to '" + TargetName() + "'", Pal.Good);
        }

        void SaveOutfitFrom(string file, string label)
        {
            if (!File.Exists(file)) throw new Exception("save not found");
            SaveFile sf = SaveFile.Load(file);
            byte[] blob = sf.GetOutfit();
            string def = sf.Nick.Length > 0 ? sf.Nick + " fit" : "outfit";
            string n = S2Dialog.Ask(this, "new outfit", "name this outfit (" + label + ")?", def, 40);
            if (n == null) return;
            string name = lib.AddOutfit(blob, n);
            Reload(); cfg.Outfit = name; cfg.Save();
            Toast("New Outfit", "'" + name + "' is in the wardrobe · you can attach a photo", 1);
        }

        static string NickOrDefault(string file, string def)
        {
            try { SaveFile s = SaveFile.Load(file); if (s.Nick.Length > 0) return s.Nick; } catch { }
            return def;
        }

        void FindSaves(bool quiet)
        {
            Cursor = Cursors.WaitCursor;
            List<string> found = Library.FindSaves(cfg.GameExe);
            Cursor = Cursors.Default;
            if (found.Count == 1)
            {
                cfg.SaveDir = found[0]; cfg.Save(); foundSaveDirs.Clear(); Reload();
                SetStatus("found save: " + found[0], Pal.Good);
            }
            else if (found.Count > 1) { foundSaveDirs = found; SetStatus("found several - pick one", Pal.Warn); }
            else if (!quiet) SetStatus("SKATER.P not found - set the folder manually", Pal.Warn);
        }

        static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        static void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new Exception("folder missing");
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        // ============================================================ запуск
        void Play()
        {
            if (gameRunning) return;
            string problem = PlayProblem();
            if (problem != null) throw new Exception(problem);

            SlotInfo s = CurrentSlot();
            OutfitEntry o = cfg.Outfit.Length > 0 ? lib.FindOutfit(cfg.Outfit) : null;
            bool edits = o != null || cfg.OptMoney || cfg.OptUnlock;
            string gameSave = cfg.GameSave;

            if (!s.Live || edits)
            {
                string src = s.Live ? gameSave : s.File;
                SaveFile sf = SaveFile.Load(src);
                if (o != null) sf.ApplyOutfit(File.ReadAllBytes(o.File));
                if (cfg.OptMoney && sf.HasMoney) sf.SetMoney(9999999);
                if (cfg.OptUnlock && sf.HasInventory) sf.UnlockAllGear(cfg.OptDlc);
                if (File.Exists(gameSave)) lib.Backup(gameSave, "game");
                sf.WriteTo(gameSave);
            }

            ProcessStartInfo psi = new ProcessStartInfo(cfg.GameExe);
            psi.WorkingDirectory = Path.GetDirectoryName(cfg.GameExe);
            psi.UseShellExecute = true;
            game = Process.Start(psi);
            gameRunning = true;
            gameStarted = DateTime.Now;
            runningSlot = s.Live ? null : s.Name;
            if (game != null)
            {
                game.EnableRaisingEvents = true;
                game.SynchronizingObject = this;
                game.Exited += delegate { OnGameExit(); };
            }
            else
            {
                gameRunning = false;
                SetStatus("game launched, but its exit can't be tracked - hit 'grab progress' after playing", Pal.Warn);
            }
            Toast("Drop In", (s.Live ? "game save" : "slot '" + s.Name + "'") + (o != null ? " · '" + o.Name + "'" : ""), 4);
            SetStatus("game launched", Pal.Good);
            WindowState = FormWindowState.Minimized;
        }

        void OnGameExit()
        {
            gameRunning = false;
            double secs = (DateTime.Now - gameStarted).TotalSeconds;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (secs < 15)
            {
                SetStatus("game closed after " + (int)secs + " s - if that was a launcher, hit 'grab progress' after playing", Pal.Warn);
                Reload();
                return;
            }
            if (cfg.OptSync && runningSlot != null) SyncBack(false);
            else { Reload(); SetStatus("welcome back", Pal.Cream); }
        }

        void SyncBack(bool manual)
        {
            string slotName = runningSlot ?? (CurrentSlot() != null && !CurrentSlot().Live ? CurrentSlot().Name : null);
            SlotInfo s = slotName != null ? lib.FindSlot(slotName) : null;
            if (s == null) throw new Exception("no slot selected");
            if (!File.Exists(cfg.GameSave)) throw new Exception("no SKATER.P in the game folder");
            SaveFile.Load(cfg.GameSave);   // проверка, что файл целый
            lib.Backup(s.File, BackupTag(s));
            File.Copy(cfg.GameSave, s.File, true);
            Reload();
            Toast("Progress Saved", "progress saved to slot '" + s.Name + "'", 2);
            SetStatus("progress saved to '" + s.Name + "'" + (manual ? "" : " · welcome back"), Pal.Good);
        }

        void Grab()
        {
            SlotInfo s = CurrentSlot();
            if (s == null || s.Live) throw new Exception("pick a slot");
            if (!S2Dialog.Confirm(this, "grab progress", "copy the current game save into slot '" + s.Name + "'?\nthe old slot goes to backups.")) return;
            runningSlot = null;
            SyncBack(true);
        }
    }
}
