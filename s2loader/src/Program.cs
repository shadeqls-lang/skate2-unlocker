// s2 loader — точка входа
using System;
using System.IO;
using System.Windows.Forms;

namespace S2Loader
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Something went wrong:\n" + e.Exception.Message, "s2 loader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Res.Load();
            string data = Path.Combine(Application.StartupPath, "s2loader_data");
            Application.Run(new MainForm(data));
        }
    }
}
