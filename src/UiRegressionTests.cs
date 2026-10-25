using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TokenGrove {
  static class UiRegressionTests {
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static string View(MainWindow form){return (string)typeof(MainWindow).GetField("view",Hidden).GetValue(form);}
    static void DoubleClick(FloatingWindow form){typeof(Control).GetMethod("OnMouseDoubleClick",Hidden).Invoke(form,new object[]{new MouseEventArgs(MouseButtons.Left,2,10,10,0)});}
    public static void Run(string directory,Action<bool,string> check){
      Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
      string original=Settings.Root;Settings.Root=Path.Combine(directory,"ui-settings");
      try{
        using(var c=new GroveContext(new string[0],false)){
          try{
          c.Prefs=new Settings{themeMode="light",glass=true,motion=false,mainOpacity=100,widgetOpacity=100,surfaceOpacity=65};c.Imported=true;c.SavePrefs();
          c.Main.StartPosition=FormStartPosition.Manual;c.Main.Location=new Point(-20000,-20000);c.Floating.Location=new Point(-20000,-20000);
          bool firstLayout=true;c.Main.ControlAdded+=(s,e)=>{if(e.Control is UsageView)firstLayout=e.Control.Left>=80&&e.Control.Top>=70&&e.Control.Width>800;};
          foreach(string page in new[]{"overview","accounts","settings","usage"}){
            c.Main.SetView(page);c.Main.Show();Application.DoEvents();c.ShowWidget();DoubleClick(c.Floating);Application.DoEvents();
            check(View(c.Main)==page&&c.Main.Visible&&!c.Floating.Visible,"Floating double-click restores the previous "+page+" page");
          }
          check(firstLayout,"The first usage page is sized before it can be presented");
          var usage=c.Main.Controls.OfType<UsageView>().Single();var search=usage.Controls.OfType<GroveSearch>().Single();search.Text="fixture-model";
          var date=usage.Controls.OfType<GroveDateInput>().First();date.Text="2026-01-02 03:04";
          c.ShowWidget();DoubleClick(c.Floating);
          check(ReferenceEquals(usage,c.Main.Controls.OfType<UsageView>().Single())&&search.Text=="fixture-model"&&date.Text=="2026-01-02 03:04","Restoring from the widget retains the existing query, model filter and controls");
          c.Main.SetView("settings");var settings=c.Main.Controls.OfType<GroveScrollPanel>().Single();
          typeof(Control).GetMethod("OnKeyDown",Hidden).Invoke(settings,new object[]{new KeyEventArgs(Keys.End)});
          int offset=(int)typeof(GroveScrollPanel).GetField("offset",Hidden).GetValue(settings);c.ShowWidget();DoubleClick(c.Floating);
          check(offset>0&&(int)typeof(GroveScrollPanel).GetField("offset",Hidden).GetValue(settings)==offset,"Widget restoration preserves the settings scroll position");
          c.SetSurfaceOpacity(52);var slider=settings.ContentControls.OfType<GroveSlider>().Single(x=>x.AccessibleName=="内容底板不透明度");
          check(slider.Value==52&&c.Main.Opacity==1&&c.Prefs.mainOpacity==100&&c.Prefs.widgetOpacity==100,"Content tint updates its slider without changing either window opacity");
          check(WindowAppearance.CaptionColor(c.Colors,true)==-1&&WindowAppearance.CaptionColor(c.Colors,false)!=-1,"Native caption material is restored when glass is enabled");
          if(c.Main.GlassActive){
            using(var bitmap=new Bitmap(20,20,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(bitmap)){
              for(int i=0;i<20;i++)GlassSurface.Paint(settings,g,new Rectangle(0,0,20,20),c.Colors.Panel);
              check(Math.Abs(bitmap.GetPixel(3,3).A-133)<=1&&c.Colors.Text.A==255,"Nested repeated paints preserve the requested tint alpha and solid text");
              bool stable=true;foreach(string page in new[]{"overview","usage","accounts","settings","usage"}){
                c.Main.SetView(page);g.Clear(Color.Magenta);var saved=g.Save();g.TranslateTransform(-120,-400);
                typeof(PaintedWindow).GetMethod("OnPaintBackground",Hidden).Invoke(c.Main,new object[]{new PaintEventArgs(g,new Rectangle(120,400,20,20))});g.Restore(saved);
                stable&=bitmap.GetPixel(4,4).A==0;
              }
              check(stable,"Switching pages retains the same full-window glass underlay");
            }
          }
          c.SetTheme("dark");check(c.IsDark&&c.Colors.Panel.R<60&&c.Prefs.surfaceOpacity==52,"Dark mode retains content opacity and uses the dark palette");
          c.Main.SetView("accounts");c.Prefs.Save();var savedPrefs=Settings.Load();check(savedPrefs.mainView=="accounts"&&savedPrefs.surfaceOpacity==52,"The last page and content opacity survive a settings reload");
          }finally{c.Quit();}
        }
        var legacy=J.Serializer().Deserialize<Settings>("{\"mainOpacity\":95,\"themeMode\":\"light\"}");legacy.Normalize();
        check(legacy.mainOpacity==95&&legacy.surfaceOpacity==78&&legacy.mainView=="overview","Existing settings gain a glass tint default while preserving window opacity");
        var invalid=new Settings{surfaceOpacity=0,mainView="missing"};invalid.Normalize();check(invalid.surfaceOpacity==35&&invalid.mainView=="overview","Invalid appearance settings are clamped to usable values");
      }finally{Settings.Root=original;}
    }
  }
}
