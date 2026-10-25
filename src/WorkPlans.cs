using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TokenGrove {
  public sealed class WorkPlan {
    public string id,title,body,provider,reminder="quota",createdUtc,updatedUtc,armedUtc,dueUtc,readyUtc,readyReason,notifiedUtc,completedUtc;
    public string[] waitingWindows=new string[0];
    [ScriptIgnore]public bool Completed{get{return completedUtc!=null;}}
    [ScriptIgnore]public bool Ready{get{return !Completed&&readyUtc!=null;}}
    [ScriptIgnore]public string Client{get{return provider=="claude"?"Claude":"Codex";}}
    public string Prompt(){return string.IsNullOrWhiteSpace(body)?title:(title+Environment.NewLine+Environment.NewLine+body);}
  }
  public sealed class PlanDocument {public int version=1;public List<WorkPlan> plans=new List<WorkPlan>();}
  public sealed class WorkPlanStore {
    public readonly string FilePath;public readonly List<WorkPlan> Items=new List<WorkPlan>();
    public string LoadError{get;private set;}
    public bool CanWrite{get{return LoadError==null;}}
    public int ReadyCount{get{return Items.Count(p=>p.Ready);}}
    public WorkPlanStore(string path){FilePath=Path.GetFullPath(path);}
    public static WorkPlanStore Open(string path){
      var store=new WorkPlanStore(path);if(!File.Exists(path))return store;
      try{
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException();
        var document=J.Serializer().Deserialize<PlanDocument>(J.Read(path));
        if(document==null||document.version!=1)throw new InvalidDataException();Validate(document.plans);store.Items.AddRange(document.plans);
      }catch{store.LoadError="计划文件读取失败，原文件已保留。请检查数据目录中的 plans.json 或备份。";}
      return store;
    }
    static void Validate(List<WorkPlan> items){
      if(items==null||items.Count>500||items.Any(p=>p==null)||items.Select(p=>p.id).Distinct().Count()!=items.Count)throw new InvalidDataException("计划格式无效");
      foreach(var p in items){
        Guid id;if(!Guid.TryParse(p.id,out id)||(p.provider!="claude"&&p.provider!="chatgpt")||!new[]{"quota","time","none"}.Contains(p.reminder)||(p.title??"").Length>160||(p.body??"").Length>32000||!J.Time(p.createdUtc).HasValue||!J.Time(p.updatedUtc).HasValue)throw new InvalidDataException("计划内容无效");
        foreach(string date in new[]{p.armedUtc,p.dueUtc,p.readyUtc,p.notifiedUtc,p.completedUtc})if(date!=null&&!J.Time(date).HasValue)throw new InvalidDataException("计划时间无效");
        if(p.reminder=="time"&&p.dueUtc==null)throw new InvalidDataException("请填写提醒时间");
        if(p.waitingWindows==null)p.waitingWindows=new string[0];if(p.waitingWindows.Length>20||p.waitingWindows.Any(w=>string.IsNullOrEmpty(w)||w.Length>80))throw new InvalidDataException("额度窗口无效");
      }
    }
    public void Save(){
      if(!CanWrite)throw new IOException(LoadError);Validate(Items);
      var directory=Path.GetDirectoryName(FilePath);Directory.CreateDirectory(directory);
      string temp=Path.Combine(directory,Path.GetFileName(FilePath)+"."+Guid.NewGuid().ToString("N")+".tmp");
      try{
        File.WriteAllText(temp,J.Serializer().Serialize(new PlanDocument{plans=Items}),new UTF8Encoding(false));
        if(File.Exists(FilePath))File.Replace(temp,FilePath,FilePath+".bak",true);else File.Move(temp,FilePath);
      }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public WorkPlan Create(string provider,Snapshot data,DateTime now){
      if(!CanWrite)throw new IOException(LoadError);if(Items.Count>=500)throw new InvalidOperationException("请先清理一些已完成的计划。");
      var plan=new WorkPlan{id=Guid.NewGuid().ToString("N"),title="未命名计划",body="",provider=provider=="chatgpt"?"chatgpt":"claude",createdUtc=J.Iso(now),updatedUtc=J.Iso(now)};
      PlanScheduler.Arm(plan,data,now);Items.Insert(0,plan);return plan;
    }
  }
  public static class PlanScheduler {
    static Account AccountFor(WorkPlan plan,Snapshot data){return data?.accounts.FirstOrDefault(a=>a.provider==plan.provider);}
    public static void Arm(WorkPlan plan,Snapshot data,DateTime now,DateTime? custom=null){
      if(plan.reminder=="time"&&!custom.HasValue)throw new ArgumentException("请填写提醒时间");
      plan.armedUtc=J.Iso(now);plan.readyUtc=plan.readyReason=plan.notifiedUtc=plan.dueUtc=null;plan.waitingWindows=new string[0];
      if(plan.reminder=="time"){if(!custom.HasValue)throw new ArgumentException("请填写提醒时间");plan.dueUtc=J.Iso(custom.Value);}
      else if(plan.reminder=="quota")Resolve(plan,data,now);
    }
    static bool Resolve(WorkPlan plan,Snapshot data,DateTime now){
      var account=AccountFor(plan,data);if(account==null)return false;
      string previous=plan.dueUtc+"|"+string.Join(",",plan.waitingWindows);
      var blocked=account.windows.Where(w=>w.usedPercent>=100&&!string.IsNullOrEmpty(w.id)).ToArray();
      if(blocked.Length>0){
        plan.waitingWindows=blocked.Select(w=>w.id).ToArray();
        if(blocked.All(w=>J.Time(w.resetsAt).HasValue))plan.dueUtc=J.Iso(blocked.Max(w=>J.Time(w.resetsAt).Value));
      }else{
        var next=new[]{account.Five,account.Week}.FirstOrDefault(w=>J.Time(w?.resetsAt)>now);
        if(next!=null)plan.dueUtc=next.resetsAt;
      }
      return previous!=plan.dueUtc+"|"+string.Join(",",plan.waitingWindows);
    }
    static bool Recovered(WorkPlan plan,Snapshot data,DateTime now){
      if(plan.waitingWindows.Length==0)return false;var account=AccountFor(plan,data);if(account==null)return false;
      if(account.windows.Any(w=>w.usedPercent>=100&&!(J.Time(w.resetsAt)<=now)))return false;
      var armed=J.Time(plan.armedUtc??plan.createdUtc);
      return plan.waitingWindows.All(id=>{
        var w=account.windows.FirstOrDefault(q=>q.id==id);var observed=J.Time(w?.observedAt);
        return w?.usedPercent!=null&&w.usedPercent<100&&observed.HasValue&&observed>=armed&&observed<=now.AddMinutes(2)&&now-observed.Value<=TimeSpan.FromMinutes(30);
      });
    }
    public static bool Advance(IEnumerable<WorkPlan> plans,Snapshot data,DateTime now){
      bool changed=false;foreach(var p in plans){
        if(p.Completed||p.Ready||p.reminder=="none")continue;
        bool due=J.Time(p.dueUtc)<=now;bool recovered=p.reminder=="quota"&&Recovered(p,data,now);
        if(!due&&!recovered&&p.reminder=="quota"&&p.dueUtc==null){changed|=Resolve(p,data,now);due=J.Time(p.dueUtc)<=now;}
        if(due||recovered){p.readyUtc=J.Iso(now);p.readyReason=recovered?"quota":p.reminder=="time"?"time":"reset";changed=true;}
      }return changed;
    }
    public static int Deliver(WorkPlanStore store,DateTime now,Action<WorkPlan[]> deliver){
      var due=store.Items.Where(p=>p.Ready&&p.notifiedUtc==null).ToArray();if(due.Length==0)return 0;
      foreach(var p in due)p.notifiedUtc=J.Iso(now);
      try{store.Save();deliver(due);}catch{foreach(var p in due)p.notifiedUtc=null;try{store.Save();}catch{}throw;}
      return due.Length;
    }
    public static string Status(WorkPlan plan){
      if(plan.Completed)return "已完成";if(plan.Ready)return plan.readyReason=="quota"?"额度已更新，可继续":"到了提醒时间";
      if(plan.reminder=="none")return "只记录";var due=J.Time(plan.dueUtc);
      return due.HasValue?due.Value.ToLocalTime().ToString("MM/dd HH:mm")+(plan.reminder=="quota"?" 预计重置":" 提醒"):"等待额度重置时间";
    }
  }
}
