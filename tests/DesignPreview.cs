using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace AzerothSync
{
    static class DesignProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new MainForm(false,true))
            {
                if(args.Length>0 && args[0]=="--capture")
                {
                    form.Show();Application.DoEvents();
                    string root=AppDomain.CurrentDomain.BaseDirectory;
                    Capture(form,Path.Combine(root,"docs/screenshot.png"));
                    Call(form,"SelectPage",Field(form,"pathsPage"),Field(form,"navPaths"));
                    Capture(form,Path.Combine(root,"docs/design/paths.png"));
                    Call(form,"OpenWizard");Set(form,"wizardStep",1);Call(form,"RenderWizard");
                    Capture(form,Path.Combine(root,"docs/design/wizard-cloud.png"));
                    Set(form,"wizardStep",2);Call(form,"RenderWizard");
                    form.Size=new Size(940,760);
                    Capture(form,Path.Combine(root,"docs/design/wizard-first-sync.png"));
                    form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.FixConfigFolder,Title="Game config folder not found",Message=@"AddOns and addon settings cannot be synced until the benilla-config folder is chosen. The installer default is C:\Git\wenilla\benilla-config, or benilla-config next to the game.",ConfigMissing=true,Skipped=new List<string>{"AddOns","Addon settings"} };
                    Call(form,"CheckWizardAdvice",new object[]{null});WaitCheck(form);
                    Capture(form,Path.Combine(root,"docs/design/wizard-config-missing.png"));
                    form.previewAdvice=null;Call(form,"CheckWizardAdvice",new object[]{null});WaitCheck(form);
                    form.Size=new Size(940,760);Set(form,"wizardStep",1);Call(form,"RenderWizard");
                    Capture(form,Path.Combine(root,"docs/design/wizard-cloud-minimum.png"));
                    Set(form,"wizardStep",2);Call(form,"RenderWizard");
                    Capture(form,Path.Combine(root,"docs/design/wizard-first-sync-minimum.png"));
                    Call(form,"SelectPage",Field(form,"pathsPage"),Field(form,"navPaths"));
                    Capture(form,Path.Combine(root,"docs/design/paths-minimum.png"));
                }
                else Application.Run(form);
            }
        }
        const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        static object Field(object o,string n){return o.GetType().GetField(n,Hidden).GetValue(o);}
        static void Set(object o,string n,object v){o.GetType().GetField(n,Hidden).SetValue(o,v);}
        static void Call(object o,string n,params object[] args){o.GetType().GetMethod(n,Hidden).Invoke(o,args);}
        static void WaitCheck(MainForm form)
        {
            DateTime deadline=DateTime.UtcNow.AddSeconds(5);
            while((bool)Field(form,"wizardChecking") && DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(10);}
        }
        static void Capture(Form form,string file)
        {
            WaitCheck((MainForm)form);Application.DoEvents();form.Refresh();Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(file);}
            Console.WriteLine("Captured "+file);
        }
    }
}
