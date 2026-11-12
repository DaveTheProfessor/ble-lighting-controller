using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class SignController : Form {
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly CheckedListBox signs = new CheckedListBox();
    readonly TextBox message = new TextBox(), status = new TextBox();
    readonly ComboBox color = new ComboBox(), animation = new ComboBox();
    readonly NumericUpDown speed = new NumericUpDown(), brightness = new NumericUpDown();
    readonly List<Button> buttons = new List<Button>();
    readonly Timer timer = new Timer();
    readonly string[] colors = {"ffffff","ff0000","00ff00","0000ff","ffff00","00ffff","ff00ff"};
    List<Dictionary<string,object>> devices = new List<Dictionary<string,object>>();
    Process worker;
    string requestPath, responsePath, operation;
    public SignController() {
        Text="LED Sign Controller"; ClientSize=new Size(660,660); StartPosition=FormStartPosition.CenterScreen;
        FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; Font=new Font("Segoe UI",10);
        LabelAt("Select the signs to control",20,16,620);
        Place(signs,20,46,620,100); signs.CheckOnClick=true;
        AddButton("Scan for signs",20,156,150,"scan"); AddButton("Check connection",180,156,170,"info");
        LabelAt("Message",20,205,620); Place(message,20,232,620,32); message.MaxLength=500;
        LabelAt("Color",20,275,140); Place(color,20,302,140,30); color.DropDownStyle=ComboBoxStyle.DropDownList;
        color.Items.AddRange(new object[]{"White","Red","Green","Blue","Yellow","Cyan","Magenta"}); color.SelectedIndex=0;
        LabelAt("Animation",180,275,170); Place(animation,180,302,170,30); animation.DropDownStyle=ComboBoxStyle.DropDownList;
        animation.Items.AddRange(new object[]{"scroll_left","static","scroll_right"}); animation.SelectedIndex=0;
        LabelAt("Speed (0-100)",370,275,120); Place(speed,370,302,100,30); speed.Maximum=100; speed.Value=60;
        AddButton("Send message",490,299,150,"text");
        LabelAt("Brightness (%)",20,354,180); Place(brightness,20,383,100,30); brightness.Maximum=100; brightness.Value=50;
        AddButton("Apply brightness",135,380,165,"brightness"); AddButton("Turn on",330,380,140,"on"); AddButton("Turn off",490,380,150,"off");
        LabelAt("Close iPixel Color on your iPhone if a sign is busy or cannot connect.",20,433,620);
        Place(status,20,467,620,170); status.Multiline=true; status.ReadOnly=true; status.ScrollBars=ScrollBars.Vertical;
        status.Text="Ready. Select signs, then check the connection or send a message.";
        timer.Interval=400; timer.Tick+=Poll;
        LoadDevices();
        FormClosing+=(s,e)=>{ if(worker!=null && !worker.HasExited) { e.Cancel=true; status.Text="Please wait for the current Bluetooth operation to finish."; } };
    }
    void Place(Control c,int x,int y,int w,int h) { c.SetBounds(x,y,w,h); Controls.Add(c); }
    void LabelAt(string text,int x,int y,int w) { Label l=new Label(); l.Text=text; Place(l,x,y,w,25); }
    void AddButton(string text,int x,int y,int w,string action) {
        Button b=new Button(); b.Text=text; Place(b,x,y,w,34); b.Click+=(s,e)=>RunAction(action); buttons.Add(b);
    }
    void LoadDevices() {
        string path=Path.Combine(root,"devices.json");
        if(!File.Exists(path))return;
        devices=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(path));
        signs.Items.Clear(); foreach(var d in devices) signs.Items.Add(d["name"]+"   ["+d["address"]+"]",true);
    }
    void RunAction(string action) {
        if(worker!=null)return;
        var targets=new List<Dictionary<string,object>>();
        foreach(int i in signs.CheckedIndices) targets.Add(devices[i]);
        if(action!="scan" && targets.Count==0) {status.Text="Select at least one sign.";return;}
        if(action=="text" && String.IsNullOrWhiteSpace(message.Text)) {status.Text="Enter a message first.";return;}
        var request=new Dictionary<string,object>{{"action",action},{"devices",targets},{"text",message.Text},{"color",colors[color.SelectedIndex]},
            {"animation",animation.SelectedItem.ToString()},{"speed",(int)speed.Value},{"brightness",(int)brightness.Value}};
        string id=Guid.NewGuid().ToString("N"); requestPath=Path.Combine(Path.GetTempPath(),"led-"+id+"-request.json");
        responsePath=Path.Combine(Path.GetTempPath(),"led-"+id+"-response.json"); operation=action;
        try {
            File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));
            var start=new ProcessStartInfo(Path.Combine(root,"runtime","python.exe"));
            start.Arguments="\""+Path.Combine(root,"controller.py")+"\" --request \""+requestPath+"\" --response \""+responsePath+"\"";
            start.UseShellExecute=false; start.CreateNoWindow=true; worker=Process.Start(start);
            foreach(var b in buttons)b.Enabled=false;
            status.Text="Working: "+action+". Connecting to signs can take a few seconds..."; timer.Start();
        } catch(Exception e) {status.Text=e.Message;worker=null;}
    }
    void Poll(object sender,EventArgs e) {
        if(worker==null || !worker.HasExited)return;
        timer.Stop();
        try {
            var response=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(responsePath));
            if(response.ContainsKey("error"))status.Text=response["error"].ToString();
            else if(operation=="scan") {LoadDevices();status.Text=response["message"].ToString();}
            else {
                var lines=new List<string>();
                foreach(object obj in (System.Collections.IEnumerable)response["results"]) {
                    var r=(Dictionary<string,object>)obj;
                    if((bool)r["ok"]) {var info=(Dictionary<string,object>)r["info"]; lines.Add(r["name"]+": OK - "+info["width"]+"x"+info["height"]+" - "+operation+" completed");}
                    else lines.Add(r["name"]+": "+r["error"]);
                }
                status.Text=String.Join(Environment.NewLine,lines);
            }
        } catch(Exception ex) {status.Text=ex.Message;}
        finally {
            worker.Dispose();worker=null;foreach(var b in buttons)b.Enabled=true;
            try {File.Delete(requestPath);File.Delete(responsePath);}catch{}
        }
    }
    [STAThread] public static void Main() {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SignController());}
}
