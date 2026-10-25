using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TokenGrove {
  static class PlanRegressionTests {
    static Snapshot Quota(DateTime now,double five,double week,string fiveReset,string weekReset,string observed=null){return new Snapshot{accounts=new List<Account>{new Account{provider="claude",state="ready",windows=new List<WindowQuota>{new WindowQuota{id="five_hour",usedPercent=five,resetsAt=fiveReset,observedAt=observed??J.Iso(now)},new WindowQuota{id="seven_day",usedPercent=week,resetsAt=weekReset,observedAt=observed??J.Iso(now)}}}}};}
    public static void Run(string root,Action<bool,string> check){
      var now=new DateTime(2026,10,26,4,0,0,DateTimeKind.Utc);var five=now.AddHours(2);var week=now.AddDays(3);
      var quota=Quota(now,100,40,J.Iso(five),J.Iso(week));var store=new WorkPlanStore(Path.Combine(root,"plans","plans.json"));
      var plan=store.Create("claude",quota,now);plan.title="灵感：做个小工具";plan.body="目标：记录想法\r\n要求：本机保存 ✨";store.Save();
      var loaded=WorkPlanStore.Open(store.FilePath);check(loaded.CanWrite&&loaded.Items.Count==1&&loaded.Items[0].body==plan.body,"Plans persist Chinese text, emoji and multiline details locally");
      check(J.Time(plan.dueUtc)==five&&plan.waitingWindows.SequenceEqual(new[]{"five_hour"}),"A quota plan captures the exhausted five-hour reset time");
      plan.title="更新后的想法";store.Save();check(WorkPlanStore.Open(store.FilePath+".bak").Items[0].title=="灵感：做个小工具","Atomic plan saves preserve the previous good document as a backup");
      check(plan.Prompt().Contains("更新后的想法")&&plan.Prompt().Contains("记录想法"),"Copied plan text contains both title and full requirements");
      var later=Quota(now.AddHours(3),0,45,J.Iso(now.AddHours(8)),J.Iso(week));PlanScheduler.Advance(store.Items,later,now.AddHours(3));
      check(plan.Ready&&J.Time(plan.dueUtc)==five,"A later quota sample cannot push an already captured reminder into the next window");
      int deliveries=0;check(PlanScheduler.Deliver(store,now.AddHours(3),items=>deliveries+=items.Length)==1&&deliveries==1,"Due plans are delivered as one persisted notification batch");
      var restarted=WorkPlanStore.Open(store.FilePath);check(PlanScheduler.Deliver(restarted,now.AddHours(4),items=>deliveries++)==0&&deliveries==1,"A notified plan does not notify again after an app restart");
      var both=store.Create("claude",Quota(now,100,100,J.Iso(five),J.Iso(week)),now);check(J.Time(both.dueUtc)==week&&both.waitingWindows.Length==2,"When both limits are exhausted the plan waits for the later reset");
      var unknown=store.Create("claude",Quota(now,100,100,null,null),now);PlanScheduler.Advance(new[]{unknown},null,now.AddDays(20));check(!unknown.Ready&&unknown.dueUtc==null,"Unknown reset times are never guessed from a rolling five-hour lookback");
      var recover=store.Create("claude",Quota(now,100,30,null,null),now);
      PlanScheduler.Advance(new[]{recover},Quota(now.AddMinutes(5),0,30,null,null,J.Iso(now.AddHours(-1))),now.AddMinutes(5));check(!recover.Ready,"Old cached percentages cannot claim that a plan's quota recovered");
      PlanScheduler.Advance(new[]{recover},Quota(now.AddMinutes(6),0,30,null,null),now.AddMinutes(6));check(recover.Ready&&recover.readyReason=="quota","Fresh recovery data can remind a plan whose reset time was unavailable");
      var stillBlocked=store.Create("claude",Quota(now,100,30,null,null),now);PlanScheduler.Advance(new[]{stillBlocked},Quota(now.AddMinutes(5),0,100,null,null),now.AddMinutes(5));check(!stillBlocked.Ready,"A second exhausted window prevents a false quota-recovery notification");
      var fixedClock=new DateTime(2000,1,1,0,0,0,DateTimeKind.Utc);var clockPlan=store.Create("claude",Quota(fixedClock,100,30,null,null),fixedClock);PlanScheduler.Advance(new[]{clockPlan},Quota(fixedClock.AddMinutes(5),0,100,null,J.Iso(fixedClock.AddDays(2))),fixedClock.AddMinutes(5));check(!clockPlan.Ready,"Quota recovery uses the supplied clock for every window-expiry decision");
      var resolve=store.Create("claude",Quota(now,100,30,null,null),now);PlanScheduler.Advance(new[]{resolve},quota,now.AddMinutes(1));check(J.Time(resolve.dueUtc)==five,"A missing reset time is captured once a later snapshot supplies it");
      var time=store.Create("chatgpt",null,now);time.reminder="time";PlanScheduler.Arm(time,null,now,now.AddMinutes(10));PlanScheduler.Advance(new[]{time},null,now.AddMinutes(9));check(!time.Ready,"A custom-time plan waits until its exact due time");PlanScheduler.Advance(new[]{time},null,now.AddMinutes(10));check(time.Ready&&time.readyReason=="time","A custom-time plan becomes ready without a logged-in account");
      var note=store.Create("claude",quota,now);note.reminder="none";PlanScheduler.Arm(note,quota,now);PlanScheduler.Advance(new[]{note},quota,now.AddYears(1));check(!note.Ready&&note.dueUtc==null,"Record-only ideas remain stored without generating reminders");
      var done=store.Create("claude",quota,now);done.completedUtc=J.Iso(now);PlanScheduler.Advance(new[]{done},quota,now.AddYears(1));check(!done.Ready,"Completed plans stay archived and never become due again");
      var catchup=store.Create("claude",null,now);catchup.reminder="time";PlanScheduler.Arm(catchup,null,now,now.AddMinutes(1));store.Save();var afterSleep=WorkPlanStore.Open(store.FilePath);PlanScheduler.Advance(afterSleep.Items,null,now.AddHours(1));check(afterSleep.Items.Single(p=>p.id==catchup.id).Ready,"Reopening after an overdue reminder catches the plan up");
      var failingStore=new WorkPlanStore(Path.Combine(root,"plans","delivery-failure.json"));var retry=failingStore.Create("claude",null,now);retry.reminder="time";PlanScheduler.Arm(retry,null,now,now);PlanScheduler.Advance(failingStore.Items,null,now);
      try{PlanScheduler.Deliver(failingStore,now,items=>{throw new InvalidOperationException("fixture");});}catch(InvalidOperationException){}
      check(retry.notifiedUtc==null&&WorkPlanStore.Open(failingStore.FilePath).Items[0].notifiedUtc==null,"Failed notification delivery remains eligible for a retry");
      var broken=Path.Combine(root,"plans","broken.json");File.WriteAllText(broken,"{unfinished");var protectedStore=WorkPlanStore.Open(broken);bool blocked=false;try{protectedStore.Save();}catch(IOException){blocked=true;}
      check(!protectedStore.CanWrite&&blocked&&File.ReadAllText(broken)=="{unfinished","An unreadable plan file is protected from empty-list overwrites");
      var settings=new Settings{mainView="plans"};settings.Normalize();check(settings.mainView=="plans","The plans page is a valid remembered startup page");
    }
  }
}
