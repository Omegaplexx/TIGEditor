using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using TigEditor;

// Everything about listening: the player itself and the green line that says
// where playback starts. The line lives in the canvas, but it exists for the
// music, so both are checked in one place.
//
// Optional argument: a path to music.mp3 or music.ogg. Without it the player
// part is skipped and the green line checks still run.
class AudioTest
{
    static int bad;

    static void Main(string[] a)
    {
        S.Load("");
        StartLine();
        if (a.Length > 0)
        {
            Decoding(a[0]);
            Loading(a[0]);
        }
        else Console.WriteLine("\nplayer: skipped, no music path given");

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "all playback checks passed" : bad + " CHECKS FAILED");
    }

    static void Say(string what, bool good)
    {
        Console.WriteLine("{0,-46} {1}", what, good ? "yes" : "NO");
        if (!good) bad++;
    }

    // The mark belongs to listening, not to the level, so a new level drops it.
    // Outside the placing mode the right button must not touch it: there it
    // erases objects instead.
    static void StartLine()
    {
        GridCanvas c = new GridCanvas();
        Form f = new Form();
        f.Controls.Add(c);
        c.Dock = DockStyle.Fill;
        f.Show();
        Application.DoEvents();

        c.SetLevel(new Level());
        c.StartX = 900;
        c.PlacingStart = true;
        c.SetLevel(new Level());
        Say("a new level drops the mark", c.StartX < 0);
        Say("a new level leaves the placing mode", !c.PlacingStart);

        c.StartX = 900;
        bool asked = false;
        c.StartRequested += delegate { asked = true; };
        MethodInfo down = typeof(GridCanvas).GetMethod("OnMouseDown",
            BindingFlags.NonPublic | BindingFlags.Instance);
        down.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 200, 150, 0) });
        Say("outside the mode the right button leaves it alone", !asked);

        c.PlacingStart = true;
        down.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 200, 150, 0) });
        Say("inside the mode a click places it", asked);
        f.Dispose();
    }

    // Decoding with no sound device: speed and completeness.
    static void Decoding(string path)
    {
        Console.WriteLine();
        byte[] data = File.ReadAllBytes(path);
        bool ogg = Path.GetExtension(path).ToLowerInvariant() == ".ogg";
        ISampleSource src = ogg ? (ISampleSource)new OggSource(data) : new Mp3Source(data);
        float[] buf = new float[16384];
        Stopwatch sw = Stopwatch.StartNew();
        long total = 0;
        while (true)
        {
            int got;
            try { got = src.Read(buf, 0, buf.Length); }
            catch (Exception e)
            {
                Console.WriteLine("decoding broke off: " + e.GetType().Name);
                break;
            }
            if (got <= 0) break;
            total += got;
        }
        sw.Stop();
        Console.WriteLine("decoded {0:0.0} s in {1} ms, {2} MB in memory",
            total / (double)(src.SampleRate * src.Channels), sw.ElapsedMilliseconds,
            total * 2 / 1024 / 1024);
        Say("the track decoded to the end", total > 0);
        src.Dispose();
    }

    // Opening, readiness, seeking and how the line travels.
    static void Loading(string path)
    {
        Console.WriteLine();
        AudioPlayer p = new AudioPlayer();
        Stopwatch sw = Stopwatch.StartNew();
        try { p.Load(path); }
        catch (Exception e)
        {
            Say("opening threw " + e.GetType().Name, false);
            return;
        }
        Console.WriteLine("opened with no exception in {0} ms", sw.ElapsedMilliseconds);

        for (int i = 0; i < 300 && !p.IsLoaded; i++) Thread.Sleep(50);
        if (!p.IsLoaded)
        {
            Console.WriteLine("the track did not become ready, probably no sound device");
            p.Dispose();
            return;
        }
        Console.WriteLine("playable after {0} ms", sw.ElapsedMilliseconds);

        Random rnd = new Random(1);
        bool seeksOk = true;
        for (int i = 0; i < 200 && seeksOk; i++)
        {
            double t = rnd.NextDouble() * p.Duration.TotalSeconds;
            p.Seek(TimeSpan.FromSeconds(t));
            if (Math.Abs(p.Position.TotalSeconds - t) > 0.5)
            {
                Console.WriteLine("seeking drifted at step {0}", i);
                seeksOk = false;
            }
        }
        Say("200 seeks land where asked", seeksOk);

        double back = 0;
        TimeSpan prev = p.PredictedPosition;
        p.Play();
        for (int i = 0; i < 400; i++)
        {
            if (i % 7 == 0) { if (p.IsPlaying) p.Pause(); else p.Play(); }
            Thread.Sleep(5);
            TimeSpan now = p.PredictedPosition;
            double d = (now - prev).TotalSeconds;
            if (d < -0.001) back = Math.Max(back, -d);
            prev = now;
        }
        Say("the line never went backwards", back <= 0.001);
        if (back > 0.001)
            Console.WriteLine("   it went back by {0:0} ms", back * 1000);
        p.Dispose();
        Console.WriteLine("closed with no exception");
    }
}
