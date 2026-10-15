using System;
using System.Threading;
using System.Windows;

namespace MongVolume
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (var mutex = new Mutex(true, "MongVolume.SingleInstance", out first))
            {
                if (!first) return; // 이미 실행 중이면 두 번째 실행은 종료(원래 볼륨 보관이 꼬이지 않게)
                // 홈페이지(설치 확인 페이지)가 이 앱이 켜져 있는지 확인할 수 있게 하는 로컬 핑 서버
                MongWebCheck.WebCheck.Start("mongvolume", "몽볼륨", new int[] { 47151, 47152, 47153 }, MainWindow.Version);
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var win = new MainWindow();
                // 시험 실행: --at X,Y 로 지정한 위치에 포커스를 가져가지 않고 띄운다
                int ai = Array.IndexOf(args, "--at");
                if (ai >= 0 && ai + 1 < args.Length)
                {
                    var xy = args[ai + 1].Split(',');
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = double.Parse(xy[0]); win.Top = double.Parse(xy[1]); win.ShowActivated = false;
                }
                app.Run(win);
            }
        }
    }
}
