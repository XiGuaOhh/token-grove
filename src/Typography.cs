using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace TokenGrove {
  public static class Typography {
    static readonly PrivateFontCollection collection=new PrivateFontCollection();
    static readonly FontFamily family=LoadFamily();
    public static bool BundledFontLoaded{get;private set;}
    public static string FamilyName{get{return family.Name;}}
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern int AddFontResourceEx(string file,uint flags,IntPtr reserved);
    static FontFamily LoadFamily(){
      try{string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fonts","GroveRoundF-Regular.ttf");if(File.Exists(file)){collection.AddFontFile(file);int registered=AddFontResourceEx(file,0x10,IntPtr.Zero);var families=collection.Families;if(families.Length>0&&registered>0){BundledFontLoaded=true;return families[0];}}}catch{}
      try{return new FontFamily("幼圆");}catch{return FontFamily.GenericSansSerif;}
    }
    public static Font Font(float size,bool emphasis=false){
      // The supplied regular outlines stay soft; do not synthesize a heavy bold face.
      float pixels=size>=18?size:Math.Max(10.5f,size+.5f);return new Font(family,pixels,FontStyle.Regular,GraphicsUnit.Pixel);
    }
    public static void Prepare(Graphics graphics){graphics.TextRenderingHint=TextRenderingHint.AntiAlias;graphics.TextContrast=4;}
    public static void Draw(Graphics graphics,string text,Font font,RectangleF bounds,Color color,StringAlignment align=StringAlignment.Near){Prepare(graphics);using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})graphics.DrawString(text??"",font,brush,bounds,format);}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]sealed class LogicalFont {
      public int height,width,escapement,orientation,weight;
      public byte italic,underline,strikeout,charSet,outPrecision,clipPrecision,quality,pitchAndFamily;
      [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string faceName;
    }
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateFontIndirect([In]LogicalFont font);
    [DllImport("gdi32.dll")]internal static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")]internal static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    internal static IntPtr NativeFont(Font font){var logical=new LogicalFont();font.ToLogFont(logical);logical.quality=4;return CreateFontIndirect(logical);}
  }
}
