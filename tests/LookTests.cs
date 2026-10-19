using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace AzerothSync
{
    static class LookTests
    {
        static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        static object Field(object o,string name){return o.GetType().GetField(name,Hidden).GetValue(o);}
        static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Hidden).Invoke(o,args);}
        static void Check(bool condition,string text){if(!condition)throw new Exception(text);}
        static IEnumerable<Control> Descendants(Control p){foreach(Control c in p.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
        [STAThread]
        static int Main()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new MainForm(false,true))
            {
                var settings=(Settings)Field(form,"settings");
                settings.StateDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-safety-"+Guid.NewGuid().ToString("N"));
                Call(form,"SaveSettings");Call(form,"FileLog","test");Call(form,"CreateShortcut");
                Check(!Directory.Exists(settings.StateDir),"Preview wrote settings or logs");Console.WriteLine("PASS Preview writes no settings, logs or shortcuts");
                Call(form,"SetBusy",true);
                var group=(Control)Field(form,"settingsGroup");
                Check(!group.Enabled,"Settings not disabled");
                Check(Descendants(group).OfType<Button>().Count()==4,"Expected four pickers");
                Check(Descendants(group).OfType<Button>().All(b=>!b.Enabled),"Picker remained enabled");Console.WriteLine("PASS All path pickers are disabled during work");
                Call(form,"SetBusy",false);
                var rows=(Dictionary<string,AreaRow>)Field(form,"areaRows");
                Call(form,"UpdateAreaState","  Save game: in sync",Level.Good);
                Check((string)Field(rows["Save game"],"state")=="In sync","Known status not mapped");
                Call(form,"UpdateAreaState","  Save game: unknown future engine message",Level.Good);
                Check((string)Field(rows["Save game"],"state")=="See log","Unknown message incorrectly claims success");Console.WriteLine("PASS Unknown engine status never claims successful sync");
                Call(form,"UpdateAreaState","  Save game: the cloud copy does not match the last upload yet",Level.Warn);
                Check((string)Field(rows["Save game"],"state")=="Cloud incomplete","Incomplete state not shown");
                Call(form,"UpdateAreaState","  Save game: cloud kept, your local changes were NOT uploaded",Level.Warn);
                Check((string)Field(rows["Save game"],"state")=="Not uploaded","Unsent progress not shown");Console.WriteLine("PASS Incomplete cloud and unsent progress remain visible warnings");
            }
            using(var bar=new BronzeProgress())
            {
                bar.Value=65;Check(bar.AccessibilityObject.Value=="65 %","Accessible progress differs from displayed progress");
                Check(!bar.TabStop,"Decorative progress steals keyboard focus");Console.WriteLine("PASS Accessible progress exposes the actual percentage");
            }
            Console.WriteLine("5 passed, 0 failed");return 0;
        }
    }
}
