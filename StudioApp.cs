using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class PixelPreview : Control {
    Image image;
    MemoryStream stream;
    public PixelPreview() { DoubleBuffered=true; BackColor=Color.Black; }
    public void LoadFile(string path) {
        if(image!=null) {ImageAnimator.StopAnimate(image,Changed);image.Dispose();stream.Dispose();}
        stream=new MemoryStream(File.ReadAllBytes(path)); image=Image.FromStream(stream);
        ImageAnimator.Animate(image,Changed); Invalidate();
    }
    public void ClearImage() {if(image!=null){ImageAnimator.StopAnimate(image,Changed);image.Dispose();stream.Dispose();image=null;}Invalidate();}
    void Changed(object s,EventArgs e) {if(!IsDisposed && IsHandleCreated){try{BeginInvoke((Action)(()=>Invalidate()));}catch(InvalidOperationException){}}}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); if(image==null)return; ImageAnimator.UpdateFrames(image);
        int scale=Math.Max(1,Math.Min(ClientSize.Width/image.Width,ClientSize.Height/image.Height));
        int w=image.Width*scale,h=image.Height*scale;
        e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;
        e.Graphics.DrawImage(image,new Rectangle((Width-w)/2,(Height-h)/2,w,h),0,0,image.Width,image.Height,GraphicsUnit.Pixel);
    }
    protected override void Dispose(bool disposing) {if(disposing && image!=null){ImageAnimator.StopAnimate(image,Changed);image.Dispose();stream.Dispose();image=null;}base.Dispose(disposing);}
}

class PixelCanvas : Control {
    public Bitmap Bitmap;
    public Color Ink=Color.White;
    public PixelCanvas() {DoubleBuffered=true;BackColor=Color.Black;NewCanvas(96,16);MouseDown+=PaintPixel;MouseMove+=(s,e)=>{if(e.Button!=MouseButtons.None)PaintPixel(s,e);};}
    public void NewCanvas(int width,int height) {if(Bitmap!=null)Bitmap.Dispose();Bitmap=new Bitmap(width,height);using(var g=Graphics.FromImage(Bitmap))g.Clear(Color.Black);Invalidate();}
    void PaintPixel(object sender,MouseEventArgs e) {int x=e.X*Bitmap.Width/Math.Max(1,Width),y=e.Y*Bitmap.Height/Math.Max(1,Height);if(x>=0&&y>=0&&x<Bitmap.Width&&y<Bitmap.Height){Bitmap.SetPixel(x,y,e.Button==MouseButtons.Right?Color.Black:Ink);Invalidate();}}
    protected override void OnPaint(PaintEventArgs e) {e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;e.Graphics.DrawImage(Bitmap,new Rectangle(0,0,Width,Height),0,0,Bitmap.Width,Bitmap.Height,GraphicsUnit.Pixel);if(Width/Bitmap.Width>=5){using(var pen=new Pen(Color.FromArgb(35,255,255,255))){for(int x=0;x<Bitmap.Width;x++)e.Graphics.DrawLine(pen,x*Width/Bitmap.Width,0,x*Width/Bitmap.Width,Height);for(int y=0;y<Bitmap.Height;y++)e.Graphics.DrawLine(pen,0,y*Height/Bitmap.Height,Width,y*Height/Bitmap.Height);}}}
    protected override void Dispose(bool disposing) {if(disposing)Bitmap.Dispose();base.Dispose(disposing);}
}

