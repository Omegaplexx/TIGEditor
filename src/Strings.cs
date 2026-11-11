using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

namespace TigEditor
{
	// Interface text, Android style: code names a string by key, the text
	// lives in lang/<code>.xml as <string key="...">text</string>.
	// The files are built into the exe as resources named lang.<code>.xml.
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

		private const string Prefix = "lang.";
		private const string Suffix = ".xml";

		// Resource name of a language file, or null.
		private static string ResourceOf(string code)
		{
			foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
				if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
					&& name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(CodeOf(name), code, StringComparison.OrdinalIgnoreCase))
					return name;
			return null;
		}

		private static string CodeOf(string resource)
		{
			return resource.Substring(Prefix.Length, resource.Length - Prefix.Length - Suffix.Length);
		}

		public static string[] Available()
		{
			List<string> list = new List<string>();
			foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
				if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
					&& name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
					list.Add(CodeOf(name).ToLowerInvariant());
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
			string resource = ResourceOf(language);
			if (resource == null) return;

			Dictionary<string, string> loaded = new Dictionary<string, string>();
			try
			{
				XmlDocument doc = new XmlDocument();
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
					doc.Load(stream);
				foreach (XmlNode node in doc.SelectNodes("/strings/string"))
				{
					XmlAttribute key = node.Attributes["key"];
					if (key != null) loaded[key.Value] = node.InnerText;
				}
			}
			catch { return; }
			map = loaded;
		}
	}
}
