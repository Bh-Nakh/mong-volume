using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MongVolume
{
    // 프로세스 경로·부모 관계·아이콘·표시 이름 도우미
    internal static class ProcessUtil
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr s, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr s, ref PROCESSENTRY32 e);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        public static string GetPath(int pid)
        {
            if (pid <= 0) return null;
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
            finally { CloseHandle(h); }
        }

        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);

        // 프로세스가 아직 실행 중인지(접근이 막힌 프로세스는 실행 중으로 본다)
        public static bool IsAlive(int pid)
        {
            if (pid <= 0) return true;
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;   // 5 = 접근 거부(살아 있음), 87 = 없는 PID
            try { uint code; return !GetExitCodeProcess(h, out code) || code == 259; }   // 259 = STILL_ACTIVE
            finally { CloseHandle(h); }
        }

        public static Dictionary<int, int> ParentMap()
        {
            var map = new Dictionary<int, int>();
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);
            if (snap == (IntPtr)(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                for (bool ok = Process32First(snap, ref e); ok; ok = Process32Next(snap, ref e)) map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID;
            }
            finally { CloseHandle(snap); }
            return map;
        }

        // 같은 실행 파일인 조상을 끝까지 올라가 루트(프로세스 트리의 최상위) PID를 구한다
        public static int RootPid(int pid, string path, Dictionary<int, int> parents)
        {
            int cur = pid;
            for (int i = 0; i < 32; i++)
            {
                int par;
                if (!parents.TryGetValue(cur, out par) || par <= 0) break;
                string pp = GetPath(par);
                if (pp == null || !string.Equals(pp, path, StringComparison.OrdinalIgnoreCase)) break;
                cur = par;
            }
            return cur;
        }

        // PID → 그 프로세스의 대표 창 제목(보이는 최상위 창 중 첫 번째, 도구 창 제외)
        public static Dictionary<int, string> WindowTitles()
        {
            var d = new Dictionary<int, string>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;   // 4 = GW_OWNER
                if ((GetWindowLong(h, -20) & 0x80) != 0) return true;                    // WS_EX_TOOLWINDOW
                var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
                if (sb.Length == 0) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!d.ContainsKey((int)pid)) d[(int)pid] = sb.ToString();
                return true;
            }, IntPtr.Zero);
            return d;
        }

        public static string FriendlyName(string path)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(vi.ProductName)) return vi.ProductName.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(path);
        }

        public static ImageSource IconOf(string path)
        {
            try
            {
                using (var ic = Icon.ExtractAssociatedIcon(path))
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze(); return src;
                }
            }
            catch { return null; }
        }
    }
}
