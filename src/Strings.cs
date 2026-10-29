using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace TigEditor
{
	// Interface text, Android style: code names a string by key, the text
	// lives in lang/<code>.xml as <string key="...">text</string>.
	// A key missing from the file shows up as the key itself.
	public static class S
	{
		private static Dictionary<string, string> map = new Dictionary<string, string>();

		public static string Get(string key)
		{
			string value;
			return map.TryGetValue(key, out value) ? value : key;
		}

		public static string LanguageName(string code)
		{
			try { return new System.Globalization.CultureInfo(code).NativeName; }
			catch { return code; }
		}

		public static string[] Available()
		{
			List<string> list = new List<string>();
			try
			{
				string dir = LangFolder();
				if (Directory.Exists(dir))
					foreach (string f in Directory.GetFiles(dir, "*.xml"))
						list.Add(Path.GetFileNameWithoutExtension(f).ToLowerInvariant());
			}
			catch { }
			list.Sort();
			return list.ToArray();
		}

		public static string SystemLanguage()
		{
			string[] avail = Available();
			if (avail.Length == 0) return null;
			string code = System.Globalization.CultureInfo
				.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
			foreach (string have in avail)
				if (have == code) return code;
			return avail[0];
		}

		public static void Load(string language)
		{
			if (string.IsNullOrEmpty(language)) return;
			string path = Path.Combine(LangFolder(), language + ".xml");
			if (!File.Exists(path)) return;

			Dictionary<string, string> loaded = new Dictionary<string, string>();
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				foreach (XmlNode node in doc.SelectNodes("/strings/string"))
				{
					XmlAttribute key = node.Attributes["key"];
					if (key != null) loaded[key.Value] = node.InnerText;
				}
			}
			catch { return; }
			map = loaded;
		}

		private static string LangFolder()
		{
			return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "lang");
		}
	}
}
