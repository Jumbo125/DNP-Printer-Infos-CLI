# HANDBUCH

## Ziel

`dnp_info` liest den Druckerstatus eines DNP/Citizen-Druckers.

Minimalwerte:
- Status
- Remaining Prints
- Media
- Free Buffer

## Befehle

### `detect`
Erkennt, ob ein DNP/Citizen-Drucker so erreichbar ist, dass danach direkt abgefragt werden kann.

- **Windows:** Ausgabe ist standardmäßig die **CSP-Portnummer**
- **Linux:** Ausgabe ist bevorzugt ein **USB-Selector** wie `usb:vid=1343,pid=000c,serial=...`, sonst der **Device-Pfad**
- Mit `--json` gibt es eine maschinenlesbare Antwort mit `success`, `printerName`, `port` bzw. `devicePath` sowie zusätzlichen USB-Metadaten unter Linux

```powershell
dnp_info.exe detect
dnp_info.exe detect --json
```

Linux-Beispiel:

```bash
./dnp_info detect --transport linux
./dnp_info status --transport linux --device usb:vid=1343,pid=000c,serial=ABC12345
```

Nur Windows. Liest mehrere CSP-Portnummern und zeigt, welche plausibel aussehen.

```powershell
```

### `info`
Liest alle vier Minimalwerte.

```powershell
dnp_info.exe info --port 0
dnp_info.exe info --json
```

`--model` ist nur noch ein **freier Hint** und keine feste Modell-Whitelist mehr.

### `status`

```powershell
dnp_info.exe status --port 0
```

### `remaining`

```powershell
dnp_info.exe remaining --port 0
```

### `media`

```powershell
dnp_info.exe media --port 0
```

### `free-buffer`

```powershell
dnp_info.exe free-buffer --port 0
```

## Optionen

- `--json`
- `--simulate`
- `--transport auto|windows|linux`
- `--model <freier Text-Hint>`
- `--printer "DNP DS620"`
- `--device /dev/usb/lp0`
- `--device usb:vid=1343,pid=000c,serial=...`
- `--port 0`

## Windows-DLL

Gesucht wird in dieser Reihenfolge:
- `dll\cspstatx32.dll` für eine x86-EXE
- `dll\cspstatx64.dll` für eine x64-EXE
- danach weiterhin die alten Fallbacks im EXE-Ordner bzw. `Native\...`

Die Anwendung erkennt automatisch, ob die gestartete EXE **32 Bit oder 64 Bit** ist, und bevorzugt die passende DLL.

## Linux

Der aktuelle Linux-Stand in diesem ZIP ist bewusst zweigeteilt:

1. **bereits integriert**
   - Detect über Sysfs/USB-Metadaten
   - stabiler wiederverwendbarer Query-Wert
   - Abfragen über den vorhandenen Linux-Gerätepfadtransport

2. **als nächster Schritt vorgesehen**
   - echtes `libusb`-Backend ohne Abhängigkeit von Gutenprint zur Laufzeit

Details dazu stehen in:

- `docs/LINUX_BACKEND_SPEC.md`

## Sichere erste Testreihenfolge

1. `dnp_info.exe detect`
2. `dnp_info.exe detect --json`
3. `dnp_info.exe status --port <n>` oder unter Linux `dnp_info --transport linux --device <wert> status`
4. `dnp_info.exe info --port <n>`

Nur im Idle-Zustand testen, nicht während eines laufenden Druckjobs.
