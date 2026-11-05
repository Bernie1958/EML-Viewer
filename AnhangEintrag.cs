using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MimeKit;

namespace EMailViewer
{
	public class AnhangEintrag
	{
		/// <summary>
		/// Der Dateiname des Anhangs (oder ein Fallback-Name, falls beschädigt).
		/// </summary>
		public string? CustomFilename { get; set; }

		/// <summary>
		/// Das originale MimeKit-Objekt, das die Rohdaten der Datei enthält.
		/// </summary>
		public MimePart? MimeTeil { get; set; }

		/// <summary>
		/// Die bereits für die UI formatierte Dateigröße (z.B. "142.5 KB").
		/// </summary>
		public string? GroesseFormatiert { get; set; }
		public string? TempFilename { get; set; }
	}
}
