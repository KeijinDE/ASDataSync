using System;
using System.Windows.Forms;
namespace AzerothSync
{
    static class DesignProgram
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(false,true));
        }
    }
}
