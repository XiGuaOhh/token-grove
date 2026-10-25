using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TokenGrove {
  internal static class Native {
    [DllImport("user32.dll")]public static extern bool ReleaseCapture();
    [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,int w,int l);
    [DllImport("user32.dll")]public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
  }
  static class Program {
    [STAThread]static void Main(string[] args){
      try{
        if(args.Length>=3&&args[0]=="--query"){Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),new System.Text.UTF8Encoding(false)){AutoFlush=true});var start=DateTimeOffset.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeLocal).UtcDateTime;var end=DateTimeOffset.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeLocal).UtcDateTime;var reader=new DataCollector();reader.RefreshUsageAsync().GetAwaiter().GetResult();var query=UsageEngine.Query(reader.UsageRecords,start,end,args.Length>3?args[3]:null,args.Length>4?args[4]:null);Console.WriteLine(J.Serializer().Serialize(new{fromUtc=J.Iso(query.fromUtc),toUtc=J.Iso(query.toUtc),totalTokens=query.totals.total,query.records,query.models,query.daily}));return;}
        if(args.Length>0){Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),new System.Text.UTF8Encoding(false)){AutoFlush=true});if(args[0]=="--self-test"){SelfTests.Run();return;}if(args[0]=="--claude-json"){Console.WriteLine(J.Serializer().Serialize(DataCollector.ReadClaudeAsync().GetAwaiter().GetResult()));return;}if(args[0]=="--snapshot"){var d=new DataCollector().CollectAsync(true).GetAwaiter().GetResult();var json=J.Serializer().Serialize(d);if(args.Length>1)File.WriteAllText(args[1],json,new System.Text.UTF8Encoding(false));else Console.WriteLine(json);return;}if(args[0]=="--validate-snapshot"&&args.Length>1){ImportExport.LoadSnapshot(args[1]);Console.WriteLine("OK");return;}}
        bool created;using(var mutex=new Mutex(true,"Local\\TokenGroveDesktop-v2",out created))using(var activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\TokenGroveDesktopActivate-v2")){if(!created){activate.Set();return;}
          try{Native.SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{Native.SetProcessDPIAware();}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.ThreadException+=(s,e)=>MessageBox.Show("小站遇到了一个问题，请重新打开。\n"+e.Exception.Message,"Token Grove");var context=new GroveContext(args);var registered=ThreadPool.RegisterWaitForSingleObject(activate,(s,t)=>{try{context.Main.BeginInvoke(new Action(()=>context.ShowMain()));}catch{}},null,Timeout.Infinite,false);try{Application.Run(context);}finally{registered.Unregister(null);}
        }
      }catch(Exception error){if(args.Length>0){Console.Error.WriteLine(error.GetType().Name+": "+error.Message);Environment.ExitCode=1;}else MessageBox.Show("未能打开额度小站："+error.Message,"Token Grove");}
    }
  }
}
