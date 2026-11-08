// s2 loader — модальные окна в стиле журнальной карточки
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace S2Loader
{
    class S2Dialog : Form
    {
        string title, message;
        bool input;
        TextBox box;
        RectangleF okR, cancelR;
        string hover;
        Font fTitle, fBody, fBtn, fKey;

        public string Value { get { return box != null ? box.Text : ""; } }

        S2Dialog(Form owner, string title, string message, string value, bool input, int maxLen)
        {
            this.title = title; this.message = message; this.input = input;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(460, input ? 214 : 190);
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            fTitle = Res.Presto(22); fBody = Res.Body(14, false); fBtn = Res.Presto(18); fKey = Res.Presto(12);

            okR = new RectangleF(ClientSize.Width - 250, ClientSize.Height - 46, 110, 30);
            cancelR = new RectangleF(ClientSize.Width - 130, ClientSize.Height - 46, 110, 30);

            if (input)
            {
                box = new TextBox();
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Pal.Paper;
                box.ForeColor = Pal.Ink;
                box.Font = Res.Body(17, false);
                box.MaxLength = maxLen;
                box.Text = value ?? "";
                box.SetBounds(30, 116, ClientSize.Width - 60, 26);
                Controls.Add(box);
            }
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (box != null) { box.Focus(); box.SelectAll(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            RectangleF all = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
            g.DrawImage(Res.Card, all, new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), GraphicsUnit.Pixel);
            using (Pen p = new Pen(Color.FromArgb(120, 255, 240, 220), 1)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            Gfx.Brackets(g, new RectangleF(8, 8, ClientSize.Width - 16, ClientSize.Height - 16), Color.FromArgb(200, 255, 245, 230), 8, 1.4f, 0);

            Font tf = Res.PrestoCan(title) ? fTitle : Res.Body(17, true);
            Gfx.Header(g, new RectangleF(20, 20, ClientSize.Width - 40, 32), title, tf, null, null);
            Gfx.Text(g, message, fBody, Pal.Cream, new RectangleF(24, 60, ClientSize.Width - 48, input ? 46 : 74), new StringFormat());

            if (input)
            {
                RectangleF pr = new RectangleF(20, 108, ClientSize.Width - 40, 40);
                g.DrawImage(Res.Paper, pr, new RectangleF(0, 0, pr.Width, Res.Paper.Height), GraphicsUnit.Pixel);
            }
            DrawBtn(g, okR, "A", Pal.BtnA, input ? "ok" : "yes", hover == "ok");
            DrawBtn(g, cancelR, "B", Pal.BtnB, input ? "cancel" : "no", hover == "cancel");
        }

        void DrawBtn(Graphics g, RectangleF r, string key, Color col, string label, bool hot)
        {
            if (hot) Gfx.Fill(g, r, Color.FromArgb(40, 255, 220, 170));
            Gfx.PadButton(g, r.X + 14, r.Y + r.Height / 2, 22, key, col, true, fKey);
            Gfx.Text(g, label, fBtn, hot ? Pal.OrangeHi : Pal.Cream, new RectangleF(r.X + 30, r.Y, r.Width - 30, r.Height), Gfx.Left(true));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string h = okR.Contains(e.Location) ? "ok" : (cancelR.Contains(e.Location) ? "cancel" : null);
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (okR.Contains(e.Location)) { DialogResult = DialogResult.OK; Close(); }
            else if (cancelR.Contains(e.Location)) { DialogResult = DialogResult.Cancel; Close(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter) { DialogResult = DialogResult.OK; Close(); return true; }
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public static string Ask(Form owner, string title, string message, string value, int maxLen)
        {
            using (S2Dialog d = new S2Dialog(owner, title, message, value, true, maxLen))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
        }

        public static bool Confirm(Form owner, string title, string message)
        {
            using (S2Dialog d = new S2Dialog(owner, title, message, null, false, 0))
                return d.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
