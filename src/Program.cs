using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security.Principal;

[assembly:AssemblyTitle("프리파라 코디 리모컨")]
[assembly:AssemblyProduct("PriPara Coordinate Remote")]
[assembly:AssemblyVersion("1.1.2.0")]
namespace PriParaCoordRemote {
 public static class AppInfo {
  public const string Name="프리파라 코디 리모컨";
  public static string Version{get{return "v"+Assembly.GetExecutingAssembly().GetName().Version.ToString(3);}}
  public static string WindowTitle{get{return Name+" "+Version;}}
 }
 public static class Theme {
  public static float Density=1;
  public static int Px(int value){return (int)Math.Round(value*Density);}
  public static Color Blue=Color.FromArgb(69,91,204),Ink=Color.FromArgb(35,43,68),Muted=Color.FromArgb(112,119,140),Line=Color.FromArgb(226,231,241),Back=Color.FromArgb(246,248,253);
  public static Font Font(float size,FontStyle style){return new Font("맑은 고딕",size,style,GraphicsUnit.Point);}
  public static void Text(Graphics g,string text,Font font,Color color,Rectangle r,TextFormatFlags flags){TextRenderer.DrawText(g,text??"",font,r,color,flags|TextFormatFlags.NoPadding);}
  public static void ImageFit(Graphics g,Image im,Rectangle r){
   if(im==null){using(Font f=Font(23,FontStyle.Regular))Text(g,"✦",f,Color.FromArgb(192,198,220),r,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);return;}
   double scale=Math.Min((double)r.Width/im.Width,(double)r.Height/im.Height);int w=(int)(im.Width*scale),h=(int)(im.Height*scale);
   g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(im,r.X+(r.Width-w)/2,r.Y+(r.Height-h)/2,w,h);
  }
  public static Button Button(string text,bool primary){return new Button{Text=text,FlatStyle=FlatStyle.Flat,BackColor=primary?Blue:Color.White,ForeColor=primary?Color.White:Ink,Font=Font(10,FontStyle.Bold),Cursor=Cursors.Hand,Height=36,AutoSize=false,TabStop=true};}
 }
 public sealed class Entry {public OutfitItem Item;public OutfitSet Set;public string Key{get{return Item!=null?"i"+Item.id:"s"+Set.id;}}public string Name{get{return Item!=null?Item.name:Set.name;}}public string Brand{get{return Item!=null?Item.brandName:Set.brandName;}}}
 public sealed class Gallery:Control {
  readonly Catalog catalog;List<Entry> rows=new List<Entry>();public int Page;public string SelectedKey="";
  public event Action<Entry> Chosen;public event Action PagingChanged;int hover=-1;ToolTip tip=new ToolTip();
  public Gallery(Catalog c){catalog=c;DoubleBuffered=true;BackColor=Theme.Back;SetStyle(ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;TabStop=true;}
  public int Columns{get{return Math.Max(1,ClientSize.Width/Theme.Px(166));}}
  public int VisibleRows{get{return Math.Max(1,ClientSize.Height/Theme.Px(184));}}
  public int PageSize{get{return Columns*VisibleRows;}}
  public int Pages{get{return Math.Max(1,(rows.Count+PageSize-1)/PageSize);}}
  public int Count{get{return rows.Count;}}
  public void SetRows(List<Entry> list){rows=list;Page=0;hover=-1;Notify();Invalidate();}
  public void MovePage(int delta){Page=Math.Max(0,Math.Min(Pages-1,Page+delta));Notify();Invalidate();}
  void Notify(){if(PagingChanged!=null)PagingChanged();}
  protected override void OnResize(EventArgs e){base.OnResize(e);Page=Math.Min(Page,Pages-1);Notify();}
  Rectangle Card(int i){int w=ClientSize.Width/Columns;return new Rectangle((i%Columns)*w+Theme.Px(6),(i/Columns)*Theme.Px(184)+Theme.Px(6),w-Theme.Px(12),Theme.Px(172));}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);Graphics g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
   using(Font name=Theme.Font(9.3f,FontStyle.Bold))using(Font small=Theme.Font(8.2f,FontStyle.Regular)){
    int first=Page*PageSize,count=Math.Min(PageSize,rows.Count-first);
    if(count<=0){Theme.Text(g,"조건에 맞는 의상이 없어요.",name,Theme.Muted,ClientRectangle,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);return;}
    for(int i=0;i<count;i++){
     Entry entry=rows[first+i];Rectangle r=Card(i);bool selected=entry.Key==SelectedKey;
     using(Brush b=new SolidBrush(selected?Color.FromArgb(235,239,255):Color.White))g.FillRectangle(b,r);
     using(Pen p=new Pen(selected?Theme.Blue:(i==hover?Color.FromArgb(167,180,226):Theme.Line),selected?2:1))g.DrawRectangle(p,r);
     if(entry.Item!=null)Theme.ImageFit(g,catalog.Art(entry.Item.id),new Rectangle(r.X+Theme.Px(12),r.Y+Theme.Px(8),r.Width-Theme.Px(24),Theme.Px(100)));
     else{
      int[] ids={entry.Set.pieces["head"],entry.Set.pieces["top"],entry.Set.pieces["bottom"],entry.Set.pieces["shoes"]};
      int cw=Math.Min(Theme.Px(66),(r.Width-Theme.Px(20))/2),left=r.X+(r.Width-2*cw)/2;
      for(int k=0;k<4;k++)if(ids[k]!=0)Theme.ImageFit(g,catalog.Art(ids[k]),new Rectangle(left+(k%2)*cw,r.Y+Theme.Px(6+(k/2)*51),cw,Theme.Px(50)));
     }
     Theme.Text(g,entry.Name,name,Theme.Ink,new Rectangle(r.X+Theme.Px(10),r.Y+Theme.Px(110),r.Width-Theme.Px(20),Theme.Px(39)),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
     Theme.Text(g,entry.Brand,small,Theme.Muted,new Rectangle(r.X+Theme.Px(10),r.Y+Theme.Px(151),r.Width-Theme.Px(20),Theme.Px(17)),TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
    }
   }
  }
  int Hit(Point p){for(int i=0;i<PageSize&&Page*PageSize+i<rows.Count;i++)if(Card(i).Contains(p))return i;return -1;}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int h=Hit(e.Location);if(h!=hover){hover=h;tip.SetToolTip(this,h<0?"":rows[Page*PageSize+h].Name);Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);int i=Hit(e.Location);if(i>=0&&e.Button==MouseButtons.Left){Entry row=rows[Page*PageSize+i];SelectedKey=row.Key;Invalidate();if(Chosen!=null)Chosen(row);}}
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);MovePage(e.Delta<0?1:-1);}
 }
 public sealed class SelectionView:Control {
  readonly Catalog catalog;public Outfit Outfit=new Outfit();public event Action<string> ClearSlot;
  readonly string[] slots={"head","top","bottom","shoes"},labels={"헤어액세서리","상의 · 원피스","하의","슈즈"};
  public SelectionView(Catalog c){catalog=c;DoubleBuffered=true;BackColor=Color.White;Height=430;}
  int Id(string slot){return slot=="top"?Outfit.top:slot=="bottom"?Outfit.bottom:slot=="shoes"?Outfit.shoes:Outfit.head;}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);Graphics g=e.Graphics;
   using(Font title=Theme.Font(8.4f,FontStyle.Bold))using(Font name=Theme.Font(9,FontStyle.Regular))using(Pen pen=new Pen(Theme.Line)){
    for(int i=0;i<4;i++){
     int h=Height/4,y=i*h;g.DrawLine(pen,Theme.Px(10),y+h-2,Width-Theme.Px(10),y+h-2);
     Theme.Text(g,labels[i],title,Theme.Muted,new Rectangle(Theme.Px(12),y+Theme.Px(5),Width-Theme.Px(30),Theme.Px(20)),TextFormatFlags.SingleLine);
     int id=Id(slots[i]);OutfitItem item=null;if(id!=0)catalog.Items.TryGetValue(id,out item);
     Theme.ImageFit(g,item==null?null:catalog.Art(id),new Rectangle(Theme.Px(12),y+Theme.Px(28),Theme.Px(65),Math.Min(Theme.Px(65),h-Theme.Px(30))));
     string text=item==null?(slots[i]=="bottom"&&Outfit.top!=0&&catalog.Items[Outfit.top].isDress?"원피스에 포함":"선택 안 함"):item.name;
     Theme.Text(g,text,name,item==null?Theme.Muted:Theme.Ink,new Rectangle(Theme.Px(87),y+Theme.Px(28),Width-Theme.Px(99),h-Theme.Px(32)),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
     if(item!=null)Theme.Text(g,"×",title,Theme.Muted,new Rectangle(Width-Theme.Px(29),y+Theme.Px(2),Theme.Px(21),Theme.Px(26)),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
   }
  }
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);int h=Math.Max(1,Height/4),i=e.Y/h;if(i>=0&&i<4&&e.X>Width-Theme.Px(36)&&e.Y%h<Theme.Px(32)&&ClearSlot!=null)ClearSlot(slots[i]);}
 }
 public sealed class MainForm:Form {
  readonly Catalog catalog;readonly GameBridge bridge;readonly Gallery gallery;readonly SelectionView selection;readonly bool testMode;
  Outfit outfit=new Outfit();string category="sets";bool busy,refreshing;GameStatus game=new GameStatus{Message="게임 연결 확인 중…"};
  Label badge,versionLabel,status,pageLabel,resultCount;TextBox search;ComboBox brand;CheckBox autoSend,autoSet;Button send,prev,next;
  System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer(),debounce=new System.Windows.Forms.Timer(),findDelay=new System.Windows.Forms.Timer();
  readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
  public MainForm(Catalog c,bool testMode=false){
   this.testMode=testMode;
   SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
   catalog=c;bridge=new GameBridge(c.Items);Text=AppInfo.WindowTitle;StartPosition=FormStartPosition.CenterScreen;Size=new Size(1220,850);MinimumSize=new Size(1060,780);BackColor=Theme.Back;Font=Theme.Font(9,FontStyle.Regular);
   try{Icon=new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"));}catch{}
   Panel header=new Panel{Dock=DockStyle.Top,Height=94,BackColor=Color.White};Controls.Add(header);
   Label title=new Label{Text=AppInfo.Name,Font=Theme.Font(20,FontStyle.Bold),ForeColor=Theme.Ink,Location=new Point(25,15),AutoSize=true};header.Controls.Add(title);
   header.Controls.Add(new Label{Text="의상을 고르면, 지금 게임에 입혀요"+(c.ImagesAvailable?"":" · 이미지 없이 사용 중"),Font=Theme.Font(9.5f,FontStyle.Regular),ForeColor=Theme.Muted,Location=new Point(28,57),AutoSize=true});
   Panel connection=new Panel{Dock=DockStyle.Right,Width=450};header.Controls.Add(connection);
   versionLabel=new Label{Name="applicationVersion",Text=AppInfo.Version,Font=Theme.Font(10,FontStyle.Bold),ForeColor=Theme.Muted,AutoSize=false,TextAlign=ContentAlignment.MiddleRight,Height=39,Dock=DockStyle.Top,Padding=new Padding(0,0,22,0)};
   badge=new Label{Text=game.Message,Font=Theme.Font(9,FontStyle.Bold),ForeColor=Theme.Muted,AutoSize=false,TextAlign=ContentAlignment.MiddleRight,Dock=DockStyle.Fill,Padding=new Padding(0,0,22,6)};connection.Controls.Add(badge);connection.Controls.Add(versionLabel);
   Panel foot=new Panel{Dock=DockStyle.Bottom,Height=45,BackColor=Color.White};Controls.Add(foot);
   status=new Label{Dock=DockStyle.Fill,Text="게임에서 코디 화면을 열어 주세요. DEBUG를 켜지 않아도 선택한 의상을 입힐 수 있어요.",Padding=new Padding(22,12,12,0),ForeColor=Theme.Muted};foot.Controls.Add(status);
   Panel body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(20,16,20,12)};Controls.Add(body);body.BringToFront();
   Panel left=new Panel{Dock=DockStyle.Left,Width=290,BackColor=Color.White,Padding=new Padding(10)};body.Controls.Add(left);
   Label selectedTitle=new Label{Text="선택한 코디",Dock=DockStyle.Top,Height=37,Font=Theme.Font(12,FontStyle.Bold),ForeColor=Theme.Ink,Padding=new Padding(4,5,0,0)};left.Controls.Add(selectedTitle);
   selection=new SelectionView(c){Dock=DockStyle.Top};left.Controls.Add(selection);selection.BringToFront();selectedTitle.BringToFront();
   // Top-docked controls are explicitly arranged by a single flow panel for stable resizing.
   left.Controls.Clear();FlowLayoutPanel leftFlow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(2)};left.Controls.Add(leftFlow);
   selectedTitle.Dock=DockStyle.None;selectedTitle.Width=260;leftFlow.Controls.Add(selectedTitle);selection.Dock=DockStyle.None;selection.Width=260;leftFlow.Controls.Add(selection);
   autoSend=new CheckBox{Text="선택 즉시 게임에 입히기",Checked=true,AutoSize=true,Margin=new Padding(9,9,0,3),ForeColor=Theme.Ink};leftFlow.Controls.Add(autoSend);
   autoSet=new CheckBox{Text="승인된 세트 자동 맞춤",Checked=true,AutoSize=true,Margin=new Padding(9,3,0,12),ForeColor=Theme.Muted};leftFlow.Controls.Add(autoSet);
   send=Theme.Button("게임에 입히기",true);send.Width=254;send.Height=42;send.Margin=new Padding(4,2,0,8);send.Enabled=false;leftFlow.Controls.Add(send);
   Label hint=new Label{Width=254,Height=65,Text="게임의 코디 화면에서 사용할 수 있어요.\nDEBUG OFF / ON 모두 지원해요.\n게임 연결이 확인될 때만 전송해요.",ForeColor=Theme.Muted,Font=Theme.Font(8.2f,FontStyle.Regular),Margin=new Padding(7,5,0,0)};leftFlow.Controls.Add(hint);
   leftFlow.Controls.Clear();left.Controls.Clear();leftFlow.Dispose();
   TableLayoutPanel leftGrid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(2)};
   leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute,37));leftGrid.RowStyles.Add(new RowStyle(SizeType.Percent,100));leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute,28));leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute,28));leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute,48));leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
   Control[] leftRows={selectedTitle,selection,autoSend,autoSet,send,hint};for(int i=0;i<leftRows.Length;i++){Control control=leftRows[i];control.Dock=DockStyle.Fill;control.Margin=new Padding(3);if(control is CheckBox)((CheckBox)control).AutoSize=false;leftGrid.Controls.Add(control,0,i);}left.Controls.Add(leftGrid);
   Panel center=new Panel{Dock=DockStyle.Fill,Padding=new Padding(18,0,0,0)};body.Controls.Add(center);center.BringToFront();
   FlowLayoutPanel tabLine=new FlowLayoutPanel{Dock=DockStyle.Top,Height=46,WrapContents=false,Padding=new Padding(0)};center.Controls.Add(tabLine);
   string[] keys={"sets","all","head","top","bottom","shoes"},labels={"의상세트","전체","헤어 액세서리","상의 원피스","하의","슈즈"};
   int[] widths={90,64,122,118,70,70};
   for(int i=0;i<keys.Length;i++){string key=keys[i];Button b=Theme.Button(labels[i],false);b.Name="tab_"+key;b.Width=widths[i];b.Margin=new Padding(0,0,7,0);tabs[key]=b;tabLine.Controls.Add(b);b.Click+=(s,e)=>{category=key;StyleTabs();Filter();};}
   Panel filters=new Panel{Dock=DockStyle.Top,Height=54};center.Controls.Add(filters);filters.BringToFront();tabLine.BringToFront();
   search=new TextBox{Location=new Point(1,9),Width=350,Font=Theme.Font(11,FontStyle.Regular)};filters.Controls.Add(search);
   brand=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(365,8),Width=190,Font=Theme.Font(10,FontStyle.Regular)};filters.Controls.Add(brand);brand.Items.Add("모든 브랜드");
   foreach(string b in catalog.Data.items.Select(x=>x.brandName).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x=>x))brand.Items.Add(b);brand.SelectedIndex=0;
   resultCount=new Label{Text="",ForeColor=Theme.Muted,AutoSize=true,Location=new Point(568,15)};filters.Controls.Add(resultCount);
   Panel paging=new Panel{Dock=DockStyle.Bottom,Height=43};center.Controls.Add(paging);
   prev=Theme.Button("이전",false);prev.Width=76;prev.Location=new Point(0,5);paging.Controls.Add(prev);
   pageLabel=new Label{AutoSize=false,Font=Theme.Font(9,FontStyle.Regular),TextAlign=ContentAlignment.MiddleCenter,Width=160,Height=36,Location=new Point(80,5),ForeColor=Theme.Muted};paging.Controls.Add(pageLabel);
   next=Theme.Button("다음",false);next.Width=76;next.Location=new Point(246,5);paging.Controls.Add(next);
   gallery=new Gallery(c){Dock=DockStyle.Fill};center.Controls.Add(gallery);gallery.BringToFront();paging.BringToFront();filters.BringToFront();tabLine.BringToFront();
   center.Controls.Clear();TableLayoutPanel centerLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0),Padding=new Padding(0)};
   centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));centerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));centerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));centerLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));centerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
   foreach(Control control in new Control[]{tabLine,filters,gallery,paging}){control.Dock=DockStyle.Fill;control.Margin=new Padding(0);}centerLayout.Controls.Add(tabLine,0,0);centerLayout.Controls.Add(filters,0,1);centerLayout.Controls.Add(gallery,0,2);centerLayout.Controls.Add(paging,0,3);center.Controls.Add(centerLayout);
   body.Controls.Clear();TableLayoutPanel columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,290));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));columns.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.Dock=DockStyle.Fill;center.Dock=DockStyle.Fill;left.Margin=new Padding(0);center.Margin=new Padding(0);columns.Controls.Add(left,0,0);columns.Controls.Add(center,1,0);body.Controls.Add(columns);
   gallery.PagingChanged+=UpdatePaging;prev.Click+=(s,e)=>gallery.MovePage(-1);next.Click+=(s,e)=>gallery.MovePage(1);
   gallery.Chosen+=Choose;selection.ClearSlot+=Clear;send.Click+=async(s,e)=>await SendNow();
   findDelay.Interval=170;findDelay.Tick+=(s,e)=>{findDelay.Stop();Filter();};search.TextChanged+=(s,e)=>{findDelay.Stop();findDelay.Start();};brand.SelectedIndexChanged+=(s,e)=>Filter();
   debounce.Interval=320;debounce.Tick+=async(s,e)=>{debounce.Stop();if(autoSend.Checked)await SendNow();};
   autoSend.CheckedChanged+=(s,e)=>{if(!autoSend.Checked)debounce.Stop();};
   poll.Interval=1500;poll.Tick+=async(s,e)=>await RefreshGame();
   if(!testMode)Shown+=async(s,e)=>{await RefreshGame();poll.Start();};FormClosed+=(s,e)=>{poll.Stop();debounce.Stop();findDelay.Stop();};
   ResumeLayout(true);StyleTabs();Filter();UpdateSelection();
  }
  void StyleTabs(){foreach(var pair in tabs){bool on=pair.Key==category;pair.Value.BackColor=on?Theme.Blue:Color.White;pair.Value.ForeColor=on?Color.White:Theme.Ink;}}
  bool Matches(string text,string q){return String.IsNullOrWhiteSpace(q)||(text??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0;}
  public void Filter(){
   if(gallery==null||search==null||brand==null)return;string q=search.Text.Trim();string b=brand.SelectedIndex<=0?"":brand.Text;
   List<Entry> result=category=="sets"?catalog.Data.sets.Where(x=>Matches(x.search,q)&&(b==""||x.brandName==b)).Select(x=>new Entry{Set=x}).ToList():catalog.Data.items.Where(x=>(category=="all"||x.category==category)&&Matches(x.search,q)&&(b==""||x.brandName==b)).Select(x=>new Entry{Item=x}).ToList();
   gallery.SetRows(result);resultCount.Text=result.Count.ToString("N0")+"개";
  }
  void UpdatePaging(){if(pageLabel==null||gallery==null)return;pageLabel.Text=(gallery.Page+1)+" / "+gallery.Pages+" 페이지";prev.Enabled=gallery.Page>0;next.Enabled=gallery.Page+1<gallery.Pages;}
  public void Choose(Entry entry){
   if(entry.Set!=null)outfit=catalog.FromSet(entry.Set);
   else{
    OutfitItem item=entry.Item;List<OutfitSet> candidates=autoSet.Checked?catalog.Data.sets.Where(x=>x.pieces.ContainsKey(item.category)&&x.pieces[item.category]==item.id).ToList():new List<OutfitSet>();
    if(candidates.Count==1)outfit=catalog.FromSet(candidates[0]);
    else{
     if(item.category=="top"){outfit.top=item.id;if(item.isDress)outfit.bottom=0;}
     else if(item.category=="bottom"){outfit.bottom=item.id;if(outfit.top!=0&&catalog.Items[outfit.top].isDress)outfit.top=0;}
     else if(item.category=="shoes")outfit.shoes=item.id;else outfit.head=item.id;
    }
   }
   UpdateSelection();Changed();
  }
  void Clear(string slot){if(slot=="top")outfit.top=0;else if(slot=="bottom")outfit.bottom=0;else if(slot=="shoes")outfit.shoes=0;else outfit.head=0;UpdateSelection();Changed();}
  void Changed(){
   debounce.Stop();if(autoSend.Checked&&game.Ready)debounce.Start();
   else SetStatus(game.Ready?"코디를 골랐어요. ‘게임에 입히기’를 누르면 전송해요.":"코디를 골랐어요. 게임 연결 후 ‘게임에 입히기’를 눌러 주세요.",false);
  }
  void UpdateSelection(){selection.Outfit=outfit;selection.Invalidate();send.Enabled=game.Ready&&!busy&&outfit.Values().Any(x=>x!=0);}
  void SetStatus(string message,bool success){status.Text=message;status.ForeColor=success?Color.FromArgb(34,126,96):Theme.Muted;}
  async Task RefreshGame(){
   if(testMode||refreshing||IsDisposed)return;refreshing=true;
   try{GameStatus result=await Task.Run(()=>bridge.Inspect());if(IsDisposed)return;game=result;badge.Text=result.Message;badge.ForeColor=result.Ready?Color.FromArgb(34,126,96):Theme.Muted;UpdateSelection();}finally{refreshing=false;}
  }
  async Task SendNow(){
   if(testMode){SetStatus("화면 확인 중에는 게임으로 전송하지 않아요.",false);return;}
   if(busy){if(autoSend.Checked)debounce.Start();return;}if(!outfit.Values().Any(x=>x!=0)){SetStatus("의상을 한 부위 이상 선택해 주세요.",false);return;}
   busy=true;UpdateSelection();Outfit copy=outfit.Copy();SetStatus("게임에 코디를 전달하고 있어요…",false);
   try{SendResult result=await Task.Run(()=>bridge.Send(copy));if(!IsDisposed)SetStatus(result.Message,result.Accepted);}
   catch(Exception e){if(!IsDisposed)SetStatus(GameBridge.Friendly(e),false);}
   finally{busy=false;if(!IsDisposed)UpdateSelection();}
  }
  public void Snapshot(string path){
   search.Text="트윙클리본";Filter();OutfitSet sample=catalog.Data.sets.First(x=>x.pieces["top"]==7106&&x.pieces["shoes"]==7307&&x.pieces["head"]==7412);
   outfit=catalog.FromSet(sample);UpdateSelection();CreateControl();PerformLayout();
   File.WriteAllText(path+".json",new JavaScriptSerializer().Serialize(new{density=Theme.Density,size=new[]{Width,Height},windowTitle=Text,version=versionLabel.Text,badge=new{text=badge.Text,bounds=new[]{badge.Left,badge.Top,badge.Width,badge.Height},visible=badge.Visible,font=badge.Font.ToString()},page=new{text=pageLabel.Text,bounds=new[]{pageLabel.Left,pageLabel.Top,pageLabel.Width,pageLabel.Height},visible=pageLabel.Visible,font=pageLabel.Font.ToString()}}));
   using(Bitmap b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
  }
  public void CheckInterface(){
   autoSend.Checked=false;autoSet.Checked=false;CreateControl();PerformLayout();
   if(Text!=AppInfo.WindowTitle||versionLabel.Text!=AppInfo.Version)throw new Exception("Displayed application version mismatch");
   string[] tabOrder={"sets","all","head","top","bottom","shoes"};
   if(!tabs["sets"].Parent.Controls.Cast<Control>().Select(x=>x.Name).SequenceEqual(tabOrder.Select(x=>"tab_"+x)))throw new Exception("Category tab order changed");
   category="all";search.Text="";Filter();if(gallery.Count!=catalog.Data.items.Count)throw new Exception("All items must exclude sets and include every item");
   search.Text="트윙클리본";Filter();int allTwinkle=gallery.Count,bySlot=0;
   foreach(string slot in new[]{"head","top","bottom","shoes"}){category=slot;Filter();bySlot+=gallery.Count;}
   if(allTwinkle<1||allTwinkle!=bySlot)throw new Exception("All-category Korean search failed");
   category="all";search.Text="";brand.SelectedIndex=1;Filter();if(gallery.Count!=catalog.Data.items.Count(x=>x.brandName==brand.Text))throw new Exception("All-category brand filtering failed");
   brand.SelectedIndex=0;category="sets";
   search.Text="트윙클리본";Filter();if(gallery.Count<1)throw new Exception("Korean search failed");
   Choose(new Entry{Set=catalog.Data.sets.First(x=>x.pieces["top"]==7106&&x.pieces["shoes"]==7307&&x.pieces["head"]==7412)});
   if(!outfit.Values().SequenceEqual(new[]{7106,0,7307,7412}))throw new Exception("Set selection failed");
   Choose(new Entry{Item=catalog.Data.items.First(x=>x.category=="bottom")});if(outfit.top!=0||outfit.bottom==0)throw new Exception("Skirt/dress exclusion failed");
   Choose(new Entry{Item=catalog.Items[7106]});if(outfit.bottom!=0||outfit.top!=7106)throw new Exception("Dress/skirt exclusion failed");
   Clear("head");if(outfit.head!=0||outfit.top!=7106)throw new Exception("Clear slot failed");
   category="top";search.Text="1501_D01";Filter();if(gallery.Count<1)throw new Exception("Model search failed");
   search.Text="";Filter();if(gallery.Count<100||gallery.Pages<2)throw new Exception("Paging unavailable");gallery.MovePage(1);if(gallery.Page!=1)throw new Exception("Next page failed");
   category="sets";search.Text="no_such_coordinate_999999";Filter();if(gallery.Count!=0||gallery.Page!=0)throw new Exception("Empty search failed");
   if(debounce.Enabled)throw new Exception("UI test unexpectedly scheduled a send");
  }
 }
 public static class Program {
  [STAThread]public static int Main(string[] args){
   try{
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    using(Graphics graphics=Graphics.FromHwnd(IntPtr.Zero))Theme.Density=graphics.DpiX/96f;
    using(Catalog catalog=new Catalog()){
     if(args.Length>0&&args[0]=="--self-test"){SelfTests.Run(catalog,args.Length>1?args[1]:"self-test.json");return 0;}
     if(args.Length>0&&args[0]=="--status"){
      GameStatus status=new GameBridge(catalog.Items).Inspect();File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(status),System.Text.Encoding.UTF8);return 0;
     }
     if(args.Length>0&&args[0]=="--send-twinkle-test"){
      SendResult result=new GameBridge(catalog.Items).Send(new Outfit{top=7106,shoes=7307,head=7412});File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result),System.Text.Encoding.UTF8);return result.Accepted?0:2;
     }
     if(args.Length>0&&args[0]=="--ui-snapshot"){
      using(MainForm form=new MainForm(catalog,true)){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();form.Snapshot(args[1]);if(args.Length>2){form.Size=form.MinimumSize;Application.DoEvents();form.Snapshot(args[2]);}form.Close();}return 0;
     }
     Application.Run(new MainForm(catalog));
    }return 0;
   }catch(Exception e){
    if(args.Length>0){string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-error.txt");File.WriteAllText(file,e.ToString());return 1;}
    MessageBox.Show("리모컨을 시작하지 못했어요.\n\n"+e.Message,"프리파라 코디 리모컨",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;
   }
  }
 }
}
