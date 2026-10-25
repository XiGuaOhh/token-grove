using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TokenGrove {
  public static class SystemAppearance {
    public static bool Dark {get{try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))return Convert.ToInt32(key?.GetValue("AppsUseLightTheme",1))==0;}catch{return false;}}}
    public static bool Animations {get{int value=1;try{return Spi(0x1042,0,ref value,0)&&value!=0;}catch{return true;}}}
    public static int Build {get{try{using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){int v;return int.TryParse(Convert.ToString(key?.GetValue("CurrentBuildNumber","0")),out v)?v:0;}}catch{return 0;}}}
    public static bool Windows11 {get{return Build>=22000;}}
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]static extern bool Spi(uint action,uint param,ref int value,uint flags);
  }
  public static class WindowAppearance {
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
    [StructLayout(LayoutKind.Sequential)]struct Margins{public int left,right,top,bottom;}
    static int ColorRef(Color c){return c.R|(c.G<<8)|(c.B<<16);}
    public static bool Backdrop(Form window,bool enabled,Rectangle opaque){if(!window.IsHandleCreated)return false;try{int backdrop=enabled?3:1;var margins=enabled?(opaque.IsEmpty?new Margins{left=-1}:new Margins{left=opaque.Left,top=opaque.Top,right=Math.Max(0,window.ClientSize.Width-opaque.Right),bottom=Math.Max(0,window.ClientSize.Height-opaque.Bottom)}):new Margins();bool ok=DwmSetWindowAttribute(window.Handle,38,ref backdrop,4)==0;DwmExtendFrameIntoClientArea(window.Handle,ref margins);return enabled&&ok;}catch{return false;}}
    public static bool Apply(Form window,Palette colors,bool dark,bool compact){
      if(!window.IsHandleCreated)return false;
      bool rounded=false;
      try{int mode=dark?1:0;DwmSetWindowAttribute(window.Handle,20,ref mode,4);int corner=2;rounded=SystemAppearance.Windows11&&DwmSetWindowAttribute(window.Handle,33,ref corner,4)==0;int border=-1;if(!compact){int bg=ColorRef(colors.Bg),text=ColorRef(colors.Text);DwmSetWindowAttribute(window.Handle,35,ref bg,4);DwmSetWindowAttribute(window.Handle,36,ref text,4);}DwmSetWindowAttribute(window.Handle,34,ref border,4);}
      catch{/* Older Windows versions retain their native frame. */}
      return rounded;
    }
  }
  public abstract class PaintedWindow:Form {
    protected GroveContext C;protected new float Scale=1;protected readonly Timer animation=new Timer();
    protected float Intro=1,DataBlend=1,HoverBlend;protected int frame;
    protected double MotionTime;
    protected bool Hovering,Blinking;
    DateTime introAt=DateTime.MinValue,dataAt=DateTime.MinValue,activeUntil=DateTime.MinValue,nextBlink=DateTime.UtcNow.AddSeconds(6),blinkUntil;
    readonly System.Diagnostics.Stopwatch elapsed=System.Diagnostics.Stopwatch.StartNew();
    bool prepared,appearanceGuard,frameLease;
    protected virtual bool Compact{get{return false;}}
    protected virtual Rectangle OpaqueArea{get{return Rectangle.Empty;}}
    protected bool MotionAllowed{get{return C.Prefs.motion&&SystemAppearance.Animations;}}
    public bool NativeRounded{get;private set;}
    public bool GlassActive{get;private set;}
    protected virtual Rectangle AnimationArea{get{return ClientRectangle;}}
    protected virtual bool AnimateActors{get{return true;}}
    public PaintedWindow(GroveContext context){
      C=context;AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;Font=Art.Font(12);Icon=Art.MakeIcon();BackColor=C.Colors.Bg;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
      using(var g=CreateGraphics())Scale=g.DpiX/96f;
      animation.Tick+=(s,e)=>TickAnimation();VisibleChanged+=(s,e)=>{if(Visible){introAt=DateTime.UtcNow;Intro=MotionAllowed?0:1;activeUntil=DateTime.UtcNow.AddMilliseconds(520);}UpdateAnimation();};Resize+=(s,e)=>{if(prepared){ApplyAppearance();LayoutSurface();UpdateAnimation();}};
      MouseEnter+=(s,e)=>{Hovering=true;activeUntil=DateTime.UtcNow.AddMilliseconds(350);UpdateAnimation();};MouseLeave+=(s,e)=>{Hovering=false;activeUntil=DateTime.UtcNow.AddMilliseconds(250);UpdateAnimation();};
      FormClosing+=(s,e)=>{if(!C.Quitting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;OnSoftClose();}};
    }
    protected void PrepareSurface(){prepared=true;ApplyAppearance();LayoutSurface();}
    protected abstract void OnSoftClose();
    protected virtual void LayoutSurface(){}
    protected virtual void PaintSurface(Graphics g){}
    protected void DragWindow(){Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,2,0);}
    public void ApplyAppearance(){if(C==null||appearanceGuard||IsDisposed)return;appearanceGuard=true;try{var p=C.Colors;double opacity=(Compact?C.Prefs.widgetOpacity:C.Prefs.mainOpacity)/100d;if(Math.Abs(Opacity-opacity)>.005)Opacity=opacity;NativeRounded=WindowAppearance.Apply(this,p,C.IsDark,Compact);GlassActive=WindowAppearance.Backdrop(this,C.Prefs.glass&&SystemAppearance.Build>=22621,OpaqueArea);BackColor=GlassActive?Color.Black:p.Bg;if(Compact){using(var path=Art.Round(new RectangleF(0,0,Width,Height),12*Scale)){var region=new Region(path);var previous=Region;Region=region;previous?.Dispose();}}Invalidate();}finally{appearanceGuard=false;}}
    public void AnimateData(){dataAt=DateTime.UtcNow;DataBlend=MotionAllowed?0:1;activeUntil=DateTime.UtcNow.AddMilliseconds(780);UpdateAnimation();Invalidate();}
    protected void EnterView(){introAt=DateTime.UtcNow;Intro=MotionAllowed?0:1;activeUntil=DateTime.UtcNow.AddMilliseconds(460);UpdateAnimation();}
    public void Nudge(){activeUntil=DateTime.UtcNow.AddMilliseconds(1000);UpdateAnimation();}
    static float Ease(double t){t=Math.Max(0,Math.Min(1,t));return (float)(1-Math.Pow(1-t,3));}
    void TickAnimation(){
      if(!Visible||WindowState==FormWindowState.Minimized){StopAnimation();return;}
      var now=DateTime.UtcNow;if(MotionAllowed&&AnimateActors&&now>=nextBlink){blinkUntil=now.AddMilliseconds(135);nextBlink=now.AddSeconds(6.5);if(activeUntil<now.AddMilliseconds(210))activeUntil=now.AddMilliseconds(210);}Blinking=MotionAllowed&&now<blinkUntil;MotionTime=elapsed.Elapsed.TotalSeconds;frame++;Intro=MotionAllowed?Ease((now-introAt).TotalMilliseconds/420):1;DataBlend=MotionAllowed?Ease((now-dataAt).TotalMilliseconds/650):1;float target=Hovering?1:0;HoverBlend+=(target-HoverBlend)*.3f;
      if(Intro<.999f||DataBlend<.999f)Invalidate();else if(AnimateActors||now<activeUntil)Invalidate(AnimationArea);
      UpdateAnimation();
    }
    void StopAnimation(){animation.Stop();if(frameLease){frameLease=false;FramePacing.Release();}}
    public virtual void UpdateAnimation(){if(IsDisposed)return;bool active=DateTime.UtcNow<activeUntil||Intro<.999f||DataBlend<.999f;bool idle=AnimateActors&&MotionAllowed;if(!Visible||WindowState==FormWindowState.Minimized||(!active&&!idle)){StopAnimation();return;}bool fast=active&&MotionAllowed;if(fast&&!frameLease){frameLease=true;FramePacing.Acquire();}else if(!fast&&frameLease){frameLease=false;FramePacing.Release();}animation.Interval=fast?FramePacing.FrameMilliseconds:Math.Max(25,Math.Min(6500,(int)(nextBlink-DateTime.UtcNow).TotalMilliseconds));animation.Start();}
    protected GroveButton Button(string text,Rectangle rect,Action action,bool primary=false,string glyph=null){var b=new GroveButton(text,action){Colors=()=>C.Colors,Primary=primary,Glyph=glyph};Place(b,rect.X,rect.Y,rect.Width,rect.Height);Controls.Add(b);return b;}
    protected void Place(Control ctrl,float x,float y,float w,float h){ctrl.SetBounds((int)Math.Round(x*Scale),(int)Math.Round(y*Scale),(int)Math.Round(w*Scale),(int)Math.Round(h*Scale));}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(C!=null)BeginInvoke(new Action(()=>{if(!IsDisposed)ApplyAppearance();}));}
    protected override void WndProc(ref Message m){
      if(Compact&&(m.Msg==0x83||m.Msg==0x85||m.Msg==0x86)){m.Result=m.Msg==0x86?new IntPtr(1):IntPtr.Zero;return;}
      base.WndProc(ref m);
      if(m.Msg==0x31A||m.Msg==0x320||m.Msg==0x1A){if(C!=null&&!C.Quitting)C.QueueAppearanceRefresh();}
      if(m.Msg==0x2E0){float next=(m.WParam.ToInt32()&65535)/96f;if(Math.Abs(next-Scale)>.01f){Scale=next;LayoutSurface();ApplyAppearance();}}
    }
    protected override CreateParams CreateParams{get{var cp=base.CreateParams;if(Compact){cp.Style=(cp.Style&~0x00CF0000)|unchecked((int)0x80000000);cp.ClassStyle&=~0x00020000;}return cp;}}
    protected override void OnPaintBackground(PaintEventArgs e){if(GlassActive){e.Graphics.Clear(Color.Transparent);if(!OpaqueArea.IsEmpty)using(var panel=new SolidBrush(C.Colors.Panel))e.Graphics.FillRectangle(panel,OpaqueArea);}else using(var b=new SolidBrush(C.Colors.Bg))e.Graphics.FillRectangle(b,e.ClipRectangle);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;Typography.Prepare(e.Graphics);e.Graphics.ScaleTransform(Scale,Scale);PaintSurface(e.Graphics);}
    protected override void Dispose(bool disposing){if(disposing){StopAnimation();animation.Dispose();Icon?.Dispose();}base.Dispose(disposing);}
  }
}
