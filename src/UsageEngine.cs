using System;
using System.Collections.Generic;
using System.Linq;

namespace TokenGrove {
  public sealed class UsageTotals {
    public long input,output,cached,write,total,freshInput;
  }
  public sealed class UsageBucket {
    public DateTime fromUtc,toUtc;public long claude,chatgpt;public int records;
  }
  public sealed class WindowTokenUsage {
    public string state,startUtc,endUtc,measuredThroughUtc,note;
    public long? usedTokens;
    public int records;
  }
  public sealed class UsageResult {
    public DateTime fromUtc,toUtc;
    public UsageTotals totals=new UsageTotals();
    public int records;
    public List<ModelUsage> models=new List<ModelUsage>();
    public List<DayUsage> daily=new List<DayUsage>();
    public List<UsageBucket> trend=new List<UsageBucket>();
  }
  public static class UsageEngine {
    public static DateTime Utc(DateTime value){return value.Kind==DateTimeKind.Utc?value:DateTime.SpecifyKind(value,DateTimeKind.Local).ToUniversalTime();}
    public static UsageResult Query(IEnumerable<Record> records,DateTime from,DateTime to,string provider=null,string model=null){
      from=Utc(from);to=Utc(to);if(to<=from)throw new ArgumentException("截止时间必须晚于开始时间。");
      var result=new UsageResult{fromUtc=from,toUtc=to};var models=new Dictionary<string,ModelUsage>();var days=new Dictionary<DateTime,DayUsage>();
      double hours=(to-from).TotalHours;long step=hours<=48?TimeSpan.TicksPerHour:TimeSpan.TicksPerDay*Math.Max(1,(long)Math.Ceiling((to-from).TotalDays/60));
      int buckets=(int)Math.Ceiling((to-from).Ticks/(double)step);for(int i=0;i<buckets;i++){var begin=from.AddTicks(step*i);result.trend.Add(new UsageBucket{fromUtc=begin,toUtc=to-begin<TimeSpan.FromTicks(step)?to:begin.AddTicks(step)});}
      foreach(var row in records){if(!Matches(row,from,to,provider,model))continue;result.records++;var t=result.totals;t.input+=row.input;t.output+=row.output;t.cached+=row.cached;t.write+=row.write;t.total+=row.total;t.freshInput+=Math.Max(0,row.input-(row.provider=="chatgpt"?row.cached:0));
        string key=row.provider+":"+row.model;ModelUsage m;if(!models.TryGetValue(key,out m)){m=new ModelUsage{id=key,provider=row.provider,name=row.model};models.Add(key,m);}m.inputTokens+=row.input;m.outputTokens+=row.output;m.cachedTokens+=row.cached;m.cacheWriteTokens+=row.write;m.totalTokens+=row.total;
        var date=row.time.ToLocalTime().Date;DayUsage day;if(!days.TryGetValue(date,out day)){day=new DayUsage{date=date.ToString("yyyy-MM-dd")};days.Add(date,day);}if(row.provider=="claude")day.claude+=row.total;else day.chatgpt+=row.total;
        var bucket=result.trend[(int)((row.time-from).Ticks/step)];if(row.provider=="claude")bucket.claude+=row.total;else bucket.chatgpt+=row.total;bucket.records++;
      }
      result.models=models.Values.OrderByDescending(m=>m.totalTokens).ToList();result.daily=days.OrderBy(d=>d.Key).Select(d=>d.Value).ToList();return result;
    }
    static bool Matches(Record row,DateTime from,DateTime to,string provider,string model){return row.time>=from&&row.time<to&&(string.IsNullOrEmpty(provider)||row.provider==provider)&&(string.IsNullOrWhiteSpace(model)||(row.model??"").IndexOf(model,StringComparison.OrdinalIgnoreCase)>=0);}
    // The collector publishes an immutable array ordered by time. Only one page is retained by the view.
    public static List<Record> Page(Record[] orderedRecords,DateTime from,DateTime to,string provider,string model,int skip,int take){from=Utc(from);to=Utc(to);var page=new List<Record>(Math.Max(0,take));if(take<=0)return page;int low=0,high=orderedRecords.Length;while(low<high){int mid=low+(high-low)/2;if(orderedRecords[mid].time<to)low=mid+1;else high=mid;}for(int i=low-1;i>=0;i--){var row=orderedRecords[i];if(row.time<from)break;if(!Matches(row,from,to,provider,model))continue;if(skip>0){skip--;continue;}page.Add(row);if(page.Count==take)break;}return page;}
    public static WindowTokenUsage ForWindow(string provider,WindowQuota window,IEnumerable<Record> records,DateTime now){
      now=Utc(now);var result=new WindowTokenUsage{state="unknown_window",note="服务商未提供窗口时间，可在下方查询近 5 小时或近 7 天。"};
      if(window==null||!window.windowMinutes.HasValue||window.windowMinutes<=0||window.windowMinutes>525600)return result;
      var end=J.Time(window.resetsAt);if(!end.HasValue)return result;if(end.Value<=now){result.state="expired";result.note="额度窗口已结束，等待新的重置时间。";return result;}
      DateTime start;try{start=end.Value.AddMinutes(-window.windowMinutes.Value);}catch{return result;}if(start>now)return result;
      long tokens=0;int count=0;foreach(var row in records)if(row.provider==provider&&row.time>=start&&row.time<now){tokens+=row.total;count++;}result.state="ready";result.startUtc=J.Iso(start);result.endUtc=J.Iso(end.Value);result.measuredThroughUtc=J.Iso(now);result.usedTokens=tokens;result.records=count;result.note="按服务商窗口时间统计的本机 Token；不含网页端或其他设备。";return result;
    }
  }
}
