using System;
using System.Drawing;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed class GroveEdit:TextBox {
    IntPtr nativeFont;bool applying;
    void ApplyFont(){if(applying||!IsHandleCreated||IsDisposed||Disposing)return;var next=Typography.NativeFont(Font);if(next==IntPtr.Zero)return;applying=true;try{Typography.SendMessage(Handle,0x30,next,new IntPtr(1));var old=nativeFont;nativeFont=next;if(old!=IntPtr.Zero)Typography.DeleteObject(old);}finally{applying=false;}}
    protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x30&&!applying)ApplyFont();}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyFont();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);ApplyFont();}
    protected override void OnHandleDestroyed(EventArgs e){base.OnHandleDestroyed(e);if(nativeFont!=IntPtr.Zero){Typography.DeleteObject(nativeFont);nativeFont=IntPtr.Zero;}}
  }
  public sealed class GroveLabel:Label {
    public GroveLabel(){UseCompatibleTextRendering=true;DoubleBuffered=true;}
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
    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e){
      base.OnCellPainting(e);if(e.Handled||e.ColumnIndex<0||e.RowIndex< -1)return;
      e.Paint(e.ClipBounds,DataGridViewPaintParts.All&~DataGridViewPaintParts.ContentForeground);
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
