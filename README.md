# GangDrogaCity Launcher

Launcher del modpack [GangDrogaCity](https://github.com/jamnaga/wtf-modpack) per **Windows, Linux e macOS**.

## Struttura

```
src/GangDrogaCity.Core/   logica del launcher (VB.NET, nessuna dipendenza da UI o OS)
src/GangDrogaCity.App/    interfaccia Avalonia (C#), un solo eseguibile per piattaforma
.github/workflows/        release automatica al push di un tag
build-release.ps1         build locale di tutti gli artefatti (serve solo il .NET 8 SDK)
```

Il Core contiene: sincronizzazione del manifest, separazione **user data / game data**
(`UserData.vb`), installazione Fabric, download di Minecraft vanilla, gestione Java,
avvio del gioco, aggiornamento automatico del launcher (`SelfUpdater.vb`).

## Build

Serve solo il [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), su qualsiasi OS.

```bash
dotnet build GangDrogaCity.sln
dotnet run --project src/GangDrogaCity.App
```

Eseguibile autosufficiente per una piattaforma (`win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`):

```bash
dotnet publish src/GangDrogaCity.App -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o out/osx-arm64
```

`build-release.ps1` produce tutti gli artefatti della release nella cartella `release/`.

## Release

1. Aggiorna `<Version>` in `src/GangDrogaCity.App/GangDrogaCity.App.csproj` (es. `2.2.6.0`).
2. Commit, poi `git tag 2.2.6.0 && git push origin main 2.2.6.0`.
3. Il workflow compila per tutte le piattaforme e crea la release con:

| Asset | Piattaforma |
|---|---|
| `GangDrogaCity.exe`, `GangDrogaCity-windows-x64.zip` | Windows x64 |
| `GangDrogaCity.7z` | exe compresso, usato dai launcher fino alla 2.2.5 per aggiornarsi |
| `GangDrogaCity-linux-x64.tar.gz`, `GangDrogaCity-linux-arm64.tar.gz` | Linux |
| `GangDrogaCity-macos-arm64.zip`, `GangDrogaCity-macos-x64.zip` | macOS (bundle `.app`, non firmato) |

Il launcher si aggiorna da solo scaricando l'asset della propria piattaforma.

## Dati

Tutto vive nella cartella `.gangdrogacity` dei dati utente
(`%APPDATA%` su Windows, `~/.config` su Linux, `~/Library/Application Support` su macOS):

```
game/      dati di gioco gestiti dal manifest (ricostruibili)
downloads/ manifest, regole, pacchetti, cache
userdata/  dati del giocatore: mirror di impostazioni, keybind, waypoint + default del pack
settings.json, launcher.log
```

## Convenzioni del repo del modpack

- `[server]` nel nome di un file: ignorato dal launcher.
- `[windows]`, `[linux]`, `[macos]` nel nome: scaricato solo su quell'OS.
- Su Linux/macOS vengono ignorati automaticamente `java/`, `utils/`, `*.dll`, `*.exe`
  (la JRE viene scaricata da Adoptium, i natives estratti dai jar di Mojang).
- `.userdata` (facoltativo, sintassi `.gitignore`): file che appartengono al giocatore e non
  vengono mai sovrascritti o cancellati; si aggiunge alla lista integrata nel launcher.
- `.oncelist`: come `.userdata` (compatibilita').
- `.gitignore` e `.manifestignore`: file che la pulizia non deve mai cancellare.

## Note per piattaforma

- **Windows**: eseguibile singolo; l'aggiornamento usa uno script `.cmd` come in passato.
- **Linux**: `chmod +x GangDrogaCity` se necessario. Serve `7zz`/`7z` solo per velocizzare
  l'estrazione dei pacchetti; senza, viene usato l'estrattore integrato.
- **macOS**: l'app non e' firmata. Al primo avvio: tasto destro → Apri, oppure
  `xattr -dr com.apple.quarantine GangDrogaCity.app`. Gli aggiornamenti successivi non lo richiedono.