public class SignStudio : Form {
    const int DefaultSpeed = 40;
    protected override bool ShowWithoutActivation {get{return WindowState==FormWindowState.Minimized;}}
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly CheckedListBox signs=new CheckedListBox();
    readonly TextBox message=new TextBox(),status=new TextBox(),alias=new TextBox(),imagePath=new TextBox();
    readonly ComboBox animation=new ComboBox(),background=new ComboBox(),palette=new ComboBox(),font=new ComboBox(),effect=new ComboBox(),previewSize=new ComboBox(),fit=new ComboBox();
    readonly NumericUpDown speed=new NumericUpDown(),fontSize=new NumericUpDown(),brightness=new NumericUpDown(),slot=new NumericUpDown(),clockStyle=new NumericUpDown(),score1=new NumericUpDown(),score2=new NumericUpDown();
    readonly CheckBox persist=new CheckBox(),showDate=new CheckBox(),format24=new CheckBox();
    readonly Button fore=new Button(),back=new Button(),accent=new Button(),ink=new Button(),send=new Button(),previewButton=new Button(),stopSending=new Button();
    readonly PixelPreview preview=new PixelPreview();
    readonly PixelCanvas canvas=new PixelCanvas();
    readonly TabControl tabs=new TabControl();
    readonly List<Button> actionButtons=new List<Button>();
    readonly Timer timer=new Timer();
    readonly Label previewCaption=new Label();
    readonly ListBox themes=new ListBox(),saved=new ListBox();
    readonly ComboBox historySign=new ComboBox();
    readonly ListBox historyList=new ListBox();
    readonly TextBox historyDetail=new TextBox();
    readonly CheckedListBox fairyLights=new CheckedListBox();
    readonly TextBox fairyAlias=new TextBox(),fairyStatus=new TextBox();
    readonly NumericUpDown fairyBrightness=new NumericUpDown(),fairyEffectNumber=new NumericUpDown();
    readonly Button fairyColor=new Button();
    readonly ComboBox fairyEffects=new ComboBox();
    List<Dictionary<string,object>> fairyDevices=new List<Dictionary<string,object>>(),fairyEffectData=new List<Dictionary<string,object>>();
    readonly DataGridView scheduleGrid=new DataGridView();
    readonly TextBox scheduleTitle=new TextBox();
    readonly DateTimePicker scheduleWhen=new DateTimePicker(),scheduleEnd=new DateTimePicker();
    readonly ComboBox scheduleRepeat=new ComboBox(),scheduleDesign=new ComboBox();
    readonly CheckedListBox scheduleSigns=new CheckedListBox();
    readonly Label scheduleWorker=new Label();
    string editingSchedule=null;
    List<Dictionary<string,object>> scheduleJobs=new List<Dictionary<string,object>>();
    List<Dictionary<string,object>> historyEntries=new List<Dictionary<string,object>>();
    List<Dictionary<string,object>> devices=new List<Dictionary<string,object>>(),themeData=new List<Dictionary<string,object>>();
    Process worker;
    string requestPath,responsePath,operation,previewKey;
    string currentContent="text";
    bool stopRequested=false;
    Dictionary<string,object> prepared;
    readonly ToolTip tips=new ToolTip();
    readonly HashSet<string> selectedAddresses=new HashSet<string>();
    public SignStudio() {
        Icon=new Icon(Path.Combine(root,"assets","led-sign-studio-taskbar.ico"),new Size(32,32));ShowIcon=true;ShowInTaskbar=true;
        Text="LED Sign Studio";ClientSize=new Size(1040,800);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(245,247,250);
        LabelAt(this,"YOUR SIGNS",18,16,245);
        Put(this,signs,18,44,250,155);signs.CheckOnClick=true;signs.HorizontalScrollbar=true;
        signs.SelectedIndexChanged+=(s,e)=>{if(signs.SelectedIndex>=0){alias.Text=Get(devices[signs.SelectedIndex],"alias",Get(devices[signs.SelectedIndex],"name",""));if(historySign.Items.Count==devices.Count)historySign.SelectedIndex=signs.SelectedIndex;}};
        Btn(this,"Scan",18,208,80,()=>RunAction("scan"));Btn(this,"Connect / info",106,208,162,()=>RunAction("info"));
        LabelAt(this,"Selected sign name",18,253,250);Put(this,alias,18,279,160,27);
        Btn(this,"Rename",185,276,83,Rename);
        Btn(this,"Select all",18,317,120,()=>{for(int i=0;i<signs.Items.Count;i++)signs.SetItemChecked(i,true);});
        Btn(this,"Select none",148,317,120,()=>{for(int i=0;i<signs.Items.Count;i++)signs.SetItemChecked(i,false);});
        LabelAt(this,"Brightness (%)",18,367,250);Number(this,brightness,18,394,100,0,100,50);
        Btn(this,"Apply",128,391,140,()=>RunAction("brightness"));
        Btn(this,"Turn on",18,435,120,()=>RunAction("on"));Btn(this,"Turn off",148,435,120,()=>RunAction("off"));
        LabelAt(this,"Preview size",18,490,250);Combo(this,previewSize,18,518,250,new[]{"96 x 16  (Original / Large)","192 x 16  (XL)"});
        LabelAt(this,"Close iPixel Color on your iPhone if a sign is busy. Each command releases the Bluetooth connection when finished.",18,572,250,92);
        Put(this,stopSending,18,676,250,36);stopSending.Text="Stop sending";stopSending.Enabled=false;stopSending.Click+=(s,e)=>RequestStop();
        LabelAt(this,"Designs fit each sign automatically.",18,718,250,23);
        LabelAt(this,"LED SIGN STUDIO",290,16,720);
        Put(this,preview,290,43,730,120);preview.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        Put(this,previewCaption,290,170,730,25);previewCaption.Text="Rendered preview - click Preview after editing.";
        Put(this,tabs,290,201,730,414);tabs.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        BuildText();BuildThemes();BuildMedia();BuildDrawing();BuildSaved();BuildDevice();BuildHistory();BuildFairy();BuildSchedule();
        Btn(this,"Plan sign updates",18,746,250,()=>{tabs.SelectedIndex=8;RunSchedule("schedule_list");});
        Put(this,previewButton,290,626,125,36);previewButton.Text="Preview";previewButton.Click+=(s,e)=>RunAction("preview");actionButtons.Add(previewButton);
        Put(this,send,425,626,180,36);send.Text="Send to selected signs";send.BackColor=Color.FromArgb(210,237,255);send.Click+=(s,e)=>RunAction("design");actionButtons.Add(send);
        Btn(this,"Export preview",615,626,135,ExportPreview);
        Put(this,persist,765,630,135,30);persist.Text="Save on sign";
        Number(this,slot,915,632,65,1,10,1);tips.SetToolTip(persist,"When enabled, sending replaces this numbered preset on each selected sign.");
        LabelAt(this,"Save on sign replaces the chosen slot. Leave unchecked for a temporary display.",290,668,730);
        Put(this,status,290,698,730,84);status.Multiline=true;status.ReadOnly=true;status.ScrollBars=ScrollBars.Vertical;
        status.Text="Ready. Choose a theme or create a design, preview it, then send.";
        tabs.SelectedIndexChanged+=(s,e)=>{if(tabs.SelectedIndex<2)currentContent="text";else if(tabs.SelectedIndex==2)currentContent="image";else if(tabs.SelectedIndex==3)currentContent="drawing";if(tabs.SelectedIndex==6)RefreshHistory();if(tabs.SelectedIndex==7){preview.ClearImage();prepared=null;previewKey=null;previewCaption.Text="Hello Fairy lights - use the controls below. Sign controls are separate.";}if(tabs.SelectedIndex==8){RefreshScheduleDesigns();if(worker==null)RunSchedule("schedule_list");}UpdateButtons();};
        timer.Interval=400;timer.Tick+=Poll;
        try {string path=Path.Combine(root,"studio-settings.json");if(File.Exists(path)){var settings=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));if(settings.ContainsKey("selected"))foreach(object address in (IEnumerable)settings["selected"])selectedAddresses.Add(address.ToString());if(settings.ContainsKey("design")){var design=(Dictionary<string,object>)settings["design"];ApplyDesign(design);if(Get(design,"content","text")=="image"){tabs.SelectedIndex=2;currentContent="image";}previewSize.SelectedIndex=Get(design,"width","96")=="192"?1:0;}}}catch(Exception ex){status.Text="Previous settings could not be loaded: "+ex.Message;}
        LoadDevices();RefreshSaved();LoadFairyDevices();LoadScheduleTargets();
        FormClosing+=(s,e)=>{try{var checkedAddresses=new List<string>();foreach(int i in signs.CheckedIndices)checkedAddresses.Add(devices[i]["address"].ToString());var settings=new Dictionary<string,object>{{"selected",checkedAddresses},{"design",ContentRequest()}};File.WriteAllText(Path.Combine(root,"studio-settings.json"),json.Serialize(settings),Encoding.UTF8);}catch{}timer.Stop();if(worker!=null){worker.Dispose();worker=null;}};
    }
    static string Get(Dictionary<string,object> d,string k,string fallback) {return d.ContainsKey(k)&&d[k]!=null?d[k].ToString():fallback;}
    void Put(Control p,Control c,int x,int y,int w,int h) {c.SetBounds(x,y,w,h);p.Controls.Add(c);}
    void LabelAt(Control p,string text,int x,int y,int w,int h=23) {var l=new Label();l.Text=text;Put(p,l,x,y,w,h);}
    Button Btn(Control p,string text,int x,int y,int w,Action action) {var b=new Button();b.Text=text;Put(p,b,x,y,w,32);b.Click+=(s,e)=>{try{action();}catch(Exception ex){status.Text=ex.Message;}};actionButtons.Add(b);return b;}
    void Combo(Control p,ComboBox c,int x,int y,int w,string[] items) {Put(p,c,x,y,w,28);c.DropDownStyle=ComboBoxStyle.DropDownList;c.Items.AddRange(items);c.SelectedIndex=0;}
    void Number(Control p,NumericUpDown n,int x,int y,int w,int min,int max,int value) {Put(p,n,x,y,w,27);n.Minimum=min;n.Maximum=max;n.Value=value;}
    TabPage Tab(string title) {var p=new TabPage(title);p.BackColor=Color.White;tabs.TabPages.Add(p);return p;}
    void ColorButton(Control p,Button b,string label,int x,int y,Color initial) {b.BackColor=initial;b.ForeColor=initial.GetBrightness()<.5?Color.White:Color.Black;b.Text=label;Put(p,b,x,y,132,30);b.Click+=(s,e)=>{using(var d=new ColorDialog()){d.Color=b.BackColor;d.FullOpen=true;if(d.ShowDialog()==DialogResult.OK){b.BackColor=d.Color;b.ForeColor=d.Color.GetBrightness()<.5?Color.White:Color.Black;canvas.Ink=ink.BackColor;}}};}
    string Hex(Color c) {return c.R.ToString("x2")+c.G.ToString("x2")+c.B.ToString("x2");}
    void BuildText() {
        var p=Tab("Text + emoji");LabelAt(p,"Your message (paste emoji, or choose from the palette below)",14,10,680);
        Put(p,message,14,35,686,48);message.Multiline=true;message.MaxLength=500;message.Text="WELCOME!";
        ColorButton(p,fore,"Text color",14,93,Color.White);ColorButton(p,back,"Background",156,93,Color.Black);ColorButton(p,accent,"Second color",298,93,Color.FromArgb(0,0,48));
        LabelAt(p,"Background",14,132,125);Combo(p,background,14,153,132,new[]{"Solid","Gradient","Stripes","Rainbow"});
        LabelAt(p,"Letter colors",156,132,132);Combo(p,palette,156,153,132,new[]{"Solid","Rainbow","Red / green","Candy cane"});
        LabelAt(p,"Font",298,132,100);Combo(p,font,298,153,100,new[]{"Pixel","Bold","Regular"});
        LabelAt(p,"Size",410,132,70);Number(p,fontSize,410,153,65,6,16,13);
        LabelAt(p,"Motion",490,132,100);Combo(p,animation,490,153,210,new[]{"Scroll left","Still","Scroll right","Blink","Fade"});
        LabelAt(p,"Decoration",14,190,132);Combo(p,effect,14,212,132,new[]{"None","Snow","Sparkle","Confetti"});
        LabelAt(p,"Speed (0-100)",156,190,132);Number(p,speed,156,212,90,0,100,DefaultSpeed);
        LabelAt(p,"Emoji palette - click to insert",270,211,430);
        var panel=new FlowLayoutPanel();Put(p,panel,14,251,686,113);panel.AutoScroll=true;
        var entries=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(Path.Combine(root,"assets","emojis.json"),Encoding.UTF8));
        foreach(var entry in entries) {string token=entry["text"].ToString();var b=new Button();b.Size=new Size(39,39);b.Margin=new Padding(2);b.AccessibleName=entry["name"].ToString();using(var original=Image.FromFile(Path.Combine(root,"assets",entry["file"].ToString())))b.Image=new Bitmap(original,new Size(27,27));tips.SetToolTip(b,entry["name"].ToString());b.Click+=(s,e)=>{message.SelectedText=token;message.Focus();};panel.Controls.Add(b);}
    }
    void BuildThemes() {
        var p=Tab("Themes");LabelAt(p,"HOLIDAYS + OCCASIONS",14,12,680);Put(p,themes,14,43,350,308);
        themeData=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(Path.Combine(root,"assets","themes.json"),Encoding.UTF8));
        foreach(var t in themeData)themes.Items.Add(t["name"]);
        LabelAt(p,"Choose a theme, then customize its message, colors, emoji and effects in Text + emoji.",385,48,300,85);
        LabelAt(p,"Themes include Christmas, winter, New Year, Valentine's Day, St. Patrick's Day, Easter, July 4th, Halloween, Thanksgiving, birthdays, weddings and more.",385,148,300,130);
        Btn(p,"Use selected theme",385,295,280,()=>{if(themes.SelectedIndex>=0){ApplyDesign(themeData[themes.SelectedIndex]);tabs.SelectedIndex=0;status.Text="Theme loaded. Click Preview to see it, or customize it first.";}});
        themes.DoubleClick+=(s,e)=>{if(themes.SelectedIndex>=0){ApplyDesign(themeData[themes.SelectedIndex]);tabs.SelectedIndex=0;}};
        themes.SelectedIndexChanged+=(s,e)=>{if(themes.SelectedIndex>=0)ApplyDesign(themeData[themes.SelectedIndex]);};
    }
    void BuildMedia() {
        var p=Tab("Image / GIF");LabelAt(p,"Send your own artwork or animations",14,18,680);
        Put(p,imagePath,14,52,540,27);Btn(p,"Browse...",565,49,135,()=>{using(var d=new OpenFileDialog()){d.Filter="Images / GIF|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff";if(d.ShowDialog()==DialogResult.OK)imagePath.Text=d.FileName;}});
        LabelAt(p,"Resize",14,108,200);Combo(p,fit,14,136,280,new[]{"fit","crop"});
        LabelAt(p,"Fit preserves the full image with background padding. Crop fills the sign and trims the edges. GIF animations loop on the sign.",14,190,660,75);
        LabelAt(p,"The sign is only 16 pixels tall. Simple, bold artwork is easier to read. GIFs can have up to 300 frames; large animations take longer to transfer.",14,280,660,75);
    }
    void BuildDrawing() {
        var p=Tab("Draw");LabelAt(p,"Pixel canvas - left click draws; right click erases",14,14,680);Put(p,canvas,14,52,686,150);
        ColorButton(p,ink,"Brush color",14,222,Color.White);
        Btn(p,"Clear canvas",160,222,140,()=>canvas.NewCanvas(canvas.Bitmap.Width,16));
        Btn(p,"New 96 x 16",315,222,170,()=>canvas.NewCanvas(96,16));Btn(p,"New 192 x 16",500,222,185,()=>canvas.NewCanvas(192,16));
        LabelAt(p,"Draw a logo, symbol, border or pixel message. Preview uses the selected preview size; sending fits it to every selected sign.",14,286,680,65);
    }
    void BuildSaved() {
        var p=Tab("Saved designs");LabelAt(p,"Your local design library",14,14,680);Put(p,saved,14,45,390,252);
        Btn(p,"Save current design",425,45,270,SaveDesign);Btn(p,"Load selected design",425,91,270,LoadDesign);
        Btn(p,"Refresh list",425,137,270,RefreshSaved);
        LabelAt(p,"Saved designs stay in this app's designs folder. Image designs reference the original image file, so keep it in the same location.",14,315,680,55);
    }
    void BuildDevice() {
        var p=Tab("Clock + tools");LabelAt(p,"Clock",14,12,300);LabelAt(p,"Style (0-8)",14,43,110);Number(p,clockStyle,14,69,85,0,8,1);
        Put(p,showDate,125,69,130,27);showDate.Text="Show date";showDate.Checked=true;Put(p,format24,270,69,140,27);format24.Text="24-hour time";
        Btn(p,"Show clock",485,66,210,()=>RunAction("clock"));
        LabelAt(p,"Scoreboard",14,124,250);Number(p,score1,14,154,90,0,99,0);Number(p,score2,125,154,90,0,99,0);Btn(p,"Show scores",250,151,200,()=>RunAction("scores"));
        LabelAt(p,"Stopwatch",14,211,250);Btn(p,"Start",14,240,130,()=>RunAction("timer","start"));Btn(p,"Pause",158,240,130,()=>RunAction("timer","pause"));Btn(p,"Stop",302,240,130,()=>RunAction("timer","stop"));
        LabelAt(p,"Saved sign presets",14,299,240);Btn(p,"Show selected slot",250,294,220,()=>RunAction("slot"));
        LabelAt(p,"Use the slot number below. Clock and tool layouts depend on the sign's firmware. Clock is synced to this computer's local time.",14,339,680,48);
    }
    class HistoryRow {
        public Dictionary<string,object> Data;
        public override string ToString(){DateTimeOffset time;string when=Get(Data,"sent_at","");if(DateTimeOffset.TryParse(when,out time))when=time.ToLocalTime().ToString("MMM d, h:mm:ss tt");return when+"   "+Get(Data,"summary",Get(Data,"action","Design"));}
    }
    void BuildHistory() {
        var p=Tab("Last sent");LabelAt(p,"Last successful sends from this controller",14,12,680);
        Put(p,historySign,14,40,420,28);historySign.DropDownStyle=ComboBoxStyle.DropDownList;historySign.SelectedIndexChanged+=(s,e)=>RefreshHistory();
        Btn(p,"Refresh history",470,38,225,RefreshHistory);
        Put(p,historyList,14,83,681,153);historyList.HorizontalScrollbar=true;historyList.SelectedIndexChanged+=(s,e)=>ViewHistory();
        Put(p,historyDetail,14,247,681,59);historyDetail.Multiline=true;historyDetail.ReadOnly=true;historyDetail.ScrollBars=ScrollBars.Vertical;
        Btn(p,"Load selected design in editor",14,316,310,LoadHistoryDesign);
        LabelAt(p,"History starts with this update. It cannot read the sign's screen or detect changes made from your phone. Selecting a record previews it without sending.",345,315,350,63);
    }
    void BuildFairy() {
        var p=Tab("Fairy lights");LabelAt(p,"HELLO FAIRY LIGHT SETS",14,10,680);
        Put(p,fairyLights,14,36,400,63);fairyLights.CheckOnClick=true;
        fairyLights.SelectedIndexChanged+=(s,e)=>{if(fairyLights.SelectedIndex>=0)fairyAlias.Text=Get(fairyDevices[fairyLights.SelectedIndex],"alias",Get(fairyDevices[fairyLights.SelectedIndex],"name",""));};
        Btn(p,"Scan for lights",430,34,125,()=>RunFairy("fairy_scan"));Btn(p,"Read status",565,34,130,()=>RunFairy("fairy_status"));
        Put(p,fairyAlias,430,75,160,26);Btn(p,"Rename",598,72,97,RenameFairy);
        LabelAt(p,"Brightness (%)",14,109,115);Number(p,fairyBrightness,14,133,95,0,100,30);Btn(p,"Apply brightness",125,130,150,()=>RunFairy("fairy_brightness"));
        Btn(p,"Turn on",300,130,110,()=>RunFairy("fairy_on"));Btn(p,"Turn off",425,130,110,()=>RunFairy("fairy_off"));
        ColorButton(p,fairyColor,"Choose color",14,179,Color.FromArgb(255,180,70));Btn(p,"Apply solid color",160,179,185,()=>RunFairy("fairy_color"));
        LabelAt(p,"Built-in holiday / decorative effect",14,223,350);Combo(p,fairyEffects,14,246,330,new[]{"Christmas"});
        LabelAt(p,"Effect number",360,223,135);Number(p,fairyEffectNumber,360,246,95,1,58,18);Btn(p,"Apply effect",480,243,215,()=>RunFairy("fairy_effect"));
        string path=Path.Combine(root,"assets","fairy-effects.json");if(File.Exists(path)){fairyEffectData=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(path,Encoding.UTF8));fairyEffects.Items.Clear();foreach(var effect in fairyEffectData)fairyEffects.Items.Add(effect["name"]);fairyEffects.SelectedIndexChanged+=(s,e)=>{if(fairyEffects.SelectedIndex>=0)fairyEffectNumber.Value=Convert.ToInt32(fairyEffectData[fairyEffects.SelectedIndex]["number"]);};if(fairyEffects.Items.Count>0)fairyEffects.SelectedIndex=0;}
        Put(p,fairyStatus,14,288,681,55);fairyStatus.Multiline=true;fairyStatus.ReadOnly=true;fairyStatus.ScrollBars=ScrollBars.Vertical;fairyStatus.Text="Select light sets and read their current status. Color/effect changes use the brightness above.";
        LabelAt(p,"Close Hello Fairy on your phone if a set is busy. Effects depend on the light controller. Colors and effects apply only to the light sets checked here.",14,351,681,32);
    }
    void BuildSchedule() {
        var p=Tab("Schedule");LabelAt(p,"Update title",14,10,250);Put(p,scheduleTitle,14,32,250,27);
        LabelAt(p,"Send date and time (Eastern)",14,66,250);Put(p,scheduleWhen,14,87,250,27);scheduleWhen.Format=DateTimePickerFormat.Custom;scheduleWhen.CustomFormat="MMM d, yyyy  h:mm tt";scheduleWhen.Value=DateTime.Now.AddMinutes(10);
        LabelAt(p,"Repeat",14,119,113);Combo(p,scheduleRepeat,14,140,113,new[]{"Once","Daily","Weekdays","Weekly"});LabelAt(p,"Through",141,119,123);Put(p,scheduleEnd,141,140,123,27);scheduleEnd.Format=DateTimePickerFormat.Short;scheduleEnd.Value=DateTime.Today.AddDays(14);
        LabelAt(p,"Design",14,173,250);Combo(p,scheduleDesign,14,195,250,new[]{"Current editor design","Keep selected entry's design"});
        LabelAt(p,"Target signs",14,228,250);Put(p,scheduleSigns,14,250,250,82);scheduleSigns.CheckOnClick=true;
        Put(p,scheduleGrid,282,14,415,192);scheduleGrid.ReadOnly=true;scheduleGrid.AllowUserToAddRows=false;scheduleGrid.AllowUserToDeleteRows=false;scheduleGrid.MultiSelect=false;scheduleGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;scheduleGrid.RowHeadersVisible=false;scheduleGrid.BackgroundColor=Color.White;scheduleGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        scheduleGrid.Columns.Add("when","Next (Eastern)");scheduleGrid.Columns.Add("title","Update");scheduleGrid.Columns.Add("signs","Signs");scheduleGrid.Columns.Add("status","Status");scheduleGrid.SelectionChanged+=(s,e)=>SelectSchedule();
        Btn(p,"Save entry",282,219,130,()=>RunSchedule("schedule_save"));Btn(p,"New entry",424,219,120,NewSchedule);Btn(p,"Pause / resume",556,219,141,()=>RunSchedule("schedule_toggle"));
        Btn(p,"Enable background sending",282,261,245,()=>RunSchedule("schedule_enable"));Btn(p,"Refresh",539,261,158,()=>RunSchedule("schedule_list"));
        Put(p,scheduleWorker,282,305,415,43);scheduleWorker.Text="Background sender status appears here after Refresh.";
        LabelAt(p,"Eastern time. Keep this computer awake, signed in, and in Bluetooth range. Transfers start at the planned time; large designs take longer. You can close this app.",14,345,683,38);
    }
    void LoadScheduleTargets() {scheduleSigns.Items.Clear();foreach(var d in devices)scheduleSigns.Items.Add(Get(d,"alias",Get(d,"name","Sign")),selectedAddresses.Contains(d["address"].ToString()));}
    void RefreshScheduleDesigns() {string selected=scheduleDesign.SelectedItem==null?null:scheduleDesign.SelectedItem.ToString();scheduleDesign.Items.Clear();scheduleDesign.Items.Add("Current editor design");scheduleDesign.Items.Add("Keep selected entry's design");foreach(string path in Directory.GetFiles(Path.Combine(root,"designs"),"*.json"))scheduleDesign.Items.Add("Saved: "+Path.GetFileNameWithoutExtension(path));scheduleDesign.SelectedIndex=selected!=null&&scheduleDesign.Items.Contains(selected)?scheduleDesign.Items.IndexOf(selected):0;}
    void NewSchedule() {editingSchedule=null;scheduleGrid.ClearSelection();scheduleTitle.Text="";scheduleWhen.Value=DateTime.Now.AddMinutes(10);scheduleEnd.Value=DateTime.Today.AddDays(14);scheduleRepeat.SelectedIndex=0;scheduleDesign.SelectedIndex=0;status.Text="New entry. Choose a design, signs and send time, then Save entry.";}
    void SelectSchedule() {
        if(scheduleGrid.SelectedRows.Count==0)return;var job=scheduleGrid.SelectedRows[0].Tag as Dictionary<string,object>;if(job==null)return;
        editingSchedule=job["id"].ToString();scheduleTitle.Text=job["title"].ToString();scheduleWhen.Value=DateTime.Parse(job["start_local"].ToString(),System.Globalization.CultureInfo.InvariantCulture);scheduleEnd.Value=DateTime.Parse(job["end_date"].ToString(),System.Globalization.CultureInfo.InvariantCulture);SetCombo(scheduleRepeat,job["repeat"].ToString());scheduleDesign.SelectedIndex=1;
        var addresses=new HashSet<string>();foreach(object item in (IEnumerable)job["devices"])addresses.Add(((Dictionary<string,object>)item)["address"].ToString());for(int i=0;i<devices.Count;i++)scheduleSigns.SetItemChecked(i,addresses.Contains(devices[i]["address"].ToString()));
        var mediaBySize=(Dictionary<string,object>)job["previews"];string size=previewSize.SelectedIndex==1?"192x16":"96x16";if(!mediaBySize.ContainsKey(size))foreach(string key in mediaBySize.Keys){size=key;break;}if(mediaBySize.ContainsKey(size)){var media=(Dictionary<string,object>)mediaBySize[size];string path=Path.Combine(root,media["path"].ToString());if(File.Exists(path)){preview.LoadFile(path);prepared=new Dictionary<string,object>(media);prepared["path"]=path;previewKey=null;previewCaption.Text="Planned design: "+job["title"]+" — "+size+" pixels";}}
    }
    void RunSchedule(string action) {
        if(worker!=null)return;var request=new Dictionary<string,object>{{"action",action}};
        if(action=="schedule_save") {
            var addresses=new List<string>();foreach(int i in scheduleSigns.CheckedIndices)addresses.Add(devices[i]["address"].ToString());
            request["id"]=editingSchedule;request["title"]=scheduleTitle.Text;request["start_local"]=scheduleWhen.Value.ToString("yyyy-MM-ddTHH:mm");request["end_date"]=scheduleEnd.Value.ToString("yyyy-MM-dd");request["repeat"]=scheduleRepeat.SelectedItem;request["addresses"]=addresses;
            if(scheduleDesign.SelectedIndex==1){if(editingSchedule==null){status.Text="Choose a design for this new entry.";return;}request["retain_design"]=true;}
            else if(scheduleDesign.SelectedIndex>=2){string name=scheduleDesign.SelectedItem.ToString().Substring(7);request["design"]=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"designs",name+".json"),Encoding.UTF8));}
            else request["design"]=ContentRequest();
        } else if(action=="schedule_toggle"){if(editingSchedule==null){status.Text="Select a planned update first.";return;}request["id"]=editingSchedule;}
        string id=Guid.NewGuid().ToString("N");requestPath=Path.Combine(Path.GetTempPath(),"led-"+id+"-request.json");responsePath=Path.Combine(Path.GetTempPath(),"led-"+id+"-response.json");operation=action;
        File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));var start=new ProcessStartInfo(Path.Combine(root,"runtime","python.exe"));start.Arguments="\""+Path.Combine(root,"controller.py")+"\" --request \""+requestPath+"\" --response \""+responsePath+"\"";start.UseShellExecute=false;start.CreateNoWindow=true;worker=Process.Start(start);UpdateButtons();status.Text=action=="schedule_save"?"Saving the plan and preparing an independent copy of its design...":"Checking the schedule and background sender...";timer.Start();
    }
    void HandleScheduleResult(Dictionary<string,object> response) {
        string remember=Get(response,"saved_id",editingSchedule);
        scheduleJobs.Clear();foreach(object obj in (IEnumerable)response["jobs"])scheduleJobs.Add((Dictionary<string,object>)obj);
        scheduleGrid.Rows.Clear();foreach(var job in scheduleJobs){string next=Get(job,"next_run","");DateTimeOffset date;string when=DateTimeOffset.TryParse(next,out date)?date.ToLocalTime().ToString("MMM d h:mm tt"):"—";var names=new List<string>();foreach(object obj in (IEnumerable)job["devices"]){var d=(Dictionary<string,object>)obj;names.Add(Get(d,"alias",Get(d,"name","Sign")));}int index=scheduleGrid.Rows.Add(when,job["title"],String.Join(", ",names),job["last_state"]);scheduleGrid.Rows[index].Tag=job;}
        scheduleGrid.ClearSelection();editingSchedule=null;
        if(remember!=null)foreach(DataGridViewRow row in scheduleGrid.Rows){var job=(Dictionary<string,object>)row.Tag;if(job["id"].ToString()==remember){row.Selected=true;SelectSchedule();break;}}
        var sender=(Dictionary<string,object>)response["worker"];string stamp=Get(sender,"finished_at",Get(sender,"started_at",""));DateTimeOffset checkedAt;scheduleWorker.Text=DateTimeOffset.TryParse(stamp,out checkedAt)?"Sender last checked: "+checkedAt.ToLocalTime().ToString("MMM d h:mm:ss tt")+" — "+Get(sender,"state",""):"Background sender has not checked yet. Click Enable background sending.";
        if(sender.ContainsKey("error"))scheduleWorker.Text=sender["error"].ToString();status.Text=Get(response,"message","Schedule loaded. Select a row to edit it, or click New entry.");
    }
    void LoadFairyDevices() {
        string path=Path.Combine(root,"fairy-devices.json");fairyLights.Items.Clear();if(!File.Exists(path))return;
        fairyDevices=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(path,Encoding.UTF8));foreach(var d in fairyDevices)fairyLights.Items.Add(Get(d,"alias",Get(d,"name","Fairy lights")),true);
    }
    void RenameFairy() {int index=fairyLights.SelectedIndex;if(index<0){status.Text="Select a light set row first.";return;}string name=fairyAlias.Text.Trim();if(name.Length==0)return;fairyDevices[index]["alias"]=name;File.WriteAllText(Path.Combine(root,"fairy-devices.json"),json.Serialize(fairyDevices),Encoding.UTF8);LoadFairyDevices();status.Text="Light set name saved on this computer.";}
    void RunFairy(string action) {
        if(worker!=null)return;
        var targets=new List<Dictionary<string,object>>();foreach(int i in fairyLights.CheckedIndices)targets.Add(fairyDevices[i]);
        if(action!="fairy_scan"&&targets.Count==0){status.Text="Check at least one Hello Fairy light set.";return;}
        var request=new Dictionary<string,object>{{"action",action},{"fairy_devices",targets},{"fairy_color",Hex(fairyColor.BackColor)},{"fairy_brightness",(int)fairyBrightness.Value},{"fairy_effect",(int)fairyEffectNumber.Value}};
        string id=Guid.NewGuid().ToString("N");requestPath=Path.Combine(Path.GetTempPath(),"led-"+id+"-request.json");responsePath=Path.Combine(Path.GetTempPath(),"led-"+id+"-response.json");operation=action;
        File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));
        var start=new ProcessStartInfo(Path.Combine(root,"runtime","python.exe"));start.Arguments="\""+Path.Combine(root,"controller.py")+"\" --request \""+requestPath+"\" --response \""+responsePath+"\"";start.UseShellExecute=false;start.CreateNoWindow=true;
        worker=Process.Start(start);UpdateButtons();status.Text="Working on Hello Fairy light sets...";fairyStatus.Text="Connecting and waiting for the lights to report their state...";timer.Start();
    }
    void HandleFairyResult(Dictionary<string,object> response) {
        if(operation=="fairy_scan"){LoadFairyDevices();fairyStatus.Text=response["message"].ToString();status.Text=fairyStatus.Text;return;}
        var lines=new List<string>();foreach(object obj in (IEnumerable)response["results"]){var row=(Dictionary<string,object>)obj;string name=Get(row,"name","Fairy lights");if(!(bool)row["ok"]){lines.Add(name+": "+row["error"]);continue;}
            var state=(Dictionary<string,object>)row["status"];string mode=Get(state,"mode","");string description=mode=="1"?"color #"+Get(state,"color",""):mode=="2"?Get(state,"effect_name","Built-in effect"):"mode "+mode;
            lines.Add(name+": "+((bool)state["power"]?"On":"Off")+" | "+description+(state.ContainsKey("brightness")?" | "+state["brightness"]+"%":""));
            if(row.ContainsKey("info")&&row["info"]!=null){var info=(Dictionary<string,object>)row["info"];foreach(var device in fairyDevices)if(device["address"].ToString()==row["address"].ToString())foreach(string key in new[]{"model","led_count","columns","rows"})if(info.ContainsKey(key))device[key]=info[key];}
        }fairyStatus.Text=String.Join(Environment.NewLine,lines);status.Text="Hello Fairy device reports:"+Environment.NewLine+fairyStatus.Text;File.WriteAllText(Path.Combine(root,"fairy-devices.json"),json.Serialize(fairyDevices),Encoding.UTF8);
    }
    void RefreshHistory() {
        historyList.Items.Clear();historyEntries.Clear();int selected=historySign.SelectedIndex;
        if(selected<0||selected>=devices.Count)return;
        string path=Path.Combine(root,"history.json");if(File.Exists(path))historyEntries=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(path,Encoding.UTF8));
        string address=devices[selected]["address"].ToString();
        foreach(var entry in historyEntries)if(Get(entry,"address","")==address)historyList.Items.Add(new HistoryRow{Data=entry});
        if(historyList.Items.Count>0)historyList.SelectedIndex=0;
        else{historyDetail.Text="No successful sends recorded for this sign yet. Earlier sends were not recorded by the previous version.";if(tabs.SelectedIndex==6){preview.ClearImage();prepared=null;previewKey=null;previewCaption.Text="No recorded send for "+Get(devices[selected],"alias",Get(devices[selected],"name","this sign"));}}
    }
    void ViewHistory() {
        var selected=historyList.SelectedItem as HistoryRow;if(selected==null)return;var entry=selected.Data;
        historyDetail.Text=selected.ToString()+Environment.NewLine+"Action: "+Get(entry,"action","")+" | Address: "+Get(entry,"address","");
        if(tabs.SelectedIndex!=6)return;
        if(entry.ContainsKey("preview")){var media=(Dictionary<string,object>)entry["preview"];string path=Path.Combine(root,media["path"].ToString());if(File.Exists(path)){preview.LoadFile(path);prepared=new Dictionary<string,object>(media);prepared["path"]=path;previewKey=null;previewCaption.Text="Last sent to "+historySign.Text+" - "+selected.ToString();return;}}
        preview.ClearImage();prepared=null;previewKey=null;previewCaption.Text="Recorded command: "+Get(entry,"summary","")+" (no screen image available)";
    }
    void LoadHistoryDesign() {
        var selected=historyList.SelectedItem as HistoryRow;if(selected==null){status.Text="Select a history record first.";return;}
        var entry=selected.Data;string action=Get(entry,"action","");if(action!="design"&&action!="text"){status.Text="This record is a device command. Its details are shown above.";return;}
        var design=new Dictionary<string,object>((Dictionary<string,object>)entry["request"]);
        if(Get(design,"content","text")=="image"&&design.ContainsKey("image_path"))design["image_path"]=Path.Combine(root,design["image_path"].ToString());
        ApplyDesign(design);currentContent=Get(design,"content","text");tabs.SelectedIndex=currentContent=="image"?2:0;previewSize.SelectedIndex=Get(design,"width","96")=="192"?1:0;status.Text="Recorded design loaded. Nothing was sent to the sign.";
    }
    void LoadDevices() {
        string path=Path.Combine(root,"devices.json");if(!File.Exists(path))return;
        if(signs.Items.Count>0){selectedAddresses.Clear();foreach(int i in signs.CheckedIndices)selectedAddresses.Add(devices[i]["address"].ToString());}
        devices=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(path,Encoding.UTF8));signs.Items.Clear();
        foreach(var d in devices) {string label=Get(d,"alias",Get(d,"name","Sign"));string size=Get(d,"width","")+"x"+Get(d,"height","");signs.Items.Add(label+"  "+size+"  ["+d["address"].ToString().Substring(12)+"]",selectedAddresses.Contains(d["address"].ToString()));}
        int previous=historySign.SelectedIndex;historySign.Items.Clear();foreach(var d in devices)historySign.Items.Add(Get(d,"alias",Get(d,"name","Sign")));if(devices.Count>0)historySign.SelectedIndex=previous>=0&&previous<devices.Count?previous:(signs.CheckedIndices.Count>0?signs.CheckedIndices[0]:0);
    }
    void Rename() {int i=signs.SelectedIndex;if(i<0){status.Text="Select a sign row to rename it.";return;}string name=alias.Text.Trim();if(name.Length==0)return;devices[i]["alias"]=name;File.WriteAllText(Path.Combine(root,"devices.json"),json.Serialize(devices),Encoding.UTF8);LoadDevices();status.Text="Sign name saved on this computer.";}
    Dictionary<string,object> ContentRequest() {
        string content=currentContent;
        string path=imagePath.Text;
        if(content=="drawing") {Directory.CreateDirectory(Path.Combine(root,"drawings"));path=Path.Combine(root,"drawings","canvas.png");canvas.Bitmap.Save(path,ImageFormat.Png);content="image";}
        return new Dictionary<string,object>{{"content",content},{"text",message.Text},{"color",Hex(fore.BackColor)},{"bg_color",Hex(back.BackColor)},{"bg_color2",Hex(accent.BackColor)},
            {"background",background.SelectedItem},{"text_palette",palette.SelectedItem},{"font",font.SelectedItem},{"font_size",(int)fontSize.Value},{"effect",effect.SelectedItem},
            {"animation",animation.SelectedItem.ToString()=="Still"?"static":animation.SelectedItem.ToString().ToLowerInvariant().Replace(' ','_')},{"speed",(int)speed.Value},{"image_path",path},{"fit",fit.SelectedItem},{"width",previewSize.SelectedIndex==1?192:96},{"height",16}};
    }
    void ApplyDesign(Dictionary<string,object> d) {
        message.Text=Get(d,"text","WELCOME!");SetColor(fore,Get(d,"color","ffffff"));SetColor(back,Get(d,"bg_color","000000"));SetColor(accent,Get(d,"bg_color2","000030"));
        SetCombo(background,Get(d,"background","Solid"));SetCombo(palette,Get(d,"text_palette","Solid"));SetCombo(font,Get(d,"font","Pixel"));SetCombo(effect,Get(d,"effect","None"));string motion=Get(d,"animation","scroll_left");SetCombo(animation,motion=="static"?"Still":System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(motion.Replace('_',' ')).Replace("Left","left").Replace("Right","right"));
        fontSize.Value=Math.Max(6,Math.Min(16,Int32.Parse(Get(d,"font_size","13"))));speed.Value=Math.Max(0,Math.Min(100,Int32.Parse(Get(d,"speed",DefaultSpeed.ToString()))));
        imagePath.Text=Get(d,"image_path","");SetCombo(fit,Get(d,"fit","fit"));
    }
    void SetColor(Button b,string hex) {b.BackColor=ColorTranslator.FromHtml("#"+hex);b.ForeColor=b.BackColor.GetBrightness()<.5?Color.White:Color.Black;}
    void SetCombo(ComboBox c,string value) {if(c.Items.Contains(value))c.SelectedItem=value;}
    void RefreshSaved() {Directory.CreateDirectory(Path.Combine(root,"designs"));saved.Items.Clear();foreach(string path in Directory.GetFiles(Path.Combine(root,"designs"),"*.json"))saved.Items.Add(Path.GetFileNameWithoutExtension(path));}
    void SaveDesign() {
        var d=ContentRequest();using(var dialog=new SaveFileDialog()){dialog.InitialDirectory=Path.Combine(root,"designs");dialog.Filter="Design|*.json";dialog.FileName="My design.json";
            if(dialog.ShowDialog()==DialogResult.OK){if(currentContent=="drawing"){string image=Path.ChangeExtension(dialog.FileName,".png");canvas.Bitmap.Save(image,ImageFormat.Png);d["image_path"]=image;}File.WriteAllText(dialog.FileName,json.Serialize(d),Encoding.UTF8);RefreshSaved();status.Text="Design saved.";}}
    }
    void LoadDesign() {if(saved.SelectedItem==null)return;var d=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"designs",saved.SelectedItem+".json"),Encoding.UTF8));ApplyDesign(d);tabs.SelectedIndex=Get(d,"content","text")=="image"?2:0;status.Text="Design loaded.";}
    void ExportPreview() {if(prepared==null){status.Text="Create a preview first.";return;}string path=prepared["path"].ToString();using(var d=new SaveFileDialog()){d.Filter=Path.GetExtension(path)==".gif"?"GIF animation|*.gif":"PNG image|*.png";d.FileName="LED design"+Path.GetExtension(path);if(d.ShowDialog()==DialogResult.OK){File.Copy(path,d.FileName,true);status.Text="Preview exported.";}}}
    void UpdateButtons() {bool ready=worker==null;bool fairy=tabs.SelectedIndex==7;foreach(var b in actionButtons)b.Enabled=ready&&!(fairy&&b.Parent==this&&b.Left<280);tabs.Enabled=ready;signs.Enabled=ready&&!fairy;alias.Enabled=ready&&!fairy;brightness.Enabled=ready&&!fairy;previewSize.Enabled=ready&&!fairy;persist.Enabled=ready&&!fairy;slot.Enabled=ready&&!fairy;previewButton.Enabled=ready&&tabs.SelectedIndex<5;send.Enabled=ready&&tabs.SelectedIndex<5;stopSending.Enabled=!ready&&!stopRequested&&!operation.StartsWith("schedule_")&&operation!="preview"&&operation!="scan"&&operation!="fairy_scan";}
    void RequestStop() {if(worker==null||responsePath==null)return;try{File.WriteAllText(responsePath+".cancel","stop");stopRequested=true;stopSending.Enabled=false;status.Text="Stop requested. A transfer already in progress will finish safely; remaining signs will not be sent.";}catch(Exception ex){status.Text="Could not request stop: "+ex.Message;}}
    void RunAction(string action,string timerAction="start") {
        if(worker!=null)return;
        stopRequested=false;
        try {
            var targets=new List<Dictionary<string,object>>();foreach(int i in signs.CheckedIndices)targets.Add(devices[i]);
            if(action!="scan"&&action!="preview"&&targets.Count==0){status.Text="Select at least one sign.";return;}
            var request=(action=="design"||action=="preview")?ContentRequest():new Dictionary<string,object>();
            string key=json.Serialize(request);
            if(action=="design"&&prepared!=null&&previewKey==key&&currentContent!="drawing")request["prepared"]=prepared;
            request["action"]=action;request["devices"]=targets;request["brightness"]=(int)brightness.Value;
            request["save_slot"]=persist.Checked?(int)slot.Value:0;request["slot"]=(int)slot.Value;request["clock_style"]=(int)clockStyle.Value;
            request["show_date"]=showDate.Checked;request["format_24"]=format24.Checked;request["score1"]=(int)score1.Value;request["score2"]=(int)score2.Value;request["timer_action"]=timerAction;
            if(action=="preview")previewKey=key;
            string id=Guid.NewGuid().ToString("N");requestPath=Path.Combine(Path.GetTempPath(),"led-"+id+"-request.json");responsePath=Path.Combine(Path.GetTempPath(),"led-"+id+"-response.json");operation=action;
            File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));
            var start=new ProcessStartInfo(Path.Combine(root,"runtime","python.exe"));start.Arguments="\""+Path.Combine(root,"controller.py")+"\" --request \""+requestPath+"\" --response \""+responsePath+"\"";
            start.UseShellExecute=false;start.CreateNoWindow=true;worker=Process.Start(start);UpdateButtons();
            status.Text=action=="preview"?"Rendering preview...":"Working: "+action+". Signs are handled one at a time. Animated designs may take a minute to transfer.";timer.Start();
        }catch(Exception ex){status.Text=ex.Message;worker=null;UpdateButtons();}
    }
    void Poll(object sender,EventArgs e) {
        if(worker==null)return;
        if(!worker.HasExited){string path=responsePath+".progress.json";if(File.Exists(path)){try{var progress=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));var lines=new List<string>();if(stopRequested)lines.Add("Stopping after the current transfer; queued signs are cancelled.");lines.Add(progress["message"].ToString());foreach(object item in (IEnumerable)progress["completed"]){var row=(Dictionary<string,object>)item;lines.Add(row["name"]+((bool)row["ok"]?": completed":": failed / not attempted"));}status.Text=String.Join(Environment.NewLine,lines);}catch{}}return;}
        timer.Stop();
        try {
            var r=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(responsePath,Encoding.UTF8));
            if(r.ContainsKey("error"))status.Text=r["error"].ToString();
            else if(operation.StartsWith("schedule_"))HandleScheduleResult(r);
            else if(operation.StartsWith("fairy_"))HandleFairyResult(r);
            else if(operation=="preview") {prepared=(Dictionary<string,object>)r["preview"];preview.LoadFile(prepared["path"].ToString());previewCaption.Text=prepared["width"]+" x "+prepared["height"]+" pixels | "+prepared["frames"]+" frame(s) | "+prepared["bytes"]+" bytes";status.Text="Preview ready. Click Send to selected signs when you like the design.";}
            else if(operation=="scan"){LoadDevices();status.Text=r["message"].ToString();}
            else {
                var lines=new List<string>();foreach(object obj in (IEnumerable)r["results"]) {var row=(Dictionary<string,object>)obj;string name=Get(row,"name","Sign");foreach(var d in devices)if(d["address"].ToString()==row["address"].ToString())name=Get(d,"alias",name);
                    if((bool)row["ok"]) {var info=(Dictionary<string,object>)row["info"];lines.Add(name+": OK - "+info["width"]+" x "+info["height"]+" - "+operation+" completed");if(row.ContainsKey("history_warning"))lines.Add(row["history_warning"].ToString());if(row.ContainsKey("connection_warning"))lines.Add(row["connection_warning"].ToString());foreach(var d in devices)if(d["address"].ToString()==row["address"].ToString()){d["width"]=info["width"];d["height"]=info["height"];}}
                    else lines.Add(name+": "+row["error"]);
                }status.Text=String.Join(Environment.NewLine,lines);File.WriteAllText(Path.Combine(root,"devices.json"),json.Serialize(devices),Encoding.UTF8);
            }
        }catch(Exception ex){status.Text=ex.Message;}
        finally{worker.Dispose();worker=null;stopRequested=false;UpdateButtons();try{File.Delete(requestPath);File.Delete(responsePath);File.Delete(responsePath+".progress.json");File.Delete(responsePath+".cancel");}catch{}}
    }
    [STAThread]public static void Main(string[] args) {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var app=new SignStudio()) {if(args.Length>0&&args[0]=="--selfcheck"){File.WriteAllText(Path.Combine(app.root,"studio-check.txt"),"Startup OK\r\nSigns: "+app.devices.Count+"\r\nFairy lights: "+app.fairyDevices.Count+"\r\nThemes: "+app.themeData.Count+"\r\nTabs: "+app.tabs.TabPages.Count);return;}if(Array.IndexOf(args,"--minimized")>=0)app.WindowState=FormWindowState.Minimized;if(Array.IndexOf(args,"--fairy")>=0)app.tabs.SelectedIndex=7;if(Array.IndexOf(args,"--schedule")>=0)app.tabs.SelectedIndex=8;Application.Run(app);}
    }
}
