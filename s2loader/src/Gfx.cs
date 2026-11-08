// s2 loader — ресурсы, шрифты и рисование
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace S2Loader
{
    static class Pal
    {
        public static readonly Color Ink = Color.FromArgb(36, 26, 18);
        public static readonly Color InkSoft = Color.FromArgb(110, 92, 70);
        public static readonly Color Paper = Color.FromArgb(240, 230, 208);
        public static readonly Color Cream = Color.FromArgb(243, 232, 206);
        public static readonly Color CreamDim = Color.FromArgb(170, 243, 232, 206);
        public static readonly Color Maroon = Color.FromArgb(110, 34, 24);
        public static readonly Color Orange = Color.FromArgb(232, 132, 42);
        public static readonly Color OrangeHi = Color.FromArgb(250, 176, 84);
        public static readonly Color Dim = Color.FromArgb(150, 132, 110);
        public static readonly Color Good = Color.FromArgb(150, 196, 96);
        public static readonly Color Bad = Color.FromArgb(226, 84, 60);
        public static readonly Color Warn = Color.FromArgb(236, 186, 30);
        public static readonly Color BtnA = Color.FromArgb(96, 170, 44);
        public static readonly Color BtnB = Color.FromArgb(206, 52, 40);
        public static readonly Color BtnX = Color.FromArgb(44, 112, 214);
        public static readonly Color BtnY = Color.FromArgb(236, 186, 30);
    }

    static class Res
    {
        public static Bitmap Bg1, Bg2, Card, Paper, MaroonTex, Band, Logo;
        static PrivateFontCollection pfc;
        static IntPtr fontMem = IntPtr.Zero;
        static FontFamily presto;
        static string bodyFamily;

        static byte[] Bytes(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("S2." + name))
            {
                if (s == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        static Bitmap Img(string name, int w, int h)
        {
            byte[] b = Bytes(name);
            if (b != null)
            {
                using (MemoryStream ms = new MemoryStream(b))
                using (Image im = Image.FromStream(ms))
                    return new Bitmap(im);
            }
            Bitmap fb = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(fb)) g.Clear(Color.FromArgb(30, 22, 16));
            return fb;
        }

        public static void Load()
        {
            Bg1 = Img("bg1.jpg", 1200, 740);
            Bg2 = Img("bg2.jpg", 1200, 740);
            Card = Img("card.png", 900, 620);
            Paper = Img("paper.png", 640, 48);
            MaroonTex = Img("maroon.png", 640, 30);
            Band = Img("band.png", 1000, 84);
            Logo = Img("logo.png", 314, 80);

            byte[] f = Bytes("HelveticaPresto.ttf");
            if (f != null)
            {
                pfc = new PrivateFontCollection();
                fontMem = Marshal.AllocCoTaskMem(f.Length);
                Marshal.Copy(f, 0, fontMem, f.Length);
                pfc.AddMemoryFont(fontMem, f.Length);
                if (pfc.Families.Length > 0) presto = pfc.Families[0];
            }
            HashSet<string> inst = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (InstalledFontCollection ifc = new InstalledFontCollection())
                foreach (FontFamily ff in ifc.Families) inst.Add(ff.Name);
            bodyFamily = "Arial";
            foreach (string n in new string[] { "Segoe UI", "Tahoma", "Arial" })
                if (inst.Contains(n)) { bodyFamily = n; break; }
        }

        // шрифт игры (только латиница)
        public static Font Presto(float px)
        {
            if (presto != null) return new Font(presto, px, FontStyle.Regular, GraphicsUnit.Pixel);
            return new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static Font Body(float px, bool bold)
        {
            return new Font(bodyFamily, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // Presto не умеет кириллицу — для пользовательских строк выбираем шрифт по содержимому
        public static bool PrestoCan(string s)
        {
            if (presto == null) return false;
            foreach (char c in s) if (c > 0xFF) return false;
            return true;
        }
    }

    static class Gfx
    {
        public static StringFormat Center()
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            return sf;
        }

        public static StringFormat Left(bool vcenter)
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Near;
            sf.LineAlignment = vcenter ? StringAlignment.Center : StringAlignment.Near;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            return sf;
        }

        public static StringFormat Right(bool vcenter)
        {
            StringFormat sf = Left(vcenter);
            sf.Alignment = StringAlignment.Far;
            return sf;
        }

        public static Color A(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        public static void TextShadow(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.DrawString(s, f, sh, new RectangleF(r.X + 2, r.Y + 2, r.Width, r.Height), sf);
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        public static void Fill(Graphics g, RectangleF r, Color c)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        public static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
        {
            using (Pen p = new Pen(c, w)) g.DrawLine(p, x1, y1, x2, y2);
        }

        // уголки-скобки
        public static void Brackets(Graphics g, RectangleF r, Color c, float len, float w, float pad)
        {
            using (Pen p = new Pen(c, w))
            {
                float x0 = r.X - pad, y0 = r.Y - pad, x1 = r.Right + pad, y1 = r.Bottom + pad;
                g.DrawLine(p, x0, y0, x0 + len, y0); g.DrawLine(p, x0, y0, x0, y0 + len);
                g.DrawLine(p, x1, y0, x1 - len, y0); g.DrawLine(p, x1, y0, x1, y0 + len);
                g.DrawLine(p, x0, y1, x0 + len, y1); g.DrawLine(p, x0, y1, x0, y1 - len);
                g.DrawLine(p, x1, y1, x1 - len, y1); g.DrawLine(p, x1, y1, x1, y1 - len);
            }
        }

        public static void Strip(Graphics g, Bitmap tex, RectangleF r)
        {
            g.DrawImage(tex, r, new RectangleF(0, 0, Math.Min(tex.Width, r.Width), tex.Height), GraphicsUnit.Pixel);
        }

        // карточка: тёмная подложка + рамка-уголки
        public static void Card(Graphics g, RectangleF r)
        {
            for (int i = 4; i >= 1; i--)
                Fill(g, new RectangleF(r.X - i * 2 + 5, r.Y - i * 2 + 8, r.Width + i * 4, r.Height + i * 4), Color.FromArgb(24, 0, 0, 0));
            g.DrawImage(Res.Card, r, new RectangleF(0, 0, Math.Min(Res.Card.Width, r.Width), Math.Min(Res.Card.Height, r.Height)), GraphicsUnit.Pixel);
            using (Pen p = new Pen(Color.FromArgb(50, 255, 240, 220), 1)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            Brackets(g, r, Color.FromArgb(220, 255, 245, 230), 10, 1.6f, 6);
        }

        // бордовая шапка + заголовок шрифтом игры
        public static void Header(Graphics g, RectangleF r, string title, Font f, string right, Font rf)
        {
            Strip(g, Res.MaroonTex, r);
            Text(g, title, f, Color.White, new RectangleF(r.X + 10, r.Y, r.Width - 20, r.Height + 2), Left(true));
            if (!string.IsNullOrEmpty(right))
                Text(g, right, rf, Color.FromArgb(220, 243, 232, 206), new RectangleF(r.X + 10, r.Y, r.Width - 20, r.Height), Right(true));
        }

        // кнопка геймпада
        public static void PadButton(Graphics g, float cx, float cy, float d, string key, Color col, bool on, Font f)
        {
            if (!on) col = Color.FromArgb(90, 90, 86);
            RectangleF c = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            using (SolidBrush dark = new SolidBrush(Color.FromArgb(200, 10, 8, 6))) g.FillEllipse(dark, c.X - 2, c.Y - 2, c.Width + 4, c.Height + 4);
            using (LinearGradientBrush b = new LinearGradientBrush(c, Mix(col, Color.White, 0.3f), Mix(col, Color.Black, 0.35f), 90f))
                g.FillEllipse(b, c);
            using (SolidBrush gl = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                g.FillEllipse(gl, c.X + d * 0.18f, c.Y + d * 0.08f, d * 0.64f, d * 0.42f);
            Text(g, key, f, Color.White, new RectangleF(c.X, c.Y + 1, c.Width, c.Height), Center());
        }

        // галочка-квадратик в духе журнальной анкеты
        public static void Check(Graphics g, RectangleF r, bool on, bool hot)
        {
            Fill(g, r, Color.FromArgb(hot ? 255 : 235, 250, 244, 228));
            using (Pen p = new Pen(Pal.Ink, 1.5f)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            if (on)
            {
                using (Pen p = new Pen(Pal.Maroon, 3f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, r.X + 3, r.Y + r.Height * 0.55f, r.X + r.Width * 0.42f, r.Bottom - 3);
                    g.DrawLine(p, r.X + r.Width * 0.42f, r.Bottom - 3, r.Right + 2, r.Y - 2);
                }
            }
        }

        // сегменты прогресса (как полоска на экранах журнала)
        public static void Segments(Graphics g, RectangleF r, int segs, float value)
        {
            float gap = 4, sw = (r.Width - gap * (segs - 1)) / segs;
            float filled = value * segs;
            for (int i = 0; i < segs; i++)
            {
                RectangleF s = new RectangleF(r.X + i * (sw + gap), r.Y, sw, r.Height);
                Fill(g, s, Color.FromArgb(80, 80, 28, 6));
                float f = Math.Max(0, Math.Min(1, filled - i));
                if (f > 0)
                    using (LinearGradientBrush fb = new LinearGradientBrush(s, Color.FromArgb(255, 250, 240), Color.FromArgb(222, 207, 178), 90f))
                        g.FillRectangle(fb, new RectangleF(s.X, s.Y, s.Width * f, s.Height));
            }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------ иконки
        public static void IconTee(Graphics g, RectangleF r, Color tee, Color art)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            PointF[] pts = {
                new PointF(x + w * 0.34f, y + h * 0.10f), new PointF(x + w * 0.42f, y + h * 0.16f),
                new PointF(x + w * 0.58f, y + h * 0.16f), new PointF(x + w * 0.66f, y + h * 0.10f),
                new PointF(x + w * 0.96f, y + h * 0.28f), new PointF(x + w * 0.86f, y + h * 0.44f),
                new PointF(x + w * 0.76f, y + h * 0.38f), new PointF(x + w * 0.76f, y + h * 0.94f),
                new PointF(x + w * 0.24f, y + h * 0.94f), new PointF(x + w * 0.24f, y + h * 0.38f),
                new PointF(x + w * 0.14f, y + h * 0.44f), new PointF(x + w * 0.04f, y + h * 0.28f) };
            using (SolidBrush b = new SolidBrush(tee)) g.FillPolygon(b, pts);
            using (Pen p = new Pen(Color.FromArgb(110, 0, 0, 0), 1)) g.DrawPolygon(p, pts);
            float cx = x + w * 0.5f, cy = y + h * 0.5f, s = w * 0.12f;
            using (Pen bone = new Pen(art, Math.Max(1.5f, s * 0.35f)))
            {
                bone.StartCap = LineCap.Round; bone.EndCap = LineCap.Round;
                g.DrawLine(bone, cx - s * 1.5f, cy + s * 0.6f, cx + s * 1.5f, cy + s * 2.2f);
                g.DrawLine(bone, cx + s * 1.5f, cy + s * 0.6f, cx - s * 1.5f, cy + s * 2.2f);
            }
            using (SolidBrush b = new SolidBrush(art))
            {
                g.FillEllipse(b, cx - s, cy - s * 1.1f, s * 2, s * 1.8f);
                g.FillRectangle(b, cx - s * 0.6f, cy + s * 0.4f, s * 1.2f, s * 0.6f);
            }
            using (SolidBrush e = new SolidBrush(tee))
            {
                g.FillEllipse(e, cx - s * 0.65f, cy - s * 0.45f, s * 0.5f, s * 0.5f);
                g.FillEllipse(e, cx + s * 0.15f, cy - s * 0.45f, s * 0.5f, s * 0.5f);
            }
        }

        public static void IconDisk(Graphics g, RectangleF r, Color body, Color label)
        {
            RectangleF d = new RectangleF(r.X + r.Width * 0.16f, r.Y + r.Height * 0.12f, r.Width * 0.68f, r.Height * 0.76f);
            Fill(g, d, body);
            using (Pen pen = new Pen(label, 1.5f)) g.DrawRectangle(pen, d.X, d.Y, d.Width, d.Height);
            Fill(g, new RectangleF(d.X + d.Width * 0.18f, d.Y + d.Height * 0.5f, d.Width * 0.64f, d.Height * 0.42f), label);
            Fill(g, new RectangleF(d.X + d.Width * 0.26f, d.Y + 3, d.Width * 0.44f, d.Height * 0.3f), Color.FromArgb(150, 150, 146));
            using (Pen ink = new Pen(body, 1.2f))
                for (int i = 0; i < 3; i++)
                {
                    float yy = d.Y + d.Height * (0.6f + i * 0.1f);
                    g.DrawLine(ink, d.X + d.Width * 0.26f, yy, d.X + d.Width * 0.74f, yy);
                }
        }

        public static void IconDeck(Graphics g, RectangleF r, Color deck, Color grip)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.RotateTransform(-28);
            float L = r.Width * 0.95f, W = r.Height * 0.26f;
            using (GraphicsPath p = Round(new RectangleF(-L / 2, -W / 2, L, W), W / 2 - 0.5f))
            {
                using (SolidBrush b = new SolidBrush(deck)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1)) g.DrawPath(pen, p);
            }
            Fill(g, new RectangleF(-L / 2 + W * 0.6f, -W / 2 + 2, L - W * 1.2f, W - 4), grip);
            using (SolidBrush wb = new SolidBrush(Pal.OrangeHi))
            {
                float wr = W * 0.32f;
                foreach (float x in new float[] { -L * 0.3f, L * 0.3f })
                {
                    g.FillEllipse(wb, x - wr * 2, W / 2 - 1, wr * 2, wr * 2);
                    g.FillEllipse(wb, x, W / 2 - 1, wr * 2, wr * 2);
                }
            }
            g.Restore(st);
        }

        public static void IconLock(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (Pen p = new Pen(c, Math.Max(1.5f, w * 0.12f)))
                g.DrawArc(p, r.X + w * 0.28f, r.Y + h * 0.08f, w * 0.44f, h * 0.52f, 180, 180);
            Fill(g, new RectangleF(r.X + w * 0.18f, r.Y + h * 0.38f, w * 0.64f, h * 0.52f), c);
        }

        public static void IconPlay(Graphics g, RectangleF r, Color c)
        {
            PointF[] tri = { new PointF(r.X + r.Width * 0.28f, r.Y + r.Height * 0.2f),
                             new PointF(r.X + r.Width * 0.28f, r.Y + r.Height * 0.8f),
                             new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.5f) };
            using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, tri);
        }
    }
}
