using System;
using System.Runtime.InteropServices;
using System.Threading;
using static MongVolume.CoreAudio;

namespace MongVolume
{
    // 한 프로세스 트리의 소리를 캡처(프로세스 루프백)해 배율을 곱하고 기본 재생 장치로 다시 출력한다.
    // 캡처되는 소리는 앱 볼륨이 적용된 뒤의 소리이므로, 호출한 쪽이 앱 볼륨을 낮춰 두고 Gain 으로 보정한다.
    //
    // 시작 순서(소리가 끊기거나 폭음이 나지 않도록):
    //  1) 캡처를 열고 Ready=true (이때까지 캡처한 소리는 버리고, 앱 볼륨은 원래대로)
    //  2) 호출한 쪽이 앱 볼륨을 낮춘 뒤 OpenAfterTicks 를 정하고 Open=true
    //  3) OpenAfterTicks 이후부터 캡처한 소리를 증폭해 재생
    internal sealed class AmpEngine
    {
        public readonly int RootPid;
        public volatile float Gain = 1f;           // 캡처 신호에 곱할 배율
        public volatile bool Failed, Ready, Open;  // Failed: 재시도 끝에 실패, Ready: 캡처 시작됨, Open: 증폭 재생 허용
        public long OpenAfterTicks;                // 이 시각(UTC ticks) 이전에 캡처한 소리는 버린다
        public int Attempts;                       // 캡처 열기 시도 횟수
        public string Error = "";
        public DateTime FailedAt;
        public string DeviceId = "";
        public readonly DateTime StartedAt = DateTime.UtcNow;
        public volatile float PeakIn, PeakOut;      // 최근 입력(캡처)·출력(증폭 후) 최대 진폭 (진단용, 서서히 감소)
        public long CapturedFrames, RenderedFrames;

        const int MaxAttempts = 20, RetryDelayMs = 150;

        readonly float[] ring = new float[48000 * 2 * 2];
        int rHead, rCount; readonly object rLock = new object();
        volatile bool stop;
        Thread capThread, renThread;

        public AmpEngine(int rootPid) { RootPid = rootPid; }

        public void Start()
        {
            capThread = new Thread(CaptureLoop) { IsBackground = true, Name = "AmpCapture" };
            renThread = new Thread(RenderLoop) { IsBackground = true, Name = "AmpRender" };
            capThread.SetApartmentState(ApartmentState.MTA); renThread.SetApartmentState(ApartmentState.MTA);
            capThread.Start(); renThread.Start();
        }

        public void Stop()
        {
            stop = true;
            if (capThread != null) capThread.Join(3000);
            if (renThread != null) renThread.Join(3000);
        }

        static void ReleaseAll(object com)
        {
            if (com != null && Marshal.IsComObject(com)) { try { Marshal.FinalReleaseComObject(com); } catch { } }
        }

        void Fail(string msg) { Error = msg; FailedAt = DateTime.UtcNow; Failed = true; stop = true; }

        static IntPtr MakeFormat() // float32, 48kHz, 스테레오
        {
            IntPtr fmt = Marshal.AllocHGlobal(18);
            Marshal.WriteInt16(fmt, 0, 3); Marshal.WriteInt16(fmt, 2, 2); Marshal.WriteInt32(fmt, 4, 48000);
            Marshal.WriteInt32(fmt, 8, 48000 * 8); Marshal.WriteInt16(fmt, 12, 8); Marshal.WriteInt16(fmt, 14, 32); Marshal.WriteInt16(fmt, 16, 0);
            return fmt;
        }

        static float Limit(float x)
        {
            const float T = 0.8f; float a = Math.Abs(x);
            if (a <= T) return x;
            float y = T + (1 - T) * (float)Math.Tanh((a - T) / (1 - T));
            return x < 0 ? -y : y;
        }

        void Push(float[] src, int n)
        {
            lock (rLock)
                for (int i = 0; i < n; i++)
                {
                    if (rCount == ring.Length) { rHead = (rHead + 1) % ring.Length; rCount--; }
                    ring[(rHead + rCount) % ring.Length] = src[i]; rCount++;
                }
        }

        int Pop(float[] dst, int n)
        {
            lock (rLock)
            {
                int k = Math.Min(n, rCount);
                for (int i = 0; i < k; i++) { dst[i] = ring[rHead]; rHead = (rHead + 1) % ring.Length; }
                rCount -= k; return k;
            }
        }

