using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class PlansView:GrovePanel {
    readonly GroveContext C;readonly float scale;readonly PlanList list;
    readonly GroveEdit title,body;readonly GroveCombo client,reminder;readonly GroveDateInput when;
    readonly GroveButton add,pending,finished,copy,complete,remove,save;
    readonly Timer autosave=new Timer{Interval=600};
    WorkPlan selected;bool loading,dirty,showCompleted;string notice="",saveState="已保存";
    float W{get{return Width/scale;}}float H{get{return Height/scale;}}float Split{get{return Math.Min(315,Math.Max(270,W*.33f));}}
    RectangleF TitleBox{get{return new RectangleF(Split+18,20,W-Split-36,40);}}
    RectangleF BodyBox{get{return new RectangleF(Split+18,97,W-Split-36,Math.Max(90,H-280));}}
    public PlansView(GroveContext context,float dpi){
      C=context;scale=dpi;Colors=()=>C.Colors;AccessibleName="计划，额度重置后继续";
      list=new PlanList{Colors=()=>C.Colors};list.SelectPlan+=SelectPlan;Controls.Add(list);
      pending=Button("待办",()=>Filter(false));pending.Quiet=true;pending.Selected=true;
      finished=Button("已完成",()=>Filter(true));finished.Quiet=true;
      add=Button("+ 新计划",NewPlan,true);
      title=new GroveEdit{BorderStyle=BorderStyle.None,MaxLength=160,AccessibleName="计划标题",Font=Art.Font(15*scale)};
      body=new GroveEdit{Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,MaxLength=32000,AccessibleName="计划内容",Font=Art.Font(12*scale)};
      client=new GroveCombo{Colors=()=>C.Colors,Font=Art.Font(11.5f*scale),AccessibleName="计划使用的客户端"};client.Items.AddRange(new object[]{"Claude","Codex"});
      reminder=new GroveCombo{Colors=()=>C.Colors,Font=Art.Font(11.5f*scale),AccessibleName="计划提醒方式"};reminder.Items.AddRange(new object[]{"额度重置时提醒","指定时间提醒","只记录"});
      when=new GroveDateInput("计划提醒时间"){Colors=()=>C.Colors,Font=Art.Font(11.5f*scale)};
      foreach(Control control in new Control[]{title,body,client,reminder,when})Controls.Add(control);
      copy=Button("复制计划",CopyPlan,false,"copy");complete=Button("标记完成",ToggleCompleted);remove=Button("删除",DeletePlan);remove.Quiet=true;save=Button("保存",()=>Flush());
      title.TextChanged+=(s,e)=>Changed();body.TextChanged+=(s,e)=>Changed();client.SelectedIndexChanged+=(s,e)=>Changed();
      reminder.SelectedIndexChanged+=(s,e)=>{Changed();LayoutContent();};when.TextChanged+=(s,e)=>Changed();
      autosave.Tick+=(s,e)=>{autosave.Stop();Flush();};VisibleChanged+=(s,e)=>{if(!Visible)Flush();};Resize+=(s,e)=>LayoutContent();
      RefreshData();LayoutContent();ApplyColors();
    }
    GroveButton Button(string text,Action action,bool primary=false,string glyph=null){var button=new GroveButton(text,action){Colors=()=>C.Colors,Primary=primary,Glyph=glyph};Controls.Add(button);return button;}
    void Place(Control c,float x,float y,float width,float height){c.SetBounds((int)(x*scale),(int)(y*scale),(int)(Math.Max(1,width)*scale),(int)(Math.Max(1,height)*scale));}
    void LayoutContent(){if(list==null)return;float x=Split+18,right=W-18;
      Place(pending,12,16,66,32);Place(finished,82,16,76,32);Place(add,Split-111,16,98,32);Place(list,10,63,Split-20,H-76);
      Place(title,TitleBox.X+12,TitleBox.Y+8,TitleBox.Width-24,title.PreferredHeight/scale);
      Place(body,BodyBox.X+12,BodyBox.Y+11,BodyBox.Width-24,BodyBox.Height-22);
      float content=right-x;Place(client,x,H-149,Math.Min(166,content*.34f),32);Place(reminder,x+Math.Min(166,content*.34f)+12,H-149,content-Math.Min(166,content*.34f)-12,32);
      Place(when,x,H-102,Math.Min(240,content),33);
      Place(copy,x,H-51,110,32);Place(complete,x+120,H-51,112,32);Place(remove,x+242,H-51,64,32);Place(save,right-82,H-51,82,32);
      bool has=selected!=null;foreach(Control control in new Control[]{title,body,client,reminder,copy,complete,remove,save})control.Visible=has;
      when.Visible=has&&reminder.SelectedIndex==1;add.Enabled=C.Plans.CanWrite;Invalidate();
    }
    void Changed(){if(loading||selected==null)return;dirty=true;saveState="保存中…";notice="";autosave.Stop();autosave.Start();Invalidate();}
    public bool Flush(){
      autosave.Stop();if(!dirty||selected==null)return true;
      DateTime date=DateTime.Now;string mode=reminder.SelectedIndex==1?"time":reminder.SelectedIndex==2?"none":"quota";
      if(mode=="time"&&(!when.TryValue(out date)||TimeZoneInfo.Local.IsInvalidTime(date))){saveState="未保存";notice="请补全有效提醒时间，或选择“只记录”。";Invalidate();return false;}
      try{
        string provider=client.SelectedIndex==1?"chatgpt":"claude";
        bool rearm=selected.provider!=provider||selected.reminder!=mode||mode=="time"&&J.Time(selected.dueUtc)!=date.ToUniversalTime();
        selected.title=string.IsNullOrWhiteSpace(title.Text)?"未命名计划":title.Text.Trim();selected.body=body.Text;selected.provider=provider;selected.reminder=mode;selected.updatedUtc=J.Iso(DateTime.UtcNow);
        if(rearm)PlanScheduler.Arm(selected,C.Imported?null:C.Data,DateTime.UtcNow,mode=="time"?(DateTime?)date:null);
        C.Plans.Save();dirty=false;saveState="已保存";notice="";RefreshData();C.PlansChanged();C.CheckPlans();return true;
      }catch{saveState="未保存";notice="保存失败，内容仍在当前窗口。请点击保存重试。";Invalidate();return false;}
    }
    public void NewPlan(){
      if(!Flush())return;
      try{showCompleted=false;UpdateFilters();string provider=C.Data.accounts.FirstOrDefault(a=>a.windows.Any(w=>w.usedPercent>=100))?.provider??"claude";var plan=C.Plans.Create(provider,C.Imported?null:C.Data,DateTime.UtcNow);dirty=false;SelectPlan(plan);dirty=true;Flush();title.Focus();title.SelectAll();}
      catch(Exception e){notice=e.Message;Invalidate();}
    }
    void Filter(bool done){if(!Flush())return;showCompleted=done;UpdateFilters();selected=null;RefreshData();}
    void UpdateFilters(){pending.Selected=!showCompleted;finished.Selected=showCompleted;pending.Invalidate();finished.Invalidate();}
    public void SelectPlan(WorkPlan plan){
      if(plan==selected)return;if(!Flush())return;selected=plan;loading=true;
      try{title.Text=plan?.title??"";body.Text=plan?.body??"";client.SelectedIndex=plan?.provider=="chatgpt"?1:0;reminder.SelectedIndex=plan?.reminder=="time"?1:plan?.reminder=="none"?2:0;when.Value=J.Time(plan?.dueUtc)?.ToLocalTime()??DateTime.Now.AddHours(1);}
      finally{loading=false;}dirty=false;saveState="已保存";notice="";complete.Text=plan?.Completed==true?"标记未完成":"标记完成";list.SelectedId=plan?.id;list.Invalidate();LayoutContent();
    }
    public void FocusReady(){if(!Flush())return;showCompleted=false;UpdateFilters();RefreshData();var plan=C.Plans.Items.FirstOrDefault(p=>p.Ready);if(plan!=null)SelectPlan(plan);}
    public void RefreshData(){
      if(list==null)return;var rows=C.Plans.Items.Where(p=>p.Completed==showCompleted).OrderByDescending(p=>p.Ready).ThenByDescending(p=>J.Time(p.updatedUtc)).ToList();list.SetItems(rows);
      if(selected==null||!rows.Contains(selected)){if(!dirty)SelectPlan(rows.FirstOrDefault());}else{list.SelectedId=selected.id;list.Invalidate();}
      if(C.Plans.LoadError!=null)notice=C.Plans.LoadError;LayoutContent();Invalidate();
    }
    void CopyPlan(){if(selected==null)return;try{var text=string.IsNullOrWhiteSpace(title.Text)?"未命名计划":title.Text.Trim();if(!string.IsNullOrWhiteSpace(body.Text))text+=Environment.NewLine+Environment.NewLine+body.Text;Clipboard.SetText(text);notice="已复制，可以粘贴到 "+(client.SelectedIndex==1?"Codex":"Claude")+"。";}catch{notice="剪贴板暂不可用，请稍后重试。";}Invalidate();}
    void ToggleCompleted(){
      if(selected==null||!Flush())return;string previous=selected.completedUtc;selected.completedUtc=selected.Completed?null:J.Iso(DateTime.UtcNow);
      try{C.Plans.Save();selected=null;RefreshData();C.PlansChanged();}catch{selected.completedUtc=previous;notice="保存失败，请重试。";Invalidate();}
    }
    void DeletePlan(){
      if(selected==null||!Flush())return;if(MessageBox.Show(this,"删除这个计划？","计划",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
      var plan=selected;int index=C.Plans.Items.IndexOf(plan);C.Plans.Items.Remove(plan);
      try{C.Plans.Save();selected=null;RefreshData();C.PlansChanged();}catch{C.Plans.Items.Insert(index,plan);notice="删除未保存，请重试。";Invalidate();}
    }
    public void ApplyColors(){var p=C.Colors;BackColor=p.Panel;title.BackColor=body.BackColor=p.Hover;title.ForeColor=body.ForeColor=p.Text;client.BackColor=reminder.BackColor=p.Hover;client.ForeColor=reminder.ForeColor=p.Text;when.ApplyColors();foreach(Control control in Controls)control.Invalidate();Invalidate();}
    protected override bool ProcessCmdKey(ref Message message,Keys keys){if(keys==(Keys.Control|Keys.S)){Flush();return true;}if(keys==(Keys.Control|Keys.N)){NewPlan();return true;}return base.ProcessCmdKey(ref message,keys);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;var state=g.Save();g.ScaleTransform(scale,scale);var p=C.Colors;Art.Line(g,Split,18,Split,H-18,p.Line);
      if(selected==null){Art.Text(g,"先记下来，等额度恢复再继续。",Split+36,H*.36f,17,p.Muted,false,W-Split-72,StringAlignment.Center);}
      else{
        Art.Box(g,TitleBox,p.Hover,p.Line,10);Art.Box(g,BodyBox,p.Hover,p.Line,12);Art.Text(g,"计划内容",Split+18,75,10.5f,p.Muted);Art.Text(g,saveState,W-120,76,9.5f,dirty?p.Warning:p.Subtle,false,100,StringAlignment.Far);
        Art.Text(g,"客户端",Split+18,H-171,10.5f,p.Muted);Art.Text(g,"提醒",client.Right/scale+12,H-171,10.5f,p.Muted);
        if(reminder.SelectedIndex==1)Art.Text(g,"本机时间",when.Right/scale+12,H-93,10,p.Subtle);
        else Art.Text(g,PlanScheduler.Status(selected)+(selected.reminder=="quota"?" · "+selected.Client:""),Split+20,H-94,11,selected.Ready?p.Green:p.Muted,false,W-Split-40);
      }
      if(notice.Length>0)Art.Text(g,notice,Split+18,H-18,9,p.Warning,false,W-Split-36);g.Restore(state);
    }
    protected override void Dispose(bool disposing){if(disposing){autosave.Stop();autosave.Dispose();}base.Dispose(disposing);}
  }
  sealed class PlanList:Control {
    public Func<Palette> Colors;public event Action<WorkPlan> SelectPlan;public string SelectedId;
    List<WorkPlan> items=new List<WorkPlan>();int scroll;
    float Dpi{get{return DeviceDpi/96f;}}int RowHeight{get{return (int)(91*Dpi);}}int Limit{get{return Math.Max(0,items.Count*RowHeight-Height);}}
    public PlanList(){DoubleBuffered=true;TabStop=true;AccessibleName="计划列表";SetStyle(ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);}
    public void SetItems(List<WorkPlan> value){items=value;scroll=Math.Min(scroll,Limit);Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);scroll=Math.Max(0,Math.Min(Limit,scroll-e.Delta/120*RowHeight));Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();int index=(e.Y+scroll)/RowHeight;if(index>=0&&index<items.Count)SelectPlan?.Invoke(items[index]);}
    protected override bool IsInputKey(Keys keys){return keys==Keys.Down||keys==Keys.Up||base.IsInputKey(keys);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode!=Keys.Down&&e.KeyCode!=Keys.Up)return;int current=items.FindIndex(p=>p.id==SelectedId),next=Math.Max(0,Math.Min(items.Count-1,current+(e.KeyCode==Keys.Down?1:-1)));if(next<0||next>=items.Count)return;scroll=Math.Max(0,Math.Min(Limit,Math.Min(scroll,next*RowHeight)));if((next+1)*RowHeight-scroll>Height)scroll=(next+1)*RowHeight-Height;SelectPlan?.Invoke(items[next]);Invalidate();e.Handled=true;}
    protected override void OnPaintBackground(PaintEventArgs e){GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,Parent?.BackColor??SystemColors.Control);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var p=Colors?.Invoke()??new Palette(false);var g=e.Graphics;var state=g.Save();g.ScaleTransform(Dpi,Dpi);float width=Width/Dpi;
      if(items.Count==0)Art.Text(g,"还没有计划",0,32,11,p.Subtle,false,width,StringAlignment.Center);
      int start=scroll/Math.Max(1,RowHeight),end=Math.Min(items.Count,start+Height/Math.Max(1,RowHeight)+2);
      for(int i=start;i<end;i++){var plan=items[i];float y=(i*RowHeight-scroll)/Dpi;bool selected=plan.id==SelectedId;if(selected)Art.Box(g,new RectangleF(1,y+2,width-3,85),p.Soft,null,12);
        Art.Text(g,plan.title,12,y+11,12.5f,p.Text,true,width-26);Art.Text(g,(plan.body??"").Replace("\r"," ").Replace("\n"," "),12,y+34,10,p.Muted,false,width-26);
        ProviderIcons.Draw(g,plan.provider=="claude",new RectangleF(13,y+60,14,14),p.Muted);Art.Text(g,PlanScheduler.Status(plan),34,y+59,9.3f,plan.Ready?p.Green:p.Subtle,false,width-46);
      }
      if(Limit>0){float height=Height/Dpi,thumb=Math.Max(20,height*Height/(items.Count*RowHeight));Art.Box(g,new RectangleF(width-4,(height-thumb)*scroll/Limit,3,thumb),p.Line,null,2);}g.Restore(state);
    }
  }
}
