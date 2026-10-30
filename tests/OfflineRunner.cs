// This non-elevated host whitelists only offline tests. No game process API.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

public static class OfflineRunner {
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Save(string path,object data){File.WriteAllText(path,new JavaScriptSerializer().Serialize(data),new UTF8Encoding(false));}
 [STAThread]public static int Main(string[] args){
  try{
   Require(args.Length==4,"Usage: OfflineRunner self-test|catalog <exe> <data-folder> <report>");
   string mode=args[0],exe=Path.GetFullPath(args[1]),folder=Path.GetFullPath(args[2]),report=Path.GetFullPath(args[3]);
   Require(mode=="self-test"||mode=="catalog","Only offline modes are allowed");
   if(mode=="self-test"){
    Require(Path.GetDirectoryName(exe).TrimEnd(Path.DirectorySeparatorChar)==folder.TrimEnd(Path.DirectorySeparatorChar),"Offline EXE must be beside its fixture data");
    AppDomain domain=AppDomain.CreateDomain("CoordinateOffline",null,new AppDomainSetup{ApplicationBase=folder});
    try{return domain.ExecuteAssembly(exe,new[]{"--self-test",report});}finally{AppDomain.Unload(domain);}
   }
   Assembly assembly=Assembly.LoadFrom(exe);string[] resources=assembly.GetManifestResourceNames();
   Require(resources.Length==1&&resources[0]=="app.ico","Catalog/images must not be embedded");
   Require(assembly.GetName().Version.ToString()=="1.1.2.0","Unexpected source version");
   Type type=assembly.GetType("PriParaCoordRemote.Catalog",true);object catalog=Activator.CreateInstance(type,new object[]{folder});
   int items=0,sets=0,declared=0,decoded=0;bool images=false;
   try{
    object bundle=type.GetField("Data").GetValue(catalog);IEnumerable rows=(IEnumerable)bundle.GetType().GetField("items").GetValue(bundle);
    images=(bool)type.GetProperty("ImagesAvailable").GetValue(catalog,null);MethodInfo art=type.GetMethod("Art");
    foreach(object row in rows){
     items++;Type rowType=row.GetType();if(!(bool)rowType.GetField("thumbnail").GetValue(row))continue;declared++;
     int id=(int)rowType.GetField("id").GetValue(row);Image image=(Image)art.Invoke(catalog,new object[]{id});
     if(images){Require(image!=null&&image.Width>0&&image.Height>0&&image.Width<=128&&image.Height<=128,"Bundled thumbnail failed: "+id);decoded++;}
     else Require(image==null,"Artwork unexpectedly available without a pack");
    }
    foreach(object row in (IEnumerable)bundle.GetType().GetField("sets").GetValue(bundle))sets++;
   }finally{((IDisposable)catalog).Dispose();}
   bool builtin=false;foreach(ProcessModule module in Process.GetCurrentProcess().Modules)if(String.Equals(module.ModuleName,"winsqlite3.dll",StringComparison.OrdinalIgnoreCase))builtin=true;
   Require(builtin,"Windows built-in SQLite not loaded");
   Save(report,new{passed=true,items=items,sets=sets,thumbnailItems=declared,decodedImages=decoded,imagesAvailable=images,embeddedResources=resources,windowsBuiltinSqlite=true,gameAccessed=false,scope="Actual Catalog and GDI only; Program.Main/GameBridge live APIs were not called."});return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
