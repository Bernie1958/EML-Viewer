using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using MimeKit;

namespace EMailViewer
{
	public sealed partial class MainWindow : Window
	{
		private string _archivPfad = string.Empty;
		private CancellationTokenSource? _cts;
		public ObservableCollection<EmailEintrag> EmailListe { get; set; } = new ObservableCollection<EmailEintrag>();

		public MainWindow()
		{
			System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

			// FirstChanceException anpassen: Harmlose Abbrüche beim Schließen ignorieren
			AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
			{
				if (e.Exception is OperationCanceledException || e.Exception is TaskCanceledException)
				{
					// Diese Exceptions sind beim Schließen gewollt und KEIN echter Fehler
					return;
				}
				System.Diagnostics.Debug.WriteLine($"[CRITICAL UI ERROR] {e.Exception.Message}");
			};

			this.InitializeComponent();
			EmailListView.KeyDown += EmailListView_KeyDown;

			// Die ObservableCollection fest mit dem ListView verdrahten
			EmailListView.ItemsSource = EmailListe;

			PfadTextBox.Text = _archivPfad;
			this.Title = "Lokales E-Mail Archiv - Bitte Ordner wählen";

			// NEU: Beim Schließen des Fensters alle laufenden Tasks hart aber sauber stoppen
			this.Closed += (sender, args) =>
			{
				_cts?.Cancel();
				_cts?.Dispose();
			};

			// NEU: Das vorhandene CancellationToken an die Methode übergeben
			// (Stelle sicher, dass die Signatur deiner Methode MailsAusOrdnerLadenAsync(CancellationToken token) akzeptiert)
			_ = MailsAusOrdnerLadenAsync();
		}

		// SCHNELLSTES CHUNK-LADEN: Holt 30.000 Mails blockierungsfrei in 100er-Paketen via Header-Only
		private async Task MailsAusOrdnerLadenAsync()
		{
			// 1. Sicheres Instanziieren: Altes Token abbrechen, falls vorhanden
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			var token = _cts.Token;

			if (string.IsNullOrWhiteSpace(_archivPfad))
			{
				EmailListe.Clear();
				EmailDetailPanel.Visibility = Visibility.Collapsed;
				NoSelectionText.Visibility = Visibility.Visible;
				NoSelectionText.Text = "Bitte gib oben einen gültigen Ordnerpfad ein und klicke auf 'Ordner laden'.";
				return;
			}

			LadeAnzeige.IsActive = true;
			LadeAnzeige.Visibility = Visibility.Visible;
			NoSelectionText.Visibility = Visibility.Collapsed;
			EmailDetailPanel.Visibility = Visibility.Collapsed;
			EmailListe.Clear();

			try
			{
				if (!Directory.Exists(_archivPfad))
				{
					NoSelectionText.Visibility = Visibility.Visible;
					NoSelectionText.Text = $"Der Ordner '{_archivPfad}' wurde nicht gefunden.";
					return;
				}

				// Token auch an Task.Run übergeben
				string[] alleDateipfade = await Task.Run(() => Directory.GetFiles(_archivPfad, "*.eml"), token);

				if (alleDateipfade.Length == 0)
				{
					NoSelectionText.Visibility = Visibility.Visible;
					NoSelectionText.Text = "Keine .eml-Dateien in diesem Ordner gefunden.";
					return;
				}

				NoSelectionText.Visibility = Visibility.Visible;
				int chunkGroesse = 100;
				bool ersteRunde = true;

				for (int i = 0; i < alleDateipfade.Length; i += chunkGroesse)
				{
					// 2. VOR dem nächsten Schritt prüfen, ob die App geschlossen wird
					if (token.IsCancellationRequested) return;

					var aktuelleDateien = alleDateipfade.Skip(i).Take(chunkGroesse).ToList();

					List<EmailEintrag> parsedChunk = await Task.Run(async () =>
					{
						var temporaereListe = new List<EmailEintrag>();
						foreach (var datei in aktuelleDateien)
						{
							if (token.IsCancellationRequested) break;
							try
							{
								var headers = await HeaderList.LoadAsync(datei, token);
								string roherAbsender = headers[HeaderId.From] ?? "Unbekannt";
								string bereinigterAbsender = roherAbsender.Split('<')[0].Trim().Replace("\"", "");

								if (string.IsNullOrEmpty(bereinigterAbsender) && roherAbsender.Contains("<"))
								{
									bereinigterAbsender = roherAbsender.Split('<')[1].Replace(">", "").Trim();
								}

								string rohesDatum = headers[HeaderId.Date] ?? string.Empty;
								string formatiertesDatum = string.Empty;

								if (DateTime.TryParse(rohesDatum, out DateTime parsedDate))
								{
									formatiertesDatum = parsedDate.ToString("dd.MM.yyyy HH:mm");
								}
								else
								{
									formatiertesDatum = rohesDatum;
								}

								temporaereListe.Add(new EmailEintrag
								{
									Absender = bereinigterAbsender,
									Betreff = headers[HeaderId.Subject] ?? "(Kein Betreff)",
									Datum = formatiertesDatum,
									DateiPfad = datei
								});
							}
							catch { /* Defekte Mail überspringen */ }
						}
						return temporaereListe;
					}, token);

					// 3. NACH dem Task.Run prüfen: Wenn abgebrochen wurde, UI-Zugriffe strikt unterlassen!
					if (token.IsCancellationRequested) return;

					foreach (var mail in parsedChunk) EmailListe.Add(mail);

					if (ersteRunde)
					{
						LadeAnzeige.IsActive = false;
						LadeAnzeige.Visibility = Visibility.Collapsed;
						ersteRunde = false;
					}

					NoSelectionText.Text = $"{EmailListe.Count} von {alleDateipfade.Length} E-Mails geladen...";

					// Ermöglicht der UI zu atmen, bricht hier aber auch sauber ab, falls token aktiv
					await Task.Delay(1, token);
				}

				if (!token.IsCancellationRequested)
				{
					NoSelectionText.Text = $"Erfolgreich alle {EmailListe.Count} E-Mails geladen. Wähle eine aus, um die Vorschau anzuzeigen.";
				}
			}
			catch (OperationCanceledException)
			{
				// Völlig normaler Abbruch beim Schließen
			}
			catch (Exception ex)
			{
				// Nur im echten UI-Kontext schreiben
				if (!token.IsCancellationRequested)
				{
					NoSelectionText.Text = $"Fehler beim Laden des Archivs: {ex.Message}";
				}
			}
			finally
			{
				// 4. Absicherung im Finally: Nur UI manipulieren, wenn die App noch lebt
				if (!token.IsCancellationRequested)
				{
					LadeAnzeige.IsActive = false;
					LadeAnzeige.Visibility = Visibility.Collapsed;
				}
			}
		}

