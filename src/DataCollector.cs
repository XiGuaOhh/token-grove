using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TokenGrove {
  public sealed class DataCollector {
    public static string Home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string ClaudeRoot=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")??Path.Combine(Home,".claude");
    public static string CodexRoot=Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Home,".codex");
    static readonly HttpClient Http=NewHttp();
    static HttpClient NewHttp(){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;return new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};}
    readonly Dictionary<string,FileCursor> files=new Dictionary<string,FileCursor>(StringComparer.OrdinalIgnoreCase);
    Account cachedCodex;DateTime codexAt=DateTime.MinValue;
    bool completedInitialScan;
    Record[] usageRecords=new Record[0];bool usageReady;
    public Record[] UsageRecords{get{return Volatile.Read(ref usageRecords);}}
    public bool UsageReady{get{return Volatile.Read(ref usageReady);}}
    public int UnreadableUsageFiles{get;private set;}
    public bool Busy{get;private set;}
    public string LastTiming{get;private set;}
    readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
    public static List<string> DesktopRoots(){
      var roots=new List<string>();var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);var roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      var configured=Environment.GetEnvironmentVariable("CLAUDE_USER_DATA_DIR");if(!string.IsNullOrWhiteSpace(configured)&&Directory.Exists(configured))roots.Add(configured);
      var baseDir=Path.Combine(local,"Claude-Data");if(Directory.Exists(baseDir)){try{roots.AddRange(Directory.GetDirectories(baseDir));}catch{}}
      roots.Add(Path.Combine(roaming,"Claude"));
      var packages=Path.Combine(local,"Packages");if(Directory.Exists(packages)){try{roots.AddRange(Directory.GetDirectories(packages,"Claude_*").Select(d=>Path.Combine(d,"LocalCache","Roaming","Claude")));}catch{}}
      return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static bool DesktopRunning(){foreach(var p in Process.GetProcessesByName("Claude")){using(p){try{var file=p.MainModule.FileName;if(file.IndexOf("WindowsApps",StringComparison.OrdinalIgnoreCase)>=0||file.IndexOf("Anthropic",StringComparison.OrdinalIgnoreCase)>=0||file.IndexOf("Local\\AnthropicClaude",StringComparison.OrdinalIgnoreCase)>=0)return true;}catch{if(p.MainWindowHandle!=IntPtr.Zero)return true;}}}return false;}
    public static Account ReadHistory(string root,bool running){
      var a=new Account{id="claude-local",provider="claude",label="Claude",plan="Desktop",state="partial",scope="Claude Desktop · 本机",source="Claude Desktop 额度记录",lastSyncedAt=J.Iso(DateTime.UtcNow),clients=new{desktop=new{running=running,detected=true}}};
      try{var history=J.Parse(J.Read(Path.Combine(root,"plan-usage-history.json")));var samples=J.Items(J.Get(history,"samples"));
        var sample=samples.Where(x=>J.Get(x,"u")!=null||J.Get(x,"fh")!=null||J.Get(x,"sd")!=null).OrderByDescending(x=>J.Num(J.Get(x,"t"))??0).FirstOrDefault();
        if(sample!=null){var observed=J.Time(J.Get(sample,"t"));var u=J.Get(sample,"u")??sample;foreach(var pair in new[]{new[]{"fh","five_hour","5 小时额度"},new[]{"sd","seven_day","每周额度"}}){var p=J.Num(J.Get(u,pair[0]));if(p.HasValue)a.windows.Add(new WindowQuota{id=pair[1],label=pair[2],usedPercent=Math.Max(0,Math.Min(100,p.Value)),windowMinutes=pair[1]=="five_hour"?300:10080,observedAt=observed.HasValue?J.Iso(observed.Value):null});}
          a.message="已识别 Claude 桌面版。此处为桌面版保存的额度，重置时间在缓存中未提供。";if(observed.HasValue)a.lastSyncedAt=J.Iso(observed.Value);
        }
      }catch{a.message="已识别 Claude 桌面版，暂未读取到额度记录。";}
      return a;
    }
    public static List<WindowQuota> ClaudeWindows(object payload,string observed){
      var windows=new List<WindowQuota>();foreach(var item in new[]{new[]{"five_hour","5 小时额度"},new[]{"seven_day","每周额度"},new[]{"seven_day_opus","Opus 每周额度"},new[]{"seven_day_sonnet","Sonnet 每周额度"}}){var w=J.Get(payload,item[0]);var p=J.Num(J.Get(w,"utilization"))??J.Num(J.Get(w,"used_percentage"));if(!p.HasValue)continue;var reset=J.Time(J.Get(w,"resets_at"));windows.Add(new WindowQuota{id=item[0],label=item[1],usedPercent=Math.Max(0,Math.Min(100,p.Value)),resetsAt=reset.HasValue?J.Iso(reset.Value):null,windowMinutes=item[0]=="five_hour"?300:10080,observedAt=observed});}return windows;
    }
    static string DisplayPlan(string plan){if(string.IsNullOrEmpty(plan))return "Desktop";if(plan.IndexOf("max",StringComparison.OrdinalIgnoreCase)>=0)return "Max";if(plan.IndexOf("pro",StringComparison.OrdinalIgnoreCase)>=0)return "Pro";return plan.Replace("claude_","");}
    static async Task<object> FetchClaude(string token){
      using(var request=new HttpRequestMessage(HttpMethod.Get,"https://api.anthropic.com/api/oauth/usage")){
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Headers.TryAddWithoutValidation("anthropic-beta","oauth-2025-04-20");
        using(var response=await Http.SendAsync(request).ConfigureAwait(false)){if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Claude 额度接口暂不可用（"+(int)response.StatusCode+"）");return J.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));}
      }
    }
    public static async Task<Account> ReadClaudeAsync(){
      bool running=DesktopRunning();var roots=DesktopRoots().OrderByDescending(d=>{try{return File.GetLastWriteTimeUtc(Path.Combine(d,"plan-usage-history.json"));}catch{return DateTime.MinValue;}}).ToList();Account fallback=null;
      foreach(var root in roots){var history=ReadHistory(root,running);if(fallback==null||history.windows.Count>fallback.windows.Count)fallback=history;
        try{
          var cfg=J.Parse(J.Read(Path.Combine(root,"config.json")));var accountId=J.Str(J.Get(cfg,"lastKnownAccountUuid"));var payload=J.Str(J.Get(cfg,"oauth:tokenCacheV2"));if(payload.Length==0)continue;
          var local=J.Parse(J.Read(Path.Combine(root,"Local State")));var wrapped=J.Str(J.Get(J.Get(local,"os_crypt"),"encrypted_key"));
          var entries=J.Parse(LocalCrypto.DecryptSafeStorage(payload,wrapped));
          var now=(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
          var candidates=entries.Where(e=>e.Key.IndexOf("api.anthropic.com",StringComparison.OrdinalIgnoreCase)>=0&&(accountId.Length==0||e.Key.Contains(accountId))&&J.Str(J.Get(e.Value,"token")).Length>0&&(J.Num(J.Get(e.Value,"expiresAt"))??0)>now).OrderByDescending(e=>J.Num(J.Get(e.Value,"expiresAt"))??0).ToList();
          if(candidates.Count==0)continue;
          var entry=candidates[0].Value;var token=J.Str(J.Get(entry,"token"));var data=await FetchClaude(token).ConfigureAwait(false);token=null;
          history.windows=ClaudeWindows(data,J.Iso(DateTime.UtcNow));history.state=history.windows.Count>0?"ready":"partial";history.source="Claude Desktop · 授权额度接口";history.plan=DisplayPlan(J.Str(J.Get(entry,"subscriptionType")));history.lastSyncedAt=J.Iso(DateTime.UtcNow);history.message="已自动读取桌面版当前账号的授权额度。凭据仅在内存使用，不读取 Cookie，不刷新或改写登录。";
          // Account metadata is used only when it matches the Desktop identity.
          try{var meta=J.Get(J.Parse(J.Read(Path.Combine(Home,".claude.json"))),"oauthAccount");if(J.Str(J.Get(meta,"accountUuid"))==accountId)history.email=J.Str(J.Get(meta,"emailAddress"));}catch{}
          return history;
        }catch{history.message="已识别 Claude 桌面版；当前展示本机保存的额度。授权接口未返回新数据，请在 Claude 中确认登录后稍后刷新。";}
      }
      // Existing Claude Code credentials are a separate identity source.
      try{var creds=J.Get(J.Parse(J.Read(Path.Combine(ClaudeRoot,".credentials.json"))),"claudeAiOauth");var token=J.Str(J.Get(creds,"accessToken"));if(token.Length>0){var result=await FetchClaude(token).ConfigureAwait(false);return new Account{id="claude-local",provider="claude",label="Claude",plan=DisplayPlan(J.Str(J.Get(creds,"subscriptionType"))),state="ready",windows=ClaudeWindows(result,J.Iso(DateTime.UtcNow)),source="Claude Code · 授权额度接口",scope="Claude Code · 本机",lastSyncedAt=J.Iso(DateTime.UtcNow),message="通过本机已登录 Claude Code 的现有授权读取。",clients=new{desktop=new{running=running,detected=roots.Count>0},code=new{detected=true}}};}}catch{}
      // Official status-line capture remains supported without requiring it for Desktop.
      foreach(var capture in new[]{Path.Combine(Settings.Root,"claude-quota.json"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".local","claude-quota.json"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","token-grove",".local","claude-quota.json")}){try{var saved=J.Parse(J.Read(capture));var windows=ClaudeWindows(J.Get(saved,"rate_limits"),J.Str(J.Get(saved,"observedAt")));if(windows.Count>0)return new Account{id="claude-local",provider="claude",label="Claude",plan="Code",state="partial",windows=windows,source="Claude Code 官方状态栏",scope="Claude Code · 本机",lastSyncedAt=J.Str(J.Get(saved,"observedAt")),message="来自 Claude Code 的最近状态栏采样。",clients=new{desktop=new{running=running,detected=roots.Count>0}}};}catch{}}
      if(fallback!=null){fallback.message=running?"已检测到正在运行的 Claude 桌面版，正在等待可读取的账号额度；不会再当成 Code 未登录。":fallback.message;return fallback;}
      return new Account{id="claude-local",provider="claude",label="Claude",state=running?"partial":"not_logged_in",plan="LOCAL",source="本机自动检测",scope="Claude Desktop / Code",message=running?"Claude 桌面版正在运行，但尚未找到可读取的额度。":"未发现已登录的 Claude Desktop / Code。",clients=new{desktop=new{running=running,detected=running}}};
    }
    public static string FindCodex(){
      var configured=Environment.GetEnvironmentVariable("TOKEN_GROVE_CODEX_BIN");if(!string.IsNullOrEmpty(configured)&&File.Exists(configured))return configured;
      foreach(var p in Process.GetProcessesByName("codex")){using(p){try{var exe=p.MainModule.FileName;if(File.Exists(exe))return exe;}catch{}}}
      var baseDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");if(Directory.Exists(baseDir)){var exe=Directory.GetFiles(baseDir,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(exe!=null)return exe;}
      var npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","node_modules","@openai","codex");if(Directory.Exists(npm)){var exe=Directory.GetFiles(npm,"codex.exe",SearchOption.AllDirectories).FirstOrDefault();if(exe!=null)return exe;}
      foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)){try{var exe=Path.Combine(dir,"codex.exe");if(File.Exists(exe))return exe;}catch{}}
      return null;
    }
    static async Task<object> Rpc(Process p,int id,string method,object parameters){
      var msg=new Dictionary<string,object>{{"id",id},{"method",method}};if(parameters!=null)msg["params"]=parameters;
      await p.StandardInput.WriteLineAsync(J.Serializer().Serialize(msg)).ConfigureAwait(false);await p.StandardInput.FlushAsync().ConfigureAwait(false);
      var deadline=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<deadline){var read=p.StandardOutput.ReadLineAsync();var done=await Task.WhenAny(read,Task.Delay(Math.Max(1,(int)(deadline-DateTime.UtcNow).TotalMilliseconds))).ConfigureAwait(false);if(done!=read)throw new TimeoutException("Codex 账号读取超时");var line=await read.ConfigureAwait(false);if(line==null)throw new IOException("Codex 账号服务退出");if(line.Length>2*1024*1024)throw new IOException("Codex 响应过大");object packet;try{packet=J.Parse(line);}catch{continue;}if(J.Num(J.Get(packet,"id"))!=id)continue;if(J.Get(packet,"error")!=null)throw new IOException("Codex 额度接口暂不可用");return J.Get(packet,"result");}throw new TimeoutException();
    }
    public static List<WindowQuota> CodexWindows(object bucket,string observed){
      var result=new List<WindowQuota>();foreach(var key in new[]{"primary","secondary"}){var w=J.Get(bucket,key);if(w==null)continue;int minutes=(int)J.Count(J.Get(w,"windowDurationMins")??J.Get(w,"window_minutes"));var p=J.Num(J.Get(w,"usedPercent")??J.Get(w,"used_percent"));var reset=J.Time(J.Get(w,"resetsAt")??J.Get(w,"resets_at"));result.Add(new WindowQuota{id=key=="primary"?"five_hour":"seven_day",label=minutes==300?"5 小时额度":minutes==10080?"每周额度":minutes>0?(minutes%60==0?(minutes/60)+" 小时额度":minutes+" 分钟额度"):"额度窗口",usedPercent=p.HasValue?(double?)Math.Max(0,Math.Min(100,p.Value)):null,windowMinutes=minutes>0?(int?)minutes:null,resetsAt=reset.HasValue?J.Iso(reset.Value):null,observedAt=observed});}return result;
    }
    public static async Task<Account> ReadCodexAsync(){
      var a=new Account{id="chatgpt-local",provider="chatgpt",label="ChatGPT",plan="LOCAL",scope="Codex · ChatGPT 订阅",source="Codex App Server",state="unavailable",lastSyncedAt=J.Iso(DateTime.UtcNow)};var exe=FindCodex();if(exe==null){a.message="未找到 Codex 原生程序。打开 Codex 后再刷新。";return a;}
      using(var p=new Process{StartInfo=new ProcessStartInfo(exe,"app-server"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Home,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
        try{p.Start();p.ErrorDataReceived+=(s,e)=>{};p.BeginErrorReadLine();await Rpc(p,0,"initialize",new{clientInfo=new{name="token_grove_desktop",title="Token Grove Desktop",version="2.0.0"}}).ConfigureAwait(false);await p.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}").ConfigureAwait(false);
          var identity=J.Get(await Rpc(p,1,"account/read",new{refreshToken=false}).ConfigureAwait(false),"account");if(identity==null){a.state="not_logged_in";a.message="Codex 当前未登录。请在 Codex 中完成登录后刷新。";return a;}
          a.email=J.Str(J.Get(identity,"email"));a.plan=J.Str(J.Get(identity,"planType"));var type=J.Str(J.Get(identity,"type"));if(type!="chatgpt"&&type!="chatgptAuthTokens"){a.state="partial";a.plan=type;a.message="当前为 API 或其他认证，未提供 ChatGPT 订阅额度。";return a;}
          var rate=await Rpc(p,2,"account/rateLimits/read",null).ConfigureAwait(false);var bucket=J.Get(J.Get(rate,"rateLimitsByLimitId"),"codex")??J.Get(rate,"rateLimits");a.windows=CodexWindows(bucket,J.Iso(DateTime.UtcNow));a.state=a.windows.Any(w=>w.usedPercent.HasValue)?"ready":"partial";a.message="额度属于 ChatGPT 订阅中的 Codex，不等同 ChatGPT 网页各模型消息限制。";return a;
        }catch{a.message="Codex 账号服务未返回新额度，请稍后刷新。";return a;}finally{try{p.StandardInput.Close();if(!p.WaitForExit(1200))p.Kill();}catch{}}
      }
    }
    public sealed class FileCursor {
      public long position;public DateTime modified;public string provider,model,session;public long previousInput,previousOutput,previousCached,previousTotal;public bool hasPreviousTotal,partialHistory;
      public Dictionary<string,Record> records=new Dictionary<string,Record>();
    }
    static IEnumerable<string> Walk(string root,int depth=0){
      if(depth>10||!Directory.Exists(root))yield break;string[] direct,dirs;try{direct=Directory.GetFiles(root,"*.jsonl");dirs=Directory.GetDirectories(root);}catch{yield break;}
      foreach(var file in direct)yield return file;foreach(var dir in dirs){FileAttributes attr;try{attr=File.GetAttributes(dir);}catch{continue;}if((attr&FileAttributes.ReparsePoint)!=0)continue;foreach(var file in Walk(dir,depth+1))yield return file;}
    }
    public static Record ClaudeRecord(object item){
      if(J.Str(J.Get(item,"type"))!="assistant")return null;var message=J.Get(item,"message");var usage=J.Get(message,"usage");if(usage==null)return null;var time=J.Time(J.Get(item,"timestamp"));if(!time.HasValue)return null;
      var r=new Record{provider="claude",time=time.Value,model=J.Str(J.Get(message,"model")),input=J.Count(J.Get(usage,"input_tokens")),output=J.Count(J.Get(usage,"output_tokens")),cached=J.Count(J.Get(usage,"cache_read_input_tokens")),write=J.Count(J.Get(usage,"cache_creation_input_tokens"))};r.total=r.input+r.output+r.cached+r.write;
      var id=J.Str(J.Get(message,"id"));r.key="claude:"+J.Str(J.Get(item,"sessionId"))+":"+(id.Length>0?id:J.Str(J.Get(item,"uuid")));if(r.key.EndsWith(":"))r.key+=r.time.Ticks+":"+r.total;return r.total>0?r:null;
    }
    public static Record CodexRecord(object item,FileCursor c){
      var type=J.Str(J.Get(item,"type"));var payload=J.Get(item,"payload");if(type=="session_meta"){var session=J.Str(J.Get(payload,"id"));if(session.Length>0&&session!=c.session){c.session=session;c.hasPreviousTotal=false;}c.partialHistory=J.Get(payload,"history_base")!=null||J.Str(J.Get(payload,"history_mode"))=="paginated";return null;}if(type=="turn_context"){c.model=J.Str(J.Get(payload,"model"));return null;}if(type!="event_msg"||J.Str(J.Get(payload,"type"))!="token_count")return null;
      var info=J.Get(payload,"info");var total=J.Get(info,"total_token_usage");var last=J.Get(info,"last_token_usage");if(total==null&&last==null)return null;var time=J.Time(J.Get(item,"timestamp"));if(!time.HasValue)return null;
      var data=total??last;long input=J.Count(J.Get(data,"input_tokens")),output=J.Count(J.Get(data,"output_tokens")),cached=J.Count(J.Get(data,"cached_input_tokens"));long count=J.Get(data,"total_tokens")==null?input+output:J.Count(J.Get(data,"total_tokens"));
      var r=new Record{provider="chatgpt",model=string.IsNullOrEmpty(c.model)?"Codex · 未标记模型":c.model,time=time.Value};
      if(total!=null){
        bool boundary=!c.hasPreviousTotal||count<c.previousTotal;
        // Paginated/resumed histories carry older cumulative usage into a new file.
        // At a counter boundary only last_token_usage identifies a new request.
        if(boundary&&last!=null){r.input=J.Count(J.Get(last,"input_tokens"));r.output=J.Count(J.Get(last,"output_tokens"));r.cached=J.Count(J.Get(last,"cached_input_tokens"));r.total=J.Get(last,"total_tokens")==null?r.input+r.output:J.Count(J.Get(last,"total_tokens"));}
        else if(!boundary||!c.partialHistory){r.input=Math.Max(0,input-(boundary?0:c.previousInput));r.output=Math.Max(0,output-(boundary?0:c.previousOutput));r.cached=Math.Max(0,cached-(boundary?0:c.previousCached));r.total=Math.Max(0,count-(boundary?0:c.previousTotal));}
        c.previousInput=input;c.previousOutput=output;c.previousCached=cached;c.previousTotal=count;c.hasPreviousTotal=true;
      }else{r.input=input;r.output=output;r.cached=cached;r.total=count;}
      r.key="codex:"+c.session+":"+time.Value.Ticks+":"+count;return r.total>0?r:null;
    }
    void ProcessLine(FileCursor c,string line){
      if(c.provider=="claude"&&(!line.Contains("\"usage\"")||!line.Contains("\"assistant\"")))return;
      if(c.provider=="chatgpt"&&!line.Contains("\"token_count\"")&&!line.Contains("\"turn_context\"")&&!line.Contains("\"session_meta\""))return;
      try{var item=J.Parse(line);var r=c.provider=="claude"?ClaudeRecord(item):CodexRecord(item,c);if(r==null)return;Record old;if(!c.records.TryGetValue(r.key,out old)||r.total>old.total)c.records[r.key]=r;}catch{/* Active partial writes and malformed lines do not replace good data. */}
    }
    void ReadIncrement(string file,string provider){
      FileCursor c;var size=new FileInfo(file);if(!files.TryGetValue(file,out c)||size.Length<c.position){c=new FileCursor{provider=provider,model="",session=Path.GetFileName(file)};files[file]=c;}
      if(c.position==size.Length&&c.modified==size.LastWriteTimeUtc)return;
      // Byte offsets and complete newline boundaries avoid rescans and partial-line loss.
      using(var fs=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){fs.Seek(c.position,SeekOrigin.Begin);var bytes=new byte[65536];using(var line=new MemoryStream()){int n;while((n=fs.Read(bytes,0,bytes.Length))>0){int start=0;for(int i=0;i<n;i++){if(bytes[i]!=10)continue;line.Write(bytes,start,i-start);if(line.Length<=32*1024*1024)ProcessLine(c,Encoding.UTF8.GetString(line.GetBuffer(),0,(int)line.Length).TrimEnd('\r'));c.position+=line.Length+1;line.SetLength(0);start=i+1;}if(start<n)line.Write(bytes,start,n-start);if(line.Length>32*1024*1024)break;}}}c.modified=size.LastWriteTimeUtc;
    }
    internal List<Record> ReadUsageFile(string file,string provider){ReadIncrement(file,provider);return files[file].records.Values.ToList();}
    Record[] ReadUsage(out int count){
      var roots=new List<Tuple<string,string>>{Tuple.Create(Path.Combine(ClaudeRoot,"projects"),"claude"),Tuple.Create(Path.Combine(CodexRoot,"sessions"),"chatgpt"),Tuple.Create(Path.Combine(CodexRoot,"archived_sessions"),"chatgpt")};
      foreach(var dir in DesktopRoots())roots.Add(Tuple.Create(Path.Combine(dir,"claude-code-sessions"),"claude"));
      var candidates=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var root in roots){foreach(var f in Walk(root.Item1))candidates[f]=root.Item2;}
      foreach(var old in files.Keys.Except(candidates.Keys,StringComparer.OrdinalIgnoreCase).ToList())files.Remove(old);
      UnreadableUsageFiles=0;foreach(var f in candidates){try{ReadIncrement(f.Key,f.Value);}catch{UnreadableUsageFiles++;}}
      var all=new Dictionary<string,Record>();foreach(var c in files.Values){foreach(var r in c.records.Values){Record old;if(!all.TryGetValue(r.key,out old)||r.total>old.total)all[r.key]=r;}}
      count=candidates.Count;var result=all.Values.ToArray();Array.Sort(result,(a,b)=>a.time.CompareTo(b.time));Volatile.Write(ref usageRecords,result);Volatile.Write(ref usageReady,true);return result;
    }
    public static Estimate Calculate(Account account,List<Record> records){
      return CalculateWindow(account.provider,account.Five,records,DateTime.UtcNow);
    }
    public static Estimate CalculateWindow(string provider,WindowQuota window,IEnumerable<Record> records,DateTime now){
      if(window==null||!window.usedPercent.HasValue||window.usedPercent<=1||window.usedPercent>=100||!window.windowMinutes.HasValue||window.windowMinutes<=0||window.windowMinutes>525600)return null;
      var end=J.Time(window.resetsAt);var observed=J.Time(window.observedAt);if(!end.HasValue||end<=now||!observed.HasValue||now-observed.Value>TimeSpan.FromMinutes(30)||observed.Value>now.AddMinutes(2))return null;
      var start=end.Value.AddMinutes(-window.windowMinutes.Value);if(observed.Value<start||observed.Value>=end)return null;
      long tokens=0;foreach(var row in records)if(row.provider==provider&&row.time>=start&&row.time<=observed.Value)tokens=checked(tokens+row.total);if(tokens<1000)return null;
      double remaining=tokens/window.usedPercent.Value*(100-window.usedPercent.Value);if(remaining>9007199254740991d)return null;
      return new Estimate{remainingTokens=(long)remaining,sampleTokens=tokens,sampleUsedPercent=window.usedPercent.Value,windowId=window.id,startUtc=J.Iso(start),endUtc=J.Iso(end.Value),measuredAt=J.Iso(observed.Value),note="按当前额度窗口的本机 Token 与官方已用比例估算；不含网页或其他设备，不是官方余额。"};
    }
    public async Task RefreshUsageAsync(){if(!await gate.WaitAsync(0).ConfigureAwait(false))return;Busy=true;try{await Task.Run(()=>{int count;ReadUsage(out count);}).ConfigureAwait(false);}finally{Busy=false;gate.Release();}}
    public async Task<Snapshot> CollectAsync(bool force=false){
      if(!await gate.WaitAsync(0).ConfigureAwait(false))return null;Busy=true;var watch=Stopwatch.StartNew();
      try{
        var claudeTask=ReadClaudeAsync();bool refresh=force||cachedCodex==null||DateTime.UtcNow-codexAt>=TimeSpan.FromMinutes(5)||cachedCodex.windows.Any(w=>w.Expired);
        var codexTask=refresh?ReadCodexAsync():Task.FromResult(cachedCodex);int fileCount=0;var usageTask=Task.Run(()=>{int n;var r=ReadUsage(out n);fileCount=n;return r;});
        await Task.WhenAll(claudeTask,codexTask,usageTask).ConfigureAwait(false);if(refresh){cachedCodex=codexTask.Result;codexAt=DateTime.UtcNow;}
        var allRecords=usageTask.Result;var records=allRecords.Where(r=>r.time>=DateTime.UtcNow.AddDays(-30)).ToList();var snapshot=new Snapshot{updatedAt=J.Iso(DateTime.UtcNow),accounts=new List<Account>{claudeTask.Result,codexTask.Result},coverage="最近 30 天展示 · 已索引 "+fileCount+" 份本机记录（不含网页会话）"};
        snapshot.models=records.GroupBy(r=>r.provider+":"+r.model).Select(g=>new ModelUsage{id=g.Key,name=g.First().model,provider=g.First().provider,inputTokens=g.Sum(r=>r.input),outputTokens=g.Sum(r=>r.output),cachedTokens=g.Sum(r=>r.cached),cacheWriteTokens=g.Sum(r=>r.write),totalTokens=g.Sum(r=>r.total)}).OrderByDescending(m=>m.totalTokens).ToList();
        snapshot.daily=records.GroupBy(r=>r.time.ToLocalTime().ToString("yyyy-MM-dd")).Select(g=>new DayUsage{date=g.Key,claude=g.Where(r=>r.provider=="claude").Sum(r=>r.total),chatgpt=g.Where(r=>r.provider=="chatgpt").Sum(r=>r.total)}).OrderBy(d=>d.date).ToList();
        var measured=DateTime.UtcNow;foreach(var a in snapshot.accounts){foreach(var w in a.windows){w.localUsage=UsageEngine.ForWindow(a.provider,w,allRecords,measured);w.estimate=CalculateWindow(a.provider,w,allRecords,measured);}a.estimate=a.Five?.estimate;a.weekEstimate=a.Week?.estimate;}watch.Stop();LastTiming=watch.Elapsed.TotalSeconds.ToString("0.00")+"s · "+fileCount+" 份记录";return snapshot;
      }finally{Busy=false;gate.Release();if(!completedInitialScan){completedInitialScan=true;if(GC.GetTotalMemory(false)>12*1024*1024){System.Runtime.GCSettings.LargeObjectHeapCompactionMode=System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;GC.Collect(2,GCCollectionMode.Forced,true,true);}}}
    }
  }
}
