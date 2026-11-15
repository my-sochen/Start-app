using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
// Test-only creation-time containment, not product behavior. Never arbitrary PID kill.
public sealed class OwnedTree {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct SI { public int cb; public string reserved,desktop,title; public int x,y,xsize,ysize,xc,yc,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error; }
 [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr process,thread; public int pid,tid; }
 [StructLayout(LayoutKind.Sequential)] struct Basic { public long perProcess,perJob; public uint flags; public UIntPtr min,max; public uint active; public UIntPtr affinity; public uint priority,scheduling; }
 [StructLayout(LayoutKind.Sequential)] struct IO { public ulong a,b,c,d,e,f; }
 [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic basic; public IO io; public UIntPtr a,b,c,d; }
 [StructLayout(LayoutKind.Sequential)] struct Accounting { public long a,b,c,d; public uint e,total,active,terminated; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessW(string app,StringBuilder cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref SI si,out PI pi);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateJobObjectW(IntPtr a,IntPtr b);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr j,int info,ref Extended x,int size);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr j,int info,out Accounting x,int size,IntPtr length);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr j,IntPtr p);
 [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr t);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr p,uint c);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr j,uint c);
 [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr p,uint time);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 IntPtr job;
 public Process Process {get;private set;}
 static void Check(bool ok){if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error());}
 public static OwnedTree Start(string path,string cwd){
  var tree=new OwnedTree(); PI pi=new PI();
  try {
   tree.job=CreateJobObjectW(IntPtr.Zero,IntPtr.Zero);Check(tree.job!=IntPtr.Zero);
   var limits=new Extended();limits.basic.flags=0x2000;
   Check(SetInformationJobObject(tree.job,9,ref limits,Marshal.SizeOf(typeof(Extended))));
   var si=new SI();si.cb=Marshal.SizeOf(typeof(SI));
   Check(CreateProcessW(path,new StringBuilder("\""+path+"\""),IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,cwd,ref si,out pi));
   Check(AssignProcessToJobObject(tree.job,pi.process));
   tree.Process=Process.GetProcessById(pi.pid); // child is suspended; PID cannot exit/reuse yet
   var retained=tree.Process.Handle;
   if(ResumeThread(pi.thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
   return tree;
  } catch {
   if(pi.process!=IntPtr.Zero){TerminateProcess(pi.process,91);WaitForSingleObject(pi.process,5000);}
   if(tree.job!=IntPtr.Zero)CloseHandle(tree.job);
   if(tree.Process!=null)tree.Process.Dispose();
   throw;
  } finally {if(pi.thread!=IntPtr.Zero)CloseHandle(pi.thread);if(pi.process!=IntPtr.Zero)CloseHandle(pi.process);}
 }
 public uint ActiveCount {
  get { Accounting a;Check(QueryInformationJobObject(job,1,out a,Marshal.SizeOf(typeof(Accounting)),IntPtr.Zero));return a.active; }
 }
 public void StopAndDispose(){
  if(job==IntPtr.Zero)return;
  try {
   Check(TerminateJobObject(job,92));
   bool empty=false;
   for(int i=0;i<100;i++){
    Accounting a;Check(QueryInformationJobObject(job,1,out a,Marshal.SizeOf(typeof(Accounting)),IntPtr.Zero));
    if(a.active==0){empty=true;break;}System.Threading.Thread.Sleep(50);
   }
   if(!empty)throw new Exception("Owned job still has active descendants after termination");
   if(!Process.WaitForExit(5000))throw new Exception("Owned launcher not reaped");
  } finally {CloseHandle(job);job=IntPtr.Zero;Process.Dispose();}
 }
}
