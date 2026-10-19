using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AzerothSync
{
    partial class MainForm
    {
        readonly bool designPreview;
        Panel overviewPage,pathsPage,logPage;
        BronzeButton navOverview,navPaths,navLog;
        Label progressDetail;
        readonly ToolTip lookTips=new ToolTip { AutoPopDelay=18000,InitialDelay=350 };
        readonly Dictionary<string,AreaRow> areaRows=new Dictionary<string,AreaRow>();

        static Settings SampleSettings()
        {
            // The design build never loads or writes the user's configuration.
            return new Settings{GamePath=@"C:\Games\AzerothSurvivors\AzerothSurvivors.exe",CloudDir=@"G:\My Drive\AzerothSync",SaveDir=@"C:\Users\Player\AppData\Roaming\wenilla-rogue",ConfigDir=@"C:\Git\wenilla\benilla-config",StateDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-unused")};
        }
        void BuildWindow()
        {
            Text="AzerothSync"+(designPreview?" · Design preview":"");
            Font=Theme.Body(9.5f);BackColor=Theme.Background;ForeColor=Theme.Text;
            ClientSize=new Size(1040,760);MinimumSize=new Size(940,760);AutoScaleMode=AutoScaleMode.Font;
            StartPosition=FormStartPosition.CenterScreen;
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Theme.Background};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,106));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,37));Controls.Add(root);
            var header=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Theme.Background};
            var brand=Theme.Label("AzerothSync",26,Theme.Gold);brand.Font=Theme.Heading(26);brand.SetBounds(27,19,400,47);header.Controls.Add(brand);
            var sub=Theme.Label("YOUR PROGRESS TRAVELS WITH YOU",8.5f,Theme.Muted);sub.SetBounds(30,71,460,20);header.Controls.Add(sub);
            var device=Theme.Label(designPreview?"DESIGN PREVIEW · SAMPLE DATA":Environment.MachineName,8.5f,Theme.Bronze);device.TextAlign=ContentAlignment.MiddleRight;header.Controls.Add(device);
            header.Layout+=delegate{device.SetBounds(header.Width-478,25,448,35);};
            header.Paint+=(s,e)=>{using(var pen=new Pen(Theme.Border))e.Graphics.DrawLine(pen,28,103,header.Width-28,103);};root.Controls.Add(header,0,0);

            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(26,16,26,15),Margin=Padding.Empty,BackColor=Theme.Background};
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,176));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.Controls.Add(body,0,1);
            var sidebar=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,0,18,0),BackColor=Theme.Background};body.Controls.Add(sidebar,0,0);
            var navLabel=Theme.Label("YOUR COMPANION",8,Theme.Muted);navLabel.SetBounds(10,7,146,20);sidebar.Controls.Add(navLabel);
            navOverview=Nav("Play & Sync",sidebar,39,delegate{SelectPage(overviewPage,navOverview);});
            navPaths=Nav("Paths",sidebar,89,delegate{SelectPage(pathsPage,navPaths);});
            navLog=Nav("Log",sidebar,139,delegate{SelectPage(logPage,navLog);});
            var note=Theme.Label("One PC after\nthe other.\n\nYour cloud app\nhandles the transfer.",8.5f,Theme.Muted);note.SetBounds(10,280,146,120);sidebar.Controls.Add(note);
            var content=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Theme.Background};body.Controls.Add(content,1,0);
            overviewPage=BuildOverview();pathsPage=BuildPaths();logPage=BuildLog();
            foreach(var page in new[]{overviewPage,pathsPage,logPage}){page.Dock=DockStyle.Fill;content.Controls.Add(page);}SelectPage(overviewPage,navOverview);
            var footer=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Theme.Background};root.Controls.Add(footer,0,2);
            footer.Paint+=(s,e)=>{using(var p=new Pen(Theme.Border))e.Graphics.DrawLine(p,28,0,footer.Width-28,0);};
            var foot=Theme.Label(designPreview?"Preview · All actions use sample data only.":"Save game · AddOns · Addon settings",8,Theme.Muted);foot.SetBounds(29,10,760,20);footer.Controls.Add(foot);
            var version=Theme.Label("v"+Version,8,Theme.Muted);version.TextAlign=ContentAlignment.MiddleRight;footer.Controls.Add(version);
            var credit=new LinkLabel { Text="by Keijin", Font=Theme.Body(8.5f), LinkColor=Theme.Bronze, ActiveLinkColor=Theme.Gold, VisitedLinkColor=Theme.Bronze, LinkBehavior=LinkBehavior.HoverUnderline, BackColor=Theme.Background, TextAlign=ContentAlignment.MiddleRight, AccessibleName="by Keijin – GitHub", AutoSize=false };
            credit.Links.Add(0,credit.Text.Length,"https://github.com/KeijinDE/ASDataSync");
            lookTips.SetToolTip(credit,"GitHub: KeijinDE/ASDataSync");
            credit.LinkClicked+=delegate(object sender,LinkLabelLinkClickedEventArgs e)
            {
                try { Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute=true }); }
                catch(Exception ex) { Write("Could not open GitHub: "+ex.Message,Level.Error); }
            };
            footer.Controls.Add(credit);
            footer.Layout+=delegate{credit.SetBounds(footer.Width-114,7,84,24);version.SetBounds(footer.Width-184,9,56,20);foot.Width=footer.Width-220;};
        }
        BronzeButton Nav(string text,Control parent,int y,EventHandler action)
        {
            var b=new BronzeButton{Text=text,Navigation=true};b.SetBounds(0,y,156,42);b.Click+=action;parent.Controls.Add(b);return b;
        }
        void SelectPage(Panel page,BronzeButton selected)
        {
            foreach(var p in new[]{overviewPage,pathsPage,logPage})if(p!=null)p.Visible=p==page;
            if(page!=null)page.BringToFront();
            foreach(var b in new[]{navOverview,navPaths,navLog})if(b!=null){b.Selected=b==selected;b.Invalidate();}
        }
        Panel PageHeading(string title,string subtitle)
        {
            var panel=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Background};
            var heading=Theme.Label(title,21,Theme.Gold);heading.Font=Theme.Heading(21);heading.SetBounds(0,1,700,39);panel.Controls.Add(heading);
            var sub=Theme.Label(subtitle,9.5f,Theme.Muted);sub.SetBounds(1,44,700,26);panel.Controls.Add(sub);
            panel.Layout+=delegate{heading.Width=panel.Width;sub.Width=panel.Width-1;};return panel;
        }
        Panel BuildOverview()
        {
            var page=new Panel{BackColor=Theme.Background};
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty,Padding=Padding.Empty};
            foreach(float h in new[]{82f,130f,36f,192f,58f})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(layout);
            layout.Controls.Add(PageHeading("Your next adventure.","Your progress. On every one of your PCs."),0,0);
            var hero=new BronzePanel{Dock=DockStyle.Fill,Accent=true,Rim=Theme.Bronze,Margin=new Padding(0,0,0,8)};
            var over=Theme.Label("PLAY WITH SYNC",8,Theme.Bronze);over.SetBounds(21,17,300,18);hero.Controls.Add(over);
            var title=Theme.Label("One click. Your latest state.",15,Theme.Text);title.Font=Theme.Heading(15);title.SetBounds(20,40,400,30);hero.Controls.Add(title);
            var copy=Theme.Label("Syncs before the game starts.\nSaves to the sync folder after you close it.",9,Theme.Muted);copy.SetBounds(21,78,400,38);hero.Controls.Add(copy);
            btnPlay=MakeButton("Play","Sync, play, save after closing",delegate{Run(e=>e.Play());});
            ((BronzeButton)btnPlay).Primary=true;btnPlay.Font=Theme.Heading(16);btnPlay.Size=new Size(194,54);hero.Controls.Add(btnPlay);
            hero.Layout+=delegate{btnPlay.Location=new Point(hero.Width-218,34);title.Width=Math.Max(180,hero.Width-250);copy.Width=Math.Max(180,hero.Width-250);};layout.Controls.Add(hero,0,1);
            var section=Theme.Label("DATA AREAS · LAST CHECK",8,Theme.Bronze);section.Dock=DockStyle.Fill;section.TextAlign=ContentAlignment.MiddleLeft;section.Margin=Padding.Empty;layout.Controls.Add(section,0,2);
            var group=new BronzePanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(1,1,1,3)};
            var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Theme.Surface};
            var specs=new[]{new[]{"Save game","Save game","Characters and progress"},new[]{"AddOns","AddOns","Your installed addons"},new[]{"Addon settings","Addon settings","Your personal addon setup"}};
            for(int i=0;i<3;i++){rows.RowStyles.Add(new RowStyle(SizeType.Percent,33.333f));var row=new AreaRow(specs[i][1],specs[i][2]){Dock=DockStyle.Fill,Margin=Padding.Empty};areaRows[specs[i][0]]=row;rows.Controls.Add(row,0,i);}group.Controls.Add(rows);layout.Controls.Add(group,0,3);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,12,0,0),Margin=Padding.Empty,WrapContents=false};
            btnPull=MakeButton("Download","Replace your local data with the cloud state (one confirmation)",delegate{ManualSync(true);});btnPull.Width=164;
            btnPush=MakeButton("Upload","Replace the cloud state with your local data (one confirmation)",delegate{ManualSync(false);});btnPush.Width=164;
            btnStatus=MakeButton("Check status","Compare local and cloud",delegate{Run(e=>e.Status());});btnStatus.Width=138;
            actions.Controls.AddRange(new Control[]{btnPull,btnPush,btnStatus});layout.Controls.Add(actions,0,4);
            var status=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Theme.Background};
            progressText=Theme.Label("Ready",10,Theme.Text);progressText.SetBounds(0,10,700,24);status.Controls.Add(progressText);
            progress=new BronzeProgress();progress.SetBounds(0,42,700,9);status.Controls.Add(progress);
            progressDetail=Theme.Label("Your cloud app transfers the data to your other PCs.",8.5f,Theme.Muted);progressDetail.SetBounds(0,62,700,40);status.Controls.Add(progressDetail);
            status.Layout+=delegate{progress.Width=status.Width;progressText.Width=status.Width;progressDetail.Width=status.Width;};layout.Controls.Add(status,0,5);return page;
        }
        Panel BuildPaths()
        {
            var page=new Panel{BackColor=Theme.Background};
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,86));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,326));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(layout);
            layout.Controls.Add(PageHeading("Where everything lives.","These paths apply to this PC only."),0,0);
            var group=new BronzePanel{Dock=DockStyle.Fill,Padding=new Padding(20,20,20,12),Margin=Padding.Empty};settingsGroup=group;
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=0,Margin=Padding.Empty,BackColor=Theme.Surface};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,40));
            tbGame=AddRow(grid,"Game",settings.GamePath,true,"AzerothSurvivors.exe, used to start the game. Found automatically when AzerothSync sits in the game folder.");
            tbCloud=AddRow(grid,"Sync folder",settings.CloudDir,false,"A shared folder in Google Drive, OneDrive, Dropbox or similar.");
            tbSave=AddRow(grid,"Save game folder",settings.SaveDir,false,"Usually %APPDATA%\\wenilla-rogue.");
            tbConfig=AddRow(grid,"Game config folder",settings.ConfigDir,false,"benilla-config with the AddOns and saved folders. Next to the game, otherwise C:\\Git\\wenilla\\benilla-config.");
            for(int i=0;i<4;i++)grid.RowStyles.Add(new RowStyle(SizeType.Absolute,70));
            foreach(Control c in grid.Controls)
            {
                c.ForeColor=Theme.Text;c.BackColor=Theme.Surface;
                var box=c as TextBox;if(box!=null){box.BorderStyle=BorderStyle.FixedSingle;box.Font=Theme.Body(9);box.Margin=new Padding(0,18,10,0);lookTips.SetToolTip(box,box.Text);}
                if(c is Button)c.Margin=new Padding(0,15,0,0);
            }
            foreach(var box in grid.Controls.OfType<TextBox>().ToArray())
            {
                var cell=grid.GetPositionFromControl(box);box.AccessibleName=new[]{"Game", "Sync folder", "Save game folder", "Game config folder"}[cell.Row];grid.Controls.Remove(box);
                var field=new BronzePanel{Dock=DockStyle.Top,Height=34,Padding=new Padding(9,7,9,4),Margin=new Padding(0,15,10,0)};
                box.BorderStyle=BorderStyle.None;box.Dock=DockStyle.Fill;box.Margin=Padding.Empty;field.Controls.Add(box);grid.Controls.Add(field,cell.Column,cell.Row);
            }
            group.Controls.Add(grid);layout.Controls.Add(group,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,13,0,0),Margin=Padding.Empty};btnShortcut=MakeButton("Desktop shortcut","Starts the game with automatic sync",delegate{CreateShortcut();});btnShortcut.Width=200;actions.Controls.Add(btnShortcut);layout.Controls.Add(actions,0,2);
            var note=Theme.Label("The shortcut starts the game with automatic sync.\nPath changes are saved on this PC only.",9,Theme.Muted);note.Dock=DockStyle.Fill;note.Padding=new Padding(0,8,0,0);layout.Controls.Add(note,0,3);return page;
        }
        Panel BuildLog()
        {
            var page=new Panel{BackColor=Theme.Background};
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,86));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));page.Controls.Add(layout);
            layout.Controls.Add(PageHeading("Everything at a glance.","The technical log of this session."),0,0);
            var frame=new BronzePanel{Dock=DockStyle.Fill,Padding=new Padding(17),Margin=Padding.Empty};
            log=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BackColor=Theme.Surface,ForeColor=Theme.Text,Font=new Font("Consolas",9.5f),BorderStyle=BorderStyle.None,DetectUrls=false};frame.Controls.Add(log);layout.Controls.Add(frame,0,1);
            if(designPreview)
            {
                var b=new BronzeButton{Text="Show sample conflict",Width=220,Dock=DockStyle.Left,Margin=new Padding(0,8,0,0)};
                b.Click+=delegate{ThemedDialog.Ask(this,"Which state do you want to keep?","Your local data and the cloud both changed.\n\nSave game · local today, 20:42 · cloud today, 19:18\nAddOns · local today, 20:30 · cloud today, 19:18\n\nYour choice applies to all areas listed.\nThe replaced state is backed up first.",true);};layout.Controls.Add(b,0,2);
            }
            else{var note=Theme.Label("The log is also kept on this PC for troubleshooting (log.txt).",8.5f,Theme.Muted);note.Dock=DockStyle.Fill;note.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(note,0,2);}return page;
        }
        void ManualSync(bool pull)
        {
            SelectPage(overviewPage,navOverview);
            if(designPreview)
            {
                string text=pull?"Replace your local data with the cloud state?":"Replace the cloud state with your local data?";
                if(ThemedDialog.Ask(this,"Confirm change",text+"\n\nSave game, AddOns, Addon settings\n\nA backup is made first.\nThis preview only changes the display.",false)!=Choice.Local)return;
            }
            Run(e=>pull?e.Pull(true):e.Push(true));
        }
        static string TranslateProgress(string text)
        {
            // Engine texts are English, like the rest of the window
            return text;
        }
        static string TranslateQuestion(string text)
        {
            // Engine texts are English, like the rest of the window
            return text;
        }
        void UpdateAreaState(string text,Level level)
        {
            // Unknown engine text remains neutral. The detailed original log stays available.
            foreach(var pair in areaRows)
            {
                string prefix="  "+pair.Key+": ";if(!text.StartsWith(prefix,StringComparison.Ordinal))continue;
                string message=text.Substring(prefix.Length),state="See log";Color color=Theme.Muted;
                if(message.Contains("NOT uploaded")){state="Not uploaded";color=Theme.Warning;}
                else if(message.Contains("incomplete")||message.Contains("does not match")){state="Cloud incomplete";color=Theme.Warning;}
                else if(message.Contains("in sync")||message.Contains("up to date")||message.Contains("downloaded")||message=="uploaded."){state="In sync";color=Theme.Good;}
                else if(message.Contains("local is newer")||message.Contains("local state kept")){state="Changed locally";color=Theme.Warning;}
                else if(message.Contains("cloud is newer")){state="Changed in cloud";color=Theme.Warning;}
                else if(message.Contains("both changed")){state="Decision needed";color=Theme.Warning;}
                else if(message.Contains("not found, skipped"))state="Not set up";
                else if(message.Contains("empty")||message.Contains("nothing local"))state="No data yet";
                if(level==Level.Error){state="Check failed";color=Theme.Error;}pair.Value.SetState(state,color);
            }
        }
        void ShowSampleState()
        {
            progressText.Text="Ready for your next adventure";progressDetail.Text="Design preview with sample data · Not connected to your saves.";
            Write("Design preview — sample data only.",Level.Dim);Write("  Save game: in sync",Level.Good);Write("  AddOns: local is newer, upload",Level.Warn);Write("  Addon settings: in sync",Level.Good);
        }
        void AnimateSample()
        {
            SelectPage(overviewPage,navOverview);SetBusy(true);int value=0;var timer=new System.Windows.Forms.Timer{Interval=80};
            timer.Tick+=delegate
            {
                value+=5;ShowProgress("Checking files: "+value+" of 100 files",value);
                if(value<100)return;timer.Stop();timer.Dispose();SetBusy(false);foreach(var row in areaRows.Values)row.SetState("In sync",Theme.Good);
                progressText.Text="Preview finished";progressText.ForeColor=Theme.Good;progressDetail.Text="Sample run · No files were read or transferred.";Write("Preview completed. No files were changed.",Level.Good);
            };timer.Start();
        }
    }
}
