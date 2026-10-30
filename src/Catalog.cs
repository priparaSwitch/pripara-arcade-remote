using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace PriParaCoordRemote {
 public sealed class OutfitItem {
  public int id,brand,sourceRow;public string name,jpName,brandName,category,model,search;public bool isDress,thumbnail;
 }
 public sealed class OutfitSet {public string id,name,brandName,search;public int brand;public Dictionary<string,int> pieces;}
 public sealed class Bundle {public List<OutfitItem> items;public List<OutfitSet> sets;}

 public sealed class Catalog:IDisposable {
  public const string DatabaseFileName="data.db";
  public const string ArtworkFileName="images.bin";
  public readonly Bundle Data;
  public readonly Dictionary<int,OutfitItem> Items;
  public string DataVersion {get;private set;}
  public bool ImagesAvailable {get;private set;}
  public string ImageStatus {get;private set;}

  const int MaxCachedImages=320,MaxImageBytes=8*1024*1024,MaxImageSide=4096;
  readonly object artLock=new object();
  readonly Dictionary<int,Artwork> cache=new Dictionary<int,Artwork>();
  readonly LinkedList<int> recency=new LinkedList<int>();
  readonly HashSet<int> unavailable=new HashSet<int>();
  readonly Dictionary<int,ZipArchiveEntry> entries=new Dictionary<int,ZipArchiveEntry>();
  ZipArchive zip;
  bool disposed;
  sealed class Artwork {public Bitmap Image;public LinkedListNode<int> Node;}

  public Catalog():this(AppDomain.CurrentDomain.BaseDirectory){}
  public Catalog(string folder){
   if(String.IsNullOrWhiteSpace(folder))throw new ArgumentException("데이터 폴더를 지정해 주세요.","folder");
   string root=Path.GetFullPath(folder),database=Path.Combine(root,DatabaseFileName);
   if(!File.Exists(database))throw new InvalidDataException("프로그램과 같은 폴더에 '"+DatabaseFileName+"' 파일을 넣어 주세요.");
   Bundle data;Dictionary<int,OutfitItem> items;string version;
   try{LoadDatabase(database,out data,out items,out version);}
   catch(DllNotFoundException e){throw new InvalidDataException("Windows의 SQLite 구성 요소를 사용할 수 없어요. Windows 10 이상에서 실행해 주세요.",e);}
   catch(EntryPointNotFoundException e){throw new InvalidDataException("Windows의 SQLite 구성 요소를 사용할 수 없어요. Windows를 업데이트해 주세요.",e);}
   catch(BadImageFormatException e){throw new InvalidDataException("64비트 Windows의 SQLite 구성 요소를 사용할 수 없어요.",e);}
   Data=data;Items=items;DataVersion=version;
   OpenArtwork(Path.Combine(root,ArtworkFileName));
  }

  static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
  static bool Category(string value){return value=="top"||value=="bottom"||value=="shoes"||value=="head";}
  static string RequiredText(string value,string field){
   Require(!String.IsNullOrWhiteSpace(value),"'"+field+"' 값이 없는 데이터예요.");return value;
  }
  static long CountMetadata(Dictionary<string,string> meta,string key){
   string text;long value;
   Require(meta.TryGetValue(key,out text)&&Int64.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value>=0,"데이터의 '"+key+"' 값이 올바르지 않아요.");
   // Definite assignment is explicit for the .NET Framework C# compiler.
   return Int64.Parse(text,CultureInfo.InvariantCulture);
  }
  static void LoadDatabase(string path,out Bundle data,out Dictionary<int,OutfitItem> items,out string version){
   data=new Bundle{items=new List<OutfitItem>(),sets=new List<OutfitSet>()};
   items=new Dictionary<int,OutfitItem>();version="";
   using(SqliteReadOnly db=new SqliteReadOnly(path)){
    Require(db.Integer("PRAGMA application_id;")==0x50435231,"코디 리모컨용 데이터 파일이 아니에요.");
    Require(db.Integer("PRAGMA user_version;")==1,"지원하지 않는 데이터 형식이에요. 프로그램과 데이터 버전을 확인해 주세요.");
    HashSet<string> tables=new HashSet<string>(StringComparer.Ordinal);
    using(SqliteRows rows=db.Query("SELECT name,type FROM sqlite_master WHERE name IN ('meta','items','sets');")){
     while(rows.Step()){string name=rows.Text(0,"table name");Require(rows.Text(1,"table type")=="table","필수 데이터 테이블의 형식이 올바르지 않아요.");tables.Add(name);}
    }
    Require(tables.SetEquals(new[]{"meta","items","sets"}),"필수 코디 데이터 테이블이 없어요.");
    Dictionary<string,string> meta=new Dictionary<string,string>(StringComparer.Ordinal);
    using(SqliteRows rows=db.Query("SELECT key,value FROM meta;")){
     while(rows.Step()){string key=rows.Text(0,"meta.key");Require(!meta.ContainsKey(key),"데이터의 설정 키가 중복돼요.");meta.Add(key,rows.Text(1,"meta.value"));}
    }
    string schema;
    Require(meta.TryGetValue("schema_version",out schema)&&schema=="1","지원하지 않는 데이터 형식이에요. 프로그램과 데이터 버전을 확인해 주세요.");
    Require(meta.TryGetValue("data_version",out version),"데이터 버전 정보가 없어요.");
    long itemCount=CountMetadata(meta,"item_count"),setCount=CountMetadata(meta,"set_count");
    CountMetadata(meta,"image_count"); // Diagnostic metadata; never a condition for optional artwork.
    using(SqliteRows rows=db.Query("SELECT id,name,jpName,brand,brandName,category,model,isDress,sourceRow,thumbnail,search FROM items ORDER BY id;")){
     while(rows.Step()){
      OutfitItem item=new OutfitItem{id=rows.Integer(0,"items.id"),name=rows.Text(1,"items.name"),jpName=rows.Text(2,"items.jpName"),
       brand=rows.Integer(3,"items.brand"),brandName=rows.Text(4,"items.brandName"),category=rows.Text(5,"items.category"),
       model=rows.Text(6,"items.model"),isDress=rows.Boolean(7,"items.isDress"),sourceRow=rows.Integer(8,"items.sourceRow"),
       thumbnail=rows.Boolean(9,"items.thumbnail"),search=rows.Text(10,"items.search")};
      Require(item.id>0&&!items.ContainsKey(item.id),"의상 ID가 올바르지 않거나 중복돼요.");
      Require(item.brand>=0&&item.sourceRow>=0,"의상의 브랜드 또는 원본 행 정보가 올바르지 않아요.");
      RequiredText(item.name,"items.name");
      Require(Category(item.category),"지원하지 않는 의상 부위가 있어요.");
      Require(!item.isDress||item.category=="top","원피스의 의상 부위가 올바르지 않아요.");
      items.Add(item.id,item);data.items.Add(item);
     }
    }
    Require(data.items.Count>0,"의상 목록이 비어 있어요.");
    HashSet<string> setIds=new HashSet<string>(StringComparer.Ordinal);
    using(SqliteRows rows=db.Query("SELECT id,name,brand,brandName,search,top,bottom,shoes,head FROM sets ORDER BY name,id;")){
     while(rows.Step()){
      OutfitSet set=new OutfitSet{id=rows.Text(0,"sets.id"),name=rows.Text(1,"sets.name"),brand=rows.Integer(2,"sets.brand"),
       brandName=rows.Text(3,"sets.brandName"),search=rows.Text(4,"sets.search"),pieces=new Dictionary<string,int>(StringComparer.Ordinal)};
      RequiredText(set.id,"sets.id");RequiredText(set.name,"sets.name");
      Require(setIds.Add(set.id)&&set.brand>=0,"세트 ID가 중복되거나 브랜드 정보가 올바르지 않아요.");
      string[] slots={"top","bottom","shoes","head"};bool any=false;
      for(int i=0;i<slots.Length;i++){
       int id=rows.Integer(5+i,"sets."+slots[i]);OutfitItem item;
       Require(id>=0,"세트에 음수 의상 ID가 있어요.");
       if(id!=0){Require(items.TryGetValue(id,out item)&&item.category==slots[i],"세트의 의상 ID 또는 부위가 목록과 맞지 않아요.");any=true;}
       set.pieces.Add(slots[i],id);
      }
      Require(any,"의상이 없는 세트가 있어요.");
      Require(set.pieces["top"]==0||!items[set.pieces["top"]].isDress||set.pieces["bottom"]==0,"원피스와 하의가 함께 지정된 세트가 있어요.");
      data.sets.Add(set);
     }
    }
    // Counts follow each replacement DB's own metadata; there are no fixed release totals or hashes.
    Require(data.items.Count==itemCount&&data.sets.Count==setCount,"데이터의 목록 개수와 버전 정보가 일치하지 않아요.");
   }
  }

  void OpenArtwork(string path){
   ImageStatus="이미지 파일 없음 · 목록만 표시해요.";
   if(!File.Exists(path))return;
   FileStream stream=null;
   try{
    stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
    zip=new ZipArchive(stream,ZipArchiveMode.Read,false);stream=null;
    foreach(ZipArchiveEntry entry in zip.Entries){
     string name=entry.FullName;int id;
     if(name.IndexOf('/')>=0||name.IndexOf('\\')>=0||!name.EndsWith(".png",StringComparison.OrdinalIgnoreCase))continue;
     if(!Int32.TryParse(name.Substring(0,name.Length-4),NumberStyles.None,CultureInfo.InvariantCulture,out id)||id<=0||!Items.ContainsKey(id))continue;
     if(!entries.ContainsKey(id))entries.Add(id,entry);
    }
    ImagesAvailable=entries.Count>0;
    ImageStatus=ImagesAvailable?"이미지 파일 연결됨":"이미지 파일에 표시할 그림이 없어요. 목록은 계속 사용할 수 있어요.";
   }catch(Exception e){
    if(!ArtworkFailure(e))throw;
    if(zip!=null){zip.Dispose();zip=null;}entries.Clear();ImagesAvailable=false;
    ImageStatus="이미지 파일을 읽지 못해 목록만 표시해요.";
   }finally{if(stream!=null)stream.Dispose();}
  }
  static bool ArtworkFailure(Exception e){
   return e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is ArgumentException||
    e is NotSupportedException||e is ExternalException||e is OutOfMemoryException||e is ObjectDisposedException;
  }
  static byte[] ReadImage(ZipArchiveEntry entry){
   if(entry.Length<24||entry.Length>MaxImageBytes)return null;
   using(Stream source=entry.Open())using(MemoryStream output=new MemoryStream()){
    byte[] buffer=new byte[8192];int count;
    while((count=source.Read(buffer,0,buffer.Length))>0){
     if(output.Length>MaxImageBytes-count)return null;
     output.Write(buffer,0,count);
    }
    return output.ToArray();
   }
  }
  static bool PngSize(byte[] bytes){
   byte[] signature={137,80,78,71,13,10,26,10};
   if(bytes==null||bytes.Length<24)return false;
   for(int i=0;i<signature.Length;i++)if(bytes[i]!=signature[i])return false;
   if(bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)return false;
   uint w=((uint)bytes[16]<<24)|((uint)bytes[17]<<16)|((uint)bytes[18]<<8)|bytes[19];
   uint h=((uint)bytes[20]<<24)|((uint)bytes[21]<<16)|((uint)bytes[22]<<8)|bytes[23];
   return w>0&&h>0&&w<=MaxImageSide&&h<=MaxImageSide;
  }
  public Image Art(int id){
   lock(artLock){
    if(disposed||id<=0||zip==null||unavailable.Contains(id))return null;
    Artwork found;
    if(cache.TryGetValue(id,out found)){recency.Remove(found.Node);recency.AddLast(found.Node);return found.Image;}
    ZipArchiveEntry entry;if(!entries.TryGetValue(id,out entry)){unavailable.Add(id);return null;}
    try{
     byte[] bytes=ReadImage(entry);
     if(!PngSize(bytes)){unavailable.Add(id);return null;}
     Bitmap image;
     using(MemoryStream source=new MemoryStream(bytes,false))using(Image loaded=Image.FromStream(source,false,true)){
      if(loaded.RawFormat.Guid!=ImageFormat.Png.Guid){unavailable.Add(id);return null;}
      image=new Bitmap(loaded);
     }
     LinkedListNode<int> node=recency.AddLast(id);cache.Add(id,new Artwork{Image=image,Node=node});
     while(cache.Count>MaxCachedImages){
      int old=recency.First.Value;recency.RemoveFirst();Artwork previous=cache[old];cache.Remove(old);previous.Image.Dispose();
     }
     return image;
    }catch(Exception e){
     if(!ArtworkFailure(e))throw;
     unavailable.Add(id);ImageStatus="일부 이미지를 표시할 수 없어요. 목록은 계속 사용할 수 있어요.";return null;
    }
   }
  }
  public Outfit FromSet(OutfitSet set){
   if(set==null)throw new ArgumentNullException("set");
   return new Outfit{top=set.pieces["top"],bottom=set.pieces["bottom"],shoes=set.pieces["shoes"],head=set.pieces["head"]};
  }
  public void Dispose(){
   lock(artLock){
    if(disposed)return;disposed=true;
    foreach(Artwork artwork in cache.Values)artwork.Image.Dispose();
    cache.Clear();recency.Clear();unavailable.Clear();entries.Clear();
    if(zip!=null){zip.Dispose();zip=null;}
    ImagesAvailable=false;
   }
  }

  sealed class SqliteReadOnly:IDisposable {
   IntPtr handle;
   static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
   public SqliteReadOnly(string path){
    int result=NativeSqlite.sqlite3_open_v2(Utf8.GetBytes(path+"\0"),out handle,1,IntPtr.Zero);
    if(result!=0){string message=Error();Dispose();throw new InvalidDataException("데이터 파일을 읽을 수 없어요. "+message);}
    if(NativeSqlite.sqlite3_db_readonly(handle,Encoding.ASCII.GetBytes("main\0"))!=1){Dispose();throw new InvalidDataException("데이터 파일을 읽기 전용으로 열지 못했어요.");}
   }
   public SqliteRows Query(string sql){
    IntPtr statement;
    byte[] bytes=Utf8.GetBytes(sql+"\0");
    int result=NativeSqlite.sqlite3_prepare_v2(handle,bytes,bytes.Length-1,out statement,IntPtr.Zero);
    if(result!=0){if(statement!=IntPtr.Zero)NativeSqlite.sqlite3_finalize(statement);throw new InvalidDataException("데이터 테이블을 읽을 수 없어요. "+Error());}
    if(statement==IntPtr.Zero)throw new InvalidDataException("데이터 질의가 비어 있어요.");
    return new SqliteRows(this,statement);
   }
   public long Integer(string sql){
    using(SqliteRows rows=Query(sql)){Require(rows.Step(),"데이터 형식을 확인할 수 없어요.");long value=rows.Int64(0,"database format");Require(!rows.Step(),"데이터 형식 정보가 중복돼요.");return value;}
   }
   public string Error(){
    if(handle==IntPtr.Zero)return "";
    IntPtr text=NativeSqlite.sqlite3_errmsg(handle);if(text==IntPtr.Zero)return "";
    int count=0;while(count<4096&&Marshal.ReadByte(text,count)!=0)count++;
    byte[] bytes=new byte[count];Marshal.Copy(text,bytes,0,count);return Encoding.UTF8.GetString(bytes);
   }
   public void Dispose(){if(handle!=IntPtr.Zero){NativeSqlite.sqlite3_close_v2(handle);handle=IntPtr.Zero;}}
  }
  sealed class SqliteRows:IDisposable {
   readonly SqliteReadOnly owner;IntPtr statement;
   public SqliteRows(SqliteReadOnly db,IntPtr value){owner=db;statement=value;}
   public bool Step(){
    int result=NativeSqlite.sqlite3_step(statement);
    if(result==100)return true;if(result==101)return false;
    throw new InvalidDataException("데이터 내용을 읽을 수 없어요. "+owner.Error());
   }
   public long Int64(int index,string field){
    Require(NativeSqlite.sqlite3_column_type(statement,index)==1,"'"+field+"' 값이 정수가 아니에요.");
    return NativeSqlite.sqlite3_column_int64(statement,index);
   }
   public int Integer(int index,string field){
    long value=Int64(index,field);Require(value>=Int32.MinValue&&value<=Int32.MaxValue,"'"+field+"' 값이 처리 범위를 넘었어요.");return (int)value;
   }
   public bool Boolean(int index,string field){int value=Integer(index,field);Require(value==0||value==1,"'"+field+"' 값은 0 또는 1이어야 해요.");return value==1;}
   public string Text(int index,string field){
    Require(NativeSqlite.sqlite3_column_type(statement,index)==3,"'"+field+"' 값이 문자열이 아니에요.");
    IntPtr pointer=NativeSqlite.sqlite3_column_text(statement,index);int count=NativeSqlite.sqlite3_column_bytes(statement,index);
    Require(count>=0&&(count==0||pointer!=IntPtr.Zero),"'"+field+"' 값을 읽을 수 없어요.");
    byte[] bytes=new byte[count];if(count>0)Marshal.Copy(pointer,bytes,0,count);
    string value;
    try{value=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException e){throw new InvalidDataException("'"+field+"' 문자열의 인코딩이 올바르지 않아요.",e);}
    Require(value.IndexOf('\0')<0,"'"+field+"' 문자열에 잘못된 문자가 있어요.");return value;
   }
   public void Dispose(){if(statement!=IntPtr.Zero){NativeSqlite.sqlite3_finalize(statement);statement=IntPtr.Zero;}}
  }
  static class NativeSqlite {
   // Use Windows' own SQLite, not a bundled provider or additional SQLite DLL.
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_open_v2(byte[] filename,out IntPtr database,int flags,IntPtr vfs);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_close_v2(IntPtr database);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_db_readonly(IntPtr database,byte[] databaseName);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_prepare_v2(IntPtr database,byte[] sql,int bytes,out IntPtr statement,IntPtr tail);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_step(IntPtr statement);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_finalize(IntPtr statement);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern IntPtr sqlite3_errmsg(IntPtr database);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_column_type(IntPtr statement,int index);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern long sqlite3_column_int64(IntPtr statement,int index);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
   [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
   internal static extern int sqlite3_column_bytes(IntPtr statement,int index);
  }
 }
}

