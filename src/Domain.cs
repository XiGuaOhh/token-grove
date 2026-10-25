using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TokenGrove {
  public static class J {
    public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=128};}
    public static Dictionary<string,object> Parse(string text){return Serializer().Deserialize<Dictionary<string,object>>(text);}
    public static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object> ?? new Dictionary<string,object>();}
    public static object Get(object value,string key){object result;return Obj(value).TryGetValue(key,out result)?result:null;}
    public static string Str(object value){return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);}
    public static double? Num(object value){if(value==null)return null;double v;return double.TryParse(Str(value),NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!double.IsNaN(v)&&!double.IsInfinity(v)?(double?)v:null;}
    public static long Count(object value){return (long)Math.Max(0,Math.Min(9007199254740991d,Num(value)??0));}
    public static string Iso(DateTime time){return time.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture);}
    public static DateTime? Time(object value){if(value==null)return null;double? numeric=Num(value);if(numeric.HasValue){try{return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(numeric.Value<1e12?numeric.Value*1000:numeric.Value);}catch{return null;}}DateTimeOffset t;return DateTimeOffset.TryParse(Str(value),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out t)?(DateTime?)t.UtcDateTime:null;}
    public static object[] Items(object value){if(value is object[])return (object[])value;if(value is ArrayList)return ((ArrayList)value).ToArray();return new object[0];}
    public static string Read(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new StreamReader(f,Encoding.UTF8,true))return r.ReadToEnd();}
    public static void Write(string path,object data){Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,Serializer().Serialize(data),new UTF8Encoding(false));if(File.Exists(path)){try{File.Replace(temp,path,null);return;}catch{File.Delete(path);}}File.Move(temp,path);}
    public static string Tokens(double value){if(value>=1e9)return (value/1e9).ToString("0.00",CultureInfo.InvariantCulture)+"B";if(value>=1e6)return (value/1e6).ToString("0.00",CultureInfo.InvariantCulture)+"M";if(value>=1e3)return (value/1e3).ToString("0.#",CultureInfo.InvariantCulture)+"K";return value.ToString("N0",CultureInfo.InvariantCulture);}
    public static string Mask(string email){if(string.IsNullOrWhiteSpace(email))return "本机账号";int at=email.IndexOf('@');return at>0?email.Substring(0,Math.Min(2,at))+"•••"+email.Substring(at):"本机账号";}
  }
  public class WindowQuota {
    public string id,label,resetsAt,observedAt;
    public double? usedPercent;
    public int? windowMinutes;
    public WindowTokenUsage localUsage;
    public Estimate estimate;
    [ScriptIgnore] public string EstimateText{get{return estimate!=null&&estimate.Current(this,DateTime.UtcNow)?J.Tokens(estimate.remainingTokens):"—";}}
    [ScriptIgnore] public bool Expired {get{return J.Time(resetsAt).HasValue&&J.Time(resetsAt).Value<=DateTime.UtcNow;}}
    [ScriptIgnore] public string TokenText{get{return Expired?"等待更新":localUsage?.usedTokens!=null?J.Tokens(localUsage.usedTokens.Value):"窗口未知";}}
    [ScriptIgnore] public string ResetText {get{var t=J.Time(resetsAt);if(!t.HasValue)return "重置时间未提供";var d=t.Value-DateTime.UtcNow;if(d.TotalSeconds<=0)return "等待官方额度更新";if(d.TotalDays>=1)return (int)d.TotalDays+" 天 "+d.Hours+" 小时后重置";return (int)d.TotalHours+" 小时 "+d.Minutes.ToString("00")+" 分后重置";}}
  }
  public class Estimate {public long remainingTokens,sampleTokens;public double sampleUsedPercent;public string note,windowId,startUtc,endUtc,measuredAt;
    public bool Current(WindowQuota window,DateTime now){if(window==null||window.Expired||windowId!=null&&windowId!=window.id)return false;var observed=J.Time(measuredAt??window.observedAt);return observed.HasValue&&observed.Value<=now.AddMinutes(2)&&now-observed.Value<=TimeSpan.FromMinutes(30)&&(endUtc==null||J.Time(endUtc)==J.Time(window.resetsAt));}
  }
  public class Account {
    public string id,provider,label,email,plan,state,message,source,scope,lastSyncedAt;
    public List<WindowQuota> windows=new List<WindowQuota>();
    public Estimate estimate,weekEstimate;
    public object clients;
    [ScriptIgnore] public WindowQuota Five{get{return windows.FirstOrDefault(w=>w.id=="five_hour");}}
    [ScriptIgnore] public WindowQuota Week{get{return windows.FirstOrDefault(w=>w.id=="seven_day");}}
    [ScriptIgnore] public bool Available{get{return state=="ready"||state=="partial";}}
    [ScriptIgnore] public string Status{get{if(state=="loading")return "正在检测";if(windows.Any(w=>w.Expired))return "等待更新";if(windows.Any(w=>w.usedPercent>=100))return "额度已满";if(windows.Any(w=>w.usedPercent>=80))return "留意额度";if(state=="ready")return "已连接";if(state=="partial")return "已识别 · 缓存";if(state=="not_logged_in")return "待连接";return "暂不可用";}}
  }
  public class ModelUsage {public string id,name,provider;public long inputTokens,outputTokens,cachedTokens,cacheWriteTokens,totalTokens;}
  public class DayUsage {public string date;public long claude,chatgpt;}
  public class Snapshot {
    public int version=1,periodDays=30;
    public string mode="local",updatedAt,coverage;
    public List<Account> accounts=new List<Account>();
    public List<ModelUsage> models=new List<ModelUsage>();
    public List<DayUsage> daily=new List<DayUsage>();
    [ScriptIgnore] public long WeekTokens{get{var cutoff=DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd");return daily.Where(d=>string.CompareOrdinal(d.date,cutoff)>=0).Sum(d=>d.claude+d.chatgpt);}}
    public static Snapshot Empty(){return new Snapshot{updatedAt=J.Iso(DateTime.UtcNow),accounts=new List<Account>{new Account{id="claude-local",provider="claude",label="Claude",plan="LOCAL",state="loading",source="正在检测桌面版与 Code"},new Account{id="chatgpt-local",provider="chatgpt",label="ChatGPT",plan="LOCAL",state="loading",source="Codex 本机账号"}}};}
  }
  public class Record {
    public string key,model,provider;public DateTime time;public long input,output,cached,write,total;
  }
  public class Settings {
    public bool dark=false,widget=true,topmost=true,motion=true,eco=true,privacy=true,startup=false,glass=true;
    public string themeMode="system",mainView="overview";
    public int widgetOpacity=96,mainOpacity=100,surfaceOpacity=78;
    public int intervalSeconds=300,threshold=80,widgetX=int.MinValue,widgetY=int.MinValue;
    public static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TokenGrove");
    public static Settings Load(){try{var s=J.Serializer().Deserialize<Settings>(J.Read(Path.Combine(Root,"settings.json")))??new Settings();s.Normalize();return s;}catch{return new Settings();}}
    public void Normalize(){if(themeMode!="light"&&themeMode!="dark")themeMode="system";if(!new[]{"overview","usage","accounts","settings"}.Contains(mainView))mainView="overview";widgetOpacity=Math.Max(55,Math.Min(100,widgetOpacity));mainOpacity=Math.Max(75,Math.Min(100,mainOpacity));surfaceOpacity=Math.Max(35,Math.Min(100,surfaceOpacity));intervalSeconds=Math.Max(60,Math.Min(3600,intervalSeconds));threshold=Math.Max(50,Math.Min(100,threshold));}
    public bool EffectiveDark(bool systemDark){return themeMode=="dark"||(themeMode!="light"&&systemDark);}
    public void Save(){Normalize();J.Write(Path.Combine(Root,"settings.json"),this);}
  }
}
