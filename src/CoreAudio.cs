using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MongVolume
{
    // Windows Core Audio COM 인터페이스 선언과 세션 열거 도우미
    internal static class CoreAudio
    {
        public const uint LOOPBACK = 0x00020000, EVENTCB = 0x00040000, AUTOCONV = 0x80000000, SRC = 0x08000000;

        [StructLayout(LayoutKind.Sequential)] public struct ActivationParams { public int ActivationType; public int TargetProcessId; public int Mode; }
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct PropVariantBlob { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public int cbSize; [FieldOffset(16)] public IntPtr pBlobData; }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op); }
        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }
        [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAgileObject { }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint flags, long hnsBufferDuration, long hnsPeriodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr h);
            [PreserveSig] int GetService([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devPos, out ulong qpc);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioRenderClient
        {
            [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
            [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devs);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            [PreserveSig] int Activate([In] ref Guid iid, uint clsctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr props);
            [PreserveSig] int GetId(out IntPtr id);
        }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr g, uint f, out IntPtr c);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr g, uint f, out IntPtr v);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int n);
            [PreserveSig] int GetSession(int i, [MarshalAs(UnmanagedType.IUnknown)] out object s);
        }
        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int s);
            [PreserveSig] int GetDisplayName(out IntPtr n);
            [PreserveSig] int SetDisplayName(IntPtr n, IntPtr c);
            [PreserveSig] int GetIconPath(out IntPtr n);
            [PreserveSig] int SetIconPath(IntPtr n, IntPtr c);
            [PreserveSig] int GetGroupingParam(out Guid g);
            [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr c);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float v, [In] ref Guid ctx);
            [PreserveSig] int GetMasterVolume(out float v);
            [PreserveSig] int SetMute(bool m, [In] ref Guid ctx);
            [PreserveSig] int GetMute(out bool m);
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [In] ref Guid riid, IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);
        [DllImport("kernel32.dll")] public static extern IntPtr CreateEvent(IntPtr a, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        public sealed class ActivateHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public int Hr; public object Iface;
            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) { op.GetActivateResult(out Hr, out Iface); Done.Set(); }
        }

        public static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        }

        // 기본 재생 장치와 그 ID
        public static IMMDevice GetDefaultRender(out string id)
        {
            var en = CreateEnumerator(); IMMDevice dev;
            Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out dev));
            IntPtr p; id = "";
            if (dev.GetId(out p) == 0 && p != IntPtr.Zero) { id = Marshal.PtrToStringUni(p); CoTaskMemFree(p); }
            Marshal.ReleaseComObject(en);
            return dev;
        }

        public static string DefaultRenderId()
        {
            string id; var d = GetDefaultRender(out id); Marshal.ReleaseComObject(d); return id;
        }

        public sealed class SessionRef
        {
            public uint Pid;
            public string Id = "";   // 세션 인스턴스 식별자(같은 세션인지 비교용)
            public int State;        // 0=비활성, 1=활성(소리 재생 중), 2=만료
            public ISimpleAudioVolume Vol;
            public float Get() { float v; Vol.GetMasterVolume(out v); return v; }
            public void Set(float v) { Guid g = Guid.Empty; Vol.SetMasterVolume(Math.Max(0f, Math.Min(1f, v)), ref g); }
        }

        // 기본 재생 장치의 모든 오디오 세션
        public static List<SessionRef> EnumerateSessions()
        {
            var list = new List<SessionRef>();
            string id; var dev = GetDefaultRender(out id);
            Guid smIid = typeof(IAudioSessionManager2).GUID; object smo;
            if (dev.Activate(ref smIid, 23, IntPtr.Zero, out smo) != 0) { Marshal.ReleaseComObject(dev); return list; }
            var sm = (IAudioSessionManager2)smo; IAudioSessionEnumerator se;
            if (sm.GetSessionEnumerator(out se) == 0)
            {
                int cnt; se.GetCount(out cnt);
                for (int i = 0; i < cnt; i++)
                {
                    object so; if (se.GetSession(i, out so) != 0) continue;
                    var c2 = (IAudioSessionControl2)so;
                    uint pid; c2.GetProcessId(out pid);
                    string sid = ""; IntPtr ip;
                    if (c2.GetSessionInstanceIdentifier(out ip) == 0 && ip != IntPtr.Zero) { sid = Marshal.PtrToStringUni(ip); CoTaskMemFree(ip); }
                    int st; c2.GetState(out st);
                    list.Add(new SessionRef { Pid = pid, Id = sid, State = st, Vol = (ISimpleAudioVolume)so });
                }
                Marshal.ReleaseComObject(se);
            }
            Marshal.ReleaseComObject(sm); Marshal.ReleaseComObject(dev);
            return list;
        }
    }
}