		// ASYNCHRONES DETAIL-LADEN: Lädt die Mail erst beim Anklicken vollständig ein
		private async void EmailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (EmailListView.SelectedItem is not EmailEintrag ausgewaehlteMail) return;

			try
			{
				EmailDetailPanel.Visibility = Visibility.Collapsed;
				LadeAnzeige.IsActive = true;
				LadeAnzeige.Visibility = Visibility.Visible;
				NoSelectionText.Visibility = Visibility.Collapsed;

				MimeMessage message;
				using (var stream = new FileStream(ausgewaehlteMail.DateiPfad, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
				{
					message = await MimeMessage.LoadAsync(stream);
				}

				DetailDatum.Text = ausgewaehlteMail.Datum;
				DetailBetreff.Text = ausgewaehlteMail.Betreff;
				ausgewaehlteMail.HtmlInhalt = message.HtmlBody ?? $"<html><body><pre style='font-family: sans-serif;'>{System.Net.WebUtility.HtmlEncode(message.TextBody)}</pre></body></html>";

				// Anhänge parsen
				var anjaengeListe = new List<AnhangEintrag>();
				foreach (var attachment in message.Attachments)
				{
					if (attachment is MimePart mimePart)
					{
						anjaengeListe.Add(new AnhangEintrag
						{
							CustomFilename = mimePart.FileName ?? "unbenannter_anhang",
							TempFilename = $"{Guid.NewGuid().ToString().Substring(0, 8)}_{mimePart.FileName}",
							MimeTeil = mimePart,
							GroesseFormatiert = "Bereit"
						});
					}
				}

				AnhaengeListView.ItemsSource = anjaengeListe.Count > 0 ? anjaengeListe : null;
				AnhaengePanel.Visibility = anjaengeListe.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

				await EmailWebView.EnsureCoreWebView2Async();
				EmailWebView.NavigateToString(ausgewaehlteMail.HtmlInhalt);
				EmailDetailPanel.Visibility = Visibility.Visible;
			}
			catch (Exception ex) { DetailBetreff.Text = $"Fehler beim Laden: {ex.Message}"; }
			finally { LadeAnzeige.IsActive = false; LadeAnzeige.Visibility = Visibility.Collapsed; }
		}

		private async void AnhangOeffnen_Click(object sender, RoutedEventArgs e)
		{
			if (sender is Button button && button.Tag is AnhangEintrag anhang)
			{
				// ABSICHERUNG GEGEN NULLVERWEIS:
				if (anhang.MimeTeil == null || string.IsNullOrEmpty(anhang.TempFilename)) return;
				try
				{
					string tempOrdner = Path.Combine(Path.GetTempPath(), "EMailViewer_Attachments");
					Directory.CreateDirectory(tempOrdner);
					string zielPfad = Path.Combine(tempOrdner, anhang.TempFilename);

					using (var stream = File.Create(zielPfad))
					{
						// Das '?' stellt sicher, dass DecodeToAsync nur aufgerufen wird, wenn Content ungleich null ist
						if (anhang.MimeTeil.Content != null)
						{
							await anhang.MimeTeil.Content.DecodeToAsync(stream);
						}
					}

					var prozess = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(zielPfad) { UseShellExecute = true } };
					prozess.Start();
				}
				catch (Exception ex)
				{
					ContentDialog fehlerDialog = new ContentDialog { Title = "Fehler beim Öffnen", Content = $"Fehler:\n{ex.Message}", CloseButtonText = "OK", XamlRoot = this.Content.XamlRoot };
					await fehlerDialog.ShowAsync();
				}
			}
		}

