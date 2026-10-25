using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class UsageView:GrovePanel {
    readonly GroveContext C;readonly float scale;
    readonly GroveDateInput from,to;readonly GroveSearch search;
    readonly GroveButton query,export,previous,next;readonly GroveTable table;readonly GroveTrendChart chart;
    readonly List<GroveButton> presets=new List<GroveButton>(),sources=new List<GroveButton>(),tabs=new List<GroveButton>();
    readonly Timer debounce=new Timer{Interval=240};readonly ToolTip tips=new ToolTip{InitialDelay=450,AutoPopDelay=9000};
    readonly Dictionary<string,WindowTokenUsage> windowTokens=new Dictionary<string,WindowTokenUsage>();
    UsageResult result;Record[] recordSnapshot=new Record[0];string selectedSource,querySource,queryModel;
    string notice="选择时段，查看本机 Token 用量。";bool error,busy,ready,changingRange;int queryRevision,tab,page,selectedPreset=3;string lastTip;
    const int PageSize=50;
    float W{get{return ClientSize.Width/scale;}}float H{get{return ClientSize.Height/scale;}}
    int RowCount{get{return result==null?0:tab==0?result.models.Count:tab==1?result.daily.Count:tab==2?result.records:0;}}
    public UsageView(GroveContext context,float dpi){
      C=context;scale=dpi;Colors=()=>C.Colors;
      from=new GroveDateInput("开始时间"){Colors=()=>C.Colors,Font=Art.Font(11.5f*scale)};to=new GroveDateInput("截止时间"){Colors=()=>C.Colors,Font=Art.Font(11.5f*scale)};Controls.Add(from);Controls.Add(to);
      search=new GroveSearch{Colors=()=>C.Colors,AccessibleName="查询模型名称",Font=Art.Font(11.5f*scale)};Controls.Add(search);
      query=AddButton("查询",()=>_ = RunQuery(true),true,"search");export=AddButton("导出 CSV",Export,false,"download");
      string[] sourceLabels={"全部","Claude","Codex"};for(int i=0;i<sourceLabels.Length;i++){int index=i;var button=AddButton(sourceLabels[i],()=>SelectSource(index));button.Quiet=true;button.Glyph=i==1?"claude":i==2?"chatgpt":null;button.Selected=i==0;sources.Add(button);}
      string[] labels={"今天","昨天","近 5 小时","近 7 天","近 30 天","全部"};for(int i=0;i<labels.Length;i++){int index=i;var button=AddButton(labels[i],()=>Preset(index));button.Quiet=true;button.Selected=i==selectedPreset;presets.Add(button);}
      foreach(string label in new[]{"模型","每日","记录","额度窗口"}){int index=tabs.Count;var button=AddButton(label,()=>SelectTab(index));button.Quiet=true;button.Selected=index==0;tabs.Add(button);}
      previous=AddButton("上一页",()=>{if(page>0){page--;BindTable();}});next=AddButton("下一页",()=>{if((page+1)*PageSize<RowCount){page++;BindTable();}});
      table=new GroveTable{ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EnableHeadersVisualStyles=false,ColumnHeadersHeight=(int)(32*scale),RowTemplate={Height=(int)(33*scale)},Font=Art.Font(10.7f*scale),AccessibleName="本机用量明细，每页 50 行"};
      table.DefaultCellStyle.Padding=new Padding((int)(5*scale),0,(int)(5*scale),0);table.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.False;Controls.Add(table);
      chart=new GroveTrendChart{Colors=()=>C.Colors};Controls.Add(chart);
      debounce.Tick+=(s,e)=>{debounce.Stop();_ = RunQuery();};search.TextChanged+=(s,e)=>QueueQuery();from.TextChanged+=(s,e)=>RangeChanged();to.TextChanged+=(s,e)=>RangeChanged();
      Resize+=(s,e)=>LayoutContent();VisibleChanged+=(s,e)=>{if(!Visible){debounce.Stop();chart.SuspendAnimation();}else if(ready)BeginInvoke(new Action(()=>{if(!IsDisposed&&Visible&&result==null&&!busy)_ = RunQuery();}));};MouseMove+=ShowWindowTip;
      from.Value=DateTime.Today.AddDays(-6);to.Value=DateTime.Today.AddDays(1);ready=true;LayoutContent();ApplyColors();
      tips.SetToolTip(from,"本机时间，开始时刻计入。可直接编辑，或点击日历。");tips.SetToolTip(to,"截止时刻不计入。查询整天：当天 00:00 至次日 00:00。");
    }
    GroveButton AddButton(string text,Action action,bool primary=false,string glyph=null){var b=new GroveButton(text,action){Colors=()=>C.Colors,Primary=primary,Glyph=glyph};Controls.Add(b);return b;}
    void Place(Control control,float x,float y,float width,float height){control.SetBounds((int)(x*scale),(int)(y*scale),(int)(Math.Max(1,width)*scale),(int)(Math.Max(1,height)*scale));}
    void LayoutContent(){if(table==null)return;float x=18;for(int i=0;i<sources.Count;i++){float width=i==0?57:i==1?96:91;Place(sources[i],x,15,width,31);x+=width+4;}
      x=W-421;for(int i=0;i<presets.Count;i++){float width=i<2?54:i==2?79:i<5?74:50;Place(presets[i],x,15,width,31);x+=width+3;}
      float field=Math.Min(185,(W-365)/2);Place(from,39,58,field,34);Place(to,64+field,58,field,34);float after=78+field*2;Place(search,after,58,W-after-132,34);Place(query,W-119,58,101,34);
      Place(chart,18,180,W-36,Math.Max(120,Math.Min(170,H-455)));float details=chart.Bottom/scale+13;
      x=18;foreach(var button in tabs){Place(button,x,details,94,32);x+=98;}Place(export,W-151,details,133,32);
      Place(table,18,details+45,W-36,Math.Max(66,H-details-107));Place(previous,W-191,H-55,80,28);Place(next,W-103,H-55,85,28);Invalidate();}
    void SelectSource(int index){selectedSource=index==1?"claude":index==2?"chatgpt":null;for(int i=0;i<sources.Count;i++){sources[i].Selected=i==index;sources[i].Invalidate();}QueueQuery();}
    void SelectTab(int index){tab=index;page=0;for(int i=0;i<tabs.Count;i++){tabs[i].Selected=i==index;tabs[i].Invalidate();}BindTable();LayoutContent();Invalidate();}
    void QueueQuery(){if(!ready||!Visible)return;debounce.Stop();debounce.Start();}
    void RangeChanged(){if(!ready||changingRange)return;selectedPreset=-1;foreach(var b in presets){b.Selected=false;b.Invalidate();}ClearResult("时间已调整，点击查询更新结果。");}
    void ClearResult(string text){debounce.Stop();queryRevision++;result=null;recordSnapshot=new Record[0];var old=table.DataSource as DataTable;table.DataSource=null;old?.Dispose();chart.SetResult(null);busy=false;error=false;notice=text;AccessibleDescription=notice;query.Enabled=true;query.Text="查询";export.Enabled=false;BindTable();Invalidate();}
    void Preset(int index){var minute=DateTime.Now;minute=new DateTime(minute.Year,minute.Month,minute.Day,minute.Hour,minute.Minute,0).AddMinutes(1);DateTime a,b;
      if(index==0){a=DateTime.Today;b=a.AddDays(1);}else if(index==1){b=DateTime.Today;a=b.AddDays(-1);}else if(index==2){b=minute;a=b.AddHours(-5);}else if(index==3){b=minute;a=b.AddDays(-7);}else if(index==4){b=minute;a=b.AddDays(-30);}else{b=minute;var rows=C.Collector.UsageRecords;a=rows.Length>0?rows[0].time.ToLocalTime().Date:DateTime.Today;}
      changingRange=true;try{from.Value=a;to.Value=b;}finally{changingRange=false;}selectedPreset=index;for(int i=0;i<presets.Count;i++){presets[i].Selected=i==index;presets[i].Invalidate();}ClearResult("正在查询…");_ = RunQuery(true);}
    public void UpdateSnapshot(){RefreshWindows();ApplyColors();Invalidate();if(Visible)_ = RunQuery();}
    void RefreshWindows(){windowTokens.Clear();if(C.Imported||!C.Collector.UsageReady)return;var rows=C.Collector.UsageRecords;var now=DateTime.UtcNow;foreach(var a in C.Data.accounts)foreach(var w in a.windows)windowTokens[a.provider+":"+w.id]=UsageEngine.ForWindow(a.provider,w,rows,now);}
    WindowTokenUsage WindowUsage(Account account,WindowQuota window){if(account==null||window==null)return null;WindowTokenUsage u;return windowTokens.TryGetValue(account.provider+":"+window.id,out u)?u:window.localUsage;}
    public void ApplyColors(){var p=C.Colors;BackColor=p.Panel;from.ApplyColors();to.ApplyColors();table.BackgroundColor=p.Panel;table.DefaultCellStyle.BackColor=p.Panel;table.DefaultCellStyle.ForeColor=p.Text;table.DefaultCellStyle.SelectionBackColor=p.Soft;table.DefaultCellStyle.SelectionForeColor=p.Text;table.ColumnHeadersDefaultCellStyle.BackColor=p.Hover;table.ColumnHeadersDefaultCellStyle.ForeColor=p.Muted;table.GridColor=p.Line;foreach(Control control in Controls)control.Invalidate();}
    async Task RunQuery(bool fresh=false){
      if(!ready||IsDisposed)return;int revision=++queryRevision;DateTime start,end;
      if(!from.TryValue(out start)||!to.TryValue(out end)){Fail("请输入有效日期，格式为 YYYY-MM-DD HH:mm。");return;}if(TimeZoneInfo.Local.IsInvalidTime(start)||TimeZoneInfo.Local.IsInvalidTime(end)){Fail("这个时间落在夏令时跳过的时段，请选择有效时间。");return;}if(end<=start){Fail("截止时间必须晚于开始时间。");return;}
      if(C.Imported){Fail("离线快照不含逐条记录，刷新本机数据后可查询。");return;}if(!C.Collector.UsageReady){Fail("正在建立本机用量索引，同步完成后会自动查询。");return;}
      string source=selectedSource,model=search.Text;busy=true;error=false;query.Enabled=false;query.Text="查询中";export.Enabled=false;Invalidate();
      try{if(fresh)await C.Collector.RefreshUsageAsync();if(IsDisposed||revision!=queryRevision)return;var records=C.Collector.UsageRecords;var computed=await Task.Run(()=>UsageEngine.Query(records,start,end,source,model));if(IsDisposed||revision!=queryRevision)return;result=computed;recordSnapshot=records;querySource=source;queryModel=model;page=0;RefreshWindows();busy=false;notice=computed.records.ToString("N0")+" 条本机记录 · "+(records.Length>0?"历史始于 "+records[0].time.ToLocalTime().ToString("yyyy-MM-dd"):"暂无记录");if(C.Collector.UnreadableUsageFiles>0)notice+=" · "+C.Collector.UnreadableUsageFiles+" 份文件暂不可读";BindTable();chart.SetResult(result);AccessibleDescription="查询范围 "+start.ToString("yyyy-MM-dd HH:mm")+" 至 "+end.ToString("yyyy-MM-dd HH:mm")+"，本机 Token "+computed.totals.total.ToString("N0")+"，"+computed.records+" 条记录。";}
      catch{if(!IsDisposed&&revision==queryRevision)Fail("查询未完成，请重新选择时间后重试。");}
      finally{if(!IsDisposed&&revision==queryRevision){busy=false;query.Enabled=true;query.Text="查询";export.Enabled=result!=null&&!error;Invalidate();}}
    }
    void Fail(string message){ClearResult(message);error=true;Invalidate();}
    void BindTable(){if(table==null)return;bool window=tab==3;table.Visible=!window;previous.Visible=next.Visible=!window;previous.Enabled=page>0;next.Enabled=(page+1)*PageSize<RowCount;export.Visible=!window;if(result==null){table.DataSource=null;Invalidate();return;}var data=new DataTable();
      if(tab==0){data.Columns.Add("模型");data.Columns.Add("客户端");foreach(string s in new[]{"输入","输出","读缓存","写缓存","总 Token"})data.Columns.Add(s,typeof(long));foreach(var m in result.models.Skip(page*PageSize).Take(PageSize))data.Rows.Add(m.name,m.provider=="claude"?"Claude":"Codex",m.inputTokens,m.outputTokens,m.cachedTokens,m.cacheWriteTokens,m.totalTokens);}
      else if(tab==1){data.Columns.Add("日期");foreach(string s in new[]{"Claude","Codex","总 Token"})data.Columns.Add(s,typeof(long));foreach(var d in result.daily.Skip(page*PageSize).Take(PageSize))data.Rows.Add(d.date,d.claude,d.chatgpt,d.claude+d.chatgpt);}
      else if(tab==2){data.Columns.Add("时间");data.Columns.Add("客户端");data.Columns.Add("模型");foreach(string s in new[]{"输入","输出","读缓存","写缓存","总 Token"})data.Columns.Add(s,typeof(long));foreach(var r in UsageEngine.Page(recordSnapshot,result.fromUtc,result.toUtc,querySource,queryModel,page*PageSize,PageSize))data.Rows.Add(r.time.ToLocalTime().ToString("MM/dd HH:mm:ss"),r.provider=="claude"?"Claude":"Codex",r.model,r.input,r.output,r.cached,r.write,r.total);}
      if(window){data.Dispose();Invalidate();return;}var old=table.DataSource as DataTable;bool schema=table.Columns.Count!=data.Columns.Count;if(!schema)for(int i=0;i<data.Columns.Count;i++)if(table.Columns[i].Name!=data.Columns[i].ColumnName){schema=true;break;}if(schema){table.DataSource=null;table.Columns.Clear();}table.DataSource=data;old?.Dispose();foreach(DataGridViewColumn column in table.Columns){column.SortMode=tab==2?DataGridViewColumnSortMode.NotSortable:DataGridViewColumnSortMode.Automatic;if(column.ValueType==typeof(long)){column.DefaultCellStyle.Format="N0";column.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;}}table.Columns[0].FillWeight=tab==0?165:tab==2?123:100;if(tab==0)table.Columns[1].FillWeight=65;if(tab==2){table.Columns[1].FillWeight=63;table.Columns[2].FillWeight=130;}table.ClearSelection();Invalidate();}
    void Export(){if(result==null)return;if(tab==2)ImportExport.ExportRecords(this,UsageEngine.Page(recordSnapshot,result.fromUtc,result.toUtc,querySource,queryModel,0,result.records),result);else ImportExport.ExportUsage(this,result,tab==1);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;var saved=g.Save();g.ScaleTransform(scale,scale);var p=C.Colors;Art.Text(g,"从",18,68,10,p.Muted);Art.Text(g,"至",44+Math.Min(185,(W-365)/2),68,10,p.Muted);
      float cell=(W-36)/4;string[] labels={"总 Token","新增输入","输出","缓存"};string[] values={result==null?"—":J.Tokens(result.totals.total),result==null?"—":J.Tokens(result.totals.freshInput),result==null?"—":J.Tokens(result.totals.output),result==null?"—":J.Tokens(result.totals.cached+result.totals.write)};
      for(int i=0;i<4;i++){float x=18+i*cell;if(i>0)Art.Line(g,x-8,112,x-8,161,p.Line);Art.Text(g,labels[i],x+5,107,10.5f,p.Muted);Art.Text(g,busy?"…":values[i],x+5,130,i==0?25:22,i==0?p.Green:p.Text,true,cell-19);}
      float details=chart.Bottom/scale+13;Art.Line(g,19,details+37,W-19,details+37,p.Line);if(tab==3){float width=(W-50)/2;WindowCard(g,p,new RectangleF(18,details+46,width,Math.Max(110,H-details-78)),C.Data.accounts.FirstOrDefault(a=>a.provider=="claude"),"Claude");WindowCard(g,p,new RectangleF(32+width,details+46,width,Math.Max(110,H-details-78)),C.Data.accounts.FirstOrDefault(a=>a.provider=="chatgpt"),"ChatGPT / Codex");}
      else{Art.Text(g,RowCount==0?"暂无匹配记录":((page*PageSize+1).ToString("N0")+"–"+Math.Min(RowCount,(page+1)*PageSize).ToString("N0")+" / "+RowCount.ToString("N0")),22,H-47,10,p.Muted);Art.Text(g,(page+1)+" / "+Math.Max(1,(int)Math.Ceiling(RowCount/(double)PageSize)),W-255,H-47,10,p.Subtle);}
      if(error||result==null||busy)Art.Text(g,notice,20,H-21,9.5f,error?p.Warning:p.Subtle,false,W-40);g.Restore(saved);}
    double CacheRate(){if(result==null)return 0;double denominator=(double)result.totals.freshInput+result.totals.cached+result.totals.write;return denominator==0?0:result.totals.cached/denominator*100;}
    void WindowCard(Graphics g,Palette p,RectangleF r,Account a,string title){Art.Box(g,r,p.Hover,p.Line,12);ProviderIcons.Draw(g,a?.provider=="claude",new RectangleF(r.X+14,r.Y+12,17,17),p.Muted);Art.Text(g,title,r.X+40,r.Y+12,12,p.Text,true);WindowRow(g,p,a?.Five,WindowUsage(a,a?.Five),r.X+15,r.Y+46,r.Width-30,"5 小时");WindowRow(g,p,a?.Week,WindowUsage(a,a?.Week),r.X+15,r.Y+99,r.Width-30,"每周");}
    void WindowRow(Graphics g,Palette p,WindowQuota q,WindowTokenUsage u,float x,float y,float width,string label){Art.Text(g,(q?.label??label).Replace("额度",""),x,y,10.5f,p.Muted);string text=u?.usedTokens!=null&&q?.Expired!=true?J.Tokens(u.usedTokens.Value):"—";Art.Text(g,"已用 "+text,x,y+19,12,p.Text,true,width*.43f);Art.Text(g,"预估剩余 "+(q?.EstimateText??"—"),x+width*.40f,y+19,12,p.Text,true,width*.60f,StringAlignment.Far);}
    void ShowWindowTip(object sender,MouseEventArgs e){string text=null;float y=e.Y/scale;if(y>=103&&y<=168){int index=(int)((e.X/scale-18)/((W-36)/4));if(index==0)text="Claude：输入 + 输出 + 读缓存 + 写缓存。\nCodex：输入已含缓存，不重复累加。\n仅统计本机日志。";else if(index==1)text="不含读取缓存的新增输入 Token。";else if(index==2)text="输出 Token。";else if(index==3)text=result==null?"读取缓存与写入缓存合计。":"读取缓存："+result.totals.cached.ToString("N0")+"\n写入缓存："+result.totals.write.ToString("N0")+"\n缓存命中率："+CacheRate().ToString("0.#")+"%";}
      else if(tab==3&&y>chart.Bottom/scale+58){var a=C.Data.accounts.FirstOrDefault(x=>x.provider==(e.X/scale<W/2?"claude":"chatgpt"));if(a!=null)text=string.Join("\n",new[]{a.Five,a.Week}.Where(w=>w!=null).Select(w=>{var u=WindowUsage(a,w);return w.label+"："+(u?.usedTokens!=null&&!w.Expired?u.usedTokens.Value.ToString("N0")+" Token\n"+J.Time(u.startUtc)?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")+" 至 "+J.Time(u.endUtc)?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"窗口时间未知，未推算 Token 数量。");}))+"\n仅统计本机日志，不含网页与其他设备。";}
      if(text!=lastTip){lastTip=text;tips.SetToolTip(this,text);}}
    protected override void Dispose(bool disposing){if(disposing){queryRevision++;debounce.Dispose();tips.Dispose();var old=table.DataSource as DataTable;table.DataSource=null;old?.Dispose();recordSnapshot=new Record[0];}base.Dispose(disposing);}
  }
}


