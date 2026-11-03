# iPhoneMirror: Windows-Einrichtung und Verbindungsfixes

Dieser Fork basiert auf [fabiblian/iPhoneMirror](https://github.com/fabiblian/iPhoneMirror)
und dem ursprünglichen Projekt [RayrenSX/iPhoneMirror](https://github.com/RayrenSX/iPhoneMirror).
Die vorhandene Lizenz und die Git-Historie bleiben erhalten.

Die hier enthaltenen Bluetooth- und WLAN-Kopplungsfixes müssen aus diesem Checkout
gebaut werden. Die auf den Upstream-Seiten verlinkten Downloads enthalten diese
Änderungen nicht automatisch. Fertige Programme und persönliche Gerätedaten liegen
nicht im Git-Repository.

## Auf einem anderen Windows-PC bauen

Voraussetzungen:

- Windows 10/11 x64 und Git.
- Visual Studio 2022 Build Tools mit C++-Desktopentwicklung, MSVC v143 und Windows SDK.
  Das CMake-Preset dieses Forks verwendet Visual Studio 2022.
- .NET SDK 10.0.301, wie in `global.json` festgelegt.
- Python 3.13 x64 als `python` im PATH und eine Internetverbindung für Abhängigkeiten.

```powershell
git clone https://github.com/XiSolrac/iPhoneMirror.git
cd iPhoneMirror
./build.ps1 -Configuration Release -OmitUxPlayRuntime
```

`-OmitUxPlayRuntime` lässt den optionalen UxPlay-Empfänger weg. Der ursprüngliche
AirPlay-Empfänger bleibt enthalten; damit wurde die Verbindung getestet.
Die Tests bleiben bei diesem Build-Befehl eingeschaltet.

Das Programm liegt anschließend unter `outputs/iPhoneMirror/iPhoneMirror.exe`.
Für einen normalen Installer mit Desktop- und Startmenü-Verknüpfung:

```powershell
./scripts/build_installer.ps1 -SkipAppBuild -OmitUxPlayRuntime
```

Der Installer wird unter `outputs/releases` erzeugt. Die ausführliche
[Entwicklungsanleitung](docs/DEVELOPMENT.md) beschreibt weitere Build-Varianten.

## iPhone einmalig für diesen PC einrichten

1. Im Einrichtungsassistenten beziehungsweise unter **Drivers** die Apple-USB-
   Unterstützung und den Erfassungstreiber für das angeschlossene iPhone prüfen.
   Fehlende Komponenten über die vorgesehenen Installationsschritte ergänzen.
2. iPhone per USB anschließen, entsperren und **Trust This Computer** bestätigen.
   Nach einer Treiberinstallation das Kabel bei Aufforderung neu verbinden.
3. Am iPhone **Settings → Privacy & Security → Developer Mode** einschalten,
   neu starten und danach **Enable** beziehungsweise **Turn On** bestätigen.
4. In **Device binder** die USB- und AirPlay-Verbindung desselben iPhones einem
   gemeinsamen Profil zuordnen.
5. **Wired control** einmal starten, bis der Steuerungskanal verbunden ist.
   Dabei werden bei Bedarf Gerätedateien vorbereitet und die zusätzliche
   WLAN-Entwicklerkopplung gespeichert. Das iPhone entsperrt lassen.

Falls der Menüpunkt **Developer Mode** ausgeblendet ist, kann der mitgebaute
Python-Werkzeugsatz ihn sichtbar machen. Die eigene USB-UDID steht in
**Device binder → Wired USB**. Den Platzhalter durch diese UDID ersetzen:

```powershell
& ./work/usb-touch-bridge-python/Scripts/python.exe -m pymobiledevice3 amfi reveal-developer-mode --udid '<USB-UDID>'
```

Anschließend die Einstellungsseite erneut öffnen. Einschalten, Neustart und
Bestätigung erfolgen direkt am iPhone. Ein Developer-Mode-Status allein ersetzt
die zusätzliche WLAN-Kopplung nicht.

## AirPlay-Bild und WLAN-Steuerung testen

1. PC und iPhone mit demselben lokalen Netzwerk verbinden.
2. Am iPhone **Control Center → Screen Mirroring** öffnen und den in der App
   angezeigten AirPlay-Empfängernamen auswählen.
3. In iPhoneMirror die **AirPlay-Quelle** auswählen und das laufende Bild prüfen.
4. **Wireless control** starten und auf die erfolgreiche Verbindung warten.
5. Erst dann das USB-Kabel abziehen und ausschließlich mit der PC-Maus eine App
   auf dem iPhone öffnen.

AirPlay liefert das Bild; **Wireless control** stellt den separaten Eingabekanal
bereit. Ein USB-Bildstream wird durch den Wireless-control-Schalter nicht zu AirPlay.
Wenn USB nach einem Quellenwechsel nicht mehr erkannt wird, das Kabel neu verbinden.
Für die Einrichtung kann **Settings → App → Start mirroring automatically**
vorübergehend ausgeschaltet werden.

**Paired Computers** am iPhone zeigt bestehende Entwicklerkopplungen. Für den
hier getesteten Weg wird die Kopplung über USB abgeschlossen; dort muss kein
PC aus einer Liste neu ausgewählt werden.

Windows-Treiber, Geräteprofile und Vertrauensschlüssel werden auf jedem neuen PC
lokal eingerichtet. Persönliche Kopplungsschlüssel aus `.pymobiledevice3`,
AppData-Einstellungen, Protokolle und Aufnahmen gehören nicht ins Repository.

## Enthaltene Korrekturen

- **Bluetooth-Geräteerkennung:** Der PC stellt den BLE-HID-Dienst bereit. Ein
  verbundenes iPhone als GATT-Client muss selbst keinen HID-Dienst veröffentlichen.
  Die Geräteauswahl berücksichtigt deshalb verbundene BLE-Clients; tatsächliche
  Eingaben bleiben an die explizite Bindung und HID-Verbindung gebunden.
- **WLAN-Kopplung:** Das festgelegte `pymobiledevice3 11.3.1` übergibt SRP-Beweise
  als hexadezimal kodierte Bytes. `srptools 2.0.0` interpretiert Bytes als Rohdaten.
  Eine auf diesen SDK-Client begrenzte Anpassung übergibt Hex-Text an dieselbe
  Sicherheitsprüfung. Manipulierte Beweise werden weiterhin abgewiesen.
- **Reproduzierbarer Build:** Festgelegte SRP-Abhängigkeit, NuGet.org-Paketquelle
  und Visual-Studio-2022-Preset.
- **Tests:** Isolierter USB-Timeout-Test sowie bestehende Quelltextprüfungen an
  veränderliche Panelbreiten und gespeicherte Vorschaufenster-Platzierungen angepasst.

## Validierung und Grenzen

- 409 Python-Tests erfolgreich, einschließlich eines echten lokalen SRP-
  Schlüsselaustauschs und der Ablehnung veränderter Bestätigungen.
- .NET-Anwendungslogik-Prüfungen erfolgreich.
- Build-Quellen-, native Backend- und Laufzeitmanifest-Prüfungen erfolgreich.
- Der gebaute Steuerungsbaustein besteht `--check-runtime`.
- Mit iPhone 16 Pro Max / iOS 27.2 wurden die USB-Kopplung sowie AirPlay-Bild und
  Mausbedienung über WLAN bei abgezogenem USB-Kabel bestätigt.
- Die Bluetooth-Mausbedienung wurde auf diesem Gerät noch nicht erfolgreich
  getestet. Dieser Projektmodus benötigt **AssistiveTouch** und einen passenden
  Windows-Bluetooth-Adapter mit BLE-Peripheriemodus.

Die vollständigen .NET- und nativen Prüfungen laufen beim normalen Build;
die genannten Einzelprüfungen ersetzen keinen kompletten Build auf dem Ziel-PC.
