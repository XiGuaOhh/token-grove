using System;
using System.Runtime.InteropServices;

namespace TokenGrove {
  // A short precision lease exists only while visible UI is animating.
  public static class FramePacing {
    [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint milliseconds);
    static int users;static bool requested;
    public const int FrameMilliseconds=15;
    public static int ActiveLeases{get{return users;}}
    public static void Acquire(){if(users++!=0)return;try{requested=timeBeginPeriod(1)==0;}catch{requested=false;}}
    public static void Release(){if(users<=0)return;if(--users!=0)return;if(requested){try{timeEndPeriod(1);}catch{}requested=false;}}
  }
}
