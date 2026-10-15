using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static MongVolume.CoreAudio;

namespace MongVolume
{
    // 앱별 볼륨 믹서. 슬라이더 0~100% 는 Windows 앱 볼륨을 그대로 조절하고,
    // 100% 초과는 앱 볼륨을 낮춰 두고 캡처한 소리를 증폭해 재생한다(AmpEngine).
    // 같은 실행 파일이라도 프로세스 트리(루트 프로세스)가 다르면 별도 행으로 취급해 개별 조절한다.
    internal sealed class MainWindow : Window
    {
        const float AmpSessVol = 0.02f;   // 증폭 중 대상 앱 세션 볼륨(원본 소리는 거의 들리지 않게)
        const double MaxPercent = 200;
        public const string Version = "1.4.6";   // 기능을 바꿀 때마다 올린다(제목 표시줄에 표시되어 실행 중인 버전 확인용)

        sealed class Row
        {
            public string Key, Name, Path, MemoryKey;   // MemoryKey: 볼륨을 기억하는 단위(실행 파일 경로)
            public string TitleKey = "";               // 창 제목에서 숫자를 뺀 정규화 값(창별 기억용). 창 제목이 없으면 빈 문자열
            public int RootPid;
            public Grid Panel; public Slider Slider; public TextBlock Pct, Title; public Run State;
            public List<SessionRef> Sessions = new List<SessionRef>();
            public HashSet<string> Known = new HashSet<string>();   // 이미 볼륨을 맞춘 세션 ID
            public double Applied = 100;                            // 마지막으로 적용한 볼륨(%)
            public bool Amped, Suppress;
            public int FailCount;
            public string LastError = "";
            public DateTime RetryAt = DateTime.MinValue, LeaveAt = DateTime.MaxValue;
            public Dictionary<int, AmpEngine> Engines = new Dictionary<int, AmpEngine>();
        }

        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        readonly StackPanel list = new StackPanel();
        readonly TextBlock empty = new TextBlock();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly DispatcherTimer saveTimer = new DispatcherTimer();
        readonly int selfPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        readonly Dictionary<string, double> remembered = new Dictionary<string, double>();

        static readonly string DataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MongVolume");
        static readonly string StateFile = System.IO.Path.Combine(DataDir, "amped.txt");     // 비정상 종료 복구용
        static readonly string VolumeFile = System.IO.Path.Combine(DataDir, "volumes.txt");  // 앱별 기억된 볼륨
        static readonly string LogFile = System.IO.Path.Combine(DataDir, "log.txt");

        public MainWindow()
        {
            Title = "몽볼륨 v" + Version + " (" + BuildInfo.Commit + " · " + BuildInfo.BuiltAt + ")";
            Width = 560; Height = 460; MinWidth = 460; MinHeight = 240;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic"); FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch { }

            var dock = new DockPanel();
            var menu = BuildMenu(); DockPanel.SetDock(menu, Dock.Top); dock.Children.Add(menu);

            var status = new TextBlock
            {
                Text = "100% 초과는 앱 소리를 가로채 증폭합니다(약 40ms 지연). 소리가 찌그러지면 배율을 낮추세요.",
                Margin = new Thickness(12, 6, 12, 6), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap
            };
            var bar = new Border { Child = status, BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.White };
            DockPanel.SetDock(bar, Dock.Bottom); dock.Children.Add(bar);

            empty.Text = "소리를 내는 앱이 없습니다. 앱에서 소리를 재생하면 여기에 나타납니다.";
            empty.Margin = new Thickness(20); empty.Foreground = Brushes.Gray; empty.TextWrapping = TextWrapping.Wrap;
            var host = new StackPanel(); host.Children.Add(list); host.Children.Add(empty);
            dock.Children.Add(new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;

            LoadRemembered();
            RestoreLeftover();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (s, e) => Tick();
            saveTimer.Interval = TimeSpan.FromMilliseconds(600);
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveRemembered(); };
            Loaded += (s, e) =>
            {
                Tick(); timer.Start();
                // 앱을 실행할 때 새 버전이 있는지 확인 (있을 때만 안내, 사용자가 눌러야 받는다). 시험용 --at 실행에서는 하지 않는다.
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--at") < 0) MongUpdate.UpdateUi.CheckAtStartup(this, "mongvolume", "몽볼륨", Version, null);
            };
            Closing += (s, e) => Shutdown();
        }

