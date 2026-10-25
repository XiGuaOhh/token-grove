using System;
using System.Drawing;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class GroveEdit:TextBox {
    IntPtr nativeFont;bool applying;readonly OpaqueControlBuffer opaque=new OpaqueControlBuffer();
    void ApplyFont(){if(applying||!IsHandleCreated||IsDisposed||Disposing)return;var next=Typography.NativeFont(Font);if(next==IntPtr.Zero)return;applying=true;try{Typography.SendMessage(Handle,0x30,next,new IntPtr(1));var old=nativeFont;nativeFont=next;if(old!=IntPtr.Zero)Typography.DeleteObject(old);}finally{applying=false;}}
    protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x30&&!applying)ApplyFont();switch(m.Msg){case 0x200:if((m.WParam.ToInt64()&1)!=0)opaque.Paint(this);break;case 0xF:case 0x7:case 0x8:case 0xC:case 0x100:case 0x101:case 0x102:case 0x10E:case 0x10F:case 0x114:case 0x115:case 0x20A:case 0xB1:case 0xB6:case 0xB7:case 0xC2:case 0x201:case 0x202:case 0x300:case 0x302:case 0x303:case 0x304:opaque.Paint(this);break;}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyFont();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);ApplyFont();}
    protected override void OnHandleDestroyed(EventArgs e){base.OnHandleDestroyed(e);if(nativeFont!=IntPtr.Zero){Typography.DeleteObject(nativeFont);nativeFont=IntPtr.Zero;}}
    protected override void Dispose(bool disposing){if(disposing)opaque.Dispose();base.Dispose(disposing);}
  }
  public sealed class GroveLabel:Label {
    public GroveLabel(){UseCompatibleTextRendering=true;DoubleBuffered=true;}
    protected override void OnPaintBackground(PaintEventArgs e){GlassSurface.Paint(this,e.Graphics,e.ClipRectangle,BackColor);}
    protected override void OnPaint(PaintEventArgs e){Typography.Prepare(e.Graphics);base.OnPaint(e);}
  }
  public sealed class GroveCalendar:MonthCalendar {
    IntPtr nativeFont;bool applying;
    void ApplyFont(){if(applying||!IsHandleCreated||IsDisposed||Disposing)return;var next=Typography.NativeFont(Font);if(next==IntPtr.Zero)return;applying=true;try{Typography.SendMessage(Handle,0x30,next,new IntPtr(1));var old=nativeFont;nativeFont=next;if(old!=IntPtr.Zero)Typography.DeleteObject(old);}finally{applying=false;}}
    protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x30&&!applying)ApplyFont();}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyFont();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);ApplyFont();}
    protected override void OnHandleDestroyed(EventArgs e){base.OnHandleDestroyed(e);if(nativeFont!=IntPtr.Zero){Typography.DeleteObject(nativeFont);nativeFont=IntPtr.Zero;}}
  }
  public sealed class GroveTable:DataGridView {
    public GroveTable(){DoubleBuffered=true;}
    protected override void OnPaint(PaintEventArgs e){
      base.OnPaint(e);var window=FindForm() as PaintedWindow;if(window==null||!window.GlassActive)return;
      int bottom=ColumnHeadersVisible&&Columns.Count>0?ColumnHeadersHeight:0;
      foreach(DataGridViewRow row in Rows)if(row.Displayed)bottom=Math.Max(bottom,GetRowDisplayRectangle(row.Index,true).Bottom);
      var blank=Rectangle.Intersect(e.ClipRectangle,new Rectangle(0,bottom,ClientSize.Width,Math.Max(0,ClientSize.Height-bottom)));
      if(!blank.IsEmpty)GlassSurface.Paint(this,e.Graphics,blank,BackgroundColor);
    }
    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e){
      base.OnCellPainting(e);if(e.Handled||e.ColumnIndex<0||e.RowIndex< -1)return;
      var window=FindForm() as PaintedWindow;
      if(window!=null&&window.GlassActive){bool picked=(e.State&DataGridViewElementStates.Selected)!=0;GlassSurface.Paint(this,e.Graphics,Rectangle.Intersect(e.ClipBounds,e.CellBounds),picked?e.CellStyle.SelectionBackColor:e.CellStyle.BackColor);e.Paint(e.ClipBounds,DataGridViewPaintParts.Border|DataGridViewPaintParts.Focus|DataGridViewPaintParts.ErrorIcon);}
      else e.Paint(e.ClipBounds,DataGridViewPaintParts.All&~DataGridViewPaintParts.ContentForeground);
      var saved=e.Graphics.Save();e.Graphics.SetClip(e.ClipBounds,System.Drawing.Drawing2D.CombineMode.Intersect);
      bool selected=(e.State&DataGridViewElementStates.Selected)!=0;Color ink=selected?e.CellStyle.SelectionForeColor:e.CellStyle.ForeColor;var padding=e.CellStyle.Padding;
      var bounds=new RectangleF(e.CellBounds.X+padding.Left+3,e.CellBounds.Y+padding.Top,e.CellBounds.Width-padding.Horizontal-6,e.CellBounds.Height-padding.Vertical);
      var align=e.CellStyle.Alignment==DataGridViewContentAlignment.MiddleRight||e.CellStyle.Alignment==DataGridViewContentAlignment.TopRight||e.CellStyle.Alignment==DataGridViewContentAlignment.BottomRight?StringAlignment.Far:e.CellStyle.Alignment==DataGridViewContentAlignment.MiddleCenter?StringAlignment.Center:StringAlignment.Near;
      SortOrder sort=e.RowIndex==-1?Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection:SortOrder.None;if(sort!=SortOrder.None)bounds.Width-=13;
      Typography.Draw(e.Graphics,Convert.ToString(e.FormattedValue),e.CellStyle.Font??Font,bounds,ink,align);
      if(sort!=SortOrder.None){float x=e.CellBounds.Right-10,y=e.CellBounds.Top+e.CellBounds.Height/2;float direction=sort==SortOrder.Ascending?1:-1;using(var b=new SolidBrush(ink))e.Graphics.FillPolygon(b,new[]{new PointF(x-3,y+direction*2),new PointF(x+3,y+direction*2),new PointF(x,y-direction*2)});}
      e.Graphics.Restore(saved);e.Handled=true;
    }
  }
  public sealed class GroveMenuRenderer:ToolStripProfessionalRenderer {
    public GroveMenuRenderer(Palette colors):base(new GroveMenuColors(colors)){}
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){Typography.Draw(e.Graphics,e.Text,e.TextFont,e.TextRectangle,e.TextColor);}
  }
}
