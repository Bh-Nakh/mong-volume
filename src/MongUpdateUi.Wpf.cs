// 몽 시리즈 공용 자동 업데이트 — WPF 화면(안내창·진행 창). MongUpdate.cs 와 함께 앱 src 폴더에 복사한다.
// (WinForms 앱은 이 파일 대신 MongUpdateUi.WinForms.cs 를 쓴다. 둘 다 클래스 이름이 UpdateUi 이므로 앱에는 하나만 넣는다.)
//
// 쓰는 법 (메인 창 안):
//   앱 시작 때 한 번:        MongUpdate.UpdateService.CleanupOld();
//   창이 열린 뒤(Loaded):    MongUpdate.UpdateUi.CheckAtStartup(this, "mongview", "몽뷰", version, null);
//   메뉴 → 업데이트 확인:    MongUpdate.UpdateUi.CheckNow(this, "mongview", "몽뷰", version, null);
// 새 버전이 있으면 안내창을 띄우고, 사용자가 [예]를 눌러야 받는다. 받은 뒤 앱을 종료하고 새 버전으로 다시 시작한다.
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MongUpdate
{
    public static class UpdateUi
    {
        /// <summary>앱을 실행할 때: 백그라운드로 확인해서 새 버전이 있을 때만 안내한다 (없거나 인터넷 오류면 조용히 넘어간다).</summary>
        public static void CheckAtStartup(Window owner, string appId, string appName, string currentVersion, string restartArgs)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info;
                if (!UpdateService.TryCheck(appId, currentVersion, out info) || info == null) return;
                try { owner.Dispatcher.BeginInvoke(new Action(delegate { Offer(owner, appName, info, restartArgs); })); } catch { }
            });
        }

        /// <summary>사용자가 메뉴·버튼을 눌렀을 때: 결과(새 버전 없음·오류 포함)를 항상 알려 준다.</summary>
        public static void CheckNow(Window owner, string appId, string appName, string currentVersion, string restartArgs)
        {
            Cursor old = owner.Cursor;
            owner.Cursor = Cursors.Wait;
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info;
                bool ok = UpdateService.TryCheck(appId, currentVersion, out info);
                string err = UpdateService.LastError;
                try
                {
                    owner.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        owner.Cursor = old;
                        if (!ok)
                            MessageBox.Show(owner, "업데이트를 확인하지 못했습니다.\n\n" + err, appName + " 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
                        else if (info == null)
                            MessageBox.Show(owner, "현재 최신 버전입니다. (v" + currentVersion + ")", appName + " 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
                        else
                            Offer(owner, appName, info, restartArgs);
                    }));
                }
                catch { }
            });
        }

        static void Offer(Window owner, string appName, UpdateInfo info, string restartArgs)
        {
            string msg = appName + " 새 버전 v" + info.Version + " 이(가) 있습니다.";
            if (!string.IsNullOrEmpty(info.Note)) msg += "\n\n" + info.Note;
            msg += "\n\n지금 업데이트할까요? (끝나면 자동으로 다시 시작합니다)";
            if (MessageBox.Show(owner, msg, appName + " 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

            string file = null;
            Exception err = null;

            Window win = new Window();
            win.Title = appName + " 업데이트";
            win.Owner = owner;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ResizeMode = ResizeMode.NoResize;
            win.WindowStyle = WindowStyle.ToolWindow;
            win.ShowInTaskbar = false;
            win.SizeToContent = SizeToContent.WidthAndHeight;
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(16);
            TextBlock label = new TextBlock();
            label.Text = "새 버전을 받는 중… 0%";
            label.Margin = new Thickness(0, 0, 0, 10);
            ProgressBar bar = new ProgressBar();
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Width = 320;
            bar.Height = 18;
            panel.Children.Add(label);
            panel.Children.Add(bar);
            win.Content = panel;

            bool done = false;
            Thread t = null;
            win.Loaded += delegate
            {
                t = new Thread(delegate ()
                {
                    try
                    {
                        file = UpdateService.Download(info, delegate (int p)
                        {
                            win.Dispatcher.BeginInvoke(new Action(delegate { bar.Value = p; label.Text = "새 버전을 받는 중… " + p + "%"; }));
                        });
                    }
                    catch (Exception ex) { err = ex; }
                    done = true;
                    win.Dispatcher.BeginInvoke(new Action(delegate { win.Close(); }));
                });
                t.IsBackground = true;
                t.Start();
            };
            win.Closing += delegate (object s, System.ComponentModel.CancelEventArgs e) { if (!done) e.Cancel = true; };   // 받는 도중에는 닫지 못하게
            win.ShowDialog();

            if (err != null || file == null)
            {
                MessageBox.Show(owner, "업데이트를 받지 못했습니다.\n\n" + (err != null ? err.Message : "알 수 없는 오류"), appName + " 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                UpdateService.Apply(info, file, restartArgs);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "업데이트를 적용하지 못했습니다.\n\n" + ex.Message, appName + " 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Application.Current.Shutdown();   // 곧바로 새 버전이 다시 시작된다
        }
    }
}
