using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TigEditor
{
    // A sound source: hands out float samples interleaved by channel.
    internal interface ISampleSource : IDisposable
    {
        int Channels { get; }
        int SampleRate { get; }
        TimeSpan Duration { get; }
        TimeSpan Position { get; set; }
        int Read(float[] buffer, int offset, int count);
    }

    internal class OggSource : ISampleSource
    {
        private readonly NVorbis.VorbisReader r;
        public OggSource(byte[] data) { r = new NVorbis.VorbisReader(new MemoryStream(data), true); }
        public int Channels { get { return r.Channels; } }
        public int SampleRate { get { return r.SampleRate; } }
        public TimeSpan Duration { get { return r.TotalTime; } }
        public TimeSpan Position { get { return r.DecodedTime; } set { r.DecodedTime = value; } }
        public int Read(float[] buffer, int offset, int count) { return r.ReadSamples(buffer, offset, count); }
        public void Dispose() { r.Dispose(); }
    }

    internal class Mp3Source : ISampleSource
    {
        private readonly NLayer.MpegFile f;
        public Mp3Source(byte[] data) { f = new NLayer.MpegFile(new MemoryStream(data)); }
        public int Channels { get { return f.Channels; } }
        public int SampleRate { get { return f.SampleRate; } }
        public TimeSpan Duration { get { return f.Duration; } }
        public TimeSpan Position { get { return f.Time; } set { f.Time = value; } }
        public int Read(float[] buffer, int offset, int count) { return f.ReadSamples(buffer, offset, count); }
        public void Dispose() { f.Dispose(); }
    }

    // A waveOut player. Written here instead of taking a library: waveOut is in
    // every Windows since XP and costs zero bytes.
    public class AudioPlayer : IDisposable
    {
        private const int BufferCount = 3;
        private const int BufferMillis = 200;

        private const int WaveMapper = -1;
        private const int CallbackNull = 0x00000000;
        private const int WhdrDone = 0x00000001;
        private const int WhdrPrepared = 0x00000002;
        private const int TimeBytes = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MmTime
        {
            public int wType;
            public int val1;
            public int val2;
            public int val3;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WaveFormatEx lpFormat,
            IntPtr dwCallback, IntPtr dwInstance, int dwFlags);
        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);
        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);
        [DllImport("winmm.dll")]
        private static extern int waveOutPause(IntPtr hWaveOut);
        [DllImport("winmm.dll")]
        private static extern int waveOutRestart(IntPtr hWaveOut);
        [DllImport("winmm.dll")]
        private static extern int waveOutGetPosition(IntPtr hWaveOut, ref MmTime lpInfo, int uSize);

        private ISampleSource source;
        private volatile bool ready;     // track decoded and the device open
        private volatile bool preparing;    // decoding is running
        private volatile bool decodingDone; // decoding reached the end
        private short[] pcmAll;      // the whole track in ready samples
        private int pcmCount;        // how many there are
        private int pcmPos;          // where we feed from
        private int channels, sampleRate;
        private TimeSpan totalTime;
        private IntPtr hWaveOut = IntPtr.Zero;
        private IntPtr[] headerPtr;
        private IntPtr[] dataPtr;
        private int bufferBytes;
        private int bytesPerSecond;
        private Thread feeder;
        private volatile bool running;
        private volatile bool playing;
        private volatile bool hasPendingSeek;   // a seek is waiting
        private volatile int devBytes;          // bytes the device has played
        private TimeSpan pendingSeek;
        private readonly object clockGate = new object();
        private volatile bool endOfStream;
        private TimeSpan seekBase;
        private readonly object gate = new object();

        // Position smoothing. The device reports it in steps of a buffer, so
        // between the steps we add real time.
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TimeSpan predicted, predictedStamp;
        private double lastSyncSeconds = -1;

        public bool IsLoaded { get { return ready; } }

        // While decoding runs the editor shows it in the status row.
        public bool IsPreparing { get { return preparing; } }

        // The editor waits for this event to show the music buttons.
        public event Action Ready;
        public bool IsPlaying { get { return playing; } }
        public TimeSpan Duration { get { return totalTime; } }
        public string FilePath { get; private set; }

        public TimeSpan Position
        {
            get
            {
                if (bytesPerSecond == 0) return seekBase;
                TimeSpan pos = seekBase + TimeSpan.FromSeconds(devBytes / (double)bytesPerSecond);
                if (pos > Duration) pos = Duration;
                return pos;
            }
        }

        // The device is asked from here only, from the feeding thread.
        private void SampleDevice()
        {
            if (hWaveOut == IntPtr.Zero) return;
            MmTime t = new MmTime();
            t.wType = TimeBytes;
            if (waveOutGetPosition(hWaveOut, ref t, Marshal.SizeOf(typeof(MmTime))) == 0)
                devBytes = t.val1;
        }


        // The position for the red line. It runs by the clock on its own and
        // checks against the device twice a second: asking every frame is costly
        // and the playback speed is constant anyway.
        public TimeSpan PredictedPosition
        {
            get
            {
                TimeSpan now = clock.Elapsed;

                if (!playing)
                {
                    // On pause the line simply stays put: pulling it towards the
                    // device is wrong, that lags by the buffer depth.
                    predictedStamp = now;
                    lastSyncSeconds = now.TotalSeconds;
                    return predicted;
                }

                double dt = (now - predictedStamp).TotalSeconds;
                predictedStamp = now;
                if (dt > 0 && dt < 0.5) predicted += TimeSpan.FromSeconds(dt);

                if (lastSyncSeconds < 0 || now.TotalSeconds - lastSyncSeconds > 0.5)
                {
                    lastSyncSeconds = now.TotalSeconds;
                    TimeSpan dev = Position;
                    double diff = (dev - predicted).TotalSeconds;

                    // A large gap means a seek and there we follow the device.
                    // A small one is taken up forwards only: the line never goes
                    // back, or it twitches on pauses.
                    if (Math.Abs(diff) > 0.25) predicted = dev;
                    else if (diff > 0) predicted += TimeSpan.FromSeconds(diff * 0.5);
                }

                if (predicted > Duration) predicted = Duration;
                if (predicted < TimeSpan.Zero) predicted = TimeSpan.Zero;
                return predicted;
            }
        }

        // The file is read into memory whole and left closed: otherwise the
        // editor would hold the music busy and block replacing it.

        public void Load(string path)
        {
            Unload();
            byte[] data = File.ReadAllBytes(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ogg") source = new OggSource(data);
            else if (ext == ".mp3") source = new Mp3Source(data);
            else throw new NotSupportedException(S.ErrAudioFormat);

            FilePath = path;

            // Decoding takes a second or two and runs aside: the editor does not
            // freeze and the music buttons appear once the track is ready.
            ISampleSource src = source;
            source = null;
            preparing = true;
            Thread t = new Thread(delegate()
            {
                try
                {
                    DecodeAll(src);
                    ready = true;
                }
                catch (Exception) { }   // with no sound the editor works as usual
                finally
                {
                    src.Dispose();
                    preparing = false;
                    Action done2 = Ready;
                    if (done2 != null) done2();
                }
            });
            t.IsBackground = true;
            t.Start();
            seekBase = TimeSpan.Zero;
            predicted = TimeSpan.Zero;
            lastSyncSeconds = -1;
        }

        // The whole track is decoded into memory: minutes of music are tens of
        // megabytes, but there are no crashes inside the decoder and no waiting
        // on a seek.
        private void DecodeAll(ISampleSource src)
        {
            channels = src.Channels;
            sampleRate = src.SampleRate;
            totalTime = src.Duration;
            OpenDevice();

            int enough = sampleRate * channels * 2;   // two seconds are enough to start

            // The duration is known in advance, so the room is taken at once and
            // decoding does not spend itself moving an array about.
            int guess = (int)(totalTime.TotalSeconds * sampleRate * channels) + sampleRate * channels;
            if (guess < 4096) guess = 4096;
            short[] all = new short[guess];
            float[] buf = new float[65536];
            int done = 0;
            while (true)
            {
                int got;
                try { got = src.Read(buf, 0, buf.Length); }
                catch (Exception) { break; }   // end of file or a broken track
                if (got <= 0) break;
                if (done + got > all.Length)
                {
                    short[] bigger = new short[Math.Max(all.Length * 2, done + got)];
                    Array.Copy(all, bigger, done);
                    all = bigger;
                }
                for (int i = 0; i < got; i++)
                {
                    float v = buf[i];
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    all[done + i] = (short)(v * 32767f);
                }
                done += got;

                // The first seconds are ready: play now, the rest will catch up.
                pcmAll = all;
                pcmCount = done;
                if (!ready && done >= enough)
                {
                    ready = true;
                    Action first = Ready;
                    if (first != null) first();
                }
            }
            pcmAll = all;
            pcmCount = done;
            decodingDone = true;
            if (done > 0) totalTime = TimeSpan.FromSeconds(done / (double)(sampleRate * channels));
        }

        public void Unload()
        {
            StopFeeder();
            CloseDevice();
            ready = false;
            preparing = false;
            decodingDone = false;
            pcmAll = null;
            pcmCount = 0;
            pcmPos = 0;
            if (source != null) { source.Dispose(); source = null; }
            FilePath = null;
        }

        private void OpenDevice()
        {
            WaveFormatEx fmt = new WaveFormatEx();
            fmt.wFormatTag = 1;                       // PCM
            fmt.nChannels = (short)channels;
            fmt.nSamplesPerSec = sampleRate;
            fmt.wBitsPerSample = 16;
            fmt.nBlockAlign = (short)(fmt.nChannels * 2);
            fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;
            fmt.cbSize = 0;
            bytesPerSecond = fmt.nAvgBytesPerSec;

            int err = waveOutOpen(out hWaveOut, WaveMapper, ref fmt, IntPtr.Zero, IntPtr.Zero, CallbackNull);
            if (err != 0) throw new InvalidOperationException(S.ErrAudioDevice + err);

            bufferBytes = bytesPerSecond * BufferMillis / 1000;
            bufferBytes -= bufferBytes % fmt.nBlockAlign;

            headerPtr = new IntPtr[BufferCount];
            dataPtr = new IntPtr[BufferCount];
            for (int i = 0; i < BufferCount; i++)
            {
                dataPtr[i] = Marshal.AllocHGlobal(bufferBytes);
                headerPtr[i] = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHdr)));
                WaveHdr h = new WaveHdr();
                h.lpData = dataPtr[i];
                h.dwBufferLength = bufferBytes;
                h.dwFlags = WhdrDone;       // counted as free
                Marshal.StructureToPtr(h, headerPtr[i], false);
            }
        }

        private void CloseDevice()
        {
            if (hWaveOut == IntPtr.Zero) return;
            waveOutReset(hWaveOut);
            for (int i = 0; i < BufferCount; i++)
            {
                WaveHdr h = (WaveHdr)Marshal.PtrToStructure(headerPtr[i], typeof(WaveHdr));
                if ((h.dwFlags & WhdrPrepared) != 0)
                    waveOutUnprepareHeader(hWaveOut, headerPtr[i], Marshal.SizeOf(typeof(WaveHdr)));
                Marshal.FreeHGlobal(headerPtr[i]);
                Marshal.FreeHGlobal(dataPtr[i]);
            }
            waveOutClose(hWaveOut);
            hWaveOut = IntPtr.Zero;
            headerPtr = null;
            dataPtr = null;
        }

        public void Play()
        {
            if (!ready) return;   // the track is still being prepared
            if (playing) return;

            // Played to the end: before starting we go back to the beginning.
            if (endOfStream || Position >= Duration - TimeSpan.FromMilliseconds(50))
                Seek(TimeSpan.Zero);

            playing = true;
            endOfStream = false;
            predictedStamp = clock.Elapsed;   // time runs from this moment
            lastSyncSeconds = clock.Elapsed.TotalSeconds;
            if (feeder == null)
            {
                running = true;
                feeder = new Thread(FeedLoop);
                feeder.IsBackground = true;
                feeder.Start();
            }
            waveOutRestart(hWaveOut);
        }

        public void Pause()
        {
            if (!playing) return;
            playing = false;
            waveOutPause(hWaveOut);
        }

        public void TogglePlay()
        {
            if (playing) Pause(); else Play();
        }

        public void Seek(TimeSpan pos)
        {
            if (!ready) return;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > Duration) pos = Duration;


            // The position counter is moved at once: the line in the editor must
            // not wait for the sound to catch up.
            devBytes = 0;
            lock (clockGate)
            {
                seekBase = pos;
                predicted = pos;
                predictedStamp = clock.Elapsed;
                lastSyncSeconds = -1;
            }

            // The sound itself is seeked by the feeding thread: exactly one
            // thread talks to the device, so nothing can overlap.
            pendingSeek = pos;
            hasPendingSeek = true;
            if (!playing && feeder == null) DoSeekWork();
        }

        // The real seek: from the feeding thread only, or when there is none.
        private void DoSeekWork()
        {
            TimeSpan pos = pendingSeek;
            hasPendingSeek = false;
            lock (gate)
            {
                waveOutReset(hWaveOut);
                devBytes = 0;
                pcmPos = (int)(pos.TotalSeconds * sampleRate) * channels;
                if (pcmPos < 0) pcmPos = 0;
                if (pcmPos > pcmCount) pcmPos = Math.Max(0, pcmCount - channels);
                for (int i = 0; i < BufferCount; i++)
                {
                    WaveHdr h = (WaveHdr)Marshal.PtrToStructure(headerPtr[i], typeof(WaveHdr));
                    h.dwFlags |= WhdrDone;
                    Marshal.StructureToPtr(h, headerPtr[i], false);
                }
            }
        }


        public void Rewind() { Seek(TimeSpan.Zero); }

        private void StopFeeder()
        {
            running = false;
            playing = false;
            Thread t = feeder;
            feeder = null;
            if (t != null && t.IsAlive) t.Join(500);
        }

        private void FeedLoop()
        {
            short[] samples = new short[bufferBytes / 2];
            byte[] pcm = new byte[bufferBytes];
            int hdrSize = Marshal.SizeOf(typeof(WaveHdr));

            while (running)
            {
                if (hasPendingSeek) DoSeekWork();
                else lock (gate) SampleDevice();

                if (!playing || endOfStream) { Thread.Sleep(15); continue; }

                bool didWork = false;
                lock (gate)
                {
                    for (int i = 0; i < BufferCount && playing; i++)
                    {
                        WaveHdr h = (WaveHdr)Marshal.PtrToStructure(headerPtr[i], typeof(WaveHdr));
                        if ((h.dwFlags & WhdrDone) == 0) continue;

                        int got = Math.Min(samples.Length, pcmCount - pcmPos);
                        if (got <= 0)
                        {
                            // Feeding caught up with decoding: we wait, and only
                            // a fully decoded track counts as ended.
                            if (!decodingDone) break;
                            endOfStream = true;
                            playing = false;
                            break;
                        }

                        Buffer.BlockCopy(pcmAll, pcmPos * 2, pcm, 0, got * 2);
                        pcmPos += got;
                        Marshal.Copy(pcm, 0, h.lpData, got * 2);

                        if ((h.dwFlags & WhdrPrepared) != 0)
                            waveOutUnprepareHeader(hWaveOut, headerPtr[i], hdrSize);

                        h.dwBufferLength = got * 2;
                        h.dwFlags = 0;
                        Marshal.StructureToPtr(h, headerPtr[i], false);
                        waveOutPrepareHeader(hWaveOut, headerPtr[i], hdrSize);
                        waveOutWrite(hWaveOut, headerPtr[i], hdrSize);
                        didWork = true;
                    }
                }
                if (!didWork) Thread.Sleep(10);
            }
        }

        public void Dispose() { Unload(); }
    }
}
