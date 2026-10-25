using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class GroveMenuColors:ProfessionalColorTable {
    readonly Palette p;public GroveMenuColors(Palette colors){p=colors;UseSystemColors=false;}
    public override Color ToolStripDropDownBackground{get{return p.Panel;}}public override Color ImageMarginGradientBegin{get{return p.Panel;}}public override Color ImageMarginGradientMiddle{get{return p.Panel;}}public override Color ImageMarginGradientEnd{get{return p.Panel;}}public override Color MenuItemSelected{get{return p.Soft;}}public override Color MenuItemBorder{get{return p.Line;}}public override Color MenuBorder{get{return p.Line;}}public override Color SeparatorDark{get{return p.Line;}}public override Color SeparatorLight{get{return p.Panel;}}public override Color MenuItemPressedGradientBegin{get{return p.Soft;}}public override Color MenuItemPressedGradientEnd{get{return p.Soft;}}public override Color CheckBackground{get{return p.Soft;}}
  }
  public sealed class GroveButton:Button {
    public bool Primary,Selected,Nav,Quiet;public string Glyph;public Func<Palette> Colors;bool hover;
    readonly Timer timer=new Timer{Interval=FramePacing.FrameMilliseconds};readonly System.Diagnostics.Stopwatch transition=new System.Diagnostics.Stopwatch();float blend,fromBlend;bool frameLease;
    public GroveButton(string text,Action action){Text=text;AccessibleName=string.IsNullOrWhiteSpace(text)?"按钮":text;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;TabStop=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Click+=(s,e)=>action();MouseEnter+=(s,e)=>SetHover(true);MouseLeave+=(s,e)=>SetHover(false);VisibleChanged+=(s,e)=>{if(!Visible){hover=false;blend=0;Stop();}};timer.Tick+=(s,e)=>{float target=hover?1:0;double t=Math.Min(1,transition.Elapsed.TotalMilliseconds/110d);blend=fromBlend+(target-fromBlend)*(float)(1-Math.Pow(1-t,3));if(t>=1||!Visible||!Art.AnimationsEnabled){blend=target;Stop();}Invalidate();};}
    void Stop(){timer.Stop();transition.Stop();if(frameLease){frameLease=false;FramePacing.Release();}}
    void SetHover(bool value){hover=value;if(Nav&&!value){Stop();blend=0;}else{if(Nav)blend=Math.Max(.25f,blend);Start();}Invalidate();if(Nav&&IsHandleCreated&&Visible)Update();}
    void Start(){if(Art.AnimationsEnabled&&Visible){fromBlend=blend;transition.Restart();if(!frameLease){frameLease=true;FramePacing.Acquire();}timer.Start();}else{Stop();blend=hover?1:0;}}
    protected override void OnPaintBackground(PaintEventArgs e){}
    protected override void OnPaint(PaintEventArgs e){var p=Colors?.Invoke()??new Palette(false);float dpi=e.Graphics.DpiX/96f;GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,Nav?p.Side:Parent?.BackColor??p.Bg);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var bg=Primary?Art.Mix(p.Green,p.Text,blend*.1f):Selected?p.Soft:Nav?Art.Mix(p.Side,p.Soft,blend):Art.Mix(p.Panel,p.Hover,blend);Color ink=Primary?(p.Green.GetBrightness()>.58f?Palette.C("#22304D"):Color.White):Selected?p.Green:Nav?p.Muted:p.Text;
      if(!Quiet||blend>.01f||Selected)Art.Box(e.Graphics,new RectangleF(.5f,.5f,Width-1,Height-1),bg,Nav||Primary||Quiet?(Color?)null:p.Line,9*dpi);
      if(Selected&&Nav)Art.Box(e.Graphics,new RectangleF(1,Height*.3f,3*dpi,Height*.4f),p.Green,null,2*dpi);
      float iconSize=16*dpi;bool text=Text.Length>0;if(Glyph!=null){float x=Nav&&text?17*dpi:text?15*dpi:(Width-iconSize)/2;if(Glyph=="claude"||Glyph=="chatgpt")ProviderIcons.Draw(e.Graphics,Glyph=="claude",new RectangleF(x,(Height-iconSize)/2,iconSize,iconSize),ink);else Art.Icon(e.Graphics,Glyph,x,(Height-iconSize)/2,iconSize,ink);}
      if(text){var f=Art.CachedFont(12*dpi,Selected||Primary);var bounds=ClientRectangle;if(Nav){bounds.X=(int)(44*dpi);bounds.Width-=bounds.X;}else if(Glyph!=null){bounds.X=(int)(34*dpi);bounds.Width-=bounds.X+(int)(6*dpi);}Typography.Draw(e.Graphics,Text,f,bounds,Enabled?ink:Primary?Art.Mix(ink,bg,.35f):p.Subtle,Nav?StringAlignment.Near:StringAlignment.Center);}
      if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4),ink,bg);
    }
    protected override void Dispose(bool disposing){if(disposing){Stop();timer.Dispose();}base.Dispose(disposing);}
  }
  public sealed class GroveSwitch:CheckBox {
    public Func<Palette> Colors;
    public GroveSwitch(string title,bool value,Action<bool> change){AccessibleName=title;Checked=value;Text="";Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);CheckedChanged+=(s,e)=>{Invalidate();change(Checked);};}
    protected override void OnPaint(PaintEventArgs e){var p=Colors?.Invoke()??new Palette(false);GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,Parent?.BackColor??p.Panel);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float h=Math.Min(24,Height-2);float y=(Height-h)/2;Art.Box(e.Graphics,new RectangleF(1,y,Width-2,h),Checked?p.Green:p.Line,null,h/2);using(var b=new SolidBrush(Checked?Color.White:p.Panel))e.Graphics.FillEllipse(b,Checked?Width-h+3:4,y+3,h-6,h-6);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-1,-1));}
  }
  public sealed class GroveSlider:Control {
    public Func<Palette> Colors;public int Minimum=55,Maximum=100;int current=100;bool dragging;
    public event EventHandler ValueChanged;
    public int Value{get{return current;}set{int next=Math.Max(Minimum,Math.Min(Maximum,value));if(next==current)return;current=next;Invalidate();ValueChanged?.Invoke(this,EventArgs.Empty);AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);}}
    public GroveSlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider;Height=32;}
    void Pick(int x){Value=Minimum+(int)Math.Round(Math.Max(0,Math.Min(1,(x-10d)/Math.Max(1,Width-20)))*(Maximum-Minimum));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();dragging=true;Capture=true;Pick(e.X);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)Pick(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||key==Keys.Home||key==Keys.End||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left)Value--;if(e.KeyCode==Keys.Right)Value++;if(e.KeyCode==Keys.Home)Value=Minimum;if(e.KeyCode==Keys.End)Value=Maximum;}
    protected override void OnPaint(PaintEventArgs e){var p=Colors?.Invoke()??new Palette(false);GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,Parent?.BackColor??p.Panel);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float fraction=(Value-Minimum)/(float)Math.Max(1,Maximum-Minimum),width=Width-20;float y=Height/2;Art.Box(e.Graphics,new RectangleF(10,y-2,width,4),p.Line,null,2);if(fraction>0)Art.Box(e.Graphics,new RectangleF(10,y-2,Math.Max(4,width*fraction),4),p.Green,null,2);Art.Dot(e.Graphics,10+width*fraction-8,y-7,Color.FromArgb(24,0,0,0),17);Art.Dot(e.Graphics,10+width*fraction-7,y-7,p.Panel,14);Art.Dot(e.Graphics,10+width*fraction-4,y-4,p.Green,8);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-1,-1),p.Green,p.Panel);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccess(this);}
    sealed class SliderAccess:ControlAccessibleObject {
      readonly GroveSlider owner;public SliderAccess(GroveSlider control):base(control){owner=control;}
      public override AccessibleRole Role{get{return AccessibleRole.Slider;}}
      public override string Value{get{return owner.Value.ToString();}set{int n;if(int.TryParse((value??"").TrimEnd('%'),out n))owner.Value=n;}}
      public override AccessibleStates State{get{return base.State|AccessibleStates.Focusable;}}
    }
  }
  public sealed class GroveCombo:ComboBox {
    public Func<Palette> Colors;readonly OpaqueControlBuffer opaque=new OpaqueControlBuffer();
    public GroveCombo(){DropDownStyle=ComboBoxStyle.DropDownList;DrawMode=DrawMode.OwnerDrawFixed;FlatStyle=FlatStyle.Flat;ItemHeight=25;}
    protected override void OnDrawItem(DrawItemEventArgs e){if(e.Index<0)return;var p=Colors?.Invoke()??new Palette(false);using(var b=new SolidBrush((e.State&DrawItemState.Selected)!=0?p.Soft:p.Panel))e.Graphics.FillRectangle(b,e.Bounds);Typography.Draw(e.Graphics,Convert.ToString(Items[e.Index]),Font,new Rectangle(e.Bounds.X+9,e.Bounds.Y,e.Bounds.Width-14,e.Bounds.Height),p.Text);}
    protected override void Dispose(bool disposing){if(disposing)opaque.Dispose();base.Dispose(disposing);}
    protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg!=0xF||!IsHandleCreated)return;opaque.Paint(this);var p=Colors?.Invoke()??new Palette(false);using(var g=Graphics.FromHwnd(Handle)){float dpi=g.DpiX/96f;float button=27*dpi;using(var b=new SolidBrush(p.Hover))g.FillRectangle(b,Width-button,1,button-1,Height-2);using(var pen=new Pen(p.Line))g.DrawRectangle(pen,.5f,.5f,Width-1,Height-1);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float x=Width-button/2,y=Height/2;Art.Line(g,x-3*dpi,y-1*dpi,x,y+2*dpi,p.Muted);Art.Line(g,x,y+2*dpi,x+3*dpi,y-1*dpi,p.Muted);}}
  }
  public sealed class GroveSearch:UserControl {
    public Func<Palette> Colors;readonly TextBox edit=new GroveEdit{BorderStyle=BorderStyle.None,AccessibleName="搜索模型"};
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr value,string text);
    public GroveSearch(){DoubleBuffered=true;TabStop=false;Controls.Add(edit);edit.HandleCreated+=(s,e)=>SendMessage(edit.Handle,0x1501,IntPtr.Zero,"搜索模型名称…");edit.TextChanged+=(s,e)=>OnTextChanged(EventArgs.Empty);edit.KeyDown+=SearchKey;Click+=(s,e)=>edit.Focus();}
    void SearchKey(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.Back){int end=edit.SelectionStart,start=end;if(edit.SelectionLength==0){while(start>0&&char.IsWhiteSpace(edit.Text[start-1]))start--;while(start>0&&!char.IsWhiteSpace(edit.Text[start-1]))start--;edit.Select(start,end-start);}edit.SelectedText="";e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){edit.Clear();e.SuppressKeyPress=true;}}
    public override string Text{get{return edit==null?base.Text:edit.Text;}set{if(edit==null)base.Text=value;else edit.Text=value;}}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(edit!=null){edit.Font=Font;Arrange();}}
    protected override void OnResize(EventArgs e){base.OnResize(e);if(edit!=null)Arrange();}
    void Arrange(){using(var g=CreateGraphics()){float dpi=g.DpiX/96f;edit.SetBounds((int)(33*dpi),Math.Max(1,(Height-edit.PreferredHeight)/2),Math.Max(1,Width-(int)(45*dpi)),edit.PreferredHeight);}}
    protected override void OnPaintBackground(PaintEventArgs e){GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,Parent?.BackColor??BackColor);}
    protected override void OnPaint(PaintEventArgs e){var p=Colors?.Invoke()??new Palette(false);edit.BackColor=p.Hover;edit.ForeColor=p.Text;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float dpi=e.Graphics.DpiX/96f;Art.Box(e.Graphics,new RectangleF(.5f,.5f,Width-1,Height-1),p.Hover,p.Line,8*dpi);Art.Icon(e.Graphics,"search",10*dpi,(Height-15*dpi)/2,15*dpi,p.Subtle);base.OnPaint(e);}
  }
  public class GrovePanel:Panel {
    public Func<Palette> Colors;int roundedWidth=-1,roundedHeight=-1,roundedDpi=-1;
    public GrovePanel(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);RoundEdges();}
    void RoundEdges(){if(Width<1||Height<1||roundedWidth==Width&&roundedHeight==Height&&roundedDpi==DeviceDpi)return;roundedWidth=Width;roundedHeight=Height;roundedDpi=DeviceDpi;float radius=Math.Min(18*DeviceDpi/96f,Math.Min(Width,Height)/2f);using(var path=Art.Round(new RectangleF(0,0,Width,Height),radius)){var previous=Region;Region=new Region(path);previous?.Dispose();}}
    protected override void OnResize(EventArgs e){base.OnResize(e);RoundEdges();}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);RoundEdges();}
    protected override void OnPaintBackground(PaintEventArgs e){GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,(Colors?.Invoke()??new Palette(false)).Panel);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var p=Colors?.Invoke()??new Palette(false);float radius=Math.Min(18*DeviceDpi/96f,Math.Min(Width,Height)/2f);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=Art.Round(new RectangleF(.5f,.5f,Width-1,Height-1),radius))using(var pen=new Pen(Color.FromArgb(120,p.Line)))e.Graphics.DrawPath(pen,path);}
  }
}

