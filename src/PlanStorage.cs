using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TokenGrove {
  public static class PlanStorage {
    public static string SharedRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".token-grove"); } }
    // AppData can resolve to a launcher's private MSIX view. UserProfile is shared.
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint size,uint flags);
    static string Identity(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){var path=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(stream.SafeFileHandle,path,(uint)path.Capacity,0);if(length==0||length>=path.Capacity)throw new IOException("无法确认原计划文件的位置。");return path.ToString();}}
    public static IEnumerable<string> LegacyFiles(){
      var files=new List<string>{Path.Combine(Settings.Root,"plans.json")};
      string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
      try{if(Directory.Exists(packages))foreach(var package in Directory.GetDirectories(packages,"OpenAI.Codex_*"))files.Add(Path.Combine(package,"LocalCache","Local","TokenGrove","plans.json"));}catch(IOException){}catch(UnauthorizedAccessException){}
      return files;
    }
    public static WorkPlanStore OpenDefault(){return Open(SharedRoot,LegacyFiles());}
    static DateTime Modified(WorkPlan p){return new[]{J.Time(p.updatedUtc),J.Time(p.completedUtc)}.Where(t=>t.HasValue).Select(t=>t.Value).DefaultIfEmpty(DateTime.MinValue).Max();}
    static bool SameContent(WorkPlan a,WorkPlan b){return a.title==b.title&&(a.body??"")==(b.body??"")&&a.provider==b.provider&&a.reminder==b.reminder&&(a.reminder!="time"||a.dueUtc==b.dueUtc);}
    public static WorkPlanStore Open(string sharedRoot,IEnumerable<string> legacyFiles){
      var store=WorkPlanStore.Open(Path.Combine(sharedRoot,"plans.json"));if(!store.CanWrite)return store;
      bool changed=false;
      foreach(string file in legacyFiles){
        if(!File.Exists(file)||string.Equals(Path.GetFullPath(file),store.FilePath,StringComparison.OrdinalIgnoreCase))continue;
        string identity;try{identity=Identity(file);}catch(IOException){store.MigrationWarning="有一份旧计划暂时无法读取，原文件已保留。";continue;}catch(UnauthorizedAccessException){store.MigrationWarning="有一份旧计划暂时无法读取，原文件已保留。";continue;}
        if(store.ImportedSources.Contains(identity,StringComparer.OrdinalIgnoreCase))continue;
        var legacy=WorkPlanStore.Open(file);if(!legacy.CanWrite){store.MigrationWarning="有一份旧计划格式异常，原文件已保留。";continue;}
        // Preserve source bytes before adding any migrated items to the shared document.
        string backups=Path.Combine(sharedRoot,"migration-backups");Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(backups,"plans-"+Guid.NewGuid().ToString("N")+".json"),J.Read(file),new UTF8Encoding(false));
        foreach(var plan in legacy.Items){
          var existing=store.Items.FirstOrDefault(p=>p.id==plan.id);
          if(existing==null)store.Items.Add(plan);
          else if(!SameContent(existing,plan)){
            var copy=J.Serializer().Deserialize<WorkPlan>(J.Serializer().Serialize(plan));copy.id=Guid.NewGuid().ToString("N");copy.title=(copy.title??"");if(copy.title.Length>154)copy.title=copy.title.Substring(0,154);copy.title+="（另一份）";store.Items.Add(copy);
          }else if(Modified(plan)>Modified(existing)){int index=store.Items.IndexOf(existing);store.Items[index]=plan;}
        }
        if(store.Items.Count>500)throw new IOException("旧计划合并后超过 500 条，所有原文件已保留，请先整理旧计划。");
        if(store.ImportedSources.Count>=256)throw new IOException("旧计划来源过多，原文件已保留。");
        store.ImportedSources.Add(identity);changed=true;
      }
      if(changed)store.Save();return store;
    }
  }
}
