using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PriParaCoordRemote {
 public sealed class Outfit {
  public int top,bottom,shoes,head;
  public int[] Values(){return new[]{top,bottom,shoes,head};}
  public Outfit Copy(){return new Outfit{top=top,bottom=bottom,shoes=shoes,head=head};}
 }
 public interface IMemory : IDisposable {
  byte[] Read(uint address,int length);
  void Write(uint address,byte[] bytes);
 }
 public sealed class MemoryFailure:Exception {public MemoryFailure(string message):base(message){} }
 public static class Memory {
  public static uint U32(this IMemory m,uint a){return BitConverter.ToUInt32(m.Read(a,4),0);}
  public static byte Byte(this IMemory m,uint a){return m.Read(a,1)[0];}
  public static string Kind(this IMemory m,uint a,uint imageBase,uint imageSize){
   if(a<0x10000)return "";
   try{
    uint vt=m.U32(a);if(vt<imageBase||vt>=imageBase+imageSize)return "";
    uint col=m.U32(vt-4);if(col<imageBase||col>=imageBase+imageSize)return "";
    uint td=m.U32(col+12);if(td<imageBase||td>=imageBase+imageSize)return "";
    byte[] b=m.Read(td+8,160);int z=Array.IndexOf(b,(byte)0);if(z<0)z=b.Length;
    return Encoding.ASCII.GetString(b,0,z);
   }catch{return "";}
  }
 }
 public sealed class NativeMemory:IMemory {
  IntPtr handle;
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,UIntPtr n,out UIntPtr got);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteProcessMemory(IntPtr h,IntPtr a,byte[] b,UIntPtr n,out UIntPtr written);
  public NativeMemory(int pid,bool writable){handle=OpenProcess(writable?0x438u:0x410u,false,pid);if(handle==IntPtr.Zero)throw new MemoryFailure("게임에 연결할 수 없어요. 게임과 프로그램을 관리자 권한으로 실행해 주세요.");}
  public byte[] Read(uint a,int n){
   if(a<0x10000||n<0||n>8000000)throw new MemoryFailure("게임의 데이터 위치를 확인할 수 없어요.");
   byte[] b=new byte[n];if(n==0)return b;UIntPtr got;
   if(!ReadProcessMemory(handle,new IntPtr((long)a),b,(UIntPtr)(uint)n,out got)||got.ToUInt64()!=(ulong)n)throw new MemoryFailure("게임 상태가 바뀌었어요. 코디 화면에서 다시 선택해 주세요.");return b;
  }
  public void Write(uint a,byte[] b){UIntPtr n;if(!WriteProcessMemory(handle,new IntPtr((long)a),b,(UIntPtr)(uint)b.Length,out n)||n.ToUInt64()!=(ulong)b.Length)throw new MemoryFailure("코디를 전달하지 못했어요. 게임 화면을 확인해 주세요.");}
  public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
 }
 public sealed class GameContext {
  public int Pid;public uint Base,ImageSize,Manager,Scene,Resource,Debug,Control,My,Array,Database,Table,Count;
  public string Path;public int[] CurrentIds;public bool DebugEnabled;
  public bool Same(GameContext b){return Scene==b.Scene&&Resource==b.Resource&&Debug==b.Debug&&Control==b.Control&&My==b.My&&Array==b.Array&&Database==b.Database&&Table==b.Table&&Count==b.Count;}
 }
 public sealed class GameStatus {
  public bool Ready;public string Message;public int Pid;public int[] CurrentIds;public bool DebugEnabled;
 }
 public sealed class SendResult {
  public bool Accepted;public string Message;public int Pid;public int[] Ids;public uint[] Rows;public int WriteBytes;public string LogPath;public bool DebugEnabled;
 }
 public sealed class GameBridge {
  readonly Dictionary<int,OutfitItem> items;
  public GameBridge(Dictionary<int,OutfitItem> catalog){items=catalog;}
  public static GameContext Resolve(IMemory m,uint b,uint size){
   uint delta=b-0x400000;uint manager=m.U32(0x1600fa8+delta);uint scene=m.U32(manager+0x40);
   if(m.Kind(scene,b,size)!=".?AVSceneCoordinate@@")throw new MemoryFailure("게임에서 코디 화면을 열어 주세요.");
   uint res=m.U32(scene+0x48);
   if(m.Kind(res,b,size)!=".?AVCoordinateResource@@")throw new MemoryFailure("코디 화면을 준비하고 있어요. 잠시 뒤 다시 선택해 주세요.");
   uint debug=m.U32(res+0x418),control=m.U32(debug+4);
   byte debugFlag=m.Byte(control+0x28);
   if(debugFlag>1)throw new MemoryFailure("코디 화면 상태를 확인할 수 없어요. 잠시 뒤 다시 선택해 주세요.");
   if(m.U32(control)!=0)throw new MemoryFailure("코디 TYPE을 MyChara로 맞춰 주세요. Ctrl로 DEBUG를 켠 뒤 F11로 변경하고 다시 꺼도 됩니다.");
   uint first=m.U32(control+0x1c),last=m.U32(control+0x20);
   if(last<first||last-first!=12)throw new MemoryFailure("지원하는 코디 디버그 상태가 아니에요.");
   uint my=m.U32(first);
   if(m.Kind(my,b,size)!=".?AVCoordinateDebugMyChara@@"||m.U32(my+0x2c)!=4)throw new MemoryFailure("MyChara 코디 선택 화면을 확인해 주세요.");
   uint array=m.U32(my+0x24),db=m.U32(my+4);
   if(m.Kind(db,b,size)!=".?AVItemResource@@")throw new MemoryFailure("게임 의상 목록을 읽을 수 없어요.");
   uint header=m.U32(db+0xc),table=m.U32(db+0x10),count=m.U32(header+4);
   if(count==0||count>50000||array<0x10000||table<0x10000)throw new MemoryFailure("게임 의상 목록을 확인할 수 없어요.");
   return new GameContext{Base=b,ImageSize=size,Manager=manager,Scene=scene,Resource=res,Debug=debug,Control=control,My=my,Array=array,Database=db,Table=table,Count=count,DebugEnabled=debugFlag==1};
  }
  static GameContext OpenContext(Process p,IMemory m){
   ProcessModule module=p.MainModule;uint b=checked((uint)module.BaseAddress.ToInt64());string path=module.FileName;
   byte[] signature=m.Read(b+0x1c50a0,6);
   if(!signature.SequenceEqual(new byte[]{0x83,0xec,0x08,0xd9,0x44,0x24}))throw new MemoryFailure("게임의 코디 처리 버전이 달라 연결을 멈췄어요.");
   uint size=m.U32(b+m.U32(b+0x3c)+24+56);
   GameContext c=Resolve(m,b,size);c.Pid=p.Id;c.Path=path;return c;
  }
  static Process Find(){
   Process[] list=Process.GetProcessesByName("Prineo");
   if(list.Length==0)throw new MemoryFailure("게임 실행을 기다리고 있어요.");
   if(list.Length!=1){foreach(Process p in list)p.Dispose();throw new MemoryFailure("Prineo가 여러 개 켜져 있어요. 하나만 남겨 주세요.");}
   return list[0];
  }
  public GameStatus Inspect(){
   try{using(Process p=Find())using(IMemory m=new NativeMemory(p.Id,false)){
    GameContext c=OpenContext(p,m);int[] selected=new int[4];byte[] raw=m.Read(c.Array,16);
    for(int i=0;i<4;i++){uint row=BitConverter.ToUInt32(raw,i*4);selected[i]=row<c.Count?(int)m.U32(c.Table+row*144+0x68):0;}
    return new GameStatus{Ready=true,Message="게임 연결됨 · MyChara · DEBUG "+(c.DebugEnabled?"ON":"OFF"),Pid=p.Id,CurrentIds=selected,DebugEnabled=c.DebugEnabled};
   }}catch(Exception e){return new GameStatus{Ready=false,Message=Friendly(e)};}
  }
  public void Validate(Outfit outfit){
   int[] ids=outfit.Values();string[] slots={"top","bottom","shoes","head"};
   if(ids.All(x=>x==0))throw new MemoryFailure("입힐 의상을 한 부위 이상 선택해 주세요.");
   for(int i=0;i<4;i++){OutfitItem item;if(ids[i]!=0&&(!items.TryGetValue(ids[i],out item)||item.category!=slots[i]))throw new MemoryFailure("의상 부위가 맞지 않아요. 다시 선택해 주세요.");}
   if(outfit.top!=0&&items[outfit.top].isDress&&outfit.bottom!=0)throw new MemoryFailure("원피스에는 하의를 함께 선택할 수 없어요.");
  }
  public static uint[] MapRows(IMemory m,GameContext c,Outfit outfit,Dictionary<int,OutfitItem> items){
   byte[] table=m.Read(c.Table,checked((int)c.Count*144));int[] ids=outfit.Values();uint[] rows=new uint[4];byte[] categories={1,2,3,4};
   for(int k=0;k<4;k++){
    if(ids[k]==0){rows[k]=UInt32.MaxValue;continue;}
    OutfitItem item=items[ids[k]];int source=item.sourceRow;int found=-1;
    if(source>=0&&source<c.Count&&BitConverter.ToUInt32(table,source*144+0x68)==(uint)ids[k]&&table[source*144+0x78]==categories[k])found=source;
    if(found<0)for(int j=0;j<c.Count;j++)if(BitConverter.ToUInt32(table,j*144+0x68)==(uint)ids[k]&&table[j*144+0x78]==categories[k]){found=j;break;}
    if(found<0)throw new MemoryFailure("게임에 없는 의상이 포함되어 있어요: "+item.name);
    rows[k]=(uint)found;
   }return rows;
  }
  static bool EqualIds(int[] expected,int[] actual){return expected.Where(x=>x!=0).SequenceEqual(actual);}
  static int[] Pack(IMemory m,uint objectAddress){
   uint first=m.U32(objectAddress+0xc),last=m.U32(objectAddress+0x10);
   if(first==0||last<first||last-first>64||(last-first)%8!=0)return new int[0];
   byte[] b=m.Read(first,(int)(last-first));int[] result=new int[b.Length/8];for(int i=0;i<result.Length;i++)result[i]=(int)BitConverter.ToUInt32(b,i*8);return result;
  }
  public static SendResult Deliver(IMemory m,GameContext c,Outfit outfit,Dictionary<int,OutfitItem> items){
   GameContext now=Resolve(m,c.Base,c.ImageSize);if(!c.Same(now))throw new MemoryFailure("게임 화면이 바뀌었어요. 다시 선택해 주세요.");
   if(m.Byte(c.My+0x30)!=0)throw new MemoryFailure("이전 코디를 반영 중이에요. 잠시 기다려 주세요.");
   uint[] rows=MapRows(m,c,outfit,items);byte[] payload=new byte[16];for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes(rows[i]),0,payload,i*4,4);
   byte[] before=m.Read(c.Array,16);if(!c.Same(Resolve(m,c.Base,c.ImageSize)))throw new MemoryFailure("게임 화면이 바뀌었어요. 다시 선택해 주세요.");
   m.Write(c.Array,payload);
   if(!m.Read(c.Array,16).SequenceEqual(payload))throw new MemoryFailure("선택값을 확인할 수 없어 적용을 멈췄어요.");
   try{
    if(!c.Same(Resolve(m,c.Base,c.ImageSize)))throw new MemoryFailure("게임 화면이 바뀌어 적용을 멈췄어요.");
    m.Write(c.My+0x30,new byte[]{1});
   }catch{
    try{if(c.Same(Resolve(m,c.Base,c.ImageSize))&&m.Byte(c.My+0x30)==0)m.Write(c.Array,before);}catch{}throw;
   }
   Stopwatch watch=Stopwatch.StartNew();bool received=false;
   while(watch.ElapsedMilliseconds<2500){
    if(m.U32(c.Manager+0x40)!=c.Scene)break;
    if(m.Byte(c.My+0x30)==0&&EqualIds(outfit.Values(),Pack(m,c.Scene+0xc8))){received=true;break;}
    Thread.Sleep(15);
   }
   return new SendResult{Accepted=received,Message=received?"게임이 코디를 받았어요.":"선택값을 전달했어요. 게임 화면의 반영 상태를 확인해 주세요.",Pid=c.Pid,Ids=outfit.Values(),Rows=rows,WriteBytes=17,DebugEnabled=c.DebugEnabled};
  }
  public SendResult Send(Outfit outfit){
   Validate(outfit);using(Process p=Find())using(IMemory m=new NativeMemory(p.Id,true)){
    GameContext c=OpenContext(p,m);SendResult result=Deliver(m,c,outfit,items);
    try{
     string dir=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");Directory.CreateDirectory(dir);
     string log=System.IO.Path.Combine(dir,DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_pid"+c.Pid+".json");
     File.WriteAllText(log,new JavaScriptSerializer().Serialize(new{time=DateTimeOffset.Now.ToString("o"),pid=c.Pid,scene=c.Scene,array=c.Array,ids=result.Ids,rows=result.Rows,accepted=result.Accepted,debugEnabled=c.DebugEnabled,debugFlagWritten=false,bytesWritten=17,gameFileModified=false,codeInjection=false,remoteThreads=false}),Encoding.UTF8);result.LogPath=log;
    }catch{}return result;
   }
  }
  public static string Friendly(Exception e){
   if(e is MemoryFailure)return e.Message;
   return "게임에 연결하지 못했어요. 게임이 켜져 있는지 확인해 주세요.";
  }
 }
}
