using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TigEditor
{
	// Settings live in an ini next to the exe. If the folder is read only they
	// simply are not saved: the program is fully portable and never writes to
	// the user profile or the registry.
	public class Settings
	{
		private readonly Dictionary<string, string> values = new Dictionary<string, string>();
		private readonly string path;

		public Settings()
		{
			path = ChoosePath();
			Load();
		}

		private static string ChoosePath()
		{
			try
			{
				// The settings file is named after the editor itself, so a
				// renamed exe gets an ini with the matching name.
				string exe = Application.ExecutablePath;
				return Path.Combine(Path.GetDirectoryName(exe),
									Path.GetFileNameWithoutExtension(exe) + ".ini");
			}
			catch
			{
				return null;   // a read only folder, no settings are kept
			}
		}

		private void Load()
		{
			try
			{
				if (path == null || !File.Exists(path)) return;
				foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
				{
					int eq = line.IndexOf('=');
					if (eq <= 0) continue;
					values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
				}
			}
			catch { }
		}

		public void Save()
		{
			if (path == null) return;
			try
			{
				StringBuilder sb = new StringBuilder();
				foreach (KeyValuePair<string, string> kv in values)
					sb.AppendLine(kv.Key + "=" + kv.Value);
				File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
			}
			catch { }
		}

		public string Get(string key) { return Get(key, string.Empty); }

		public string Get(string key, string def)
		{
			string v;
			return values.TryGetValue(key, out v) ? v : def;
		}

		public int GetInt(string key, int def)
		{
			int v;
			return int.TryParse(Get(key), out v) ? v : def;
		}

		public void Set(string key, string value) { values[key] = value ?? string.Empty; }
		public void SetInt(string key, int value) { values[key] = value.ToString(); }
	}
}
