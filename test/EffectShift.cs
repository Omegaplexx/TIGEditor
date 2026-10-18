using System;
using System.Collections.Generic;
using System.IO;
using TigEditor;

// Effect stripes sit in the file ahead of what the editor shows. Three things
// are checked here: the shift lands in the file and not on screen, the display
// shift puts a stripe back where it was drawn, and resaving an existing level
// writes the same numbers it read.
class EffectShift
{
    static int bad;

    static void Main(string[] a)
    {
        NewLevel();
        DisplayShift();
        if (a.Length > 0) Existing(a[0]);
        else Console.WriteLine("\nexisting level: skipped, no path given");

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "all stripe checks passed" : bad + " CHECKS FAILED");
    }

    static void Say(string what, bool good)
    {
        Console.WriteLine("{0,-46} {1}", what, good ? "yes" : "NO");
        if (!good) bad++;
    }

    // A stripe placed on a given cell in the editor goes into the file shifted
    // and comes back on the same cell.
    static void DisplayShift()
    {
        Console.WriteLine();
        Level lv = new Level();
        int drawX = 30 * Level.Grid;                      // where it is seen in the editor
        int fileX = drawX - Level.EffectFallDrawShift;    // what goes into the file
        lv.BlocksFall.Add(new Interval(fileX, fileX + 5 * Level.Grid));
        Console.WriteLine("display shift: {0} pixels, that is {1} cells",
            Level.EffectFallDrawShift, Level.EffectFallDrawShift / (double)Level.Grid);
        Console.WriteLine("the stripe is seen at {0} and stored as {1}", drawX, fileX);

        string p = Path.Combine(Path.GetTempPath(), "eff-shift.dat");
        lv.Save(p);
        Level back = Level.Load(p);
        int shown = back.BlocksFall[0].StartX + Level.EffectFallDrawShift;
        Console.WriteLine("after write and read: {0}, shown at {1}",
            back.BlocksFall[0].StartX, shown);
        Say("the stripe returns to the same cell", shown == drawX);
        File.Delete(p);
    }

    static void NewLevel()
    {
        Level lv = new Level();
        lv.BlocksFall.Add(new Interval(900, 1200));     // orange
        lv.BlocksRise.Add(new Interval(2100, 2400));    // blue
        string p = Path.Combine(Path.GetTempPath(), "eff-new.dat");
        lv.Save(p);

        Level back = Level.Load(p);
        byte[] d = File.ReadAllBytes(p);
        int want = 900 + Level.EffectShift;

        Console.WriteLine("in the editor 900..1200, after reading {0}..{1}",
            back.BlocksFall[0].StartX, back.BlocksFall[0].EndX);
        Console.WriteLine("the file holds {0}: {1}, holds 900: {2}",
            want, Find(d, want), Find(d, 900));
        Say("orange: shifted in the file, unmoved in the editor",
            Find(d, want) && !Find(d, 900) && back.BlocksFall[0].StartX == 900);

        Say("blue: stored as it is, with no shift",
            Find(d, 2100) && !Find(d, 2100 + Level.EffectShift)
            && back.BlocksRise[0].StartX == 2100);
        File.Delete(p);
    }

    // An existing level is resaved and the stripe numbers are compared.
    static void Existing(string path)
    {
        List<int> before = EffectNumbers(path);
        Level lv = Level.Load(path);
        string p = Path.Combine(Path.GetTempPath(), "eff-again.dat");
        lv.Save(p);
        List<int> after = EffectNumbers(p);

        bool same = before.Count == after.Count;
        for (int i = 0; same && i < before.Count; i++) same &= before[i] == after[i];

        Console.WriteLine();
        Console.WriteLine("in the original file: {0}", Join(before));
        Console.WriteLine("after resaving:       {0}", Join(after));
        Say("the stripe numbers did not move", same);
        File.Delete(p);
    }

    // Stripes are read straight from the file, bypassing the editor.
    static List<int> EffectNumbers(string path)
    {
        Level lv = Level.Load(path);
        List<int> list = new List<int>();
        foreach (Interval iv in lv.BlocksFall)
        { list.Add(iv.StartX + Level.EffectShift); list.Add(iv.EndX + Level.EffectShift); }
        foreach (Interval iv in lv.BlocksRise) { list.Add(iv.StartX); list.Add(iv.EndX); }
        return list;
    }

    static string Join(List<int> list)
    {
        string s = "";
        foreach (int v in list) s += (s.Length > 0 ? ", " : "") + v;
        return s.Length > 0 ? s : "(no stripes)";
    }

    static bool Find(byte[] d, int value)
    {
        byte[] want = { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        for (int i = 0; i + 4 <= d.Length; i++)
        {
            bool same = true;
            for (int k = 0; k < 4; k++) same &= d[i + k] == want[k];
            if (same) return true;
        }
        return false;
    }
}
