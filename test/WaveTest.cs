using System;
using System.IO;
using TigEditor;

// Recoding of level sounds: odd formats become mono 16 bit with a plain header.
class WaveTest
{
	static int bad;

	static void Check(bool ok, string what)
	{
		if (ok) return;
		bad++;
		Console.WriteLine("FAIL: " + what);
	}

	static void Main()
	{
		string dir = Path.Combine(Path.GetTempPath(), "wavetest" + Environment.TickCount);
		Directory.CreateDirectory(dir);
		try { Run(dir); }
		finally { Directory.Delete(dir, true); }

		Console.WriteLine(bad == 0 ? "ok" : bad + " failed");
		Environment.Exit(bad == 0 ? 0 : 1);
	}

	static void Run(string dir)
	{
		// stereo with an extra block ahead of the data, as ffmpeg writes it
		string stereo = Path.Combine(dir, "explosion.wav");
		byte[] src = Build(1, 2, 44100, 16, Shorts(1000, 3000, -2000, -4000), true, -1);
		File.WriteAllBytes(stereo, src);

		string plain = Path.Combine(dir, "boop.wav");
		File.WriteAllBytes(plain, Build(1, 1, 44100, 16, Shorts(5, 6, 7), false, -1));

		Check(LevelFiles.RiskySounds(dir).Contains("explosion.wav"), "stereo flagged");
		Check(!LevelFiles.RiskySounds(dir).Contains("boop.wav"), "plain mono not flagged");

		WaveConverter.Convert(stereo);
		Check(LevelFiles.RiskySounds(dir).Count == 0, "nothing flagged after recoding");
		Check(File.Exists(stereo + ".bak"), "backup made");
		Check(Same(File.ReadAllBytes(stereo + ".bak"), src), "backup holds the original");
		short[] got = ReadMono(File.ReadAllBytes(stereo));
		Check(got.Length == 2 && got[0] == 2000 && got[1] == -3000, "stereo mixed down");

		WaveConverter.Convert(stereo);
		Check(Same(File.ReadAllBytes(stereo + ".bak"), src), "second run keeps the first backup");

		// 24 bit mono
		got = ReadMono(WaveConverter.Recode(Build(1, 1, 22050, 24,
			new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 }, false, -1)));
		Check(got.Length == 2 && got[0] == 16384 && got[1] == -16384, "24 bit");

		// 8 bit stereo
		got = ReadMono(WaveConverter.Recode(Build(1, 2, 8000, 8, new byte[] { 255, 129, 0, 0 }, false, -1)));
		Check(got.Length == 2 && got[0] == 16384 && got[1] == -32768, "8 bit stereo");

		// float stereo
		byte[] fl = new byte[16];
		BitConverter.GetBytes(0.5f).CopyTo(fl, 0);
		BitConverter.GetBytes(0.25f).CopyTo(fl, 4);
		BitConverter.GetBytes(2.0f).CopyTo(fl, 8);
		BitConverter.GetBytes(2.0f).CopyTo(fl, 12);
		got = ReadMono(WaveConverter.Recode(Build(3, 2, 44100, 32, fl, false, -1)));
		Check(got.Length == 2 && got[0] == 12288 && got[1] == 32767, "float, clipped");

		// a stream writer leaves the data size as all ones
		got = ReadMono(WaveConverter.Recode(Build(1, 1, 44100, 16, Shorts(1, 2, 3), false, 0xFFFFFFFFL)));
		Check(got.Length == 3, "data size all ones");
		got = ReadMono(WaveConverter.Recode(Build(1, 1, 44100, 16, Shorts(1, 2, 3), false, 0x7FFFFFFF)));
		Check(got.Length == 3, "data size larger than the file");

		Throws(typeof(NotSupportedException), Build(2, 1, 44100, 4, new byte[8], false, -1), "compressed");
		Throws(typeof(InvalidDataException), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }, "garbage");
	}

	static void Throws(Type type, byte[] data, string what)
	{
		try { WaveConverter.Recode(data); }
		catch (Exception ex) { Check(type.IsInstanceOfType(ex), what + ": wrong exception " + ex.GetType().Name); return; }
		Check(false, what + ": no exception");
	}

	static bool Same(byte[] a, byte[] b)
	{
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
		return true;
	}

	static byte[] Shorts(params short[] v)
	{
		byte[] b = new byte[v.Length * 2];
		for (int i = 0; i < v.Length; i++) BitConverter.GetBytes(v[i]).CopyTo(b, i * 2);
		return b;
	}

	// The result must be mono 16 bit with the 44 byte header; returns the samples.
	static short[] ReadMono(byte[] f)
	{
		Check(f.Length >= 44 && f[36] == 'd' && f[37] == 'a' && f[38] == 't' && f[39] == 'a', "plain header");
		Check(BitConverter.ToInt16(f, 20) == 1 && BitConverter.ToInt16(f, 22) == 1
			&& BitConverter.ToInt16(f, 34) == 16, "mono 16 bit PCM");
		Check(BitConverter.ToInt32(f, 40) == f.Length - 44, "data size matches");
		short[] s = new short[(f.Length - 44) / 2];
		for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(f, 44 + i * 2);
		return s;
	}

	// sizeOverride: -1 writes the true data size, anything else is written as is
	static byte[] Build(int tag, int channels, int rate, int bits, byte[] data, bool listBlock, long sizeOverride)
	{
		MemoryStream ms = new MemoryStream();
		BinaryWriter w = new BinaryWriter(ms);
		w.Write(new char[] { 'R', 'I', 'F', 'F' });
		w.Write(0);
		w.Write(new char[] { 'W', 'A', 'V', 'E' });
		w.Write(new char[] { 'f', 'm', 't', ' ' });
		w.Write(16);
		w.Write((short)tag);
		w.Write((short)channels);
		w.Write(rate);
		w.Write(rate * channels * bits / 8);
		w.Write((short)(channels * bits / 8));
		w.Write((short)bits);
		if (listBlock)
		{
			w.Write(new char[] { 'L', 'I', 'S', 'T' });
			w.Write(26);
			w.Write(new char[] { 'I', 'N', 'F', 'O', 'I', 'S', 'F', 'T' });
			w.Write(14);
			w.Write(new char[] { 'L', 'a', 'v', 'f', '6', '0', '.', '1', '1', '.', '1', '0', '0', '\0' });
		}
		w.Write(new char[] { 'd', 'a', 't', 'a' });
		w.Write(sizeOverride == -1 ? data.Length : unchecked((int)sizeOverride));
		w.Write(data);
		w.Flush();
		byte[] f = ms.ToArray();
		BitConverter.GetBytes(f.Length - 8).CopyTo(f, 4);
		return f;
	}
}
