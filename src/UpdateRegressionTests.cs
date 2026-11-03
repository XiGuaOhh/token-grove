using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TokenGrove {
  static class UpdateRegressionTests {
    sealed class Handler:HttpMessageHandler {
      public int Calls;public bool Anonymous=true;public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply;
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;Anonymous&=request.Headers.Authorization==null&&!request.Headers.Contains("Cookie")&&request.Headers.UserAgent.ToString().StartsWith("TokenGrove/");return Reply(request,token);}
    }
    static string Digest(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static HttpResponseMessage Response(string text,HttpStatusCode status=HttpStatusCode.OK){return new HttpResponseMessage(status){Content=new StringContent(text,Encoding.UTF8)};}
    internal static string Manifest(string tag="v9.0.0",string digest=null,long size=5,bool prerelease=false,string assetUrl=null,bool draft=false){var name="Token-Grove-"+tag+"-Windows-x64.zip";return J.Serializer().Serialize(new{tag_name=tag,html_url=AppUpdates.Repository+"/releases/tag/"+tag,body="更新说明 · 合成样例",draft,prerelease,assets=new object[]{new{name,state="uploaded",size,browser_download_url=assetUrl??AppUpdates.Repository+"/releases/download/"+tag+"/"+name,digest=digest==null?null:"sha256:"+digest},new{name="SHA256SUMS.txt",state="uploaded",size=100,browser_download_url=AppUpdates.Repository+"/releases/download/"+tag+"/SHA256SUMS.txt"}}});}
    static Handler Text(string text){return new Handler{Reply=(r,t)=>Task.FromResult(Response(text))};}
    static bool Rejects(string json){try{AppUpdates.ParseRelease(json);return false;}catch(InvalidDataException){return true;}}
    public static void Run(string root,Action<bool,string> check){RunAsync(Path.Combine(root,"updates-tests"),check).GetAwaiter().GetResult();}
    static async Task RunAsync(string root,Action<bool,string> check){
      Directory.CreateDirectory(root);var bytes=Encoding.UTF8.GetBytes("hello");string digest=Digest(bytes);var current=new Version(2,7,0);
      check(AppUpdates.ParseVersion("v2.10.0")>AppUpdates.ParseVersion("v2.9.9")&&AppUpdates.ParseVersion("v2.7.0-rc.1")==null&&AppUpdates.ParseVersion("../../v9.0.0")==null,"Update versions compare numerically and reject prerelease or path-like tags");
      var release=AppUpdates.ParseRelease(Manifest(digest:digest));check(release.Downloadable&&release.assetSize==5&&release.digest==digest,"Formal GitHub releases select the exact Windows x64 portable package and SHA-256 digest");
      check(Rejects(Manifest(prerelease:true))&&Rejects(Manifest(draft:true)),"Drafts and prereleases never become an offered software update");
      check(!AppUpdates.ParseRelease(Manifest(digest:digest,assetUrl:"https://example.com/update.exe")).Downloadable,"Release metadata cannot redirect package selection outside this repository");
      var handler=Text(Manifest(digest:digest));using(var http=new HttpClient(handler))using(var u=new AppUpdates(Path.Combine(root,"checks"),current,http)){
        check(u.Due(DateTime.UtcNow)&&u.Latest==null&&u.Status=="尚未检查","A first launch needs a check and never claims an unknown version is current");
        await u.CheckAsync();await u.CheckAsync();check(handler.Calls==1&&u.Available&&u.LastCheckedUtc!=null&&!u.Due(DateTime.UtcNow),"Automatic checks persist their result and are throttled to once per six hours");
        await u.CheckAsync(true);check(handler.Calls==2&&handler.Anonymous,"Manual update checks bypass the schedule without sending account credentials or cookies");
        using(var cached=new AppUpdates(Path.Combine(root,"checks"),current,http))check(cached.Available&&cached.Latest.tag=="v9.0.0"&&!cached.Due(DateTime.UtcNow),"Cached update information and the check interval survive a restart");
        handler.Reply=(r,t)=>Task.FromResult(Response("offline",HttpStatusCode.ServiceUnavailable));await u.CheckAsync(true);check(u.Error!=null&&u.Available&&u.Latest.tag=="v9.0.0","A failed refresh preserves a previously discovered update and displays an error");
        using(var restored=new AppUpdates(Path.Combine(root,"checks"),current,http))check(restored.Error!=null&&restored.Available,"A restart preserves the last failed check instead of falsely presenting a successful status");
      }
      using(var http=new HttpClient(Text(Manifest("v2.6.0",digest))))using(var u=new AppUpdates(Path.Combine(root,"ahead"),current,http)){await u.CheckAsync(true);check(!u.Available&&u.Status.Contains("当前版本较新"),"An installed build newer than GitHub's release never offers a downgrade");}
      using(var http=new HttpClient(Text(Manifest("v2.7.0",digest))))using(var u=new AppUpdates(Path.Combine(root,"same"),current,http)){await u.CheckAsync(true);check(!u.Available&&u.Status=="已是最新正式版","An equal official version reports that the installed build is current");}
      var failure=new Handler{Reply=(r,t)=>{throw new HttpRequestException("fixture offline");}};using(var http=new HttpClient(failure))using(var u=new AppUpdates(Path.Combine(root,"offline"),current,http)){await u.CheckAsync(true);check(u.Error!=null&&u.LastCheckedUtc==null&&u.Latest==null&&!u.Status.Contains("已是最新"),"Offline checks never falsely report that the installed build is latest");}
      var gate=new TaskCompletionSource<HttpResponseMessage>();var overlap=new Handler{Reply=(r,t)=>gate.Task};using(var http=new HttpClient(overlap))using(var u=new AppUpdates(Path.Combine(root,"overlap"),current,http)){var pending=u.CheckAsync(true);await u.CheckAsync(true);gate.SetResult(Response(Manifest(digest:digest)));await pending;check(overlap.Calls==1&&!u.Checking,"Overlapping manual and automatic checks share one in-flight request");}
      var package=new Handler{Reply=(r,t)=>Task.FromResult(r.RequestUri.AbsoluteUri==AppUpdates.Api?Response(Manifest(digest:digest)):new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})};
      using(var http=new HttpClient(package))using(var u=new AppUpdates(Path.Combine(root,"download"),current,http)){
        await u.CheckAsync(true);string path=await u.DownloadAsync();check(path!=null&&File.ReadAllText(path)=="hello"&&u.Progress==100&&Directory.GetFiles(Path.GetDirectoryName(path),"*.part").Length==0,"In-app downloads stream to a temporary file and publish only a verified complete package");
        int calls=package.Calls;check(await u.DownloadAsync()==path&&package.Calls==calls,"A previously downloaded package is reverified and reused without another download");
      }
      var bad=new Handler{Reply=(r,t)=>Task.FromResult(r.RequestUri.AbsoluteUri==AppUpdates.Api?Response(Manifest(digest:new string('0',64))):new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})};
      using(var http=new HttpClient(bad))using(var u=new AppUpdates(Path.Combine(root,"bad-digest"),current,http)){await u.CheckAsync(true);var path=await u.DownloadAsync();check(path==null&&u.Error.Contains("校验失败")&&Directory.GetFiles(Path.Combine(root,"bad-digest","updates")).Length==0,"A hash mismatch deletes the incomplete download and never exposes it as a usable update");}
      var sums=new Handler{Reply=(r,t)=>Task.FromResult(r.RequestUri.AbsoluteUri==AppUpdates.Api?Response(Manifest()):r.RequestUri.AbsoluteUri.EndsWith("SHA256SUMS.txt")?Response(digest+"  Token-Grove-v9.0.0-Windows-x64.zip\n"):new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})};
      using(var http=new HttpClient(sums))using(var u=new AppUpdates(Path.Combine(root,"sums"),current,http)){await u.CheckAsync(true);check(await u.DownloadAsync()!=null&&sums.Calls==3,"Older GitHub release assets can be verified using the matching SHA256SUMS entry");}
      var redirect=new Handler{Reply=(r,t)=>Task.FromResult(r.RequestUri.AbsoluteUri==AppUpdates.Api?Response(Manifest(digest:digest)):new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new Uri("http://example.com/package.zip")}})};
      using(var http=new HttpClient(redirect))using(var u=new AppUpdates(Path.Combine(root,"redirect"),current,http)){await u.CheckAsync(true);check(await u.DownloadAsync()==null&&redirect.Calls==2,"Download redirects reject insecure or unrelated destinations before sending a request");}
      var shortFile=new Handler{Reply=(r,t)=>Task.FromResult(r.RequestUri.AbsoluteUri==AppUpdates.Api?Response(Manifest(digest:digest,size:50)):new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})};
      using(var http=new HttpClient(shortFile))using(var u=new AppUpdates(Path.Combine(root,"short"),current,http)){await u.CheckAsync(true);check(await u.DownloadAsync()==null&&u.DownloadedFile==null,"Truncated or incorrectly sized packages are never presented as completed downloads");}
      string corrupt=Path.Combine(root,"corrupt");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"updates.json"),"{broken");using(var http=new HttpClient(Text(Manifest())))using(var u=new AppUpdates(corrupt,current,http))check(u.Latest==null&&u.Due(DateTime.UtcNow),"A corrupt update cache falls back to a fresh check without touching plans or preferences");
      var waiting=new Handler{Reply=async(r,t)=>{await Task.Delay(Timeout.Infinite,t);return Response(Manifest());}};using(var http=new HttpClient(waiting)){var u=new AppUpdates(Path.Combine(root,"cancelled"),current,http);var pending=u.CheckAsync(true);u.Dispose();await pending;using(var restarted=new AppUpdates(Path.Combine(root,"cancelled"),current,http))check(!u.Checking&&restarted.Due(DateTime.UtcNow),"Exiting during a check cancels the request and does not postpone the next launch's check");}
    }
  }
}
