using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TokenGrove {
  public sealed partial class GroveContext {
    public AppUpdates Updates;
    void InitializeUpdates(){Updates=new AppUpdates(Settings.Root);Updates.Changed+=()=>{if(Quitting)return;Main?.RefreshUpdates();if(Floating!=null&&!Floating.IsDisposed)Floating.Invalidate();BuildMenu();};}
    public async Task CheckUpdates(bool force){if(Quitting||!servicesEnabled||!force&&!Prefs.autoUpdateCheck)return;await Updates.CheckAsync(force);}
    public void ShowUpdates(){ShowMain("settings");Main.FocusUpdates();}
    public async Task DownloadUpdate(){if(Quitting||!servicesEnabled)return;string path=Updates.DownloadedFile;if(path==null||!File.Exists(path))path=await Updates.DownloadAsync();if(path==null||Quitting)return;try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true});}catch{System.Windows.Forms.MessageBox.Show(Main,"下载已保存到：\n"+path,"软件更新");}}
  }
}
