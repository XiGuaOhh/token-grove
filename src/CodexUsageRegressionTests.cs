using System;
using System.IO;
using System.Linq;

namespace TokenGrove {
  static class CodexUsageRegressionTests {
    static object Event(DateTime time,long input,long cached,long output,long lastInput,long lastCached,long lastOutput){return new{type="event_msg",timestamp=J.Iso(time),payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=input,cached_input_tokens=cached,output_tokens=output,total_tokens=input+output},last_token_usage=new{input_tokens=lastInput,cached_input_tokens=lastCached,output_tokens=lastOutput,total_tokens=lastInput+lastOutput}}}};}
    public static void Run(string root,Action<bool,string> check){
      var time=new DateTime(2026,10,31,11,0,0,DateTimeKind.Utc);
      var cursor=new DataCollector.FileCursor{session="fixture",model="gpt-test"};
      var first=Event(time,300000000,290000000,500000,50000,10000,200);
      var row=DataCollector.CodexRecord(J.Parse(J.Serializer().Serialize(first)),cursor);
      check(row.total==50200&&row.input==50000&&row.cached==10000&&row.output==200,"A paginated Codex history counts the first request rather than its inherited 300M cumulative total");
      var duplicate=DataCollector.CodexRecord(J.Parse(J.Serializer().Serialize(Event(time.AddSeconds(1),300000000,290000000,500000,50000,10000,200))),cursor);
      check(duplicate==null,"Repeated cumulative usage snapshots do not count last-request tokens again");
      var next=DataCollector.CodexRecord(J.Parse(J.Serializer().Serialize(Event(time.AddSeconds(2),300060000,290050000,500040,60000,50000,40))),cursor);
      check(next.total==60040&&next.input==60000&&next.cached==50000&&next.output==40,"Later paginated Codex events still use exact cumulative differences");
      var reset=DataCollector.CodexRecord(J.Parse(J.Serializer().Serialize(Event(time.AddSeconds(3),150000,120000,1000,2000,1500,100))),cursor);
      check(reset.total==2100&&reset.input==2000&&reset.cached==1500&&reset.output==100,"A reduced cumulative counter cannot reintroduce an earlier history baseline");
      var partial=new DataCollector.FileCursor{session="partial",partialHistory=true};
      Func<long,object> withoutLast=count=>J.Parse(J.Serializer().Serialize(new{type="event_msg",timestamp=J.Iso(time),payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=count,output_tokens=0,total_tokens=count}}}}));
      var unknown=DataCollector.CodexRecord(withoutLast(300000000),partial);var known=DataCollector.CodexRecord(withoutLast(300000100),partial);
      check(unknown==null&&known.total==100,"A partial history without per-request usage establishes a baseline without inventing historical consumption");
      DataCollector.CodexRecord(J.Parse("{\"type\":\"session_meta\",\"payload\":{\"id\":\"new-fixture\",\"history_mode\":\"paginated\"}}"),cursor);
      var newSession=DataCollector.CodexRecord(J.Parse(J.Serializer().Serialize(first)),cursor);
      check(newSession.total==50200&&newSession.key.StartsWith("codex:new-fixture:"),"A new session identity resets its counter baseline even if its inherited cumulative total grows");
      var file=Path.Combine(root,"codex-paginated.jsonl");
      var meta=J.Serializer().Serialize(new{type="session_meta",payload=new{id="fixture",history_mode="paginated",history_base=new{thread_id="previous-fixture",end_ordinal_exclusive=11026}}});
      File.WriteAllText(file,meta+"\n"+J.Serializer().Serialize(first)+"\n");
      var collector=new DataCollector();var initial=collector.ReadUsageFile(file,"chatgpt");
      File.AppendAllText(file,J.Serializer().Serialize(Event(time.AddSeconds(2),300060000,290050000,500040,60000,50000,40))+"\n");
      var updated=collector.ReadUsageFile(file,"chatgpt");
      check(initial.Sum(r=>r.total)==50200&&updated.Sum(r=>r.total)==110240,"Incremental file reads retain the corrected baseline across refreshes");
      var five=new WindowQuota{id="five_hour",usedPercent=18,windowMinutes=300,resetsAt=J.Iso(time.AddHours(4)),observedAt=J.Iso(time.AddMinutes(1))};
      var week=new WindowQuota{id="seven_day",usedPercent=85,windowMinutes=10080,resetsAt=J.Iso(time.AddDays(4)),observedAt=J.Iso(time.AddMinutes(1))};
      var range=UsageEngine.Query(updated,time.AddMinutes(-1),time.AddMinutes(1));
      check(range.totals.total==110240&&range.totals.cached==60000&&range.trend.Sum(b=>b.chatgpt)==110240,"Range queries and trends do not inherit historical Codex token totals or double-count cache");
      check(UsageEngine.ForWindow("chatgpt",five,updated,time.AddMinutes(1)).usedTokens==110240&&UsageEngine.ForWindow("chatgpt",week,updated,time.AddMinutes(1)).usedTokens==110240,"Both five-hour and weekly local token counts use corrected Codex records");
      check(DataCollector.CalculateWindow("chatgpt",five,updated,time.AddMinutes(1)).sampleTokens==110240&&DataCollector.CalculateWindow("chatgpt",week,updated,time.AddMinutes(1)).sampleTokens==110240&&five.usedPercent==18&&week.usedPercent==85,"Both token estimates use corrected samples while preserving official quota percentages");
    }
  }
}