        Menu BuildMenu()
        {
            var menu = new Menu { Background = Brushes.White };
            var file = new MenuItem { Header = "파일(_F)" };
            var exit = new MenuItem { Header = "끝내기(_X)", InputGestureText = "Alt+F4" };
            exit.Click += (s, e) => Close(); file.Items.Add(exit);

            var view = new MenuItem { Header = "보기(_V)" };
            var top = new MenuItem { Header = "항상 위에 표시(_T)", IsCheckable = true };
            top.Click += (s, e) => Topmost = top.IsChecked;
            var refresh = new MenuItem { Header = "새로 고침(_R)", InputGestureText = "F5" };
            refresh.Click += (s, e) => Tick();
            view.Items.Add(top); view.Items.Add(new Separator()); view.Items.Add(refresh);
            InputBindings.Add(new KeyBinding(new RelayCommand(Tick), Key.F5, ModifierKeys.None));

            var help = new MenuItem { Header = "도움말(_H)" };
            var about = new MenuItem { Header = "몽볼륨 정보(_A)" };
            about.Click += (s, e) => MessageBox.Show(this,
                "몽볼륨 v" + Version + "\n빌드: " + BuildInfo.Commit + " · " + BuildInfo.BuiltAt + "\n\n앱별 볼륨을 조절하고, 100%를 넘겨 증폭할 수 있습니다.\n\n" +
                "· 0~100%: Windows 앱 볼륨 그대로 조절\n· 100% 초과: 앱 소리를 캡처해 증폭 후 재생(최대 200%)\n" +
                "· 앱별 볼륨은 기억했다가 다음에 그 앱이 소리를 내면 다시 맞춥니다.\n" +
                "· 같은 프로그램이라도 별도 프로세스로 뜬 창(예: 포커 테이블)은 따로 조절합니다.\n\n" +
                "비정상 종료로 앱 볼륨이 낮게 남으면 다음 실행 때 자동으로 복원합니다.",
                "몽볼륨 정보", MessageBoxButton.OK, MessageBoxImage.Information);
            help.Items.Add(about);
            var upd = new MenuItem { Header = "업데이트 확인(_U)" };
            upd.Click += (s, e) => MongUpdate.UpdateUi.CheckNow(this, "mongvolume", "몽볼륨", Version, null);
            help.Items.Add(upd);

            menu.Items.Add(file); menu.Items.Add(view); menu.Items.Add(help);
            return menu;
        }

        sealed class RelayCommand : ICommand
        {
            readonly Action a; public RelayCommand(Action a) { this.a = a; }
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object p) { return true; }
            public void Execute(object p) { a(); }
        }

