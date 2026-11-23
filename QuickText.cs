using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QuickText
{
    static class Native
    {
        [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputUnion {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int dx, dy; public uint mouseData, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct HARDWAREINPUT { public uint msg; public ushort paramL, paramH; }
        internal const uint ALT=1, CTRL=2, SHIFT=4, WIN=8, NOREPEAT=0x4000, KEYBOARD=1, KEYUP=2, UNICODE=4;
        internal static readonly uint SHOW_SETTINGS=RegisterWindowMessage("QuickText_ShowSettings_7D9E6B5A");
    }

    class ShortcutItem
    {
        public string Text=""; public Keys Key=Keys.None; public Keys Modifiers=Keys.None;
        public string Label {
            get {
                var p=new List<string>();
                if((Modifiers&Keys.Control)!=0)p.Add("Ctrl"); if((Modifiers&Keys.Alt)!=0)p.Add("Alt");
                if((Modifiers&Keys.Shift)!=0)p.Add("Shift"); if((Modifiers&Keys.LWin)!=0)p.Add("Win");
                p.Add(Key.ToString()); return string.Join(" + ",p.ToArray());
            }
        }
        public ShortcutItem Copy(){return new ShortcutItem{Text=Text,Key=Key,Modifiers=Modifiers};}
    }

    static class Store
    {
        static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuickText");
        static readonly string PathName=Path.Combine(Folder,"shortcuts.txt");
        internal static List<ShortcutItem> Load(){
            var r=new List<ShortcutItem>();
            if(!File.Exists(PathName)){
                r.Add(new ShortcutItem{Text="✓",Key=Keys.D1,Modifiers=Keys.Control|Keys.Alt});
                r.Add(new ShortcutItem{Text="→",Key=Keys.D2,Modifiers=Keys.Control|Keys.Alt});
                r.Add(new ShortcutItem{Text="★",Key=Keys.D3,Modifiers=Keys.Control|Keys.Alt}); return r;
            }
            foreach(string line in File.ReadAllLines(PathName,Encoding.UTF8)){
                if(String.IsNullOrWhiteSpace(line)||line.StartsWith("#"))continue;
                string[] f=line.Split(new[]{'\t'},3); int k,m;
                if(f.Length!=3||!int.TryParse(f[0],out k)||!int.TryParse(f[1],out m))continue;
                try{r.Add(new ShortcutItem{Key=(Keys)k,Modifiers=(Keys)m,Text=Encoding.UTF8.GetString(Convert.FromBase64String(f[2]))});}catch{}
            } return r;
        }
        internal static void Save(IEnumerable<ShortcutItem> items){
            Directory.CreateDirectory(Folder); var lines=new List<string>{"# Managed by QuickText"};
            foreach(var x in items)lines.Add((int)x.Key+"\t"+(int)x.Modifiers+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(x.Text)));
            File.WriteAllLines(PathName,lines.ToArray(),new UTF8Encoding(false));
        }
    }

    class HotkeyWindow:NativeWindow,IDisposable
    {
        readonly Dictionary<int,ShortcutItem> map=new Dictionary<int,ShortcutItem>();
        internal event Action<string> Failed;
        internal event Action ShowSettingsRequested;
        internal HotkeyWindow(){CreateHandle(new CreateParams());}
        internal void RegisterAll(IList<ShortcutItem> items){
            Clear(); var bad=new List<string>();
            for(int i=0;i<items.Count;i++){
                var x=items[i]; uint m=Native.NOREPEAT;
                if((x.Modifiers&Keys.Control)!=0)m|=Native.CTRL; if((x.Modifiers&Keys.Alt)!=0)m|=Native.ALT;
                if((x.Modifiers&Keys.Shift)!=0)m|=Native.SHIFT; if((x.Modifiers&Keys.LWin)!=0)m|=Native.WIN;
                int id=1000+i; if(Native.RegisterHotKey(Handle,id,m,(uint)x.Key))map[id]=x; else bad.Add(x.Label);
            }
            if(bad.Count>0&&Failed!=null)Failed(string.Join(", ",bad.ToArray()));
        }
        protected override void WndProc(ref Message m){
            ShortcutItem x;
            if(m.Msg==0x0312&&map.TryGetValue(m.WParam.ToInt32(),out x))Send(x.Text);
            if((uint)m.Msg==Native.SHOW_SETTINGS&&ShowSettingsRequested!=null)ShowSettingsRequested();
            base.WndProc(ref m);
        }
        static void Send(string text){
            if(String.IsNullOrEmpty(text))return; var a=new List<Native.INPUT>();
            foreach(char c in text){a.Add(Input(c,false));a.Add(Input(c,true));} var data=a.ToArray();
            Native.SendInput((uint)data.Length,data,Marshal.SizeOf(typeof(Native.INPUT)));
        }
        static Native.INPUT Input(char c,bool up){return new Native.INPUT{type=Native.KEYBOARD,data=new Native.InputUnion{keyboard=new Native.KEYBDINPUT{scan=c,flags=Native.UNICODE|(up?Native.KEYUP:0)}}};}
        void Clear(){foreach(int id in map.Keys)Native.UnregisterHotKey(Handle,id);map.Clear();}
        public void Dispose(){Clear();DestroyHandle();}
    }

    class ShortcutDialog:Form
    {
        readonly TextBox content=new TextBox(),hotkey=new TextBox(); Keys key=Keys.None,mods=Keys.None;
        internal ShortcutItem Result;
        internal ShortcutDialog(ShortcutItem old){
            Text=old==null?"단축키 추가":"단축키 수정"; Font=new Font("맑은 고딕",9); FormBorderStyle=FormBorderStyle.FixedDialog;
            Icon=Program.AppIcon;
            MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(430,178);
            var l1=new Label{Text="입력할 문자 또는 문구",AutoSize=true,Location=new Point(18,17)};
            content.Location=new Point(21,40);content.Size=new Size(388,25);
            var l2=new Label{Text="단축키 (아래 칸을 누르고 원하는 조합을 입력하세요)",AutoSize=true,Location=new Point(18,78)};
            hotkey.Location=new Point(21,101);hotkey.Size=new Size(388,25);hotkey.ReadOnly=true;hotkey.BackColor=Color.White;hotkey.KeyDown+=CaptureHotkey;
            var ok=new Button{Text="확인",Location=new Point(253,140),Size=new Size(75,27)};
            var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel,Location=new Point(334,140),Size=new Size(75,27)};
            ok.Click+=Save;CancelButton=cancel;Controls.AddRange(new Control[]{l1,content,l2,hotkey,ok,cancel});
            if(old!=null){content.Text=old.Text;key=old.Key;mods=old.Modifiers;hotkey.Text=old.Label;}
        }
        void CaptureHotkey(object s,KeyEventArgs e){
            e.SuppressKeyPress=true;e.Handled=true;
            if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.Menu||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.LWin||e.KeyCode==Keys.RWin)return;
            key=e.KeyCode;mods=e.Modifiers;hotkey.Text=new ShortcutItem{Key=key,Modifiers=mods}.Label;
        }
        void Save(object s,EventArgs e){
            if(String.IsNullOrEmpty(content.Text)){MessageBox.Show(this,"입력할 문자나 문구를 적어 주세요.","바로문자");return;}
            if(key==Keys.None||mods==Keys.None){MessageBox.Show(this,"Ctrl, Alt 또는 Shift가 포함된 단축키를 지정해 주세요.","바로문자");return;}
            Result=new ShortcutItem{Text=content.Text,Key=key,Modifiers=mods};DialogResult=DialogResult.OK;Close();
        }
    }

    class SettingsForm:Form
    {
        readonly ListView list=new ListView(); readonly Action<List<ShortcutItem>> apply; List<ShortcutItem> items;
        internal SettingsForm(List<ShortcutItem> source,Action<List<ShortcutItem>> onApply){
            items=Clone(source);apply=onApply;Text="바로문자 설정";Font=new Font("맑은 고딕",9);StartPosition=FormStartPosition.CenterScreen;
            Icon=Program.AppIcon;
            MinimumSize=new Size(570,390);ClientSize=new Size(650,420);
            var title=new Label{Text="단축키를 누르면 지정한 내용이 현재 입력칸에 바로 입력됩니다.",AutoSize=true,Location=new Point(16,16)};
            list.Location=new Point(18,47);list.Size=new Size(614,300);list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
            list.View=View.Details;list.FullRowSelect=true;list.GridLines=true;list.HideSelection=false;list.Columns.Add("문자 / 문구",370);list.Columns.Add("단축키",210);list.DoubleClick+=(s,e)=>Edit();
            var add=Button("추가",18);var edit=Button("수정",99);var del=Button("삭제",180);var save=Button("저장",476);var close=Button("닫기",557);
            add.Click+=(s,e)=>Add();edit.Click+=(s,e)=>Edit();del.Click+=(s,e)=>Delete();save.Click+=Save;close.Click+=(s,e)=>Close();
            foreach(var b in new[]{add,edit,del,save,close}){b.Top=365;b.Anchor=AnchorStyles.Bottom|(b==save||b==close?AnchorStyles.Right:AnchorStyles.Left);}
            Controls.AddRange(new Control[]{title,list,add,edit,del,save,close});RefreshList();
        }
        Button Button(string text,int left){return new Button{Text=text,Left=left,Width=75,Height=28};}
        void Add(){using(var d=new ShortcutDialog(null))if(d.ShowDialog(this)==DialogResult.OK&&!Duplicate(d.Result,-1)){items.Add(d.Result);RefreshList();}}
        void Edit(){if(list.SelectedIndices.Count==0)return;int n=list.SelectedIndices[0];using(var d=new ShortcutDialog(items[n]))if(d.ShowDialog(this)==DialogResult.OK&&!Duplicate(d.Result,n)){items[n]=d.Result;RefreshList();}}
        void Delete(){if(list.SelectedIndices.Count==0)return;items.RemoveAt(list.SelectedIndices[0]);RefreshList();}
        bool Duplicate(ShortcutItem x,int except){for(int i=0;i<items.Count;i++)if(i!=except&&items[i].Key==x.Key&&items[i].Modifiers==x.Modifiers){MessageBox.Show(this,"이미 등록된 단축키입니다.","바로문자");return true;}return false;}
        void Save(object s,EventArgs e){apply(Clone(items));MessageBox.Show(this,"저장했습니다. 단축키가 바로 적용됩니다.","바로문자");}
        void RefreshList(){list.Items.Clear();foreach(var x in items){string t=x.Text.Replace("\r"," ").Replace("\n"," ↵ ");if(t.Length>60)t=t.Substring(0,60)+"…";list.Items.Add(new ListViewItem(new[]{t,x.Label}));}}
        static List<ShortcutItem> Clone(IEnumerable<ShortcutItem> src){var r=new List<ShortcutItem>();foreach(var x in src)r.Add(x.Copy());return r;}
    }

    class TrayApp:ApplicationContext
    {
        readonly NotifyIcon tray;readonly HotkeyWindow hotkeys;List<ShortcutItem> items;SettingsForm form;
        internal TrayApp(){
            items=Store.Load();hotkeys=new HotkeyWindow();hotkeys.Failed+=x=>tray.ShowBalloonTip(5000,"바로문자","사용 중인 단축키: "+x,ToolTipIcon.Warning);hotkeys.ShowSettingsRequested+=ShowSettings;
            var menu=new ContextMenuStrip();menu.Items.Add("설정 열기",null,(s,e)=>ShowSettings());menu.Items.Add(new ToolStripSeparator());menu.Items.Add("종료",null,(s,e)=>Exit());
            tray=new NotifyIcon{Text="바로문자 - 단축키로 바로 입력",Icon=Program.AppIcon,ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(s,e)=>ShowSettings();hotkeys.RegisterAll(items);
            Application.Idle+=OpenInitialSettings;
        }
        void OpenInitialSettings(object sender,EventArgs e){Application.Idle-=OpenInitialSettings;ShowSettings();}
        void ShowSettings(){if(form!=null&&!form.IsDisposed){form.Show();form.WindowState=FormWindowState.Normal;form.Activate();form.BringToFront();return;}form=new SettingsForm(items,Apply);form.Show();form.Activate();form.BringToFront();}
        void Apply(List<ShortcutItem> x){Store.Save(x);items=x;hotkeys.RegisterAll(items);}
        void Exit(){tray.Visible=false;hotkeys.Dispose();tray.Dispose();ExitThread();}
    }

    static class Program
    {
        internal static Icon AppIcon { get { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } }
        [STAThread] static void Main(){bool created;using(var mutex=new Mutex(true,"Local\\QuickText_Korean_Utility",out created)){
            if(!created){Native.PostMessage(new IntPtr(0xffff),Native.SHOW_SETTINGS,IntPtr.Zero,IntPtr.Zero);return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new TrayApp());GC.KeepAlive(mutex);
        }}
    }
}
