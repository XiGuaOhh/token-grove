using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class UpdateView:GrovePanel {
    readonly GroveContext C;readonly float scale;readonly GroveButton check,download,notes;
    public UpdateView(GroveContext context,float dpi){C=context;scale=dpi;Colors=()=>C.Colors;AccessibleName="软件更新";
      check=new GroveButton("检查更新",async()=>await C.CheckUpdates(true)){Colors=()=>C.Colors,Glyph="refresh",AccessibleName="检查软件更新"};
      download=new GroveButton("下载新版",async()=>await C.DownloadUpdate()){Colors=()=>C.Colors,Primary=true,Glyph="download",AccessibleName="下载新版压缩包"};
      notes=new GroveButton("更新说明",()=>MessageBox.Show(this,C.Updates.Latest?.notes??"暂无更新说明","Token Grove · 更新说明",MessageBoxButtons.OK,MessageBoxIcon.Information)){Colors=()=>C.Colors};
      Controls.AddRange(new Control[]{check,download,notes});Resize+=(s,e)=>LayoutButtons();RefreshData();LayoutButtons();
    }
    void Place(Control c,float x,float y,float w,float h){c.SetBounds((int)(x*scale),(int)(y*scale),(int)(w*scale),(int)(h*scale));}
    void LayoutButtons(){Place(check,18,106,112,33);Place(download,140,106,124,33);Place(notes,274,106,104,33);}
    public void FocusCheck(){check.Focus();}
    public void RefreshData(){var u=C.Updates;check.Enabled=!u.Checking&&!u.Downloading;check.Text=u.Checking?"检查中…":"检查更新";download.Visible=u.Available;download.Enabled=!u.Checking&&!u.Downloading&&u.Latest?.Downloadable==true;download.Text=u.Downloading?"下载 "+Math.Max(0,u.Progress)+"%":u.DownloadedFile!=null&&File.Exists(u.DownloadedFile)?"打开下载文件夹":"下载新版";notes.Visible=u.Latest!=null;AccessibleDescription=u.Status;Invalidate(true);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;var saved=g.Save();g.ScaleTransform(scale,scale);var p=C.Colors;float width=Width/scale;
      Art.Text(g,"软件更新",18,12,13,p.Text,true);Art.Text(g,"当前 v"+AppUpdates.CurrentText,width-235,14,10.5f,p.Muted,false,215,StringAlignment.Far);
      var u=C.Updates;Art.Text(g,u.Status,18,42,12,u.Error!=null?p.Warning:u.Available?p.Green:p.Text,false,width-36);
      var at=J.Time(u.LastCheckedUtc);Art.Text(g,at.HasValue?"上次检查 "+at.Value.ToLocalTime().ToString("MM/dd HH:mm")+" · 下载后完整解压运行":"仅检查正式版 · 下载后完整解压运行",18,72,10,p.Muted,false,width-36);g.Restore(saved);
    }
  }
}
