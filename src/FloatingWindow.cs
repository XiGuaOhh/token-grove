using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class FloatingWindow:PaintedWindow {
    protected override bool Compact{get{return true;}}
    protected override bool AnimateActors{get{return false;}}
    float W{get{return ClientSize.Width/Math.Max(1,Scale);}}
    readonly Dictionary<string,double?> previous=new Dictionary<string,double?>();
    readonly ToolTip tips=new ToolTip{InitialDelay=700,ReshowDelay=250,AutoPopDelay=3000};
    Point dragOrigin;bool pressed,dragged;int tipClient=-1;
    public FloatingWindow(GroveContext context):base(context){
      Text="Token Grove · 悬浮额度";AccessibleName="Token Grove 紧凑额度窗";
      FormBorderStyle=FormBorderStyle.None;ControlBox=false;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=true;TopMost=C.Prefs.topmost;StartPosition=FormStartPosition.Manual;
      ClientSize=new Size((int)(328*Scale),(int)(116*Scale));MinimumSize=Size;MaximumSize=Size;
      var area=Screen.PrimaryScreen.WorkingArea;Location=C.Prefs.widgetX!=int.MinValue?new Point(C.Prefs.widgetX,C.Prefs.widgetY):new Point(area.Right-Width-24,area.Top+36);
      if(!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(Bounds)))Location=new Point(area.Right-Width-24,area.Top+36);
      var current=Screen.FromRectangle(Bounds).WorkingArea;Left=Math.Max(current.Left,Math.Min(Left,current.Right-Width));Top=Math.Max(current.Top,Math.Min(Top,current.Bottom-Height));
      tips.SetToolTip(this,"拖动移动 · 双击返回原页面 · 右键打开菜单");
      MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){dragOrigin=e.Location;pressed=true;dragged=false;Capture=true;}};
      MouseMove+=(s,e)=>{if(!pressed||e.Button!=MouseButtons.Left)return;int dx=e.X-dragOrigin.X,dy=e.Y-dragOrigin.Y;if(!dragged&&Math.Abs(dx)+Math.Abs(dy)<4)return;dragged=true;Left+=dx;Top+=dy;};MouseCaptureChanged+=(s,e)=>{if(!Capture)pressed=false;};
      MouseMove+=(s,e)=>{if(pressed)return;int client=e.X<ClientSize.Width/2?0:1;if(client!=tipClient){tipClient=client;tips.SetToolTip(this,(client==0?"Claude":"ChatGPT / Codex")+" · 拖动移动 · 双击返回原页面 · 右键打开菜单");}};
      MouseUp+=(s,e)=>{pressed=false;Capture=false;if(e.Button==MouseButtons.Right)C.Menu?.Show(this,e.Location);if(dragged){C.Prefs.widgetX=Left;C.Prefs.widgetY=Top;C.Prefs.Save();dragged=false;}};
      MouseDoubleClick+=(s,e)=>{if(e.Button==MouseButtons.Left){pressed=false;Capture=false;C.ShowMain();}};
      KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Return)C.ShowMain();if(e.KeyCode==Keys.Escape)C.HideWidget();};PrepareSurface();
    }
    public void SnapshotChanged(Snapshot old){previous.Clear();if(old!=null)foreach(var a in old.accounts)foreach(var q in a.windows)previous[a.provider+":"+q.id]=q.usedPercent;AnimateData();UpdateDescription();}
    void UpdateDescription(){AccessibleDescription=string.Join("；",C.Data.accounts.Select(a=>a.label+"："+string.Join("，",a.windows.Where(w=>w.id=="five_hour"||w.id=="seven_day").Select(w=>w.label+"已用 "+(w.usedPercent.HasValue?w.usedPercent.Value.ToString("0.#")+"%":"未知")))));}
    protected override void OnSoftClose(){C.HideWidget();}
    protected override void PaintSurface(Graphics g){var p=C.Colors;if(GlassActive){using(var border=new Pen(p.Line))using(var path=Art.Round(new RectangleF(.5f,.5f,W-1,ClientSize.Height/Scale-1),12))g.DrawPath(border,path);}else Art.Box(g,new RectangleF(.5f,.5f,W-1,ClientSize.Height/Scale-1),p.Panel,p.Line,12);
      float column=(W-46)/2;Art.Line(g,W/2,13,W/2,103,p.Line);
      Client(g,p,C.Data.accounts.FirstOrDefault(a=>a.provider=="claude"),true,14,column);Client(g,p,C.Data.accounts.FirstOrDefault(a=>a.provider=="chatgpt"),false,32+column,column);
    }
    void Client(Graphics g,Palette p,Account a,bool claude,float x,float width){ProviderIcons.Draw(g,claude,new RectangleF(x+(width-23)/2,9,23,23),claude?p.Coral:p.Green);Cell(g,p,a,a?.Five,"5 小时",x,40,width);Cell(g,p,a,a?.Week,"每周",x,78,width);}
    void Cell(Graphics g,Palette p,Account a,WindowQuota q,string label,float x,float y,float width){double? value=q?.usedPercent,before;Art.Text(g,label,x,y+1,10.5f,p.Muted);Art.Text(g,value.HasValue?value.Value.ToString("0.#")+"%":"—",x,y-1,14,p.Text,true,width,StringAlignment.Far);if(value.HasValue&&a!=null&&previous.TryGetValue(a.provider+":"+q.id,out before)&&before.HasValue)value=before.Value+(value.Value-before.Value)*DataBlend;
      Art.Progress(g,new RectangleF(x,y+22,width,5),value,q?.usedPercent>=C.Prefs.threshold?p.Warning:a?.provider=="claude"?p.Coral:p.Mint,p);
    }
    protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
  }
}
