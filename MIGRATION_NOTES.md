# Anpassungen aus dem Python-Stand

Diese Fassung zieht die wichtigsten funktionalen Unterschiede aus dem funktionierenden Python-Programm in das C#-Projekt nach.

## Übernommen

- erweiterte CSP-Statuscodes aus `common_dnp.py`
- Kompatibilitätsbehandlung für `0x00000000` als `Idle (compat)`
- korrekte Behandlung von `0x00010020` als `Standstill` statt `Cooling`
- zusätzliche Statusarten: `HardwareError`, `UnitError`, `FlashProgramming`
- `info --json` liefert jetzt auch die Raw-Felder:
  - `Printermodel_raw`
  - `status_raw`
  - `Remaining prints_raw`
  - `Media_raw`
  - `Free buffer_raw`
- Fallback für Modellauflösung über Detektion, wenn reine Text-/Device-Hinweise nicht reichen

## Noch bewusst unverändert

- die Windows-USB-Transportschicht bleibt in C# nativ und orientiert sich nicht 1:1 an Python, weil sie bereits dieselbe Raw-USB-Idee nutzt
- der HTTP-Server aus Python wurde nicht portiert, nur die CLI-Logik
- Linux bleibt als vorhandener Transport-Platzhalter erhalten