		private async void OrdnerLaden_Click(object sender, RoutedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(PfadTextBox.Text))
			{
				_archivPfad = PfadTextBox.Text.Trim();
				this.Title = $"Lokales E-Mail Archiv - {_archivPfad}";
				await MailsAusOrdnerLadenAsync();
			}
		}

		private async void OrdnerDurchsuchen_Click(object sender, RoutedEventArgs e)
		{
			var folderPicker = new Windows.Storage.Pickers.FolderPicker();
			var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
			WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
			folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
			folderPicker.FileTypeFilter.Add("*");

			var gewaehlterOrdner = await folderPicker.PickSingleFolderAsync();
			if (gewaehlterOrdner != null)
			{
				PfadTextBox.Text = gewaehlterOrdner.Path;
				_archivPfad = gewaehlterOrdner.Path;
				this.Title = $"Lokales E-Mail Archiv - {_archivPfad}";
				await MailsAusOrdnerLadenAsync();
			}
		}

		private void EmailLoeschen_Click(object sender, RoutedEventArgs e)
		{
			FühreLoeschvorgangAus();
		}

		private void EmailListView_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Delete || e.Key == VirtualKey.Back)
			{
				e.Handled = true;
				FühreLoeschvorgangAus();
			}
		}

		private void FühreLoeschvorgangAus()
		{
			// 1. Absichern, dass eine Mail ausgewählt ist (Dadurch ist ausgewaehlteMail ab hier NIE null!)
			if (EmailListView.SelectedItem is not EmailEintrag ausgewaehlteMail)
			{
				return;
			}

			try
			{
				int aktuellerIndex = EmailListView.SelectedIndex;

				// 2. Wir erstellen eine echte, eigenständige List<EmailEintrag>, um sie Ihrem Manager zu übergeben
				var temporaereListe = new List<EmailEintrag>(EmailListe);

				// 3. Aufruf Ihres ausgelagerten Managers mit der kompatiblen Liste
				int neuerIndex = EMailManager.EMailLoeschenUndNaechstenIndexBerechnen(temporaereListe, ausgewaehlteMail, aktuellerIndex);

				// 4. Den gelöschten Eintrag aus unserer echten UI-Live-Liste entfernen
				if (EmailListe.Contains(ausgewaehlteMail))
				{
					EmailListe.Remove(ausgewaehlteMail);
				}

				// 5. UI-Auswahl auf die nächste E-Mail setzen, falls noch Mails übrig sind
				if (EmailListe.Count > 0)
				{
					// Verhindert mathematisch, dass der Index außerhalb der Liste liegt
					int zielIndex = Math.Clamp(neuerIndex, 0, EmailListe.Count - 1);

					EmailListView.SelectedIndex = zielIndex;

					// Das Scrollen sichern wir gegen Nullverweise ab
					if (EmailListView.SelectedItem != null)
					{
						EmailListView.ScrollIntoView(EmailListView.SelectedItem);
					}
				}
				else
				{
					// UI leeren, wenn alle Mails gelöscht wurden
					EmailDetailPanel.Visibility = Visibility.Collapsed;
					NoSelectionText.Visibility = Visibility.Visible;
					NoSelectionText.Text = "Alle E-Mails wurden erfolgreich gelöscht.";
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[DELETE ERROR] {ex.Message}");
			}
		}
	}
}