using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AzerothSync
{
    partial class MainForm
    {
        Panel wizardPage;
        FlowLayoutPanel wizardBody;
        Label wizardStepLabel, wizardFeedback;
        BronzeButton wizardBack, wizardNext;
        int wizardStep;
        internal FirstSyncAdvice previewAdvice = null;
        internal List<CloudCandidate> previewClouds = null;
        bool wizardActive, wizardAdvanced;
        bool wizardSyncSucceeded, wizardChecking, wizardWithoutAddons;
        int wizardCheckSequence, wizardRenderedStep = -1;
        FirstSyncAdvice wizardAdvice;
        internal Func<Settings, FirstSyncAdvice> previewAdviceCheck = null;
        readonly System.Windows.Forms.Timer pathCheckTimer = new System.Windows.Forms.Timer { Interval=400 };
        readonly Dictionary<int, Label> pathChecks = new Dictionary<int, Label>();
        readonly Dictionary<int, TextBox> pathBoxes = new Dictionary<int, TextBox>();

        List<CloudCandidate> cloudCandidates;
        CloudInfoResult cloudInfo;
        string cloudInfoPath;
        bool cloudScanning, cloudInspecting, cloudWindowShown;
        int cloudScanSequence, cloudInspectSequence;
        internal Func<bool, List<CloudCandidate>> cloudScanCheck = null;
        internal Func<string, List<CloudCandidate>, CloudInfoResult> cloudInspectCheck = null;

        void InvalidateCloudInfo()
        {
            cloudInspectSequence++;cloudInspecting=false;cloudInfo=null;cloudInfoPath=null;
        }
        void StartCloudScan(bool refresh)
        {
            if(designPreview || busy || !cloudWindowShown || IsDisposed || !IsHandleCreated || cloudScanning)return;
            if(!refresh && cloudCandidates!=null){StartCloudInspection();return;}
            int sequence=++cloudScanSequence;
            cloudScanning=true;InvalidateCloudInfo();
            var scan=cloudScanCheck;
            var thread=new Thread(delegate()
            {
                List<CloudCandidate> result;
                try { result=scan!=null ? scan(refresh) : Setup.FindClouds(refresh); }
                catch(Exception) { result=new List<CloudCandidate>(); }
                if(IsDisposed || Disposing || !IsHandleCreated)return;
                try { BeginInvoke(new Action(delegate
                {
                    if(IsDisposed || Disposing || !IsHandleCreated || busy || sequence!=cloudScanSequence)return;
                    cloudScanning=false;cloudCandidates=result;RefreshPathChecks();
                    if(wizardActive && wizardPage.Visible && wizardStep==1)RenderWizard();
                })); }
                catch(InvalidOperationException) { }
            });
            thread.IsBackground=true;thread.Start();
        }
        void StartCloudInspection()
        {
            if(designPreview || busy || cloudScanning || cloudCandidates==null || cloudInspecting || !cloudWindowShown || IsDisposed || !IsHandleCreated)return;
            string path=tbCloud.Text.Trim();
            if(cloudInfo!=null && cloudInfoPath==path)return;
            int sequence=++cloudInspectSequence;
            cloudInspecting=true;
            var candidates=new List<CloudCandidate>(cloudCandidates);
            var inspect=cloudInspectCheck;
            var thread=new Thread(delegate()
            {
                CloudInfoResult result;
                try { result=inspect!=null ? inspect(path,candidates) : Setup.InspectCloud(path,candidates); }
                catch(Exception) { result=new CloudInfoResult { Content=CloudContent.Unreachable,Message="Cloud folder could not be checked. Try again." }; }
                if(IsDisposed || Disposing || !IsHandleCreated)return;
                try { BeginInvoke(new Action(delegate
                {
                    if(IsDisposed || Disposing || !IsHandleCreated || busy || sequence!=cloudInspectSequence || tbCloud.Text.Trim()!=path)return;
                    cloudInspecting=false;cloudInfo=result;cloudInfoPath=path;RefreshPathChecks();
                    if(wizardActive && wizardPage.Visible && wizardStep==1)RenderWizard();
                })); }
                catch(InvalidOperationException) { }
            });
            thread.IsBackground=true;thread.Start();
        }

        PathCheck CheckPath(int kind, string value)
        {
            if (designPreview) return new PathCheck { Ok=true, Summary=kind==1 ? "Empty. The first upload fills it." : kind==2 ? "Found · 3 save files, character list present" : kind==3 ? "Found · 12 addons, addon settings present" : "Found", Hint="" };
            if (kind==0) return Setup.CheckGame(value);
            if (kind==1)
            {
                StartCloudScan(false);
                return cloudInfo!=null && cloudInfoPath==value.Trim() && !cloudScanning
                    ? Setup.CheckCloud(cloudInfo,value) : new PathCheck { Summary="Checking…",Hint="" };
            }
            if (kind==2) return Setup.CheckSave(value);
            return Setup.CheckConfig(value);
        }
        static string CheckText(PathCheck c)
        {
            if(c.Summary=="Checking…")return c.Summary;
            return (c.Ok ? "✓ " : "✗ ") + c.Summary + (!c.Ok && c.Hint.Length>0 ? "\n"+c.Hint : "");
        }
        void RefreshPathChecks()
        {
            pathCheckTimer.Stop();
            if (busy) return;
            SaveSettings();
            if (wizardActive && wizardPage.Visible && wizardStep==2 && wizardAdvice==null && !wizardChecking) CheckWizardAdvice(null);
            foreach (var pair in pathChecks)
            {
                var c=CheckPath(pair.Key,pathBoxes[pair.Key].Text.Trim());
                pair.Value.Text=CheckText(c);pair.Value.ForeColor=c.Ok ? Theme.Good : Theme.Warning;
            }
        }
        void ChoosePath(TextBox target, bool game)
        {
            // The preview may show the dialogs; SaveSettings ignores the preview, so nothing is stored
            if (busy) return;
            if (game)
            {
                using(var d=new OpenFileDialog { Filter="Azeroth Survivors|AzerothSurvivors.exe", FileName=target.Text })
                    if(d.ShowDialog(this)==DialogResult.OK)target.Text=d.FileName;
            }
            else using(var d=new FolderBrowserDialog { SelectedPath=target.Text, ShowNewFolderButton=!designPreview })
                if(d.ShowDialog(this)==DialogResult.OK)target.Text=d.SelectedPath;
            SaveSettings();RefreshPathChecks();
        }
        TextBox AddPathCard(FlowLayoutPanel stack,string title,string purpose,string value,int kind,bool primary)
        {
            var card=new BronzePanel { Width=700,Height=100,Accent=primary,Rim=primary ? Theme.Bronze : Theme.Border,Margin=new Padding(0,0,0,6) };
            var name=Theme.Label(title,10,primary ? Theme.Gold : Theme.Text);name.SetBounds(12,7,650,22);card.Controls.Add(name);
            var detail=Theme.Label(purpose,8.5f,Theme.Muted);detail.SetBounds(12,29,650,21);card.Controls.Add(detail);
            var box=new TextBox { Text=value,AccessibleName=title,BorderStyle=BorderStyle.FixedSingle,BackColor=Theme.Raised,ForeColor=Theme.Text,Font=Theme.Body(9) };card.Controls.Add(box);
            var choose=new BronzeButton { Text="Choose...",Width=98,Height=28,AccessibleName="Choose "+title };choose.Click+=delegate{ChoosePath(box,kind==0);};card.Controls.Add(choose);
            var open=new BronzeButton { Text="Open",Width=80,Height=28,AccessibleName="Open "+title };open.Click+=delegate
            {
                if(designPreview || busy)return;
                try { string folder=kind==0 ? Path.GetDirectoryName(box.Text) : box.Text; if(!Directory.Exists(folder))throw new IOException("Folder does not exist yet.");Process.Start(new ProcessStartInfo(folder){UseShellExecute=true}); }
                catch(Exception ex){ThemedDialog.Ask(this,"Cannot open folder",ex.Message,false);}
            };card.Controls.Add(open);
            var check=Theme.Label("",8.5f,Theme.Good);check.SetBounds(12,78,650,20);card.Controls.Add(check);
            pathBoxes[kind]=box;pathChecks[kind]=check;
            card.Layout+=delegate
            {
                int w=card.ClientSize.Width;name.Width=detail.Width=check.Width=w-24;
                box.SetBounds(12,51,w-222,25);choose.Location=new Point(w-204,50);open.Location=new Point(w-92,50);
                int h=TextRenderer.MeasureText(check.Text,check.Font,new Size(Math.Max(100,w-24),0),TextFormatFlags.WordBreak).Height;
                check.Height=Math.Max(20,h);card.Height=Math.Max(96,80+h);
            };
            check.TextChanged+=delegate{card.PerformLayout();};
            box.TextChanged+=delegate{if(kind==1)InvalidateCloudInfo();InvalidateWizardAdvice();if(wizardNext!=null && wizardStep==2)wizardNext.Enabled=false;pathCheckTimer.Stop();if(!busy)pathCheckTimer.Start();};
            box.Leave+=delegate{SaveSettings();RefreshPathChecks();};
            stack.Controls.Add(card);return box;
        }
        Panel BuildWizard()
        {
            var page=new Panel { BackColor=Theme.Background };
            var title=PageHeading("Set up this PC.","Four steps. Start with the PC that has your latest progress.");title.Dock=DockStyle.Top;title.Height=80;
            var bottom=new Panel { Dock=DockStyle.Bottom,Height=82 };
            wizardFeedback=Theme.Label("",9,Theme.Muted);wizardFeedback.Dock=DockStyle.Top;wizardFeedback.Height=32;bottom.Controls.Add(wizardFeedback);
            wizardBack=new BronzeButton { Text="Back",Width=88,TabIndex=0 };wizardBack.SetBounds(0,44,88,38);wizardBack.Click+=delegate{if(wizardStep>0){wizardStep--;RenderWizard();}};bottom.Controls.Add(wizardBack);
            wizardNext=new BronzeButton { Text="Next",TabIndex=2,Primary=true,Width=104,Anchor=AnchorStyles.Top|AnchorStyles.Right };wizardNext.Click+=delegate{if(busy || wizardChecking)return;if(wizardStep==3){CompleteSetup(true);SelectPage(overviewPage,navOverview);}else{wizardStep++;RenderWizard();}};bottom.Controls.Add(wizardNext);
            var cancel=new BronzeButton { Text="Close guide",TabIndex=1,Width=112,Anchor=AnchorStyles.Top|AnchorStyles.Right };cancel.Click+=delegate{if(busy)return;CompleteSetup(false);SelectPage(overviewPage,navOverview);};bottom.Controls.Add(cancel);
            bottom.Layout+=delegate{wizardNext.Location=new Point(bottom.Width-104,44);cancel.Location=new Point(bottom.Width-226,44);};
            var middle=new Panel { Dock=DockStyle.Fill,AutoScroll=true };
            wizardBody=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,0,4,8) };middle.Controls.Add(wizardBody);
            wizardBody.SizeChanged+=delegate{foreach(Control c in wizardBody.Controls)c.Width=Math.Max(350,wizardBody.ClientSize.Width-8);};
            page.Controls.Add(middle);page.Controls.Add(bottom);page.Controls.Add(title);return page;
        }
        void WizardText(string text, float size, Color color)
        {
            var label=Theme.Label(text,size,color);label.AutoSize=true;label.MaximumSize=new Size(Math.Max(350,wizardBody.ClientSize.Width-8),0);label.Margin=new Padding(0,0,0,8);
            label.SizeChanged+=delegate{label.MaximumSize=new Size(Math.Max(350,wizardBody.ClientSize.Width-8),0);};wizardBody.Controls.Add(label);
        }
        BronzeButton WizardButton(string text,EventHandler action,bool primary)
        {
            var b=new BronzeButton { Text=text,AccessibleName=text,Width=Math.Max(350,wizardBody.ClientSize.Width-8),Height=40,Primary=primary,Margin=new Padding(0,0,0,9) };b.Click+=action;wizardBody.Controls.Add(b);return b;
        }
        void WizardPath(string label,TextBox source,int kind)
        {
            WizardText(label,10,Theme.Bronze);WizardText(source.Text,9,Theme.Text);
            var c=CheckPath(kind,source.Text);WizardText(CheckText(c),9,c.Ok ? Theme.Good : Theme.Warning);
            WizardButton("Choose...",delegate{ChoosePath(source,kind==0);RenderWizard();},false).AccessibleName=
                kind==0 ? "Choose game executable" : kind==2 ? "Choose save game folder" : "Choose game config folder";
        }
        void OpenWizard()
        {
            if(busy)return;SaveSettings();InvalidateWizardAdvice();wizardRenderedStep=-1;wizardActive=true;wizardStep=0;SelectPage(wizardPage,navSetup);RenderWizard();
        }
        void CompleteSetup(bool finish)
        {
            if(busy)return;
            settings.SetupDone=finish || tbCloud.Text.Trim().Length>0;SaveSettings();wizardActive=false;InvalidateWizardAdvice();
        }
        void InvalidateWizardAdvice()
        {
            wizardCheckSequence++;wizardChecking=false;wizardAdvice=null;
            wizardSyncSucceeded=false;wizardWithoutAddons=false;
        }
        FirstSyncAdvice PreviewWizardAdvice()
        {
            if(previewAdvice!=null)return previewAdvice;
            return new FirstSyncAdvice { Action=FirstSync.UploadThisPc,Title="Upload this PC's progress",Message="The cloud folder is empty. Upload the progress on this PC first. On your other PCs, choose Download to this PC.",LocalNewest=new DateTime(2026,10,22,20,42,0),Areas=new List<string>{"Save game","AddOns","Addon settings"} };
        }
        int previewSampleIndex;
        static FirstSyncAdvice[] PreviewSamples()
        {
            var t=new DateTime(2026,10,22,20,42,0);var c=new DateTime(2026,10,22,19,18,0);
            return new[]
            {
                new FirstSyncAdvice { Action=FirstSync.UploadThisPc,Title="Upload this PC's progress",Message="The cloud folder is empty. Upload the progress on this PC first. On your other PCs, choose Download to this PC.",LocalNewest=t,Areas=new List<string>{"Save game","AddOns","Addon settings"} },
                new FirstSyncAdvice { Action=FirstSync.DownloadToThisPc,Title="Get your progress from the cloud",Message="Holds progress from KDESKTOP, uploaded 22.10.2026 19:18. This PC has nothing yet, so it will be downloaded.",CloudTime=c,CloudDevice="KDESKTOP",Areas=new List<string>{"Save game","AddOns","Addon settings"} },
                new FirstSyncAdvice { Action=FirstSync.AskWhichIsNewer,Title="Progress on both sides",Message="This PC and the cloud both hold progress. Choose which one to keep. The other one is backed up first.",LocalNewest=t,CloudTime=c,CloudDevice="KDESKTOP",Areas=new List<string>{"Save game","AddOns"} },
                new FirstSyncAdvice { Action=FirstSync.FixConfigFolder,Title="Game config folder not found",Message=@"AddOns and addon settings cannot be synced until the benilla-config folder is chosen. The installer default is C:\Git\wenilla\benilla-config, or benilla-config next to the game.",ConfigMissing=true,Skipped=new List<string>{"AddOns","Addon settings"} },
                new FirstSyncAdvice { Action=FirstSync.RetryLater,Title="Files are busy",Message="Some files could not be read, maybe your cloud app is still syncing. Wait a moment and click Check again.",Areas=new List<string>{"Save game"} },
                new FirstSyncAdvice { Action=FirstSync.AlreadyInSync,Title="Already in sync",Message="This PC and the cloud hold the same progress.",LocalNewest=t,CloudTime=c,CloudDevice="KDESKTOP" },
                new FirstSyncAdvice { Action=FirstSync.CloudNotReady,Title="Cloud folder not ready",Message="Not reachable. Is your cloud drive app running?" }
            };
        }
        void CheckWizardAdvice(Action<FirstSyncAdvice> completed)
        {
            if(busy || !wizardActive || wizardStep!=2 || !wizardPage.Visible)return;
            SaveSettings();
            int sequence=++wizardCheckSequence;
            wizardChecking=true;wizardWithoutAddons=false;
            var snapshot=settings.Clone();
            var sample=designPreview ? PreviewWizardAdvice() : null;
            var sampleCheck=previewAdviceCheck;
            RenderWizard();
            var thread=new Thread(delegate()
            {
                FirstSyncAdvice result;
                try { result=designPreview ? (sampleCheck!=null ? sampleCheck(snapshot) : sample) : Setup.AdviseFirstSync(snapshot); }
                catch(Exception) { result=new FirstSyncAdvice { Action=FirstSync.RetryLater,Title="Files are busy",Message="Some files could not be checked. Wait a moment and click Check again." }; }
                if(IsDisposed || Disposing || !IsHandleCreated)return;
                try { BeginInvoke(new Action(delegate
                {
                    if(IsDisposed || sequence!=wizardCheckSequence || !wizardActive || !wizardPage.Visible || wizardStep!=2)return;
                    wizardChecking=false;wizardAdvice=result;RenderWizard();
                    if(completed!=null)completed(result);
                })); }
                catch(InvalidOperationException) { } // Window closed while the check finished.
            });
            thread.IsBackground=true;thread.Start();
        }
        void WizardSync(bool pull, FirstSyncAdvice displayed)
        {
            if(busy || wizardChecking)return;
            CheckWizardAdvice(delegate(FirstSyncAdvice fresh)
            {
                if(displayed.Fingerprint!=fresh.Fingerprint)
                {
                    wizardSyncSucceeded=false;RenderWizard();
                    wizardFeedback.Text="The situation changed, please check again.";return;
                }
                bool force=displayed.Action==FirstSync.AskWhichIsNewer;
                Run(e=>pull ? e.Pull(force) : e.Push(force),delegate(bool ok){if(ok)wizardSyncSucceeded=true;});
            });
        }
        string WizardCloudTime(FirstSyncAdvice advice)
        {
            if(advice.CloudTime.HasValue)return advice.CloudTime.Value.ToString("dd.MM.yyyy HH:mm");
            return advice.Action==FirstSync.UploadThisPc || advice.Action==FirstSync.NothingYet ? "empty, nothing uploaded yet" : "no upload time available";
        }
        void RenderWizard()
        {
            if(wizardStep!=wizardRenderedStep)
            {
                wizardCheckSequence++;wizardChecking=false;wizardAdvice=null;
                wizardWithoutAddons=false;wizardRenderedStep=wizardStep;wizardFeedback.Text="";
            }
            if(wizardStep==2 && wizardAdvice==null && !wizardChecking && !busy && wizardActive && wizardPage.Visible)
            {
                CheckWizardAdvice(null);return;
            }
            wizardBody.SuspendLayout();
            while(wizardBody.Controls.Count>0)wizardBody.Controls[0].Dispose();
            wizardStepLabel=Theme.Label("Step "+(wizardStep+1)+" of 4 · "+new[]{"Game","Cloud","First sync","Done"}[wizardStep],10,Theme.Gold);wizardStepLabel.Height=30;wizardBody.Controls.Add(wizardStepLabel);
            wizardBack.Enabled=wizardStep>0;wizardNext.Text=wizardStep==3 ? "Finish" : "Next";wizardNext.Enabled=true;
            if(wizardStep==0)
            {
                WizardText("Find your game",17,Theme.Text);WizardText("Put AzerothSync.exe next to AzerothSurvivors.exe so it finds the game automatically.",10,Theme.Muted);
                WizardPath("Game",tbGame,0);wizardNext.Enabled=CheckPath(0,tbGame.Text).Ok;
            }
            else if(wizardStep==1)
            {
                WizardText("Choose your cloud folder",17,Theme.Text);
                var infoBox=new BronzePanel { Width=700,Height=80,Accent=true,Padding=new Padding(16),Margin=new Padding(0,0,0,14) };
                var info=Theme.Label("AzerothSync does not log into Google or any cloud. Your cloud app (Google Drive for desktop, OneDrive or Dropbox) syncs a normal folder on this PC. AzerothSync only copies files into that folder.",10,Theme.Text);info.Dock=DockStyle.Fill;infoBox.Controls.Add(info);wizardBody.Controls.Add(infoBox);
                var clouds=designPreview ? (previewClouds ?? new List<CloudCandidate>{new CloudCandidate{Name="Google Drive",SuggestedDir=@"G:\My Drive\AzerothSync"}}) : (cloudCandidates ?? new List<CloudCandidate>());
                if(designPreview)
                {
                    var toggle=new LinkLabel { Text=clouds.Count==0 ? "Preview: show found cloud app" : "Preview: show 'no cloud app found'",AutoSize=true,LinkColor=Theme.Bronze,ActiveLinkColor=Theme.Gold,VisitedLinkColor=Theme.Bronze,Margin=new Padding(0,0,0,10) };
                    toggle.LinkClicked+=delegate{previewClouds=previewClouds==null ? new List<CloudCandidate>() : null;RenderWizard();};wizardBody.Controls.Add(toggle);
                }
                bool scanning=!designPreview && (cloudScanning || cloudCandidates==null);
                if(scanning)WizardText("Looking for cloud apps…",10,Theme.Muted);
                if(!scanning && clouds.Count==0)WizardText("No cloud app found. Install Google Drive for desktop (or OneDrive/Dropbox), sign in, then click Search again.",10,Theme.Warning);
                foreach(var candidate in clouds)
                {
                    string suggested=candidate.SuggestedDir;
                    WizardButton("Use "+candidate.Name+"  "+suggested,delegate{tbCloud.Text=suggested;SaveSettings();RenderWizard();},true).Enabled=!scanning;
                }
                WizardButton("Choose another folder...",delegate{ChoosePath(tbCloud,false);RenderWizard();},false);
                var search=new FlowLayoutPanel { Height=38,Margin=new Padding(0,0,0,10) };
                var again=new BronzeButton { Text="Search again",AccessibleName="Search again for cloud folders",Width=132,Height=32 };again.Enabled=!scanning;again.Click+=delegate{StartCloudScan(true);RefreshPathChecks();RenderWizard();};search.Controls.Add(again);
                var link=new LinkLabel { Text="Get Google Drive for desktop",AutoSize=true,LinkColor=Theme.Bronze,ActiveLinkColor=Theme.Gold,VisitedLinkColor=Theme.Bronze,Margin=new Padding(12,7,0,0) };link.LinkClicked+=delegate{if(!designPreview)try{Process.Start("https://www.google.com/drive/download/");}catch(Exception ex){Write(ex.Message,Level.Error);}};search.Controls.Add(link);wizardBody.Controls.Add(search);
                WizardText("Selected: "+(tbCloud.Text.Trim().Length==0 ? "none yet" : tbCloud.Text),9,Theme.Text);
                var c=CheckPath(1,tbCloud.Text);WizardText(CheckText(c),9,c.Ok ? Theme.Good : Theme.Warning);
                if(!designPreview && cloudInfo!=null && cloudInfoPath==tbCloud.Text.Trim() && !cloudScanning){var cloud=cloudInfo;if(cloud.Message!=c.Summary)WizardText(cloud.Message,9,Theme.Muted);}
                WizardText("Use the SAME folder on every PC. Drive letters may differ.\nThe Google Drive website alone does not work.",9,Theme.Muted);wizardNext.Enabled=c.Ok;
            }
            else if(wizardStep==2)
            {
                var advice=wizardAdvice;
                wizardNext.Enabled=false;
                if(wizardChecking || advice==null)
                {
                    WizardText("Checking your files…",17,Theme.Text);
                    WizardText("Large AddOns folders and cloud files can take a moment. Please wait.",10,Theme.Muted);
                    wizardBody.Controls.Add(new BronzeProgress { Width=Math.Max(350,wizardBody.ClientSize.Width-8),Height=18,Value=0,Margin=new Padding(0,0,0,12) });
                    wizardFeedback.Text="Checking your files…";
                    wizardBody.ResumeLayout(true);return;
                }
                wizardFeedback.Text="";
                if(designPreview)
                {
                    // Preview only: step through every advice state with sample data
                    var samples=PreviewSamples();int index=(previewSampleIndex+1)%samples.Length;
                    var cycle=new LinkLabel { Text="Preview: show next sample state ("+(index+1)+" of "+samples.Length+")",AutoSize=true,LinkColor=Theme.Bronze,ActiveLinkColor=Theme.Gold,VisitedLinkColor=Theme.Bronze,Margin=new Padding(0,0,0,10) };
                    cycle.LinkClicked+=delegate{previewSampleIndex=index;previewAdvice=samples[index];wizardSyncSucceeded=false;wizardWithoutAddons=false;CheckWizardAdvice(null);};wizardBody.Controls.Add(cycle);
                }
                wizardNext.Enabled=advice.Action!=FirstSync.RetryLater && (advice.Action==FirstSync.FixConfigFolder ? wizardWithoutAddons : wizardSyncSucceeded || advice.Action==FirstSync.AlreadyInSync || advice.Action==FirstSync.NothingYet);
                WizardText(advice.Title,17,Theme.Text);WizardText(advice.Message,10,Theme.Text);
                if(advice.Skipped.Count>0)WizardText("Not synced on this PC: "+string.Join(", ",advice.Skipped.ToArray()),9,Theme.Warning);
                if(advice.Action==FirstSync.FixConfigFolder)
                {
                    WizardButton("Choose game config folder",delegate
                    {
                        string before=tbConfig.Text;ChoosePath(tbConfig,false);
                        // Preview: a chosen folder resolves the sample, like a real check would
                        if(designPreview && tbConfig.Text!=before){previewSampleIndex=5;previewAdvice=PreviewSamples()[5];CheckWizardAdvice(null);}
                        RenderWizard();
                    },true);
                    var skip=new LinkLabel { Text="Continue without AddOns",AccessibleName="Continue without AddOns",AutoSize=true,LinkColor=Theme.Muted,ActiveLinkColor=Theme.Gold,VisitedLinkColor=Theme.Muted,Margin=new Padding(0,0,0,12) };
                    skip.LinkClicked+=delegate{wizardWithoutAddons=true;RenderWizard();};wizardBody.Controls.Add(skip);
                }
                if(advice.Action==FirstSync.RetryLater)
                {
                    WizardButton("Check again",delegate{CheckWizardAdvice(null);},false);
                    wizardBody.ResumeLayout(true);return;
                }
                if(wizardSyncSucceeded)WizardText("Sync finished successfully. You can continue.",9,Theme.Good);
                if(advice.Areas.Count>0)WizardText((wizardSyncSucceeded ? "Still differs: " : "Differs: ")+string.Join(", ",advice.Areas.ToArray()),9,Theme.Warning);
                WizardText("This PC: "+(advice.LocalNewest.HasValue ? advice.LocalNewest.Value.ToString("dd.MM.yyyy HH:mm") : "no save yet")+"\nCloud: "+WizardCloudTime(advice),9,Theme.Muted);
                if(advice.Action==FirstSync.DownloadToThisPc)WizardButton("Download to this PC",delegate{WizardSync(true,advice);},true);
                if(advice.Action==FirstSync.UploadThisPc)WizardButton("Upload this PC's progress",delegate{WizardSync(false,advice);},true);
                if(advice.Action==FirstSync.AskWhichIsNewer)
                {
                    WizardButton("Keep this PC's progress (upload)",delegate{WizardSync(false,advice);},false);
                    WizardButton("Use the cloud progress (download)",delegate{WizardSync(true,advice);},false);
                }
                if(advice.Action==FirstSync.CloudNotReady){WizardButton("Back to cloud folder",delegate{wizardStep=1;RenderWizard();},true);}
                WizardButton((wizardAdvanced ? "Hide advanced folders" : "Advanced: save game folder and game config folder"),delegate{wizardAdvanced=!wizardAdvanced;RenderWizard();},false);
                if(wizardAdvanced){WizardPath("Save game folder",tbSave,2);WizardPath("Game config folder · installer default C:\\Git\\wenilla\\benilla-config",tbConfig,3);}
                WizardButton("Check again",delegate{CheckWizardAdvice(null);},false);
            }
            else
            {
                WizardText("Ready for your next adventure",17,Theme.Text);
                WizardText("From now on start the game with 'Azeroth Survivors (Sync)'",11,Theme.Text);
                WizardButton("Create desktop shortcut",delegate{CreateShortcut();wizardFeedback.Text=designPreview ? "Preview only. No shortcut created." : "See Log for the shortcut result.";},true);
                WizardText("Repeat these steps on your other PCs with the same cloud folder.",10,Theme.Muted);
                WizardText("On the PC with your latest progress: upload first.\nOn other PCs: download that progress before playing.\nWait for your cloud app to finish transferring before switching PCs.",10,Theme.Muted);
            }
            wizardBody.ResumeLayout(true);
        }
    }
}
