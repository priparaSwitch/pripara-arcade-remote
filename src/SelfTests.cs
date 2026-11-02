using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PriParaCoordRemote {
 public static class SelfTests {
  sealed class FakeMemory:IMemory {
   readonly Dictionary<uint,byte> memory=new Dictionary<uint,byte>();
   public readonly List<Tuple<uint,byte[]>> Writes=new List<Tuple<uint,byte[]>>();
   public const uint Manager=0x10000000,Scene=0x10001000,Resource=0x10002000,Debug=0x10003000,Control=0x10004000,Vector=0x10005000,My=0x10006000,Array=0x10007000,Database=0x10008000,Header=0x10009000,Table=0x11000000,Pack=0x12000000;
   public uint Base;public bool ChangeSceneAfterRows;
   public FakeMemory(Catalog catalog,uint imageBase){
    Base=imageBase;Set(0x1600fa8+imageBase-0x400000,Manager);Set(Manager+0x40,Scene);
    Kind(Scene,".?AVSceneCoordinate@@",0);Kind(Resource,".?AVCoordinateResource@@",1);Kind(My,".?AVCoordinateDebugMyChara@@",2);Kind(Database,".?AVItemResource@@",3);
    Set(Scene+0x48,Resource);Set(Resource+0x418,Debug);Set(Debug+4,Control);Set(Control,0);Poke(Control+0x28,new byte[]{1});
    Set(Control+0x1c,Vector);Set(Control+0x20,Vector+12);Set(Vector,My);Set(My+0x24,Array);Set(My+0x2c,4);Set(My+4,Database);
    Set(Database+0xc,Header);Set(Database+0x10,Table);Set(Header+4,6249);
    foreach(OutfitItem item in catalog.Data.items){uint r=Table+(uint)item.sourceRow*144;Set(r+0x68,(uint)item.id);Poke(r+0x78,new[]{Category(item.category)});}
   }
   public static byte Category(string s){return (byte)(s=="top"?1:s=="bottom"?2:s=="shoes"?3:4);}
   public void Kind(uint obj,string kind,int n){uint vt=Base+0xd80000+(uint)n*0x200,col=vt+0x40,td=vt+0x80;Set(obj,vt);Set(vt-4,col);Set(col+12,td);Poke(td+8,Encoding.ASCII.GetBytes(kind+"\0"));}
   public void Set(uint address,uint value){Poke(address,BitConverter.GetBytes(value));}
   public void Poke(uint address,byte[] value){for(int i=0;i<value.Length;i++)memory[address+(uint)i]=value[i];}
   public byte[] Read(uint address,int length){byte[] b=new byte[length];for(int i=0;i<length;i++){byte value;if(memory.TryGetValue(address+(uint)i,out value))b[i]=value;}return b;}
   public void Write(uint address,byte[] value){
    Writes.Add(Tuple.Create(address,(byte[])value.Clone()));Poke(address,value);
    if(address==Array&&ChangeSceneAfterRows)Set(Manager+0x40,0x13000000);
    if(address==My+0x30&&value.Length==1&&value[0]==1){
     int count=0;for(int i=0;i<4;i++){uint row=this.U32(Array+(uint)i*4);if(row!=UInt32.MaxValue){Set(Pack+(uint)count*8,this.U32(Table+row*144+0x68));count++;}}
     Set(Scene+0xc8+0xc,Pack);Set(Scene+0xc8+0x10,Pack+(uint)count*8);Poke(My+0x30,new byte[]{0});
    }
   }
   public GameContext Context(){return GameBridge.Resolve(this,Base,0x2100000);}
   public void Dispose(){}
  }
  static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
  static void Rejected(Action action){bool failed=false;try{action();}catch(MemoryFailure){failed=true;}Assert(failed,"Expected a guarded rejection");}
  public static void Run(Catalog catalog,string output){
   List<string> passed=new List<string>();Action<string,Action> test=(name,action)=>{action();passed.Add(name);};
   Outfit twinkle=new Outfit{top=7106,shoes=7307,head=7412};GameBridge bridge=new GameBridge(catalog.Items);
   test("arcade-only unique catalog and approved set slot integrity",()=>{
    Assert(catalog.Data.items.Count==6229&&catalog.Data.sets.Count==823,"Unexpected prepared catalog");
    foreach(OutfitSet set in catalog.Data.sets)bridge.Validate(catalog.FromSet(set));
    Assert(catalog.Items[7106].sourceRow==1760&&catalog.Items[7307].sourceRow==1790&&catalog.Items[7412].sourceRow==1819,"Arcade row provenance mismatch");
    Assert(catalog.Data.sets.Any(s=>s.pieces["top"]==7106&&s.pieces["shoes"]==7307&&s.pieces["head"]==7412),"Twinkle set missing");
   Assert(catalog.Data.items.Count(x=>x.hidden)==174&&catalog.Items[47000].hidden&&!catalog.Items[4446].hidden,"Audited set representatives were not filtered precisely");
   });
   test("favorite keys persist outside the EXE and item database",()=>{string folder=Path.Combine(Path.GetTempPath(),"coord-favorites-test-"+Guid.NewGuid().ToString("N"));try{string file=Path.Combine(folder,"favorites.json");var a=new Favorites(file);a.Toggle("i7106");var b=new Favorites(file);Assert(b.Contains("i7106"),"Favorite did not persist");b.Toggle("i7106");Assert(!new Favorites(file).Contains("i7106"),"Favorite removal did not persist");}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}});
   test("optional external artwork never blocks outfit data",()=>{foreach(int id in new[]{7106,7307,7412}){var image=catalog.Art(id);if(!catalog.ImagesAvailable)Assert(image==null,"Artwork present without an image pack");if(image!=null)Assert(image.Width>0&&image.Height>0,"Invalid thumbnail "+id);}});
   test("invalid slot, empty outfit and dress plus skirt rejected",()=>{
    Rejected(()=>bridge.Validate(new Outfit()));Rejected(()=>bridge.Validate(new Outfit{top=7307}));
    Rejected(()=>bridge.Validate(new Outfit{top=7106,bottom=catalog.Data.items.First(x=>x.category=="bottom").id}));
   });
   foreach(uint imageBase in new uint[]{0x400000,0xd10000})foreach(bool debugEnabled in new[]{false,true}){
    uint b=imageBase;bool enabled=debugEnabled;test("17-byte send and receipt with base "+b.ToString("X")+" DEBUG "+(enabled?"ON":"OFF"),()=>{
     using(FakeMemory m=new FakeMemory(catalog,b)){
      m.Poke(FakeMemory.Control+0x28,new byte[]{(byte)(enabled?1:0)});
      SendResult result=GameBridge.Deliver(m,m.Context(),twinkle,catalog.Items);
      Assert(result.Accepted&&result.Rows.SequenceEqual(new uint[]{1760,UInt32.MaxValue,1790,1819}),"ID to native row mapping failed");
      Assert(m.Writes.Count==2&&m.Writes[0].Item1==FakeMemory.Array&&m.Writes[0].Item2.Length==16&&m.Writes[1].Item1==FakeMemory.My+0x30&&m.Writes[1].Item2.SequenceEqual(new byte[]{1}),"Write boundaries/order changed");
      Assert(m.Byte(FakeMemory.Control+0x28)==(enabled?1:0)&&result.DebugEnabled==enabled,"DEBUG flag changed");
     }
    });
   }
   test("invalid debug flag has zero writes",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.Poke(FakeMemory.Control+0x28,new byte[]{2});Rejected(()=>GameBridge.Deliver(m,c,twinkle,catalog.Items));Assert(m.Writes.Count==0,"Wrote in an invalid context");}});
   test("other character type has zero writes",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.Set(FakeMemory.Control,1);Rejected(()=>GameBridge.Deliver(m,c,twinkle,catalog.Items));Assert(m.Writes.Count==0,"Wrote in other character type");}});
   test("other scene has zero writes",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.Kind(FakeMemory.Scene,".?AVSceneShop3rd@@",0);Rejected(()=>GameBridge.Deliver(m,c,twinkle,catalog.Items));Assert(m.Writes.Count==0,"Wrote in other scene");}});
   test("pending request has zero writes",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.Poke(FakeMemory.My+0x30,new byte[]{1});Rejected(()=>GameBridge.Deliver(m,c,twinkle,catalog.Items));Assert(m.Writes.Count==0,"Overwrote pending request");}});
   test("missing runtime item has zero writes",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.Set(FakeMemory.Table+1760*144+0x68,0);Rejected(()=>GameBridge.Deliver(m,c,twinkle,catalog.Items));Assert(m.Writes.Count==0,"Wrote invalid table row");}});
   test("runtime row relocation resolves by ID and slot",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.Set(FakeMemory.Table+1760*144+0x68,0);m.Set(FakeMemory.Table+7*144+0x68,7106);m.Poke(FakeMemory.Table+7*144+0x78,new byte[]{1});Assert(GameBridge.MapRows(m,c,twinkle,catalog.Items)[0]==7,"Row index was hard coded");Assert(m.Writes.Count==0,"Mapping mutated process");}});
   test("transition after row write never sends apply flag",()=>{using(FakeMemory m=new FakeMemory(catalog,0x400000)){GameContext c=m.Context();m.ChangeSceneAfterRows=true;Rejected(()=>GameBridge.Deliver(m,c,twinkle,catalog.Items));Assert(m.Writes.Count==1&&m.Writes[0].Item1==FakeMemory.Array,"Apply sent after scene transition");}});
   test("interface search, pagination, set selection and slot rules",()=>{using(MainForm form=new MainForm(catalog,true))form.CheckInterface();});
   File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=true,count=passed.Count,checks=passed,scope="Isolated memory fixture and own application UI. Not a new live game test."}),Encoding.UTF8);
  }
 }
}
