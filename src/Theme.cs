using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AzerothSync
{
    // Original chamfered geometry and palette, drawn without game assets.
    static class Theme
    {
        public static readonly Color Background = Color.FromArgb(15, 15, 18);
        public static readonly Color Surface = Color.FromArgb(23, 23, 27);
        public static readonly Color Raised = Color.FromArgb(30, 29, 32);
        public static readonly Color Bronze = Color.FromArgb(158, 145, 110);
        public static readonly Color Border = Color.FromArgb(78, 71, 56);
        public static readonly Color Gold = Color.FromArgb(255, 209, 71);
        public static readonly Color Text = Color.FromArgb(240, 232, 209);
        public static readonly Color Muted = Color.FromArgb(173, 169, 155);
        public static readonly Color Good = Color.FromArgb(139, 205, 145);
        public static readonly Color Warning = Color.FromArgb(238, 184, 94);
        public static readonly Color Error = Color.FromArgb(238, 125, 112);

        public static Font Body(float size) { return new Font("Segoe UI", size, FontStyle.Regular); }
        public static Font Heading(float size) { return new Font("Georgia", size, FontStyle.Regular); }
        public static Label Label(string text, float size, Color color)
        {
            return new Label { Text = text, Font = Body(size), ForeColor = color, BackColor = Color.Transparent, AutoSize = false, UseMnemonic = false };
        }
        public static GraphicsPath Contour(Rectangle r, int cut)
        {
            float x = r.X + .5f, y = r.Y + .5f, right = r.Right - 1.5f, bottom = r.Bottom - 1.5f;
            float c = Math.Min(cut, Math.Min(r.Width, r.Height) / 3f);
            var p = new GraphicsPath();
            p.AddPolygon(new[] { new PointF(x+c,y),new PointF(right-c,y),new PointF(right,y+c),new PointF(right,bottom-c),new PointF(right-c,bottom),new PointF(x+c,bottom),new PointF(x,bottom-c),new PointF(x,y+c) });
            p.CloseFigure(); return p;
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
        public static void DarkChrome(Form form)
        {
            try { int on = 1; if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4); } catch { }
        }
    }

    class BronzePanel : Panel
    {
        public Color Rim = Theme.Border;
        public bool Accent;
        public BronzePanel() { DoubleBuffered = true; BackColor = Theme.Surface; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Contour(ClientRectangle, 7))
            using (var fill = new LinearGradientBrush(ClientRectangle, Accent ? Color.FromArgb(42,36,25) : Theme.Raised, Theme.Surface, 100f))
            using (var pen = new Pen(Rim)) { e.Graphics.FillPath(fill,p); e.Graphics.DrawPath(pen,p); }
            using (var p = new Pen(Color.FromArgb(35, Theme.Bronze))) e.Graphics.DrawLine(p, 9, 3, Width-10, 3);
        }
    }

    class BronzeButton : Button
    {
        bool hover, down;
        public bool Primary, Navigation, Selected;
        public BronzeButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body(9.5f); ForeColor = Theme.Text; FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0; BackColor = Theme.Surface;
            UseVisualStyleBackColor = false; UseMnemonic = false; Height = 38; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; down=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool active = Primary || Selected;
            Color top = active ? Color.FromArgb(86,65,32) : Theme.Raised;
            Color bottom = active ? Color.FromArgb(49,40,27) : Theme.Surface;
            Color rim = active ? Theme.Bronze : Theme.Border;
            if (hover && Enabled) { top = active ? Color.FromArgb(108,81,38) : Color.FromArgb(44,41,34); rim=Theme.Gold; }
            if (down) { var c=top;top=bottom;bottom=c; }
            if (!Enabled) { top=bottom=Theme.Surface;rim=Color.FromArgb(49,47,42); }
            using (var p=Theme.Contour(ClientRectangle,4))
            using (var fill=new LinearGradientBrush(ClientRectangle,top,bottom,90f))
            using (var pen=new Pen(rim)) { e.Graphics.FillPath(fill,p); if(!Navigation || Selected || hover)e.Graphics.DrawPath(pen,p); }
            var rect=Rectangle.Inflate(ClientRectangle,-12,-2);
            TextRenderer.DrawText(e.Graphics,Text,Font,rect,!Enabled ? Color.FromArgb(103,101,94) : (active ? Theme.Gold : Theme.Text),TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis);
            if(Selected) using(var pen=new Pen(Theme.Gold,2))e.Graphics.DrawLine(pen,2,10,2,Height-11);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),Theme.Text,Theme.Surface);
        }
    }

    class BronzeProgress : Control
    {
        int value;
        public int Value { get{return value;} set{this.value=Math.Max(0,Math.Min(100,value));Invalidate();if(IsHandleCreated)AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);} }
        public BronzeProgress() { DoubleBuffered=true;Height=9;BackColor=Theme.Background;AccessibleRole=AccessibleRole.ProgressBar;AccessibleName="Fortschritt";TabStop=false; }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ProgressAccessibility(this); }
        class ProgressAccessibility : Control.ControlAccessibleObject
        {
            readonly BronzeProgress bar;
            public ProgressAccessibility(BronzeProgress bar) : base(bar) { this.bar=bar; }
            public override string Value { get { return bar.Value.ToString()+" %"; } set { } }
            public override AccessibleStates State { get { return base.State | AccessibleStates.ReadOnly; } }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if(Width<4 || Height<4)return;
            using(var p=Theme.Contour(ClientRectangle,3))
            using(var background=new SolidBrush(Theme.Background))
            using(var pen=new Pen(Theme.Border)) { e.Graphics.FillPath(background,p); e.Graphics.DrawPath(pen,p); }
            int w=(Width-4)*value/100;
            if(w>0)using(var fill=new LinearGradientBrush(new Rectangle(2,2,Math.Max(1,w),Height-4),Theme.Bronze,Theme.Gold,0f))e.Graphics.FillRectangle(fill,2,2,w,Height-4);
        }
    }

    class AreaRow : Control
    {
        public string Title, Detail;
        string state="Not checked yet";
        Color stateColor=Theme.Muted;
        public AreaRow(string title,string detail) { Title=title;Detail=detail;Height=62;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText; }
        public void SetState(string text,Color color) { state=text;stateColor=color;AccessibleName=Title+": "+text;Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            var r=new Rectangle(17,20,22,22);
            using(var p=Theme.Contour(r,5))using(var pen=new Pen(stateColor)) { e.Graphics.DrawPath(pen,p); }
            using(var pen=new Pen(stateColor,1.5f)) { if(stateColor==Theme.Good)e.Graphics.DrawLines(pen,new[]{new Point(23,31),new Point(27,35),new Point(34,26)});else e.Graphics.DrawLine(pen,24,31,32,31); }
            int statusWidth=180;
            using(var f=Theme.Body(10.5f))TextRenderer.DrawText(e.Graphics,Title,f,new Rectangle(54,10,Math.Max(10,Width-statusWidth-60),23),Theme.Text,TextFormatFlags.EndEllipsis);
            using(var f=Theme.Body(8.5f))TextRenderer.DrawText(e.Graphics,Detail,f,new Rectangle(54,34,Math.Max(10,Width-statusWidth-60),20),Theme.Muted,TextFormatFlags.EndEllipsis);
            using(var f=Theme.Body(9f))TextRenderer.DrawText(e.Graphics,state,f,new Rectangle(Width-statusWidth-18,19,statusWidth,26),stateColor,TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using(var pen=new Pen(Color.FromArgb(46,43,37)))e.Graphics.DrawLine(pen,17,Height-1,Width-18,Height-1);
        }
    }

    static class ThemedDialog
    {
        public static Choice Ask(IWin32Window owner,string title,string message,bool conflict)
        {
            using(var f=new Form { Text=title,ClientSize=new Size(600,350),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=Theme.Background,ForeColor=Theme.Text,Font=Theme.Body(10),AutoScaleMode=AutoScaleMode.Font })
            {
                var head=Theme.Label(title,20,Theme.Gold);head.Font=Theme.Heading(20);head.SetBounds(26,23,548,40);f.Controls.Add(head);
                var text=Theme.Label(message,10,Theme.Text);text.SetBounds(26,80,548,192);f.Controls.Add(text);
                Choice result=Choice.Abort;
                var buttons=new FlowLayoutPanel { FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Bottom,Height=64,Padding=new Padding(20,8,20,10),BackColor=Theme.Background };
                var cancel=new BronzeButton{Text=conflict?"Cancel":"No, go back",Width=145};cancel.Click+=delegate{f.Close();};buttons.Controls.Add(cancel);f.AcceptButton=cancel;f.CancelButton=cancel;
                var local=new BronzeButton{Text=conflict?"Keep local":"Yes, continue",Width=155};local.Click+=delegate{result=Choice.Local;f.Close();};buttons.Controls.Add(local);
                if(conflict){var cloud=new BronzeButton{Text="Use cloud",Width=165};cloud.Click+=delegate{result=Choice.Cloud;f.Close();};buttons.Controls.Add(cloud);}
                f.Controls.Add(buttons);f.Shown+=delegate{Theme.DarkChrome(f);cancel.Focus();};f.ShowDialog(owner);return result;
            }
        }
    }
}