        // ---------- 주기 갱신 ----------
        void Tick()
        {
            List<SessionRef> sessions;
            try { sessions = EnumerateSessions(); } catch { return; }
            var parents = ProcessUtil.ParentMap();
            var titles = ProcessUtil.WindowTitles();

            var groups = new Dictionary<string, List<SessionRef>>();
            var meta = new Dictionary<string, KeyValuePair<string, int>>();   // 키 → (실행 파일 경로, 루트 PID)
            foreach (var s in sessions)
            {
                if ((int)s.Pid == selfPid) continue;
                if (s.State == 2 || !ProcessUtil.IsAlive((int)s.Pid)) continue;   // 만료됐거나 이미 종료된 프로세스의 세션
                string path = ProcessUtil.GetPath((int)s.Pid), key; int root = (int)s.Pid;
                if (s.Pid == 0) key = "system";
                else if (path != null) { root = ProcessUtil.RootPid((int)s.Pid, path, parents); key = path.ToLowerInvariant() + "|" + root; }
                else key = "pid:" + s.Pid;
                List<SessionRef> l; if (!groups.TryGetValue(key, out l)) { groups[key] = l = new List<SessionRef>(); meta[key] = new KeyValuePair<string, int>(path, root); }
                l.Add(s);
            }

            foreach (var key in rows.Keys.Where(k => !groups.ContainsKey(k)).ToList()) RemoveRow(rows[key]);

            foreach (var kv in groups)
            {
                Row row; bool isNew = false;
                if (!rows.TryGetValue(kv.Key, out row)) { row = AddRow(kv.Key, meta[kv.Key].Key, meta[kv.Key].Value); isNew = true; }
                row.Sessions = kv.Value;
                string title; bool hasTitle = titles.TryGetValue(row.RootPid, out title);
                row.Title.Text = hasTitle ? title : (row.Path != null ? System.IO.Path.GetFileName(row.Path) : "");
                row.TitleKey = hasTitle ? NormalizeTitle(title) : "";

                if (isNew) InitRow(row);
                if (row.Amped && row.Slider.Value <= 100 && !row.Slider.IsMouseCaptureWithin && DateTime.UtcNow > row.LeaveAt)
                    LeaveAmp(row);   // 100% 이하로 내린 지 1.5초가 지나면 증폭 경로를 끄고 앱 볼륨 방식으로 복귀
                if (row.Amped) { MaintainAmp(row); foreach (var s in row.Sessions) row.Known.Add(s.Id); }
                else SyncFromSystem(row);
            }
            empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 처음 나타난 앱: 기억된 볼륨이 있으면 그 값으로 맞추고, 없으면 현재 시스템 볼륨을 따른다
        void InitRow(Row row)
        {
            // 창 제목이 있으면 같은 창(제목의 숫자를 뺀 값이 같은 창)에 기억된 값만 쓴다. 기억이 없으면 시스템 볼륨 그대로 둔다(다른 창의 값을 물려받지 않음).
            // 창 제목이 없는 앱만 앱 단위로 기억한 값을 쓴다.
            double saved = 0;
            bool found = row.TitleKey.Length > 0
                ? remembered.TryGetValue(row.MemoryKey + "|" + row.TitleKey, out saved)
                : remembered.TryGetValue(row.MemoryKey, out saved);
            if (found)
            {
                foreach (var s in row.Sessions) row.Known.Add(s.Id);
                row.Suppress = true; row.Slider.Value = Math.Min(saved, row.Slider.Maximum); row.Suppress = false;
                Apply(row);
            }
        }

        void SetState(Row row, string text, Brush brush) { row.State.Text = text; row.State.Foreground = brush; }

        void SyncFromSystem(Row row)
        {
            if (row.Sessions.Count == 0) return;
            bool first = row.Known.Count == 0;   // 기억된 값이 없는 첫 등장
            double wanted = row.Applied / 100.0;
            double external = -1;
            foreach (var s in row.Sessions)
            {
                float cur; try { cur = s.Get(); } catch { continue; }
                if (!row.Known.Contains(s.Id))
                {
                    row.Known.Add(s.Id);
                    // 이미 조절 중인 앱에 새 세션이 생기면 같은 볼륨으로 맞춘다(첫 등장일 땐 시스템 값을 따름)
                    if (!first && Math.Abs(cur - wanted) >= 0.01) { try { s.Set((float)wanted); } catch { } }
                    else if (first && external < 0) external = cur * 100;
                }
                else if (Math.Abs(cur - wanted) >= 0.01 && external < 0) external = cur * 100;   // Windows 믹서 등 외부에서 바뀐 값
            }
            if (external >= 0 && !row.Slider.IsMouseCaptureWithin)
            {
                row.Applied = Math.Round(external);
                row.Suppress = true; row.Slider.Value = row.Applied; row.Suppress = false;
                UpdateText(row);
                if (!first) Remember(row);
            }
        }

        // 현재 슬라이더 값 기준의 "증폭이 없을 때" 앱 볼륨 (증폭 엔진이 없거나 실패했을 때 소리가 끊기지 않게 복원)
        void RestoreNative(Row row)
        {
            float v = (float)Math.Min(1.0, Math.Round(row.Slider.Value) / 100.0);
            foreach (var s in row.Sessions) try { s.Set(v); } catch { }
        }

        void DropEngine(Row row, int key)
        {
            row.Engines[key].Stop(); row.Engines.Remove(key);
            RestoreNative(row);   // 새 엔진이 준비될 때까지 원래 소리를 유지
        }

        void MaintainAmp(Row row)
        {
            var now = DateTime.UtcNow;

            // 1) 재시도까지 모두 실패한 엔진: 로그를 남기고 앱 볼륨을 즉시 복원
            foreach (var kv in row.Engines.ToList())
                if (kv.Value.Failed)
                {
                    Log("증폭 엔진 실패 [" + row.Name + " root=" + kv.Key + "] " + kv.Value.Error);
                    row.LastError = kv.Value.Error; row.FailCount++;
                    DropEngine(row, kv.Key);
                    if (row.FailCount >= 3)
                    {
                        row.Amped = false; SaveState();
                        row.Suppress = true; row.Slider.Value = 100; row.Suppress = false; row.Applied = 100;
                        SetState(row, "  증폭 실패", Brushes.Firebrick); UpdateText(row);
                        return;
                    }
                    row.RetryAt = now.AddSeconds(2);
                }
            if (now < row.RetryAt) { SetState(row, "  증폭 재시도 중", Brushes.Firebrick); return; }
            if (row.Engines.Values.Any(x => x.Open && (now - x.StartedAt).TotalSeconds > 5)) { row.FailCount = 0; row.LastError = ""; }

            // 2) 출력 장치가 바뀌었으면 재시작
            string curDev = ""; try { curDev = DefaultRenderId(); } catch { }
            foreach (var kv in row.Engines.ToList())
            {
                var e = kv.Value;
                if (e.DeviceId.Length > 0 && curDev.Length > 0 && e.DeviceId != curDev) DropEngine(row, kv.Key);
            }

            // 3) 이 행의 프로세스 트리(루트)에 엔진을 맞춘다
            var roots = new HashSet<int> { row.RootPid };
            foreach (var r in row.Engines.Keys.Where(k => !roots.Contains(k)).ToList()) DropEngine(row, r);
            foreach (var r in roots)
                if (!row.Engines.ContainsKey(r))
                {
                    var eng = new AmpEngine(r) { Gain = (float)(row.Slider.Value / 100.0) / AmpSessVol };
                    row.Engines[r] = eng; eng.Start();
                }

            // 4) 모든 캡처가 열린 뒤에야 앱 볼륨을 낮추고 증폭 재생을 허용한다(캡처가 안 열리면 원래 소리 유지)
            var engs = row.Engines.Values.ToList();
            if (engs.Count > 0 && engs.All(e => e.Ready))
            {
                bool lowered = false;
                foreach (var s in row.Sessions)
                    try { if (Math.Abs(s.Get() - AmpSessVol) > 0.005f) { s.Set(AmpSessVol); lowered = true; } } catch { }
                foreach (var e in engs)
                    if (!e.Open) { e.OpenAfterTicks = now.AddMilliseconds(lowered ? 150 : 0).Ticks; e.Open = true; }
            }

            // 5) 상태 표시
            if (engs.Count == 0 || !engs.All(e => e.Open)) SetState(row, "  증폭 준비 중", Brushes.Gray);
            else SetState(row, "  증폭 중", Brushes.DarkOrange);
        }

        void LeaveAmp(Row row)
        {
            row.Amped = false; StopEngines(row); SaveState(); row.LeaveAt = DateTime.MaxValue;
            SetState(row, "", Brushes.Gray);
            float v = (float)(Math.Round(row.Slider.Value) / 100.0);
            foreach (var s in row.Sessions) try { s.Set(v); } catch { }
            UpdateText(row);
        }

        static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        // ---------- 행 UI ----------
        Row AddRow(string key, string path, int rootPid)
        {
            var row = new Row { Key = key, Path = path, RootPid = rootPid };
            row.MemoryKey = key == "system" ? "system" : path != null ? path.ToLowerInvariant() : key;
            row.Name = key == "system" ? "시스템 소리" : path != null ? ProcessUtil.FriendlyName(path) : key;

            var g = new Grid { Margin = new Thickness(10, 6, 10, 0), Background = Brushes.White };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var img = new Image { Width = 28, Height = 28, Margin = new Thickness(10, 10, 10, 10), VerticalAlignment = VerticalAlignment.Center };
            if (path != null) img.Source = ProcessUtil.IconOf(path);
            Grid.SetColumn(img, 0); g.Children.Add(img);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nameLine = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            nameLine.Inlines.Add(new Run(row.Name) { FontWeight = FontWeights.SemiBold });
            row.State = new Run("") { FontSize = 11 };
            nameLine.Inlines.Add(row.State);
            info.Children.Add(nameLine);
            row.Title = new TextBlock { FontSize = 11, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis };
            info.Children.Add(row.Title);
            Grid.SetColumn(info, 1); g.Children.Add(info);

            row.Slider = new Slider
            {
                Minimum = 0, Maximum = key == "system" ? 100 : MaxPercent,   // 시스템 소리는 증폭 대상이 아님
                SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center,
                IsMoveToPointEnabled = true,   // 슬라이더 바의 아무 곳이나 클릭하면 즉시 그 위치로 이동
                TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight, Ticks = new DoubleCollection { 100 }
            };
            row.Slider.ValueChanged += (s, e) => { if (!row.Suppress) { Apply(row); Remember(row); } else UpdateText(row); };
            Grid.SetColumn(row.Slider, 2); g.Children.Add(row.Slider);

            row.Pct = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(row.Pct, 3); g.Children.Add(row.Pct);

            row.Panel = g; rows[key] = row; list.Children.Add(g);
            row.Suppress = true; row.Slider.Value = 100; row.Suppress = false;
            UpdateText(row);
            return row;
        }

        void RemoveRow(Row row)
        {
            StopEngines(row); rows.Remove(row.Key); list.Children.Remove(row.Panel); SaveState();
        }

        void UpdateText(Row row)
        {
            double v = row.Slider.Value;
            row.Pct.Text = (int)Math.Round(v) + "%";
            row.Pct.Foreground = v > 150 ? Brushes.Firebrick : v > 100 ? Brushes.DarkOrange : Brushes.Black;
            if (!row.Amped && string.IsNullOrEmpty(row.LastError)) SetState(row, "", Brushes.Gray);
        }

        // ---------- 볼륨 적용 ----------
        // 슬라이더 값(또는 기억된 값)을 실제 소리에 반영한다
        void Apply(Row row)
        {
            double pct = Math.Round(row.Slider.Value);
            row.Applied = pct;
            foreach (var s in row.Sessions) row.Known.Add(s.Id);
            if (pct > 100 && !row.Amped)
            {
                // 증폭 시작: 앱 볼륨은 캡처가 열린 뒤 MaintainAmp 가 낮춘다(실패해도 소리가 끊기지 않게)
                row.LastError = ""; row.FailCount = 0; row.RetryAt = DateTime.MinValue;
                row.Amped = true; SaveState();
                MaintainAmp(row);
            }
            if (row.Amped)
            {
                // 증폭 경로가 켜져 있는 동안은 100% 이하에서도 같은 경로로 배율만 바꾼다(엔진을 껐다 켜지 않음).
                // 드래그 중에는 엔진 점검을 하지 않고 배율만 바꾼다. 점검은 주기 갱신(Tick)이 한다.
                row.LeaveAt = pct > 100 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(1.5);
                foreach (var e in row.Engines.Values) e.Gain = (float)(pct / 100.0) / AmpSessVol;
            }
            else
            {
                foreach (var s in row.Sessions) try { s.Set((float)(pct / 100.0)); } catch { }
            }
            UpdateText(row);
        }

        void StopEngines(Row row)
        {
            foreach (var e in row.Engines.Values) e.Stop();
            row.Engines.Clear();
        }

        // ---------- 앱별 볼륨 기억 ----------
        // 창 제목에서 숫자를 빼고 공백을 정리한다 (예: "Table #3762761527 - Logged In" → "table # - logged in")
        static string NormalizeTitle(string title)
        {
            string s = System.Text.RegularExpressions.Regex.Replace(title ?? "", @"\d+", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
            return s.Length > 100 ? s.Substring(0, 100) : s;
        }

        void Remember(Row row)
        {
            double v = Math.Round(row.Slider.Value);
            remembered[row.MemoryKey] = v;                                              // 앱 단위(새 창의 기본값)
            if (row.TitleKey.Length > 0) remembered[row.MemoryKey + "|" + row.TitleKey] = v;   // 창 단위
            saveTimer.Stop(); saveTimer.Start();   // 드래그 중 매번 쓰지 않도록 잠시 모아서 저장
        }

        void LoadRemembered()
        {
            try
            {
                if (!File.Exists(VolumeFile)) return;
                foreach (var line in File.ReadAllLines(VolumeFile))
                {
                    int i = line.IndexOf('\t'); double v;
                    if (i > 0 && double.TryParse(line.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) remembered[line.Substring(i + 1)] = v;
                }
            }
            catch { }
        }

        void SaveRemembered()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllLines(VolumeFile, remembered.Select(kv => kv.Value.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Key).ToArray());
            }
            catch { }
        }

        // ---------- 비정상 종료 복구 ----------
        void SaveState()
        {
            try
            {
                var keys = rows.Values.Where(r => r.Amped && r.Path != null).Select(r => r.Path.ToLowerInvariant()).Distinct().ToArray();
                Directory.CreateDirectory(DataDir);
                if (keys.Length == 0) { if (File.Exists(StateFile)) File.Delete(StateFile); }
                else File.WriteAllLines(StateFile, keys);
            }
            catch { }
        }

        // 이전 실행이 비정상 종료돼 앱 볼륨이 낮게 남은 경우 100%로 되돌린다
        void RestoreLeftover()
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                var keys = new HashSet<string>(File.ReadAllLines(StateFile));
                foreach (var s in EnumerateSessions())
                {
                    string path = ProcessUtil.GetPath((int)s.Pid);
                    if (path != null && keys.Contains(path.ToLowerInvariant()) && s.Get() < 0.1f) s.Set(1f);
                }
                File.Delete(StateFile);
            }
            catch { }
        }

        void Shutdown()
        {
            timer.Stop(); saveTimer.Stop(); SaveRemembered();
            foreach (var row in rows.Values.ToList())
            {
                StopEngines(row);
                if (row.Amped) foreach (var s in row.Sessions) try { s.Set(1f); } catch { }
            }
            try { if (File.Exists(StateFile)) File.Delete(StateFile); } catch { }
        }
    }
}
