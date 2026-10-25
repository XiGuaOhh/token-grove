using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class GroveScrollPanel:GrovePanel {
    readonly Panel viewport=new Panel();readonly ContentSurface content;
    readonly Timer timer=new Timer{Interval=FramePacing.FrameMilliseconds};readonly Stopwatch watch=new Stopwatch();
    int offset,contentHeight,dragY,dragOffset;double position,target,lastFrame;bool dragging,hover,lease,arranging;
    public Control.ControlCollection ContentControls{get{return content.Controls;}}
    public int ContentHeight{get{return contentHeight;}set{contentHeight=Math.Max(0,value);Arrange();}}
    int Limit{get{return Math.Max(0,contentHeight-ClientSize.Height);}}
    float Dpi{get{return DeviceDpi/96f;}}
    RectangleF Thumb{get{float h=Math.Max(1,ClientSize.Height-24*Dpi),length=Math.Min(h,Math.Max(36*Dpi,ClientSize.Height/(float)Math.Max(1,contentHeight)*h));return new RectangleF(Width-11*Dpi,12*Dpi+(h-length)*offset/Math.Max(1,Limit),5*Dpi,length);}}
    public GroveScrollPanel(){AutoScroll=false;TabStop=true;AccessibleName="外观设置，可平滑滚动";AccessibleDescription="滚轮浏览；Page Up、Page Down、Home、End 和 Tab 均可使用。";content=new ContentSurface(this);viewport.Controls.Add(content);Controls.Add(viewport);content.ControlAdded+=(s,e)=>Hook(e.Control);timer.Tick+=(s,e)=>Advance();VisibleChanged+=(s,e)=>{if(!Visible){ApplyPosition(target);Stop();}};}
    void Hook(Control child){child.MouseWheel+=ChildWheel;child.Enter+=Reveal;foreach(Control nested in child.Controls)Hook(nested);child.ControlAdded+=(s,e)=>Hook(e.Control);}
    void Reveal(object sender,EventArgs e){var child=sender as Control;if(child==null||!child.IsHandleCreated||!IsHandleCreated)return;var bounds=RectangleToClient(child.Parent.RectangleToScreen(child.Bounds));if(bounds.Top<12*Dpi)ScrollTo(offset+bounds.Top-12*Dpi,false);else if(bounds.Bottom>Height-12*Dpi)ScrollTo(offset+bounds.Bottom-Height+12*Dpi,false);}
    void ChildWheel(object sender,MouseEventArgs e){Wheel(e);}
    void Wheel(MouseEventArgs e){int lines=SystemInformation.MouseWheelScrollLines;if(lines==0)return;double step=lines<0?Height-40*Dpi:lines*18*Dpi,delta=-e.Delta/120d*step;double origin=(target-position)*delta<0?position:target;ScrollTo(origin+delta,true);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
    void ScrollTo(double value,bool animate){target=Math.Max(0,Math.Min(Limit,value));if(!animate||!Visible||!Art.AnimationsEnabled){ApplyPosition(target);Stop();return;}if(Math.Abs(target-position)<.4){ApplyPosition(target);Stop();return;}if(!lease){lease=true;FramePacing.Acquire();watch.Restart();lastFrame=0;}timer.Start();}
    void Advance(){if(!Visible||!Art.AnimationsEnabled){ApplyPosition(target);Stop();return;}double now=watch.Elapsed.TotalSeconds,dt=Math.Min(.05,Math.Max(.001,now-lastFrame));lastFrame=now;double next=position+(target-position)*(1-Math.Exp(-dt/.045));if(Math.Abs(target-next)<.4){ApplyPosition(target);Stop();}else ApplyPosition(next);}
    void ApplyPosition(double value){position=Math.Max(0,Math.Min(Limit,value));int next=(int)Math.Round(position);if(next==offset)return;offset=next;if(content!=null)content.Top=-offset-(int)(12*Dpi);Invalidate(new Rectangle(Math.Max(0,Width-(int)(20*Dpi)),0,(int)(20*Dpi),Height));}
    void Stop(){timer.Stop();watch.Stop();if(lease){lease=false;FramePacing.Release();}}
    void Arrange(){if(content==null||arranging)return;arranging=true;try{int inset=(int)(12*Dpi);viewport.SetBounds(inset,inset,Math.Max(1,Width-inset-(int)(19*Dpi)),Math.Max(1,Height-inset*2));content.SetBounds(-inset,-offset-inset,Math.Max(1,Width),Math.Max(Height,contentHeight)+inset*2);target=Math.Min(target,Limit);ApplyPosition(Math.Min(position,Limit));RefreshAppearance();}finally{arranging=false;}}
    public void RefreshAppearance(){var p=Colors?.Invoke()??new Palette(false);BackColor=viewport.BackColor=content.BackColor=p.Panel;}
    protected override void OnResize(EventArgs e){base.OnResize(e);Arrange();Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){Wheel(e);base.OnMouseWheel(e);}
    protected override bool IsInputKey(Keys key){return key==Keys.PageDown||key==Keys.PageUp||key==Keys.Home||key==Keys.End||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.PageDown)ScrollTo(target+Height-40*Dpi,true);if(e.KeyCode==Keys.PageUp)ScrollTo(target-Height+40*Dpi,true);if(e.KeyCode==Keys.Home)ScrollTo(0,true);if(e.KeyCode==Keys.End)ScrollTo(Limit,true);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||Limit==0||e.X<Width-20*Dpi)return;Focus();Stop();target=position;if(Thumb.Contains(e.Location)){dragging=true;dragY=e.Y;dragOffset=offset;Capture=true;}else ScrollTo(offset+(e.Y<Thumb.Y?-1:1)*(Height-40*Dpi),true);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool at=e.X>=Width-20*Dpi;if(at!=hover){hover=at;Invalidate();}if(dragging){float travel=Height-24*Dpi-Thumb.Height;ScrollTo(dragOffset+(e.Y-dragY)*Limit/Math.Max(1,travel),false);}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Limit>0){var p=Colors?.Invoke()??new Palette(false);Art.Box(e.Graphics,Thumb,hover||dragging?p.Muted:p.Line,null,3*Dpi);}}
    protected override void Dispose(bool disposing){if(disposing){Stop();timer.Dispose();}base.Dispose(disposing);}
    sealed class ContentSurface:Panel {
      readonly GroveScrollPanel owner;public ContentSurface(GroveScrollPanel panel){owner=panel;DoubleBuffered=true;}
      protected override void OnMouseWheel(MouseEventArgs e){owner.Wheel(e);base.OnMouseWheel(e);}
    }
  }
}
