using System;
using System.IO;
using MimeKit;

namespace EMailViewer
{
	public class EmailDetails
	{
		public string Betreff { get; set; } = string.Empty;
		public string Absender { get; set; } = string.Empty;
		public string Empfaenger { get; set; } = string.Empty;
		public string Datum { get; set; } = string.Empty;
		public string TextInhalt { get; set; } = string.Empty;
		public string HtmlInhalt { get; set; } = string.Empty;
	}

	public static class EmailService
	{
		public static EmailDetails LadeEmailDetails(string dateiPfad)
		{
			var details = new EmailDetails();

			if (!File.Exists(dateiPfad))
			{
				details.TextInhalt = "Fehler: Die E-Mail-Datei existiert nicht mehr auf der Festplatte.";
				return details;
			}

			try
			{
				var message = MimeMessage.Load(dateiPfad);

				details.Betreff = string.IsNullOrWhiteSpace(message.Subject) ? "(Kein Betreff)" : message.Subject;
				details.Absender = message.From?.ToString() ?? string.Empty;
				details.Empfaenger = message.To?.ToString() ?? string.Empty;
				details.Datum = message.Date.ToString("dd.MM.yyyy HH:mm");
				details.HtmlInhalt = message.HtmlBody ?? string.Empty;

				if (!string.IsNullOrEmpty(message.TextBody))
				{
					details.TextInhalt = message.TextBody;
				}
				else if (!string.IsNullOrEmpty(message.HtmlBody))
				{
					// Fallback: Wenn nur HTML da ist, entfernen wir die Tags für die TextBox ganz simpel
					string html = message.HtmlBody;
					// Entfernt alle <tags> grob, damit kein HTML-Salat in der TextBox steht
					var schlichterText = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
					details.TextInhalt = schlichterText.Trim();
				}
				else
				{
					details.TextInhalt = "(Diese E-Mail enthält keinen lesbaren Inhalt)";
				}
			}
			catch (Exception ex)
			{
				details.TextInhalt = $"Fehler beim Lesen der E-Mail-Datei: {ex.Message}";
			}

			return details;
		}
	}
}