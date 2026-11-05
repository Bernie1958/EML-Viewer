using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MimeKit;

namespace EMailViewer
{
	public static class EMailManager
	{
		/// <summary>
		/// Liest alle .eml-Dateien ASYNCHRON aus dem angegebenen Archiv-Verzeichnis ein.
		/// </summary>
		public static async Task<List<EmailEintrag>> LadeLokaleEMailsAsync(string archivPfad)
		{
			return await Task.Run(() =>
			{
				var temporaereListe = new List<EmailEintrag>();

				if (!Directory.Exists(archivPfad))
				{
					System.Diagnostics.Debug.WriteLine($"[MANAGER] Ordner nicht gefunden: {archivPfad}");
					return temporaereListe;
				}

				string[] dateien = Directory.GetFiles(archivPfad, "*.eml");

				foreach (string datei in dateien)
				{
					try
					{
						using (var stream = File.OpenRead(datei))
						{
							var message = MimeMessage.Load(stream);

							temporaereListe.Add(new EmailEintrag
							{
								Absender = string.IsNullOrWhiteSpace(message.From.ToString()) ? "(Unbekannter Absender)" : message.From.ToString(),
								Betreff = string.IsNullOrWhiteSpace(message.Subject) ? "(Kein Betreff)" : message.Subject,
								Datum = message.Date.ToString("dd.MM.yyyy HH:mm"),
								DateiPfad = datei
							});
						}
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[MANAGER] Fehler bei Datei {datei}: {ex.Message}");
					}
				}

				return temporaereListe;
			});
		}

		/// <summary>
		/// Löscht die physische .eml-Datei von der Festplatte und entfernt sie aus der Liste.
		/// </summary>
		public static bool EMailLoeschen(List<EmailEintrag> liste, EmailEintrag eintrag)
		{
			if (eintrag == null || liste == null)
			{
				return false;
			}

			try
			{
				if (File.Exists(eintrag.DateiPfad))
				{
					File.Delete(eintrag.DateiPfad);
				}

				return liste.Remove(eintrag);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MANAGER] Löschfehler bei Datei {eintrag.DateiPfad}: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Löscht eine E-Mail und berechnet den mathematisch nächsten Index für die UI.
		/// </summary>
		public static int EMailLoeschenUndNaechstenIndexBerechnen(List<EmailEintrag> liste, EmailEintrag eintrag, int aktuellerIndex)
		{
			bool erfolg = EMailLoeschen(liste, eintrag);

			if (!erfolg)
			{
				return -1;
			}

			if (liste.Count == 0)
			{
				return -1;
			}

			return Math.Min(aktuellerIndex, liste.Count - 1);
		}
	}
}