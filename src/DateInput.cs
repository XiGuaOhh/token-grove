using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class GroveDateInput:UserControl {
    public Func<Palette> Colors;
    readonly TextBox edit=new GroveEdit{BorderStyle=BorderStyle.None};
    readonly GroveButton calendar;
    ToolStripDropDown popup;
    public GroveDateInput(string name){
      DoubleBuffered=true;TabStop=false;AccessibleName=name;edit.AccessibleName=name;Controls.Add(edit);
      calendar=new GroveButton("",OpenCalendar){Colors=()=>Colors?.Invoke()??new Palette(false),Glyph="calendar",Quiet=true,AccessibleName="选择"+name+"日期"};Controls.Add(calendar);
      edit.KeyDown+=(s,e)=>{if(e.Alt&&e.KeyCode==Keys.Down){OpenCalendar();e.SuppressKeyPress=true;}};
      edit.TextChanged+=(s,e)=>OnTextChanged(EventArgs.Empty);
    }
    public bool TryValue(out DateTime value){return DateTime.TryParseExact(edit.Text,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out value);}
    public DateTime Value{set{edit.Text=value.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);}}
    public override string Text{get{return edit==null?base.Text:edit.Text;}set{if(edit==null)base.Text=value;else edit.Text=value;}}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(edit!=null){edit.Font=Font;Arrange();}}
    protected override void OnResize(EventArgs e){base.OnResize(e);if(edit!=null&&calendar!=null)Arrange();}
    void Arrange(){using(var g=CreateGraphics()){float s=g.DpiX/96f;edit.SetBounds((int)(11*s),Math.Max(1,(Height-edit.PreferredHeight)/2),Math.Max(1,Width-(int)(42*s)),edit.PreferredHeight);if(calendar!=null)calendar.SetBounds(Width-(int)(31*s),(int)(2*s),(int)(29*s),Height-(int)(4*s));}}
    public void ApplyColors(){var p=Colors?.Invoke()??new Palette(false);BackColor=p.Hover;edit.BackColor=p.Hover;edit.ForeColor=p.Text;calendar?.Invalidate();Invalidate();}
    void OpenCalendar(){
      if(popup!=null){popup.Close();popup.Dispose();popup=null;}
      var p=Colors?.Invoke()??new Palette(false);var month=new GroveCalendar{MaxSelectionCount=1,ShowWeekNumbers=false,BackColor=p.Panel,ForeColor=p.Text,Font=Font};DateTime selected;if(!TryValue(out selected))selected=DateTime.Today;
      if(selected.Date>=month.MinDate&&selected.Date<=month.MaxDate)month.SetDate(selected.Date);
      popup=new ToolStripDropDown{Padding=new Padding(4),BackColor=p.Panel};var host=new ToolStripControlHost(month){Margin=Padding.Empty,Padding=Padding.Empty};popup.Items.Add(host);
      month.DateSelected+=(s,e)=>{DateTime old;if(!TryValue(out old))old=DateTime.Today;Value=e.Start.Date.AddHours(old.Hour).AddMinutes(old.Minute);popup.Close();edit.Focus();};popup.Show(this,new Point(0,Height+3));
    }
    protected override void OnPaintBackground(PaintEventArgs e){using(var b=new SolidBrush(Parent?.BackColor??BackColor))e.Graphics.FillRectangle(b,e.ClipRectangle);}
    protected override void OnPaint(PaintEventArgs e){var p=Colors?.Invoke()??new Palette(false);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;Art.Box(e.Graphics,new RectangleF(.5f,.5f,Width-1,Height-1),p.Hover,p.Line,8);base.OnPaint(e);}
    protected override void Dispose(bool disposing){if(disposing){popup?.Dispose();}base.Dispose(disposing);}
  }
}
