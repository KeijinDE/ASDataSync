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
        static void WaitCheck(MainForm form)
        {
            DateTime deadline=DateTime.UtcNow.AddSeconds(5);
            while((bool)Field(form,"wizardChecking") && DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(10);}
            Check(!(bool)Field(form,"wizardChecking"),"Advice check timed out");
        }
        static void Render(MainForm form)
        {
            Call(form,"RenderWizard");
            if((int)Field(form,"wizardStep")==2){WaitCheck(form);Call(form,"CheckWizardAdvice",new object[]{null});WaitCheck(form);}
        }
        static void WaitCloud(MainForm form)
        {
            DateTime deadline=DateTime.UtcNow.AddSeconds(5);
            do { Application.DoEvents();System.Threading.Thread.Sleep(10); }
            while(((bool)Field(form,"cloudScanning") || (bool)Field(form,"cloudInspecting")) && DateTime.UtcNow<deadline);
            Check(!(bool)Field(form,"cloudScanning") && !(bool)Field(form,"cloudInspecting"),"Cloud check timed out");
        }
        static void CloudChecks()
        {
            using(var form=new MainForm(false,true))
            {
                int calls=0;
                form.cloudScanCheck=delegate(bool refresh){calls++;throw new Exception("Preview scanned");};
                form.cloudInspectCheck=delegate(string path,List<CloudCandidate> clouds){calls++;throw new Exception("Preview inspected");};
                form.Show();Application.DoEvents();Call(form,"OpenWizard");
                form.GetType().GetField("wizardStep",Hidden).SetValue(form,1);Render(form);
                var page=(Control)Field(form,"wizardPage");
                Descendants(page).OfType<Button>().Single(b=>b.Text=="Search again").PerformClick();
                Call(form,"StartCloudScan",true);Call(form,"RefreshPathChecks");
                Check(calls==0,"Preview invoked cloud IO");
                Console.WriteLine("PASS Preview never scans or inspects cloud folders");

                // Switch only this test form to real orchestration with injected IO and temporary paths.
                string temp=Path.Combine(Path.GetTempPath(),"AzerothSyncLook-"+Guid.NewGuid().ToString("N"));
                var settings=(Settings)Field(form,"settings");settings.StateDir=temp;
                foreach(string name in new[]{"tbCloud","tbGame","tbSave","tbConfig"})((TextBox)Field(form,name)).Text=Path.Combine(temp,name);
                form.GetType().GetField("designPreview",Hidden).SetValue(form,false);
                form.GetType().GetField("cloudWindowShown",Hidden).SetValue(form,false);
                int uiThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
                int scans=0, forced=0, inspections=0;
                form.cloudScanCheck=delegate(bool refresh)
                {
                    Check(System.Threading.Thread.CurrentThread.ManagedThreadId!=uiThread,"Scan on UI thread");
                    System.Threading.Interlocked.Increment(ref scans);
                    if(refresh)System.Threading.Interlocked.Increment(ref forced);
                    return new List<CloudCandidate>{new CloudCandidate{Name="Sample",SuggestedDir=Path.Combine(temp,"cloud")}};
                };
                form.cloudInspectCheck=delegate(string path,List<CloudCandidate> clouds)
                {
                    Check(System.Threading.Thread.CurrentThread.ManagedThreadId!=uiThread,"Inspection on UI thread");
                    System.Threading.Interlocked.Increment(ref inspections);
                    return new CloudInfoResult{Content=CloudContent.Empty,Message="Sample empty"};
                };
                try
                {
                    Call(form,"RefreshPathChecks");
                    Check(scans==0 && ((Dictionary<int,Label>)Field(form,"pathChecks"))[1].Text=="Checking…","Cold startup scanned before Shown");
                    form.GetType().GetField("cloudWindowShown",Hidden).SetValue(form,true);
                    Call(form,"StartCloudScan",false);WaitCloud(form);Render(form);
                    Check(scans==1 && inspections==1,"Warm scan/inspection was not cached");
                    Render(form);Render(form);Check(scans==1 && inspections==1,"Redraw rescanned");
                    Descendants(page).OfType<Button>().Single(b=>b.Text=="Search again").PerformClick();WaitCloud(form);
                    Check(scans==2 && forced==1,"Search again did not force detection");
                    Console.WriteLine("PASS Search again forces a background rescan; redraw uses cached results");

                    var started=new System.Threading.ManualResetEvent(false);
                    var release=new System.Threading.ManualResetEvent(false);
                    var finished=new System.Threading.ManualResetEvent(false);
                    var scan=form.cloudScanCheck;
                    form.cloudScanCheck=delegate(bool refresh)
                    {
                        started.Set();release.WaitOne();finished.Set();
                        return new List<CloudCandidate>{new CloudCandidate{Name="Stale"}};
                    };
                    Call(form,"StartCloudScan",true);Render(form);
                    Check(started.WaitOne(2000),"Slow scan did not start");
                    Check(Descendants(page).OfType<Label>().Any(l=>l.Text=="Looking for cloud apps…"),"Scanning message missing");
                    Check(Descendants(page).OfType<Button>().Where(b=>b.Text.StartsWith("Use ") || b.Text=="Search again").All(b=>!b.Enabled),"Scan controls remained enabled");
                    Check(Descendants(page).OfType<Button>().Single(b=>b.Text=="Choose another folder...").Enabled,"Folder picker disabled during scan");
                    Call(form,"SetBusy",true);int seq=(int)Field(form,"cloudScanSequence");
                    Call(form,"StartCloudScan",true);Check(seq==(int)Field(form,"cloudScanSequence"),"Scan started while busy");
                    form.cloudScanCheck=scan;Call(form,"SetBusy",false);WaitCloud(form);
                    object current=Field(form,"cloudCandidates");release.Set();Check(finished.WaitOne(2000),"Old scan did not finish");
                    for(int i=0;i<20;i++){Application.DoEvents();System.Threading.Thread.Sleep(10);}
                    Check(object.ReferenceEquals(current,Field(form,"cloudCandidates")),"Stale scan replaced newer result");
                    Console.WriteLine("PASS Slow scan leaves UI responsive, respects busy state and discards stale results");
                    started.Reset();release.Reset();finished.Reset();
                    var inspect=form.cloudInspectCheck;
                    form.cloudInspectCheck=delegate(string path,List<CloudCandidate> clouds)
                    {
                        started.Set();release.WaitOne();finished.Set();
                        return new CloudInfoResult{Content=CloudContent.Unreachable,Message="Stale inspection"};
                    };
                    Call(form,"InvalidateCloudInfo");Call(form,"RefreshPathChecks");
                    Check(started.WaitOne(2000),"Slow inspection did not start");
                    form.cloudInspectCheck=inspect;
                    ((TextBox)Field(form,"tbCloud")).Text=Path.Combine(temp,"new-cloud");
                    Call(form,"RefreshPathChecks");WaitCloud(form);
                    object info=Field(form,"cloudInfo");release.Set();Check(finished.WaitOne(2000),"Old inspection did not finish");
                    for(int i=0;i<20;i++){Application.DoEvents();System.Threading.Thread.Sleep(10);}
                    Check(object.ReferenceEquals(info,Field(form,"cloudInfo")),"Old folder inspection replaced current result");
                    Console.WriteLine("PASS Cold startup waits for Shown; path changes discard stale inspection results");
                }
                finally
                {
                    form.Close();
                    if(Directory.Exists(temp))Directory.Delete(temp,true);
                }
            }
        }
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
                Check(Descendants(group).OfType<Button>().Count()==8,"Expected four pickers and four Open buttons");
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
            using(var form=new MainForm(false,true))
            {
                form.Show();Application.DoEvents();
                Check(!((bool)Field(form,"wizardActive")),"Preview opened setup automatically");
                Call(form,"OpenWizard");
                Check((bool)Field(form,"wizardActive"),"Guide did not open");
                var page=(Control)Field(form,"wizardPage");
                Check(Descendants(page).OfType<Label>().Any(l=>l.Text.Contains("Step 1 of 4")),"Step indicator missing");
                Call(form,"SetBusy",true);Check(!page.Enabled,"Wizard remains enabled during a sync");
                Call(form,"SetBusy",false);
                foreach(FirstSync action in Enum.GetValues(typeof(FirstSync)))
                {
                    form.previewAdvice=new FirstSyncAdvice { Action=action,Title="Sample advice",Message="Sample explanation",LocalNewest=DateTime.Now,CloudTime=DateTime.Now,Areas=new List<string>{"Save game","AddOns"} };
                    form.GetType().GetField("wizardStep",Hidden).SetValue(form,2);Render(form);
                    var buttons=Descendants(page).OfType<Button>().Select(b=>b.Text).ToArray();
                    Check(buttons.Contains("Download to this PC")== (action==FirstSync.DownloadToThisPc),"Wrong download action");
                    Check(buttons.Contains("Upload this PC's progress")== (action==FirstSync.UploadThisPc),"Wrong upload action");
                    Check(buttons.Contains("Keep this PC's progress (upload)")== (action==FirstSync.AskWhichIsNewer),"Missing direction choice");
                    Check(((Button)Field(form,"wizardNext")).Enabled== (action==FirstSync.AlreadyInSync || action==FirstSync.NothingYet),"First sync can be skipped before completion");
                    Check(action==FirstSync.RetryLater || Descendants(page).OfType<Label>().Any(l=>l.Text=="Differs: Save game, AddOns"),"Differing areas missing");
                }
                Console.WriteLine("PASS Wizard exposes every advice action and locks during work");
                var settings=(Settings)Field(form,"settings");
                ((TextBox)Field(form,"tbCloud")).Text="";Call(form,"CompleteSetup",false);Check(!settings.SetupDone,"Empty cancelled setup marked complete");
                ((TextBox)Field(form,"tbCloud")).Text=@"X:\Sample\AzerothSync";Call(form,"CompleteSetup",false);Check(settings.SetupDone,"Configured cancelled setup not marked complete");
                Check(!Directory.Exists(settings.StateDir),"Wizard preview wrote settings");
                Console.WriteLine("PASS Setup close policy and preview isolation");
                Call(form,"OpenWizard");
                form.previewClouds=new List<CloudCandidate>();
                form.GetType().GetField("wizardStep",Hidden).SetValue(form,1);Render(form);
                Check(Descendants(page).OfType<Label>().Any(l=>l.Text.StartsWith("No cloud app found.")),"Cloud client installation guidance missing");
                Check(Descendants(page).OfType<Button>().Any(b=>b.Text=="Search again"),"Cloud retry missing");
                form.GetType().GetField("wizardStep",Hidden).SetValue(form,2);
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.UploadThisPc,Title="Upload",Message="Sample" };
                form.GetType().GetField("wizardAdvanced",Hidden).SetValue(form,true);Render(form);
                Check(Descendants(page).OfType<Button>().Count(b=>b.Text=="Choose...")==2,"Advanced path pickers missing");
                var pickers=Descendants(page).OfType<Button>().Where(b=>b.Text=="Choose...").Select(b=>b.AccessibleName).ToArray();
                Check(pickers.Contains("Choose save game folder") && pickers.Contains("Choose game config folder"),"Advanced pickers lack distinct accessible names");
                Check(Descendants(page).OfType<Button>().All(b=>!string.IsNullOrWhiteSpace(b.AccessibilityObject.Name)),"Wizard button lacks accessible name");
                Console.WriteLine("PASS Missing cloud client and advanced folder guidance");
                Check(((Dictionary<int,Label>)Field(form,"pathChecks")).Count==4,"Missing visible path checks");
                Console.WriteLine("PASS Four visible path checks and folder actions");
                var checks=(Dictionary<int,Label>)Field(form,"pathChecks");
                var cloudBox=(TextBox)Field(form,"tbCloud");
                checks[1].Text="Pending check";cloudBox.Text=@"X:\Sample\NewCloud";
                var debounce=(System.Windows.Forms.Timer)Field(form,"pathCheckTimer");
                Check(checks[1].Text=="Pending check" && debounce.Enabled && debounce.Interval==400,"Path check ran on a keystroke");
                typeof(Control).GetMethod("OnLeave",Hidden).Invoke(cloudBox,new object[]{EventArgs.Empty});
                Check(checks[1].Text!="Pending check" && !debounce.Enabled,"Leave did not flush path checks");
                Console.WriteLine("PASS Path checks wait for debounce or Leave");
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.UploadThisPc,Title="Upload",Message="Sample",Areas=new List<string>{"AddOns"} };
                Render(form);
                Check(Descendants(page).OfType<Label>().Any(l=>l.Text.Contains("Cloud: empty, nothing uploaded yet")),"Empty cloud explanation missing");
                var upload=Descendants(page).OfType<Button>().Single(b=>b.Text=="Upload this PC's progress");
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.DownloadToThisPc,Title="Download",Message="Changed" };
                upload.PerformClick();WaitCheck(form);
                Check(!(bool)Field(form,"busy") && !(bool)Field(form,"wizardSyncSucceeded"),"Stale action started a sync");
                Check(((Label)Field(form,"wizardFeedback")).Text=="The situation changed, please check again.","Stale advice warning missing");
                Console.WriteLine("PASS Stale first sync advice is refreshed without starting work");
                Descendants(page).OfType<Button>().Single(b=>b.Text=="Download to this PC").PerformClick();WaitCheck(form);
                Check((bool)Field(form,"busy"),"Current advice did not start preview sync");
                DateTime deadline=DateTime.UtcNow.AddSeconds(5);
                while((bool)Field(form,"busy") && DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(10);}
                Check(!(bool)Field(form,"busy") && (bool)Field(form,"wizardSyncSucceeded"),"Successful wizard sync not recorded");
                WaitCheck(form);
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.AskWhichIsNewer,Title="Remaining",Message="Sample",Areas=new List<string>{"Addon settings"} };
                Render(form);
                Check(((Button)Field(form,"wizardNext")).Enabled,"Successful sync leaves Next blocked");
                Check(Descendants(page).OfType<Label>().Any(l=>l.Text=="Still differs: Addon settings"),"Remaining differences missing");
                cloudBox.Text=@"X:\Sample\ChangedAgain";Render(form);
                Check(!((Button)Field(form,"wizardNext")).Enabled,"Path change retained old sync success");
                Console.WriteLine("PASS Successful wizard sync allows Next and lists remaining areas");
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.FixConfigFolder,Title="Game config folder not found",Message="Choose benilla-config.",Skipped=new List<string>{"AddOns","Addon settings"} };Render(form);
                Check(!((Button)Field(form,"wizardNext")).Enabled,"Missing config silently skipped");
                Check(Descendants(page).OfType<Button>().Any(b=>b.Text=="Choose game config folder"),"Direct config picker missing");
                Check(Descendants(page).OfType<Label>().Any(l=>l.Text=="Not synced on this PC: AddOns, Addon settings"),"Skipped areas missing");
                var skip=Descendants(page).OfType<LinkLabel>().Single(l=>l.Text=="Continue without AddOns");
                typeof(LinkLabel).GetMethod("OnLinkClicked",Hidden).Invoke(skip,new object[]{new LinkLabelLinkClickedEventArgs(new LinkLabel.Link())});
                Check(((Button)Field(form,"wizardNext")).Enabled,"Explicit continue did not unlock Next");
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.RetryLater,Title="Files are busy",Message="Wait and retry." };Render(form);
                Check(!((Button)Field(form,"wizardNext")).Enabled,"Busy files allow Next");
                Check(((FlowLayoutPanel)Field(form,"wizardBody")).Controls.OfType<Button>().Select(b=>b.Text).SequenceEqual(new[]{"Check again"}),"Busy files expose extra actions");
                var back=(Button)Field(form,"wizardBack");var next=(Button)Field(form,"wizardNext");var close=back.Parent.Controls.OfType<Button>().Single(b=>b.Text=="Close guide");
                Check(back.TabIndex<close.TabIndex && close.TabIndex<next.TabIndex,"Footer tab order differs from visual order");
                Console.WriteLine("PASS Missing config requires explicit opt out; busy files only offer retry; footer order matches");

                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.UploadThisPc,Title="Upload",Message="Sample",Areas=new List<string>{"Save game"} };Render(form);
                upload=Descendants(page).OfType<Button>().Single(b=>b.Text=="Upload this PC's progress");
                form.previewAdvice=new FirstSyncAdvice { Action=FirstSync.UploadThisPc,Title="Upload",Message="Sample",Areas=new List<string>{"AddOns"} };
                upload.PerformClick();WaitCheck(form);
                Check(!(bool)Field(form,"busy") && ((Label)Field(form,"wizardFeedback")).Text.Contains("situation changed"),"Same action with changed fingerprint started sync");
                object cached=Field(form,"wizardAdvice");
                Descendants(page).OfType<Button>().Single(b=>b.Text.Contains("advanced folders") || b.Text.StartsWith("Advanced:")).PerformClick();
                Check(object.ReferenceEquals(cached,Field(form,"wizardAdvice")) && !(bool)Field(form,"wizardChecking"),"Advanced recomputed advice");
                Console.WriteLine("PASS Fingerprint rejects changed areas with same action; Advanced uses cache");

                Call(form,"SetBusy",true);int sequence=(int)Field(form,"wizardCheckSequence");
                Call(form,"CheckWizardAdvice",new object[]{null});
                Check(sequence==(int)Field(form,"wizardCheckSequence") && !(bool)Field(form,"wizardChecking"),"Advice check started during sync");
                Call(form,"SetBusy",false);
                var started=new System.Threading.ManualResetEvent(false);var release=new System.Threading.ManualResetEvent(false);
                form.previewAdviceCheck=delegate(Settings ignored){started.Set();release.WaitOne();return new FirstSyncAdvice { Title="Old result",Message="Ignored" };};
                Call(form,"CheckWizardAdvice",new object[]{null});
                Check(started.WaitOne(2000),"Background check did not start");
                Check((bool)Field(form,"wizardChecking") && !next.Enabled && Descendants(page).OfType<Label>().Any(l=>l.Text=="Checking your files…"),"Checking state missing");
                Check(!Descendants(page).OfType<Button>().Any(b=>b.Text=="Upload this PC's progress"),"Sync action available while checking");
                form.previewAdviceCheck=null;Call(form,"CheckWizardAdvice",new object[]{null});WaitCheck(form);cached=Field(form,"wizardAdvice");
                release.Set();System.Threading.Thread.Sleep(50);Application.DoEvents();
                Check(object.ReferenceEquals(cached,Field(form,"wizardAdvice")),"Old result replaced newer advice");
                started.Reset();release.Reset();
                form.previewAdviceCheck=delegate(Settings ignored){started.Set();release.WaitOne();return new FirstSyncAdvice { Title="Hidden result",Message="Ignored" };};
                Call(form,"CheckWizardAdvice",new object[]{null});Check(started.WaitOne(2000),"Exit check did not start");
                Call(form,"SelectPage",Field(form,"overviewPage"),Field(form,"navOverview"));release.Set();System.Threading.Thread.Sleep(50);Application.DoEvents();
                Check(Field(form,"wizardAdvice")==null,"Result applied after sidebar exit");form.previewAdviceCheck=null;
                Check(!(bool)Field(form,"wizardActive"),"Sidebar retained wizardActive");
                Call(form,"CheckWizardAdvice",new object[]{null});Check(!(bool)Field(form,"wizardChecking"),"Hidden wizard started check");
                Call(form,"OpenWizard");form.GetType().GetField("wizardStep",Hidden).SetValue(form,1);cloudBox.Text="";Render(form);
                Check(Descendants(page).OfType<Label>().Any(l=>l.Text=="Selected: none yet"),"Empty cloud selection missing");
                Console.WriteLine("PASS Background check keeps UI responsive, rejects older results and stops on sidebar exit");
                form.Close();
            }
            using(var bar=new BronzeProgress())
            {
                bar.Value=65;Check(bar.AccessibilityObject.Value=="65 %","Accessible progress differs from displayed progress");
                Check(!bar.TabStop,"Decorative progress steals keyboard focus");Console.WriteLine("PASS Accessible progress exposes the actual percentage");
            }
            CloudChecks();
            Console.WriteLine("19 passed, 0 failed");return 0;
        }
    }
}
