using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenGrove {
  public static class GlassSurface {
    // Replace background pixels, including their alpha, rather than stacking tints
    // every time a nested WinForms control repaints.
    public static void Fill(Graphics graphics,Rectangle clip,Color color){
      var saved=graphics.Save();try{graphics.CompositingMode=CompositingMode.SourceCopy;using(var brush=new SolidBrush(color))graphics.FillRectangle(brush,clip);}finally{graphics.Restore(saved);}
    }
    public static void Paint(Control control,Graphics graphics,Rectangle clip,Color fallback){
      var window=control.FindForm() as PaintedWindow;
      if(window==null||!window.GlassActive){Fill(graphics,clip,fallback);return;}
      bool surface=control is GrovePanel;
      for(var parent=control.Parent;parent!=null&&!(parent is PaintedWindow);parent=parent.Parent)if(parent is GrovePanel){surface=true;break;}
      Fill(graphics,clip,surface?Color.FromArgb(window.SurfaceTint.A,fallback):Color.Transparent);
    }
  }
}