        // 프로세스 루프백 캡처 클라이언트를 한 번 열어 본다. 성공하면 null, 실패하면 사유를 돌려준다.
        string TryOpenCapture(IntPtr fmt, out IAudioClient client)
        {
            client = null;
            IntPtr pParams = Marshal.AllocHGlobal(12), pv = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.StructureToPtr(new ActivationParams { ActivationType = 1, TargetProcessId = RootPid, Mode = 0 }, pParams, false);
                Marshal.StructureToPtr(new PropVariantBlob { vt = 65, cbSize = 12, pBlobData = pParams }, pv, false);
                var h = new ActivateHandler(); Guid iid = typeof(IAudioClient).GUID; IActivateAudioInterfaceAsyncOperation op;
                ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, pv, h, out op);
                bool done = h.Done.WaitOne(5000);
                ReleaseAll(op);   // 활성화 비동기 작업 객체도 해제
                if (!done) return "캡처 활성화 시간 초과";
                if (h.Hr != 0 || h.Iface == null) return "캡처 활성화 실패 0x" + h.Hr.ToString("X8");
                var c = (IAudioClient)h.Iface;
                int hr = c.Initialize(0, LOOPBACK | EVENTCB | AUTOCONV | SRC, 200 * 10000, 0, fmt, IntPtr.Zero);
                if (hr != 0) { ReleaseAll(c); return "캡처 초기화 실패 0x" + hr.ToString("X8"); }
                client = c; return null;
            }
            catch (Exception e) { return "캡처 오류: " + e.Message; }
            finally { Marshal.FreeHGlobal(pv); Marshal.FreeHGlobal(pParams); }
        }

        void CaptureLoop()
        {
            IntPtr fmt = MakeFormat(), ev = IntPtr.Zero; IAudioClient cap = null; object co = null; IAudioCaptureClient cc = null;
            try
            {
                // 일시적 실패(E_UNEXPECTED 등)에 대비해 짧게 여러 번 다시 시도
                string err = "";
                for (int i = 1; i <= MaxAttempts && !stop; i++)
                {
                    Attempts = i;
                    err = TryOpenCapture(fmt, out cap);
                    if (err == null) break;
                    Thread.Sleep(RetryDelayMs);
                }
                if (cap == null) { if (!stop) Fail(err + " (" + Attempts + "회 시도)"); return; }

                ev = CreateEvent(IntPtr.Zero, false, false, null); cap.SetEventHandle(ev);
                Guid capIid = typeof(IAudioCaptureClient).GUID; cap.GetService(ref capIid, out co);
                cc = (IAudioCaptureClient)co;
                cap.Start();
                Ready = true;
                var buf = new float[48000 * 2]; float cur = Gain;
                while (!stop)
                {
                    WaitForSingleObject(ev, 100); uint n;
                    while (cc.GetNextPacketSize(out n) == 0 && n > 0)
                    {
                        IntPtr data; uint fr, fl; ulong a, b;
                        if (cc.GetBuffer(out data, out fr, out fl, out a, out b) != 0) break;
                        int cnt = Math.Min((int)fr * 2, buf.Length);
                        if ((fl & 2) != 0) Array.Clear(buf, 0, cnt); else Marshal.Copy(data, buf, 0, cnt);
                        cc.ReleaseBuffer(fr);
                        if (!Open || DateTime.UtcNow.Ticks < OpenAfterTicks) continue; // 앱 볼륨이 낮아지기 전의 소리는 버린다
                        float target = Gain, pin = 0, pout = 0;
                        for (int i = 0; i < cnt; i++)
                        {
                            cur += (target - cur) * 0.002f; // 배율 변화를 부드럽게(클릭 방지)
                            float x = Math.Abs(buf[i]); if (x > pin) pin = x;
                            buf[i] = Limit(buf[i] * cur);
                            float y = Math.Abs(buf[i]); if (y > pout) pout = y;
                        }
                        PeakIn = Math.Max(pin, PeakIn * 0.95f); PeakOut = Math.Max(pout, PeakOut * 0.95f);
                        CapturedFrames += cnt / 2;
                        Push(buf, cnt);
                    }
                }
                cap.Stop();
            }
            catch (Exception e) { Fail("캡처 오류: " + e.Message); }
            finally
            {
                Ready = false;
                if (ev != IntPtr.Zero) CloseHandle(ev);
                // 캡처 스트림은 COM 객체를 모두 해제해야 닫힌다. 해제하지 않으면 GC 가 돌 때까지 같은 앱에 대한 새 캡처가 0x8000FFFF 로 실패한다.
                ReleaseAll(cc); ReleaseAll(co); ReleaseAll(cap);
                Marshal.FreeHGlobal(fmt);
            }
        }

        void RenderLoop()
        {
            IntPtr fmt = MakeFormat(); IAudioClient ren = null; IMMDevice dev = null; object rco = null;
            try
            {
                string id; dev = GetDefaultRender(out id); DeviceId = id;
                Guid acIid = typeof(IAudioClient).GUID; object ro;
                Marshal.ThrowExceptionForHR(dev.Activate(ref acIid, 23, IntPtr.Zero, out ro));
                ren = (IAudioClient)ro;
                int hr = ren.Initialize(0, AUTOCONV | SRC, 100 * 10000, 0, fmt, IntPtr.Zero);
                if (hr != 0) { Fail("출력 초기화 실패 0x" + hr.ToString("X8")); return; }
                uint bufFrames; ren.GetBufferSize(out bufFrames);
                Guid rcIid = typeof(IAudioRenderClient).GUID; ren.GetService(ref rcIid, out rco);
                var rc = (IAudioRenderClient)rco;
                var tmp = new float[bufFrames * 2]; bool started = false;
                while (!stop)
                {
                    if (!started)
                    {
                        lock (rLock) if (rCount < 48000 * 2 / 25) { Thread.Sleep(2); continue; } // 40ms 쿠션
                        ren.Start(); started = true;
                    }
                    lock (rLock) // 시계 오차로 쌓인 지연 제거: 200ms 넘으면 80ms만 남김
                        if (rCount > 48000 * 2 / 5) { int drop = rCount - 48000 * 2 * 8 / 100; rHead = (rHead + drop) % ring.Length; rCount -= drop; }
                    uint pad; ren.GetCurrentPadding(out pad);
                    int avail = (int)(bufFrames - pad);
                    if (avail > 0)
                    {
                        int got = Pop(tmp, avail * 2) / 2;
                        if (got > 0)
                        {
                            IntPtr d;
                            if (rc.GetBuffer((uint)got, out d) == 0) { Marshal.Copy(tmp, 0, d, got * 2); rc.ReleaseBuffer((uint)got, 0); RenderedFrames += got; }
                        }
                    }
                    Thread.Sleep(4);
                }
                if (started) ren.Stop();
            }
            catch (Exception e) { Fail("출력 오류: " + e.Message); }
            finally
            {
                ReleaseAll(rco); ReleaseAll(ren); ReleaseAll(dev);
                Marshal.FreeHGlobal(fmt);
            }
        }
    }
}
