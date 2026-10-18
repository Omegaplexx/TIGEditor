using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace TigEditor
{
    // Custom level graphics. A game_1.png may sit next to the level, and how it
    // is cut up is described in res/game1.bin next to the game. That description
    // is written by a Java stream, so every number is big endian:
    //   short          version
    //   short + text   image name
    //   short          record count
    //   short + text   set name
    //   then records:  short + text, then four floats: left, top, width and
    //                  height as fractions of the image
    public class GameAtlas : IDisposable
    {
        public const string ImageName = "game_1.png";
        public const string DescriptionName = "game1.bin";

        public Bitmap Block, Spike, Pit;

        // Whole background strips, the way the game draws them, plus one colour
        // from each for the palette icons.
        public Bitmap[] BackgroundStrips;
        public Color[] BackgroundSamples;

        public bool HasBackgrounds { get { return BackgroundStrips != null; } }

        public bool HasObjects { get { return Block != null || Spike != null || Pit != null; } }

        // Piece proportions come from the atlas: a pit there is lower than a cell.
        public double PitRatio = 37.0 / 75.0;
        public double BlockRatio = 1.0;
        public double SpikeRatio = 1.0;

        public static GameAtlas TryLoad(string levelFolder, string gamePath)
        {
            if (levelFolder == null || gamePath == null) return null;
            string image = Path.Combine(levelFolder, ImageName);
            if (!File.Exists(image)) return null;

            string description = Path.Combine(
                Path.Combine(Path.GetDirectoryName(gamePath), "res"), DescriptionName);
            if (!File.Exists(description)) return null;

            try
            {
                GameAtlas atlas = new GameAtlas();
                atlas.Load(image, description);
                return atlas.HasObjects || atlas.HasBackgrounds ? atlas : null;
            }
            catch { return null; }
        }

        private void Load(string imagePath, string descriptionPath)
        {
            byte[] desc = File.ReadAllBytes(descriptionPath);
            int pos = 0;

            ReadShort(desc, ref pos);                 // version
            ReadName(desc, ref pos);                  // image name, we already know ours
            int count = ReadShort(desc, ref pos);
            ReadName(desc, ref pos);                  // set name

            // The image is read into memory whole, leaving the file free.
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(imagePath)))
            using (Bitmap sheet = new Bitmap(ms))
            {
                Bitmap[] strips = new Bitmap[6];

                for (int i = 0; i < count && pos + 2 <= desc.Length; i++)
                {
                    string name = ReadName(desc, ref pos);
                    if (pos + 16 > desc.Length) break;
                    float u = ReadFloat(desc, ref pos);
                    float v = ReadFloat(desc, ref pos);
                    float du = ReadFloat(desc, ref pos);
                    float dv = ReadFloat(desc, ref pos);

                    Rectangle r = Rect(sheet, u, v, du, dv);
                    if (r.Width < 1 || r.Height < 1) continue;

                    switch (name)
                    {
                        case "block": Block = Cut(sheet, r); BlockRatio = r.Height / (double)r.Width; break;
                        case "spike": Spike = Cut(sheet, r); SpikeRatio = r.Height / (double)r.Width; break;
                        case "pit": Pit = Cut(sheet, r); PitRatio = r.Height / (double)r.Width; break;
                        default:
                            int bg = BackgroundIndex(name);
                            if (bg >= 0) strips[bg] = Cut(sheet, r);
                            break;
                    }
                }

                // The black background has no strip, it is plain black in the game too.
                if (strips[5] == null) strips[5] = Solid(Color.Black);

                for (int i = 0; i < strips.Length; i++)
                    if (strips[i] == null)
                    {
                        foreach (Bitmap b in strips) if (b != null) b.Dispose();
                        return;                              // an incomplete set, backgrounds are left alone
                    }

                BackgroundStrips = strips;
                BackgroundSamples = new Color[strips.Length];
                for (int i = 0; i < strips.Length; i++)
                    BackgroundSamples[i] = strips[i].GetPixel(strips[i].Width / 2, strips[i].Height - 1);
            }
        }

        // "background" is the first colour, "background2" the second and so on.
        private static int BackgroundIndex(string name)
        {
            if (name == "background") return 0;
            if (name.Length == 11 && name.StartsWith("background"))
            {
                int n = name[10] - '0';
                if (n >= 2 && n <= 6) return n - 1;
            }
            return -1;
        }

        private static Rectangle Rect(Bitmap sheet, float u, float v, float du, float dv)
        {
            int x = (int)Math.Round(u * sheet.Width);
            int y = (int)Math.Round(v * sheet.Height);
            int w = (int)Math.Round(du * sheet.Width);
            int h = (int)Math.Round(dv * sheet.Height);
            x = Math.Max(0, Math.Min(x, sheet.Width - 1));
            y = Math.Max(0, Math.Min(y, sheet.Height - 1));
            w = Math.Max(0, Math.Min(w, sheet.Width - x));
            h = Math.Max(0, Math.Min(h, sheet.Height - y));
            return new Rectangle(x, y, w, h);
        }

        private static Bitmap Cut(Bitmap sheet, Rectangle r)
        {
            Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(sheet, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            }
            return b;
        }

        private static Bitmap Solid(Color c)
        {
            Bitmap b = new Bitmap(1, 2, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(c);
            return b;
        }

        private static int ReadShort(byte[] d, ref int pos)
        {
            int v = (d[pos] << 8) | d[pos + 1];
            pos += 2;
            return v;
        }

        private static string ReadName(byte[] d, ref int pos)
        {
            int n = ReadShort(d, ref pos);
            string s = System.Text.Encoding.ASCII.GetString(d, pos, n);
            pos += n;
            return s;
        }

        private static float ReadFloat(byte[] d, ref int pos)
        {
            byte[] four = { d[pos + 3], d[pos + 2], d[pos + 1], d[pos] };
            pos += 4;
            return BitConverter.ToSingle(four, 0);
        }

        public void Dispose()
        {
            if (Block != null) { Block.Dispose(); Block = null; }
            if (Spike != null) { Spike.Dispose(); Spike = null; }
            if (Pit != null) { Pit.Dispose(); Pit = null; }
            if (BackgroundStrips != null)
            {
                foreach (Bitmap b in BackgroundStrips) if (b != null) b.Dispose();
                BackgroundStrips = null;
            }
        }
    }
}
