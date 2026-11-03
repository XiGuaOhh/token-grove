using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TokenGrove {
  public sealed class AppRelease {
    public string tag,notes,pageUrl,assetName,assetUrl,checksumUrl,digest;public long assetSize;
    [ScriptIgnore]public Version Version{get{return AppUpdates.ParseVersion(tag);}}
    [ScriptIgnore]public bool Downloadable{get{return assetName=="Token-Grove-"+tag+"-Windows-x64.zip"&&AppUpdates.AssetUrl(assetUrl,tag,assetName)&&assetSize>0&&assetSize<=AppUpdates.MaxPackageBytes&&(AppUpdates.ValidDigest(digest)||AppUpdates.AssetUrl(checksumUrl,tag,"SHA256SUMS.txt"));}}
  }
  sealed class UpdateCache {public int version=1;public string lastAttemptUtc,lastCheckedUtc,lastError;public AppRelease release;}
  public sealed class AppUpdates:IDisposable {
    public const string Repository="https://github.com/XiGuaOhh/token-grove",Api="https://api.github.com/repos/XiGuaOhh/token-grove/releases/latest";
    public const long MaxPackageBytes=100*1024*1024;
    public static Version CurrentVersion{get{var v=Assembly.GetExecutingAssembly().GetName().Version;return new Version(v.Major,v.Minor,v.Build);}}
    public static string CurrentText{get{return CurrentVersion.ToString(3);}}
    public readonly Version Installed;readonly HttpClient http;readonly bool ownHttp;readonly string root,cachePath;readonly CancellationTokenSource cancel=new CancellationTokenSource();
    public event Action Changed;
    public AppRelease Latest{get;private set;}public bool Checking{get;private set;}public bool Downloading{get;private set;}
    public string Error{get;private set;}public string LastAttemptUtc{get;private set;}public string LastCheckedUtc{get;private set;}public string DownloadedFile{get;private set;}public int Progress{get;private set;}=-1;
    public bool Available{get{return Latest?.Version>Installed;}}
    public string Status{get{if(Downloading)return Progress<0?"正在下载新版…":"正在下载 "+Progress+"%";if(Checking)return "正在检查 GitHub…";if(Error!=null)return Error;if(Latest==null)return "尚未检查";if(Available)return "发现新版本 "+Latest.tag+(Latest.Downloadable?"":" · 下载包尚未就绪");return Latest.Version<Installed?"GitHub 已发布 "+Latest.tag+" · 当前版本较新":"已是最新正式版";}}
    public AppUpdates(string dataRoot,Version installed=null,HttpClient transport=null){root=Path.GetFullPath(dataRoot);cachePath=Path.Combine(root,"updates.json");Installed=installed??CurrentVersion;ownHttp=transport==null;http=transport??NewHttp();Load();}
    static HttpClient NewHttp(){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;return new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(60)};}
    public static Version ParseVersion(string tag){if(tag==null||!Regex.IsMatch(tag,@"^v?\d{1,5}\.\d{1,5}\.\d{1,5}$"))return null;Version v;return Version.TryParse(tag.TrimStart('v'),out v)?v:null;}
    public static bool ValidDigest(string value){return value!=null&&Regex.IsMatch(value,@"\A[0-9a-fA-F]{64}\z");}
    public static bool AssetUrl(string value,string tag,string name){return ParseVersion(tag)!=null&&value==Repository+"/releases/download/"+tag+"/"+name;}
    static bool TransportUrl(Uri uri){return uri!=null&&uri.Scheme=="https"&&uri.IsDefaultPort&&string.IsNullOrEmpty(uri.UserInfo)&&(uri.Host=="github.com"||uri.Host=="api.github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com");}
    static void Validate(AppRelease release){if(release==null||release.Version==null||release.pageUrl!=Repository+"/releases/tag/"+release.tag)throw new InvalidDataException("版本信息格式无效");if((release.notes??"").Length>6000)release.notes=release.notes.Substring(0,6000);if(!release.Downloadable){release.assetName=release.assetUrl=release.checksumUrl=release.digest=null;release.assetSize=0;}}
    public static AppRelease ParseRelease(string json){var data=J.Parse(json);if(J.Get(data,"draft") is bool&&(bool)J.Get(data,"draft")||J.Get(data,"prerelease") is bool&&(bool)J.Get(data,"prerelease"))throw new InvalidDataException("不是正式版本");
      var release=new AppRelease{tag=J.Str(J.Get(data,"tag_name")),notes=J.Str(J.Get(data,"body")),pageUrl=J.Str(J.Get(data,"html_url"))};string name="Token-Grove-"+release.tag+"-Windows-x64.zip";
      var assets=J.Items(J.Get(data,"assets"));var asset=assets.FirstOrDefault(a=>J.Str(J.Get(a,"name"))==name&&J.Str(J.Get(a,"state"))=="uploaded");var sums=assets.FirstOrDefault(a=>J.Str(J.Get(a,"name"))=="SHA256SUMS.txt"&&J.Str(J.Get(a,"state"))=="uploaded");
      release.assetName=name;release.assetUrl=J.Str(J.Get(asset,"browser_download_url"));release.assetSize=J.Count(J.Get(asset,"size"));release.checksumUrl=J.Str(J.Get(sums,"browser_download_url"));var digest=J.Str(J.Get(asset,"digest"));release.digest=digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)?digest.Substring(7):null;Validate(release);return release;
    }
    void Load(){try{if(!File.Exists(cachePath)||new FileInfo(cachePath).Length>1024*1024)return;var cache=J.Serializer().Deserialize<UpdateCache>(J.Read(cachePath));if(cache?.version!=1)return;if(cache.release!=null)Validate(cache.release);Latest=cache.release;LastAttemptUtc=J.Time(cache.lastAttemptUtc).HasValue?cache.lastAttemptUtc:null;LastCheckedUtc=J.Time(cache.lastCheckedUtc).HasValue?cache.lastCheckedUtc:null;Error=string.IsNullOrEmpty(cache.lastError)?null:cache.lastError.Substring(0,Math.Min(200,cache.lastError.Length));}catch{}}
    void Save(){try{J.Write(cachePath,new UpdateCache{lastAttemptUtc=LastAttemptUtc,lastCheckedUtc=LastCheckedUtc,lastError=Error,release=Latest});}catch{/* Cache failures do not turn a successful network check into a false result. */}}
    void Signal(){if(!cancel.IsCancellationRequested)Changed?.Invoke();}
    public bool Due(DateTime now){var last=J.Time(LastAttemptUtc);return !Checking&&!Downloading&&(!last.HasValue||now-last.Value>=TimeSpan.FromHours(6)||last.Value>now.AddMinutes(5));}
    async Task<HttpResponseMessage> Get(string url,CancellationToken token){var uri=new Uri(url);for(int i=0;i<5;i++){
      if(!TransportUrl(uri))throw new InvalidDataException("下载地址无效");using(var request=new HttpRequestMessage(HttpMethod.Get,uri)){request.Headers.UserAgent.ParseAdd("TokenGrove/"+CurrentText);request.Headers.TryAddWithoutValidation("Accept",uri.Host=="api.github.com"?"application/vnd.github+json":"application/octet-stream");
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);int code=(int)response.StatusCode;if(code>=300&&code<=399){var next=response.Headers.Location;response.Dispose();if(next==null)throw new IOException("下载跳转无效");uri=next.IsAbsoluteUri?next:new Uri(uri,next);continue;}if(!response.IsSuccessStatusCode){response.Dispose();if(code==403||code==429)throw new IOException("GitHub 请求较频繁，请稍后重试");if(code==404)throw new IOException("GitHub 暂无可读取的正式版本");throw new IOException("GitHub 暂时无法连接，请稍后重试");}return response;}}
      throw new IOException("下载跳转次数过多");
    }
    async Task<byte[]> Body(string url,int limit,CancellationToken token){using(var response=await Get(url,token))using(token.Register(()=>response.Dispose())){if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("版本信息过大");using(var stream=await response.Content.ReadAsStreamAsync())using(var output=new MemoryStream()){var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,token))>0){if(output.Length+n>limit)throw new InvalidDataException("版本信息过大");output.Write(buffer,0,n);}return output.ToArray();}}}
    public async Task CheckAsync(bool force=false){if(Checking||Downloading||cancel.IsCancellationRequested||!force&&!Due(DateTime.UtcNow))return;var previousAttempt=LastAttemptUtc;Checking=true;Error=null;LastAttemptUtc=J.Iso(DateTime.UtcNow);Signal();
      try{using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){timeout.CancelAfter(TimeSpan.FromSeconds(15));var release=ParseRelease(Encoding.UTF8.GetString(await Body(Api,1024*1024,timeout.Token)));if(Latest?.tag!=release.tag||Latest?.digest!=release.digest||Latest?.assetSize!=release.assetSize)DownloadedFile=null;Latest=release;LastCheckedUtc=J.Iso(DateTime.UtcNow);Save();}}
      catch(OperationCanceledException){if(!cancel.IsCancellationRequested)Error="连接 GitHub 超时，点击检查重试";else LastAttemptUtc=previousAttempt;}
      catch(InvalidDataException e){Error=e.Message;}
      catch(IOException e){Error=e.Message;}
      catch{Error="未能读取 GitHub 更新信息，点击检查重试";}
      finally{Checking=false;Save();Signal();}
    }
    public static string Checksum(string text,string name){foreach(var line in text.Split('\n')){var m=Regex.Match(line.Trim(),@"\A([0-9a-fA-F]{64})\s+\*?(.+)\z");if(m.Success&&m.Groups[2].Value==name)return m.Groups[1].Value.ToLowerInvariant();}throw new InvalidDataException("未找到对应版本的校验值");}
    static string Hash(string file){using(var input=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    public async Task<string> DownloadAsync(){if(!Available||!Latest.Downloadable||Checking||Downloading||cancel.IsCancellationRequested)return null;var release=Latest;Downloading=true;Progress=-1;Error=null;Signal();string temp=null;
      try{using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){timeout.CancelAfter(TimeSpan.FromMinutes(3));var token=timeout.Token;string digest=ValidDigest(release.digest)?release.digest.ToLowerInvariant():Checksum(Encoding.UTF8.GetString(await Body(release.checksumUrl,65536,token)),release.assetName);
        var directory=Path.Combine(root,"updates");Directory.CreateDirectory(directory);var destination=Path.Combine(directory,release.assetName);
        if(File.Exists(destination)&&new FileInfo(destination).Length==release.assetSize&&await Task.Run(()=>Hash(destination),token)==digest){DownloadedFile=destination;Progress=100;return destination;}
        temp=destination+"."+Guid.NewGuid().ToString("N")+".part";
        using(var response=await Get(release.assetUrl,token))using(token.Register(()=>response.Dispose())){if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=release.assetSize)throw new InvalidDataException("下载大小不符，请重新检查版本");
          using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){var buffer=new byte[65536];int n;long received=0;while((n=await input.ReadAsync(buffer,0,buffer.Length,token))>0){received+=n;if(received>release.assetSize)throw new InvalidDataException("下载大小不符");await output.WriteAsync(buffer,0,n,token);int percent=(int)(received*100/release.assetSize);if(percent!=Progress){Progress=percent;Signal();}}if(received!=release.assetSize)throw new InvalidDataException("下载未完成，请重试");}}
        if(await Task.Run(()=>Hash(temp),token)!=digest)throw new InvalidDataException("下载校验失败，请重试");if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);temp=null;DownloadedFile=destination;Progress=100;return destination;}}
      catch(OperationCanceledException){if(!cancel.IsCancellationRequested)Error="下载超时，可点击重试";return null;}
      catch(InvalidDataException e){Error=e.Message;return null;}
      catch(IOException e){Error=e.Message;return null;}
      catch{Error="下载未完成，请稍后重试";return null;}
      finally{if(temp!=null)try{File.Delete(temp);}catch{}Downloading=false;Signal();}
    }
    public void Dispose(){cancel.Cancel();if(ownHttp)http.Dispose();}
  }
}
