<p align="center"><img src="assets/afk-realm.png" width="160" alt="AFK Realm logo"></p>

<p align="center"><a href="README.md">English</a> · <b>Deutsch</b> · <a href="README.fr.md">Français</a> · <a href="README.es.md">Español</a> · <a href="README.pt-BR.md">Português (Brasil)</a> · <a href="README.ru.md">Русский</a> · <a href="README.zh-Hans.md">简体中文</a></p>

# AFK Realm

*Geh AFK, komm zurück zu deinem eigenen Server.*

> [!WARNING]
> **In Arbeit und sehr experimentell.** AFK Realm ist unfertig und ändert sich ständig. Jede Version und jede neue Funktion ist als experimentell zu sehen. Halte Sicherungen von allem, was dir wichtig ist (AFK Realm macht vor jedem Update und jeder Moduländerung eine), und melde Probleme bitte mit `logs\install.log`.

Bau, betreib und tune deinen eigenen **Conquest-of-Azeroth**-Server (ein AzerothCore-Fork mit Playerbots) unter Windows 10/11 oder Linux – direkt aus dem Quellcode kompiliert, alles per Mausklick.

![](docs/screenshots/8-server-management.png)

Dies ist eine Kurzfassung. Die ausführliche Beschreibung steht im [englischen README](README.md), das Benutzerhandbuch in [docs/MANUAL.md](docs/MANUAL.md) (Englisch). Das Programm selbst ist auf Deutsch: Es startet in der Sprache von Windows, die Sprache lässt sich oben rechts umstellen.

## Was AFK Realm kann

- **Geführte Installation**: Ordner und Datenbank-Passwort wählen, *Installieren* klicken – Git, CMake, Visual Studio Build Tools, OpenSSL, Boost, MySQL und Python holt AFK Realm selbst
- **Immer aktuell**: jede Installation baut den neuesten CoA-Core und Playerbots; Updates mit Sicherung vorher
- **Server im Griff**: starten, stoppen, neu starten, geplante Neustarts, beide Serverkonsolen in einem Fenster
- **Einstellungen** aller Konfigurationsdateien, durchsuchbar, mit Beschreibung und Standardwert
- **Bots, Accounts und Spielleiter-Werkzeuge**: Übersicht der Zufallsbots, Accounts anlegen und übertragen, Quests reparieren, Charakteren helfen
- **Module** mit einem Klick hinzufügen und wieder entfernen, samt ihrer Datenbankänderungen – darunter World Journey (die Welt als eine Reise von 1 bis 60), Auktionshaus-Bots, eigene Rassen und klassische Klassen
- **Mit anderen spielen**: Realm-Adresse setzen, Add-ons und Client-Patch als ZIP für die Mitspieler
- **In deiner Sprache**: Deutsch, Englisch, Französisch, Spanisch, Portugiesisch (Brasilien), Russisch und vereinfachtes Chinesisch; weitere Sprachen lassen sich als Datei ergänzen

## Loslegen

1. `AFK-Realm.exe` von der Seite [Releases](../../releases) herunterladen und starten. Es öffnet sich in einem App-Fenster von Edge oder Chrome (oder im Browser). Windows SmartScreen warnt eventuell vor einem unbekannten Herausgeber, weil die exe nicht signiert ist; sie wird von GitHub Actions aus diesem Repository gebaut.
2. **Einen neuen Server installieren** → Ordner wählen, z. B. `C:\CoA-Server` → Datenbank-Passwort festlegen → **Installieren**. Das kann lange dauern und braucht etwa 40 GB Speicherplatz.
3. In der Serververwaltung: *Kartendaten aus dem Spielclient erstellen* (einmalig), *Account erstellen*, *Server starten*.
4. In der `realmlist.wtf` deines Clients `set realmlist 127.0.0.1` eintragen und einloggen.

**Unter Linux:** `AFK-Realm-linux-x64` (für ARM `AFK-Realm-linux-arm64`) von der [Releases](../../releases)-Seite laden, einmal `chmod +x AFK-Realm-linux-x64` und mit `./AFK-Realm-linux-x64` starten. Es öffnet sich als App-Fenster oder im Browser und funktioniert wie die Windows-Version; fehlen Build-Werkzeuge, zeigt es den einen Befehl, der sie installiert.

Den CoA-Spielclient brauchst du selbst; er ist nicht Teil dieses Projekts.

## Mehr

- [Benutzerhandbuch](docs/MANUAL.md) – jedes Fenster erklärt (Englisch)
- [Änderungen](CHANGELOG.md) und [Versionshinweise](RELEASE_NOTES.md) (Englisch)
- Probleme, Ideen oder einfach Danke: gern als [Issue](../../issues) – auf Deutsch oder Englisch

## Hinweis

Ein inoffizielles Community-Werkzeug, nicht verbunden mit Blizzard Entertainment, Project Ascension, dem AzerothCore-Projekt oder den Autoren der Forks, die es baut. World of Warcraft ist eine Marke von Blizzard Entertainment. Lizenz: MIT (siehe [LICENSE](LICENSE)).
