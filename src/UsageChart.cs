using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class GroveTrendChart:Control {
    public Func<Palette> Colors;UsageResult result;bool records,lease;int hovered=-1;float progress=1;
    readonly Timer timer=new Timer{Interval=FramePacing.FrameMilliseconds};readonly Stopwatch watch=new Stopwatch();
    readonly ToolTip tip=new ToolTip{InitialDelay=180,ReshowDelay=70,AutoPopDelay=10000};
    public GroveTrendChart(){DoubleBuffered=true;TabStop=true;AccessibleName="用量趋势，左右键切换 Token 或记录数量";SetStyle(ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);timer.Tick+=(s,e)=>{progress=(float)(1-Math.Pow(1-Math.Min(1,watch.Elapsed.TotalMilliseconds/300),3));Invalidate();if(progress>=.999f)SuspendAnimation();};VisibleChanged+=(s,e)=>{if(!Visible)SuspendAnimation();};}
    public void SetResult(UsageResult value){SuspendAnimation();result=value;hovered=-1;tip.SetToolTip(this,null);if(value!=null&&Visible&&Art.AnimationsEnabled){progress=0;lease=true;FramePacing.Acquire();watch.Restart();timer.Start();}Invalidate();}
    public void SuspendAnimation(){timer.Stop();watch.Stop();progress=1;if(lease){lease=false;FramePacing.Release();}}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;float dpi=g.DpiX/96f;g.ScaleTransform(dpi,dpi);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var p=Colors?.Invoke()??new Palette(false);float w=Width/dpi,h=Height/dpi;g.Clear(p.Panel);Art.Line(g,0,0,w,0,p.Line);
      Art.Text(g,"用量趋势",15,12,11.5f,p.Text,true);Art.Dot(g,99,18,p.Coral,6);Art.Text(g,"Claude",111,12,9.5f,p.Muted);Art.Dot(g,166,18,p.Mint,6);Art.Text(g,"Codex",178,12,9.5f,p.Muted);
      Art.Box(g,new RectangleF(w-145,9,130,27),p.Panel,p.Line,7);Art.Box(g,new RectangleF(w-(records?79:144),10,64,25),p.Soft,null,6);Art.Text(g,"Tokens",w-139,15,10,!records?p.Green:p.Subtle,false,57,StringAlignment.Center);Art.Text(g,"记录",w-72,15,10,records?p.Green:p.Subtle,false,52,StringAlignment.Center);
      var plot=new RectangleF(48,48,w-63,h-76);long max=1;if(result!=null&&result.trend.Count>0)max=Math.Max(1,result.trend.Max(b=>records?(long)b.records:b.claude+b.chatgpt));
      for(int i=0;i<3;i++){float y=plot.Bottom-plot.Height*i/2;Art.Line(g,plot.Left,y,plot.Right,y,p.Line);Art.Text(g,Axis((long)(max*i/2d)),3,y-7,8.5f,p.Subtle,false,39,StringAlignment.Far);}
      if(result==null){Art.Text(g,"暂无数据",plot.Left,plot.Top+plot.Height*.35f,11,p.Subtle,false,plot.Width,StringAlignment.Center);return;}
      int count=result.trend.Count;if(count==0)return;float cell=plot.Width/count;for(int i=0;i<count;i++){var b=result.trend[i];float value=records?b.records:b.claude+b.chatgpt;float height=value/max*plot.Height*progress,x=plot.X+i*cell+cell*.17f,width=Math.Max(1,cell*.66f);if(i==hovered){using(var fill=new SolidBrush(Color.FromArgb(25,p.Green)))g.FillRectangle(fill,plot.X+i*cell,plot.Y,cell,plot.Height);}if(height>.2f){float lower=records?0:b.claude/(float)Math.Max(1,b.claude+b.chatgpt)*height;if(lower>.2f)Art.Box(g,new RectangleF(x,plot.Bottom-lower,width,Math.Max(1,lower)),p.Coral,null,Math.Min(3,width/3));if(height-lower>.2f)Art.Box(g,new RectangleF(x,plot.Bottom-height,width,Math.Max(1,height-lower)),records?p.Green:p.Mint,null,Math.Min(3,width/3));}}
      int interval=Math.Max(1,(int)Math.Ceiling(count/8d));for(int i=0;i<count;i+=interval){var when=result.trend[i].fromUtc.ToLocalTime();string label=(result.toUtc-result.fromUtc).TotalHours<=48?when.ToString("HH:mm"):when.ToString("MM/dd");Art.Text(g,label,plot.X+i*cell-cell*.7f,plot.Bottom+7,8.5f,p.Subtle,false,Math.Max(43,cell*2.4f),StringAlignment.Center);}
      if(result.records==0)Art.Text(g,"暂无记录",plot.X,plot.Y+plot.Height*.35f,11,p.Muted,false,plot.Width,StringAlignment.Center);
    }
    static string Axis(long value){double unit=value>=1000000000?1000000000:value>=1000000?1000000:value>=1000?1000:1;string suffix=unit==1000000000?"B":unit==1000000?"M":unit==1000?"K":"";return (value/unit).ToString(value/unit>=100?"0":"0.#")+suffix;}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Right){records=e.KeyCode==Keys.Right;Invalidate();e.Handled=true;}}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();float dpi=DeviceDpi/96f;if(e.Y<40*dpi&&e.X>Width-150*dpi){records=e.X>Width-80*dpi;AccessibleDescription=records?"按本机记录数量查看趋势":"按 Token 数量查看趋势";Invalidate();}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(result==null||result.trend.Count==0)return;float dpi=DeviceDpi/96f,cell=(Width/dpi-63)/result.trend.Count;int index=(int)Math.Floor((e.X/dpi-48)/cell);if(e.Y/dpi<43||e.Y>Height-22*dpi)index=-1;if(index<0||index>=result.trend.Count)index=-1;if(index==hovered)return;hovered=index;if(index<0)tip.SetToolTip(this,null);else{var b=result.trend[index];tip.SetToolTip(this,b.fromUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")+" 至 "+b.toUtc.ToLocalTime().ToString("MM-dd HH:mm")+"\nClaude  "+b.claude.ToString("N0")+" Token\nCodex  "+b.chatgpt.ToString("N0")+" Token\n"+b.records.ToString("N0")+" 条记录");}Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=-1;tip.SetToolTip(this,null);Invalidate();}
    protected override void Dispose(bool disposing){if(disposing){SuspendAnimation();timer.Dispose();tip.Dispose();}base.Dispose(disposing);}
  }
}
