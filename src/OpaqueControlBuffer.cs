using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TokenGrove {
  // Native Edit/ComboBox painting clears alpha on text pixels. Repaint their
  // own client rendering through an opaque bitmap so DWM keeps the text intact.
  sealed class OpaqueControlBuffer:IDisposable {
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    Bitmap image;bool painting;
    public void Paint(Control control){
      if(painting||!control.IsHandleCreated||control.ClientSize.Width<1||control.ClientSize.Height<1||(control.FindForm() as PaintedWindow)?.GlassActive!=true)return;
      painting=true;try{
        if(image==null||image.Size!=control.ClientSize){image?.Dispose();image=new Bitmap(control.ClientSize.Width,control.ClientSize.Height,PixelFormat.Format24bppRgb);}
        using(var graphics=Graphics.FromImage(image)){graphics.Clear(control.BackColor);var dc=graphics.GetHdc();try{SendMessage(control.Handle,0x318,dc,new IntPtr(4));}finally{graphics.ReleaseHdc(dc);}}
        using(var graphics=Graphics.FromHwnd(control.Handle))graphics.DrawImageUnscaled(image,0,0);
      }finally{painting=false;}
    }
    public void Dispose(){image?.Dispose();image=null;}
  }
}
