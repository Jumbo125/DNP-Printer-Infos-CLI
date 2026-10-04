# dnp_info

Ein kleines CLI-Projekt mit **einer einzigen `dnp_info.csproj`**.

- **Linux**: direkte Erkennung über `/dev/usb/lp*` und Sysfs, aktueller I/O weiter über Gerätedatei
- `detect` liefert direkt den Wert, der für die nächste Abfrage verwendbar ist:
  - Windows: **Portnummer**
  - Linux: bevorzugt ein stabiler **USB-Selector**, sonst **Device-Pfad**

## Projektstruktur

```text
Core/
Transports/
dll/
build/
docs/
Program.cs
dnp_info.csproj
publish-windows.cmd
```

## Windows

### DLL-Ablage

Standardmäßig wird die DLL im Unterordner `dll/` **direkt neben der EXE** gesucht:

- **x86-EXE** erwartet `dll\cspstatx32.dll`
- **x64-EXE** erwartet `dll\cspstatx64.dll`

Die Anwendung erkennt automatisch, ob gerade die **32-Bit- oder 64-Bit-EXE** läuft, und bevorzugt die jeweils passende DLL.

Optional kann weiterhin ein expliziter Pfad übergeben werden:

```powershell
```


### Windows-Publish immer für x86 und x64

```powershell
.\publish-windows.cmd
```

oder direkt:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\publish-windows.ps1
```

Das Script erzeugt immer beide Ausgaben:

- `artifacts\publish\win-x86\`
- `artifacts\publish\win-x64\`

## Linux

### Detect-Ausgabe

Unter Linux ist `detect` jetzt so vorbereitet, dass nach Möglichkeit ein stabilerer Query-Wert ausgegeben wird als nur `/dev/usb/lp0`.

Beispiele:

```bash
dnp_info detect --transport linux
# -> usb:vid=1343,pid=000c,serial=ABC12345
```

oder als Fallback:

```bash
dnp_info detect --transport linux
# -> /dev/usb/lp0
```

Beides kann direkt wiederverwendet werden:

```bash
dnp_info status --transport linux --device usb:vid=1343,pid=000c,serial=ABC12345
dnp_info info --transport linux --device /dev/usb/lp0 --json
```

### Wichtiger Status des Linux-Backends

Dieses ZIP enthält eine **saubere Vorbereitung** für das Linux-Backend, aber noch kein komplett ausgetestetes `libusb`-Produktivbackend.

Aktuell enthalten:

- Linux-Detect über Sysfs-Metadaten
- wiederverwendbarer Linux-Query-Wert
- provisorischer Linux-Transport über Gerätedatei
- Spezifikation für das spätere `libusb`-Backend in `docs/LINUX_BACKEND_SPEC.md`

## Doku

- `HANDBUCH.md` – Bedienung und Optionen
- `docs/LINUX_BACKEND_SPEC.md` – Linux-Backend, Query-Werte und Zielarchitektur

## Umgebungsvariablen

- `DNP_PRINTER_NAME`
- `DNP_PRINTER_DEVICE`
