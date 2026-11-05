using System;

namespace EMailViewer
{
	public class EmailEintrag
	{
		public string Betreff { get; set; } = string.Empty;

		// Ob String oder DateTime ist Geschmackssache. 
		// Wenn es bereits ein formatierter String ist, lassen wir es so!
		public string Datum { get; set; } = string.Empty;

		// Der Absender fehlt uns noch für die Listenübersicht
		public string Absender { get; set; } = string.Empty;

		// DateiPfad mit großem 'P' (wie in deinem Entwurf)
		public string DateiPfad { get; set; } = string.Empty;
		public string? HtmlInhalt { get; set; }
	}
}