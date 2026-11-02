using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PriParaCoordRemote {
 public sealed class Favorites {
  readonly HashSet<string> keys=new HashSet<string>(StringComparer.Ordinal);
  readonly string path;
  public Favorites(string file){path=file;try{if(File.Exists(path)){string[] saved=new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(path,Encoding.UTF8));if(saved!=null)foreach(string key in saved)if(Valid(key))keys.Add(key);}}catch(Exception){/* A damaged preferences file must not prevent startup. */}}
  static bool Valid(string key){return !String.IsNullOrEmpty(key)&&key.Length<100&&(key[0]=='i'||key[0]=='s')&&key.Skip(1).All(c=>Char.IsLetterOrDigit(c)||c=='-');}
  public bool Contains(string key){return keys.Contains(key);}
  public int Count{get{return keys.Count;}}
  public void Toggle(string key){if(!Valid(key))throw new ArgumentException("Invalid favorite key");if(!keys.Remove(key))keys.Add(key);Save();}
  void Save(){string folder=Path.GetDirectoryName(path);if(!Directory.Exists(folder))Directory.CreateDirectory(folder);string tmp=path+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray()),new UTF8Encoding(false));if(File.Exists(path))File.Delete(path);File.Move(tmp,path);}
  public static string DefaultPath(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PriParaCoordRemote","favorites.json");}
 }
}
