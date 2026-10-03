Imports System.Diagnostics
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Http
Imports System.Net.NetworkInformation
Imports System.Security.Cryptography
Imports System.Threading
Imports Newtonsoft.Json.Linq
Imports Octokit

Public Enum LauncherPhase
    ''' <summary>Installazione/aggiornamento in corso: barra di avanzamento e stato.</summary>
    Installing
    ''' <summary>Pronto: menu con il pulsante PLAY.</summary>
    Ready
    ''' <summary>PLAY premuto: sincronizzazione e avvio.</summary>
    Launching
    ''' <summary>Minecraft in esecuzione.</summary>
    Playing
End Enum

''' <summary>
''' Cuore del launcher, indipendente dall'interfaccia e dal sistema operativo.
''' Porta la logica che prima viveva in Form1 (WinForms) e parla con la UI solo tramite
''' ILauncherHost ed eventi.
''' </summary>
Public Class LauncherEngine

    Public Const ModpackOwner As String = "jamnaga"
    Public Const ModpackRepo As String = "wtf-modpack"

    Public ReadOnly Property Paths As LauncherPaths
    Public ReadOnly Property Settings As LauncherSettings
    Public ReadOnly Property UserData As UserDataManager

    Private ReadOnly host As ILauncherHost
    Private ReadOnly http As HttpClient
    Private ReadOnly mcDownloader As New MinecraftDownloader()
    Private ReadOnly mcLauncher As New MinecraftLauncher()
    Private ReadOnly updater As New UpdateChecker()

    Private manifest As JObject
    Private fileHashCache As New Dictionary(Of String, String)()
    Private pendingUserDataRestore As Boolean = False
    Private skipUserDataBackup As Boolean = False
    Private internet As Boolean = False
    Private internetMonitorStarted As Boolean = False
    Private latestModpackVersion As String = ""
    Private mcProcess As Process
    Private bootRunning As Boolean = False

    Public Event PhaseChanged(phase As LauncherPhase)
    Public Event BusyChanged(busy As Boolean, message As String)
    Public Event DoNotPowerOffChanged(visible As Boolean)
    Public Event GameExited(exitCode As Integer)
    Public Event SettingsChanged()

    Private _phase As LauncherPhase = LauncherPhase.Installing
    Public Property Phase As LauncherPhase
        Get
            Return _phase
        End Get
        Private Set(value As LauncherPhase)
            _phase = value
            RaiseEvent PhaseChanged(value)
        End Set
    End Property

    Public ReadOnly Property IsGameRunning As Boolean
        Get
            Try
                Return mcProcess IsNot Nothing AndAlso Not mcProcess.HasExited
            Catch
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property RepoBranch As String
        Get
            Return If(String.IsNullOrWhiteSpace(Settings.CurrentBranch), LauncherSettings.DefaultBranch, Settings.CurrentBranch)
        End Get
    End Property

    Private ReadOnly Property RepoBasePath As String
        Get
            Return $"https://raw.githubusercontent.com/{ModpackOwner}/{ModpackRepo}/refs/heads/{RepoBranch}/"
        End Get
    End Property

    Public ReadOnly Property VersionLabel As String
        Get
            Return "v" & Platform.AppVersionString & " | " & RepoBranch
        End Get
    End Property

    Public Sub New(host As ILauncherHost)
        Me.host = host
        Paths = New LauncherPaths()
        Paths.EnsureDirectories()
        Settings = LauncherSettings.Load(Paths.SettingsFile)
        UserData = New UserDataManager(Paths.MinecraftDir, Paths.GameDir)

        http = New HttpClient() With {.Timeout = TimeSpan.FromMinutes(10)}
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GangDrogaCity-Launcher/1.0")
        http.DefaultRequestHeaders.CacheControl = New Headers.CacheControlHeaderValue() With {.NoCache = True, .NoStore = True}

        AddHandler CoreLog.Message, AddressOf OnCoreLog
    End Sub

    Private Sub OnCoreLog(text As String)
        host.Log(text)
    End Sub

    Private Sub Log(message As String)
        host.Log(message)
    End Sub

    Private Sub LogError(message As String)
        host.LogError(message)
    End Sub

    Private Sub SetProgress(value As Integer)
        host.SetProgress(Math.Max(0, Math.Min(100, value)))
    End Sub

    Private Sub SetBusy(busy As Boolean, Optional message As String = Nothing)
        RaiseEvent BusyChanged(busy, message)
    End Sub

    Private Sub SetDoNotPowerOff(visible As Boolean)
        RaiseEvent DoNotPowerOffChanged(visible)
    End Sub

    Public Sub SaveSettings()
        Settings.Save()
        RaiseEvent SettingsChanged()
    End Sub

    Public Sub SetUsername(username As String)
        Settings.Username = If(username, "").Trim()
        SaveSettings()
    End Sub

    ' ====================================================================== BOOT

    ''' <summary>
    ''' Avvio completo: pulizia cestino, backup dati utente, rete, self-update, sync, Fabric, Minecraft.
    ''' </summary>
    Public Async Function BootAsync() As Task
        If bootRunning Then Return
        bootRunning = True
        Try
            Phase = LauncherPhase.Installing
            SetProgress(10)
            Paths.EnsureDirectories()
            StartInternetMonitor()

            ' Elimina in background eventuali cartelle "cestino" rimaste da un reset precedente
            DirectoryCleaner.CleanupLeftoverTrash(Paths.MinecraftDir)
            DirectoryCleaner.CleanupLeftoverTrashIn(Paths.MinecraftDir)

            ' Backup dei dati utente (impostazioni, keybind, waypoint) nel mirror userdata.
            ' Dopo un reset il backup viene saltato: il mirror contiene gia' i dati da ripristinare.
            If skipUserDataBackup Then
                skipUserDataBackup = False
            Else
                Try
                    UserData.LoadRules(Paths.DownloadDir)
                    Await Task.Run(Function() UserData.Backup())
                Catch
                End Try
            End If

            Log("Avvio...")
            Await Task.Delay(500)

            ' Attesa connessione internet
            Log("Verifica connessione internet...")
            Dim connectionCheckCount As Integer = 0
            If Not internet Then internet = Await CheckInternetConnectionAsync()
            While Not internet
                LogError("Non sei connesso a internet. Attendo la rete...")
                Await Task.Delay(2000)
                connectionCheckCount += 1
                If connectionCheckCount Mod 2 = 0 Then
                    internet = Await CheckInternetConnectionAsync()
                End If
            End While
            Log("Connessione a internet rilevata.")
            Await Task.Delay(300)

            SetProgress(20)

            ' Self-update del launcher
            Try
                If Await updater.CheckForUpdateAsync() Then
                    Dim versionString As String = updater.getLatestversionString()
                    Log("Aggiornamento del launcher alla versione " & versionString)
                    Dim selfUpdater As New SelfUpdater()
                    If Await selfUpdater.TryUpdateAsync(host, updater, versionString) Then
                        Await Task.Delay(800)
                        host.ExitApplication()
                        Return
                    End If
                End If
            Catch ex As Exception
                Log($"Controllo aggiornamenti non riuscito: {ex.Message}")
            End Try

            Await Step1Async(False)
        Finally
            bootRunning = False
        End Try
    End Function

    ' ====================================================================== STEP 1: SYNC MODPACK

    ''' <summary>
    ''' File del manifest da ignorare su questa piattaforma:
    '''  - tag [server] (file solo server)
    '''  - tag [windows] / [linux] / [macos] diversi dall'OS corrente
    '''  - su Linux/macOS: la JRE Windows in java\, i binari .dll/.exe e utils\
    '''  - librerie native di altri OS (.dylib fuori da macOS, .so fuori da Linux)
    ''' </summary>
    Private Function ShouldSkipManifestFile(filePath As String) As Boolean
        If String.IsNullOrEmpty(filePath) Then Return False
        Dim p As String = filePath.Replace("\"c, "/"c)
        Dim lower As String = p.ToLowerInvariant()

        If lower.Contains("[server]") Then Return True

        For Each tag In {"windows", "linux", "macos"}
            If lower.Contains("[" & tag & "]") AndAlso tag <> Platform.OSTag Then Return True
        Next

        If Not Platform.IsWindows Then
            If lower.StartsWith("java/") OrElse lower.StartsWith("utils/") Then Return True
            If lower.EndsWith(".dll") OrElse lower.EndsWith(".exe") OrElse lower.EndsWith(".dll.x") Then Return True
        End If
        If Not Platform.IsMacOS AndAlso lower.EndsWith(".dylib") Then Return True
        If Not Platform.IsLinux AndAlso lower.EndsWith(".so") Then Return True

        Return False
    End Function

    Private Async Function DownloadUserDataRulesAsync() As Task
        For Each ruleFile As String In UserDataManager.RuleFileNames
            Dim localPath As String = Path.Combine(Paths.DownloadDir, ruleFile)
            Dim tmpPath As String = localPath & ".tmp"
            Try
                Using resp = Await http.GetAsync(RepoBasePath & ruleFile)
                    If resp.StatusCode = HttpStatusCode.NotFound Then
                        Try
                            File.Delete(localPath)
                        Catch
                        End Try
                        Continue For
                    End If
                    resp.EnsureSuccessStatusCode()
                    Using fs = File.Create(tmpPath)
                        Await resp.Content.CopyToAsync(fs)
                    End Using
                End Using
                File.Copy(tmpPath, localPath, True)
            Catch
                ' Rete assente o altro errore: usa la copia locale precedente se presente
            Finally
                Try
                    File.Delete(tmpPath)
                Catch
                End Try
            End Try
        Next
    End Function

    Public Async Function Step1Async(step1only As Boolean, Optional forceSync As Boolean = False) As Task
        Dim remoteCommitId As String = ""

        If Paths.AvailableFreeSpace() < 1L * 1024 * 1024 * 1024 Then
            LogError("Non c'e' abbastanza spazio libero sul disco per continuare. Sono necessari almeno 2 GB di spazio libero.")
            Return
        End If

        SetDoNotPowerOff(True)
        Log("Recupero manifest GDC...")
        Await Task.Delay(300)

        Try
            File.Delete(Path.Combine(Paths.DownloadDir, "modpack.zip"))
        Catch
        End Try

        Dim manifestPath As String = Path.Combine(Paths.DownloadDir, "manifest.json")
        Dim manifestError As Exception = Nothing
        Try
            Await DownloadFileAsync(RepoBasePath & "manifest.json", manifestPath)
            manifest = JObject.Parse(Await File.ReadAllTextAsync(manifestPath))
        Catch ex As Exception
            manifestError = ex
        End Try
        If manifestError IsNot Nothing Then
            LogError($"Impossibile scaricare il manifest: {manifestError.Message}")
            Await host.AlertAsync("Errore", $"Impossibile scaricare il manifest del modpack: {manifestError.Message}", AlertKind.Error)
            SetProgress(0)
            Return
        End If

        ' Regole user-data / file protetti del modpack
        Await DownloadUserDataRulesAsync()
        UserData.LoadRules(Paths.DownloadDir)

        ' Dopo un reset ripristina i dati utente dal mirror PRIMA di toccare i file del manifest
        If pendingUserDataRestore Then
            pendingUserDataRestore = False
            Dim restoredCount As Integer = Await Task.Run(Function() UserData.Restore())
            If restoredCount > 0 Then Log($"Dati utente ripristinati: {restoredCount} file")
        End If

        Dim step1Error As Exception = Nothing
        Try
            Try
                remoteCommitId = Await GetRemoteModpackCommitIdAsync()
                If Not String.IsNullOrWhiteSpace(remoteCommitId) AndAlso Not forceSync Then
                    Dim localCommitId As String = GetSavedModpackCommitId()
                    If String.Equals(localCommitId, remoteCommitId, StringComparison.OrdinalIgnoreCase) Then
                        Log("Sync saltato.")
                        If Not step1only Then
                            If Await CheckJavaStatusAsync() Then
                                Await Step2Async()
                            Else
                                LogError(" Java Runtime non trovato o non valido.")
                            End If
                        End If
                        Return
                    End If
                End If
            Catch ex As Exception
                Log($"Impossibile verificare il commit remoto, continuo con il sync: {ex.Message}")
            End Try

            Log("Analisi file necessari...")

            Dim allFiles = manifest("files").ToArray()
            Dim files = allFiles.Where(Function(f) Not ShouldSkipManifestFile(f("path").ToObject(Of String)())).ToArray()
            Dim skippedFiles As Integer = allFiles.Length - files.Length
            If skippedFiles > 0 Then
                Log($"File non necessari su {Platform.OSTag} ignorati: {skippedFiles}")
            End If
            Dim totalSize As Long = Math.Max(1L, files.Sum(Function(f) f("size").ToObject(Of Long)()))

            LoadHashCache()

            Dim filesToDownload As New List(Of Object)
            Dim alreadyDownloadedSize As Long = 0

            Log("Verifica file esistenti...")

            Dim maxConcurrency As Integer = Math.Max(2, Environment.ProcessorCount * 2)
            Dim semaphore As New SemaphoreSlim(maxConcurrency)

            Dim verificationTasks = files.Select(Async Function(content)
                                                     Await semaphore.WaitAsync()
                                                     Try
                                                         Dim fileName As String = content("path").ToObject(Of String)()
                                                         Dim filePath As String = Path.Combine(Paths.GameDir, fileName.Replace("/"c, Path.DirectorySeparatorChar))
                                                         Dim fileSize As Long = content("size").ToObject(Of Long)()
                                                         Dim fileHash As String = content("sha256").ToObject(Of String)()
                                                         Dim manifestOnce As Boolean = If(content("once") IsNot Nothing, content("once").ToObject(Of Boolean)(), False)
                                                         ' File utente: flag "once" del manifest oppure regole user-data
                                                         Dim isUserFile As Boolean = manifestOnce OrElse UserData.IsUserData(fileName)
                                                         Dim downloadPath As String = filePath
                                                         Dim prevDefaultHash As String = ""
                                                         Dim isValid As Boolean

                                                         If isUserFile Then
                                                             ' La versione del pack va in userdata\defaults, mai sopra il file del giocatore
                                                             downloadPath = UserData.DefaultPath(fileName)
                                                             isValid = Await VerifyFileWithCacheAsync(downloadPath, fileSize, fileHash)
                                                             If isValid Then
                                                                 Dim applied As String = UserData.ApplyDefault(fileName, "")
                                                                 If applied IsNot Nothing Then Log(applied)
                                                             Else
                                                                 prevDefaultHash = UserData.PreviousDefaultHash(fileName)
                                                             End If
                                                         Else
                                                             isValid = Await VerifyFileWithCacheAsync(filePath, fileSize, fileHash)
                                                         End If

                                                         Return New With {
                                                             .FilePath = filePath,
                                                             .DownloadPath = downloadPath,
                                                             .FileSize = fileSize,
                                                             .FileHash = fileHash,
                                                             .FileName = fileName,
                                                             .IsValid = isValid,
                                                             .IsUserFile = isUserFile,
                                                             .PrevDefaultHash = prevDefaultHash
                                                         }
                                                     Finally
                                                         semaphore.Release()
                                                     End Try
                                                 End Function).ToArray()

            Dim verificationResults = Await Task.WhenAll(verificationTasks)

            For Each result In verificationResults
                If result.IsValid Then
                    alreadyDownloadedSize += result.FileSize
                Else
                    filesToDownload.Add(result)
                End If
            Next

            Log($" File da scaricare: {filesToDownload.Count}/{files.Length}")
            Await Task.Delay(150)

            ' Download parallelo
            Dim downloadConcurrency As Integer = Math.Max(1, Math.Min(6, filesToDownload.Count))
            Dim downloadSemaphore As New SemaphoreSlim(downloadConcurrency)
            Dim downloadedSize As Long = alreadyDownloadedSize
            Dim lastProgressUpdate As DateTime = DateTime.Now
            Dim startTime As DateTime = DateTime.Now
            Dim failedDownloads As Integer = 0

            Dim downloadTasks = filesToDownload.Select(Async Function(fileInfo)
                                                           Await downloadSemaphore.WaitAsync()
                                                           Try
                                                               Dim targetPath As String = fileInfo.DownloadPath
                                                               Directory.CreateDirectory(Path.GetDirectoryName(targetPath))

                                                               Dim fileUrl As String = RepoBasePath & Uri.EscapeDataString(fileInfo.FileName).Replace("%2F", "/")
                                                               Try
                                                                   Await DownloadFileAsync(fileUrl, targetPath, 2)
                                                               Catch ex As Exception
                                                                   Interlocked.Increment(failedDownloads)
                                                                   Log($" Download fallito: {fileInfo.FileName} ({ex.Message})")
                                                                   Return
                                                               End Try

                                                               UpdateHashCacheEntry(targetPath, fileInfo.FileSize, fileInfo.FileHash)
                                                               Interlocked.Add(downloadedSize, fileInfo.FileSize)

                                                               If fileInfo.IsUserFile Then
                                                                   ' Nuovo default scaricato: crea il file se manca, aggiornalo solo se il
                                                                   ' giocatore non l'aveva mai personalizzato, altrimenti lascialo intatto
                                                                   Dim applied As String = UserData.ApplyDefault(fileInfo.FileName, fileInfo.PrevDefaultHash)
                                                                   If applied IsNot Nothing Then Log(applied)
                                                               End If

                                                               If DateTime.Now.Subtract(lastProgressUpdate).TotalMilliseconds > 2000 Then
                                                                   lastProgressUpdate = DateTime.Now
                                                                   Dim progressValue As Integer = 25 + CInt((downloadedSize / totalSize) * 20)
                                                                   SetProgress(Math.Min(progressValue, 45))
                                                                   Dim percentage = Math.Round((downloadedSize / totalSize) * 100, 1)
                                                                   Dim elapsed As TimeSpan = DateTime.Now - startTime
                                                                   Dim etaText As String = ""
                                                                   If downloadedSize > alreadyDownloadedSize Then
                                                                       Dim remainingBytes As Long = totalSize - downloadedSize
                                                                       Dim bytesPerSec As Double = (downloadedSize - alreadyDownloadedSize) / Math.Max(1.0, elapsed.TotalSeconds)
                                                                       Dim remaining As TimeSpan = TimeSpan.FromSeconds(remainingBytes / Math.Max(1.0, bytesPerSec))
                                                                       etaText = $", ETA: ~{remaining.ToString("hh\:mm\:ss")}"
                                                                   End If
                                                                   Log($" Download: {percentage}% completato{etaText}")
                                                               End If
                                                           Finally
                                                               downloadSemaphore.Release()
                                                           End Try
                                                       End Function).ToArray()

            Await Task.WhenAll(downloadTasks)
            SaveHashCache()

            SetProgress(45)
            If failedDownloads > 0 Then
                LogError($"Download completato con {failedDownloads} errori: alcuni file verranno riprovati al prossimo avvio.")
            Else
                Log("Download completato!")
            End If

            Await RemoveAllNonManifestFilesAsync(True)
            Await ProcessManifestPackagesAsync(manifest)

            If Not String.IsNullOrWhiteSpace(remoteCommitId) AndAlso failedDownloads = 0 Then
                SaveModpackCommitId(remoteCommitId)
            End If

            If Not step1only Then
                If Await CheckJavaStatusAsync() Then
                    Await Step2Async()
                Else
                    LogError(" Java Runtime non trovato o non valido.")
                End If
            End If

        Catch ex As Exception
            step1Error = ex
        End Try

        If step1Error IsNot Nothing Then
            LogError($" Errore durante step1: {step1Error.Message}")
            Await host.AlertAsync("Errore", $"Si e' verificato un errore: {step1Error.Message}.", AlertKind.Error)
            SetProgress(0)
        End If
    End Function

    ' ====================================================================== PULIZIA

    Public Async Function RemoveAllNonManifestFilesAsync(Optional excludeMinecraft As Boolean = False) As Task
        Log("Pulizia file obsoleti...")
        Dim target = Paths.GameDir
        If Not Directory.Exists(target) Then
            Await host.AlertAsync("Errore", "La directory di gioco non esiste per la pulizia.", AlertKind.Error)
            Return
        End If
        Dim manifestPath = Path.Combine(Paths.DownloadDir, "manifest.json")
        If Not File.Exists(manifestPath) Then
            Await host.AlertAsync("Errore", "Il manifest non esiste per la pulizia.", AlertKind.Error)
            Return
        End If

        Try
            Dim localManifest As JObject = JObject.Parse(Await File.ReadAllTextAsync(manifestPath))
            Dim files = localManifest("files").ToArray()

            Dim validFiles As New HashSet(Of String)(
                files.Select(Function(f) Path.GetFullPath(Path.Combine(target, f("path").ToObject(Of String)().Replace("/"c, Path.DirectorySeparatorChar)))),
                If(Platform.IsWindows, StringComparer.OrdinalIgnoreCase, StringComparer.Ordinal)
            )

            ' Legacy: copie .once accanto ai file utente (migrate da UserDataManager)
            For Each entry In files
                If entry("once") IsNot Nothing AndAlso entry("once").ToObject(Of Boolean)() Then
                    validFiles.Add(Path.GetFullPath(Path.Combine(target, entry("path").ToObject(Of String)().Replace("/"c, Path.DirectorySeparatorChar))) & ".once")
                End If
            Next

            ' File estratti dai package
            If localManifest("packages") IsNot Nothing Then
                For Each pkg In localManifest("packages")
                    Dim extractTo As String = If(pkg("extractTo") IsNot Nothing, pkg("extractTo").ToObject(Of String)(), "")
                    If pkg("filesToExtract") IsNot Nothing Then
                        For Each extractEntry In pkg("filesToExtract")
                            Dim relPath As String = extractEntry.ToObject(Of String)()
                            validFiles.Add(Path.GetFullPath(Path.Combine(target, extractTo.Replace("/"c, Path.DirectorySeparatorChar), relPath.Replace("/"c, Path.DirectorySeparatorChar))))
                        Next
                    End If
                Next
            End If

            ' Dati utente e pattern .gitignore/.manifestignore: valutati nel filtro
            UserData.LoadRules(Paths.DownloadDir)

            If excludeMinecraft Then
                Dim excludeListFolder As String() = {
                    Path.Combine(target, "assets"),
                    Path.Combine(target, "versions"),
                    Path.Combine(target, "libraries"),
                    Path.Combine(target, "config"),
                    Path.Combine(target, "mods", "mcef-libraries")
                }
                Dim excludeListFiles As String() = {
                    Path.Combine(target, "version.txt"),
                    Path.Combine(target, "fabricInstalled")
                }
                For Each excludePath In excludeListFolder
                    If Directory.Exists(excludePath) Then
                        For Each f In Directory.GetFiles(excludePath, "*", SearchOption.AllDirectories)
                            validFiles.Add(Path.GetFullPath(f))
                        Next
                    End If
                Next
                For Each excludePath In excludeListFiles
                    If File.Exists(excludePath) Then validFiles.Add(Path.GetFullPath(excludePath))
                Next
            End If

            Dim removedCount As Integer = 0
            Dim errorCount As Integer = 0
            Dim toDeleteCount As Integer = 0

            Await Task.Run(Sub()
                               Dim allFiles = Directory.GetFiles(target, "*", SearchOption.AllDirectories)
                               Dim toDelete = allFiles.Where(Function(f) Not validFiles.Contains(Path.GetFullPath(f)) AndAlso
                                                                 Not UserData.IsProtected(UserData.RelativePath(f))).ToList()
                               toDeleteCount = toDelete.Count
                               If toDeleteCount = 0 Then Return

                               Log($"Rimozione di {toDeleteCount} file obsoleti...")
                               removedCount = DirectoryCleaner.DeleteFilesParallel(toDelete, Sub(f, ex) Interlocked.Increment(errorCount))

                               Dim allDirs = Directory.GetDirectories(target, "*", SearchOption.AllDirectories).OrderByDescending(Function(d) d.Length)
                               For Each dirPath In allDirs
                                   Try
                                       If Directory.Exists(dirPath) AndAlso Not Directory.EnumerateFileSystemEntries(dirPath).Any() Then
                                           Directory.Delete(dirPath)
                                       End If
                                   Catch
                                   End Try
                               Next
                           End Sub)

            If toDeleteCount > 0 Then
                If errorCount > 0 Then
                    Log($"Rimossi {removedCount} file obsoleti ({errorCount} non rimovibili)")
                Else
                    Log($"Rimossi {removedCount} file obsoleti")
                End If
            End If
        Catch ex As Exception
            Log($" Errore durante pulizia file: {ex.Message}")
        End Try
    End Function

    ' ====================================================================== PACKAGES

    Private Async Function ProcessManifestPackagesAsync(manifestObj As JObject) As Task
        If manifestObj Is Nothing OrElse manifestObj("packages") Is Nothing Then Return
        Dim packages = manifestObj("packages").ToArray()
        If packages.Length = 0 Then Return

        Log($"Verifica pacchetti opzionali/extra: {packages.Length}")

        Dim packageVersions = Await LoadPackageVersionsAsync()
        Dim packageVersionsDirty As Boolean = False

        For Each pkg In packages
            Dim packageName As String = If(pkg("name") IsNot Nothing, pkg("name").ToObject(Of String)(), "Package")
            Dim packageAction As String = If(pkg("action") IsNot Nothing, pkg("action").ToObject(Of String)(), "")
            Dim packageVersion As String = If(pkg("version") IsNot Nothing, pkg("version").ToObject(Of String)(), "")
            Dim isRequired As Boolean = If(pkg("required") IsNot Nothing, pkg("required").ToObject(Of Boolean)(), False)
            Dim overwrite As Boolean = If(pkg("overwrite") IsNot Nothing, pkg("overwrite").ToObject(Of Boolean)(), False)
            Dim extractTo As String = If(pkg("extractTo") IsNot Nothing, pkg("extractTo").ToObject(Of String)(), "")
            Dim packageKey As String = packageName.Trim()
            If String.IsNullOrWhiteSpace(packageKey) Then
                packageKey = If(pkg("description") IsNot Nothing, pkg("description").ToObject(Of String)(), "Package")
            End If

            Dim installedVersion As String = ""
            If packageVersions.ContainsKey(packageKey) Then installedVersion = packageVersions(packageKey)
            Dim hasVersionChange As Boolean = (Not String.IsNullOrWhiteSpace(packageVersion)) AndAlso Not packageVersion.Equals(installedVersion, StringComparison.OrdinalIgnoreCase)
            Dim effectiveOverwrite As Boolean = overwrite OrElse hasVersionChange

            If hasVersionChange Then
                If String.IsNullOrWhiteSpace(installedVersion) Then
                    Log($"Package {packageName}: installazione versione {packageVersion}")
                Else
                    Log($"Package {packageName}: aggiornamento {installedVersion} -> {packageVersion}")
                End If
            End If

            Dim pkgError As Exception = Nothing
            Try
                If Not packageAction.Equals("extract", StringComparison.OrdinalIgnoreCase) Then
                    Log($"Package non gestito ({packageAction}): {packageName}")
                    Continue For
                End If

                Dim filesToExtract As New List(Of String)()
                If pkg("filesToExtract") IsNot Nothing Then
                    For Each entry In pkg("filesToExtract")
                        filesToExtract.Add(entry.ToObject(Of String)())
                    Next
                End If

                Dim targetDir As String = Path.Combine(Paths.GameDir, extractTo.Replace("/"c, Path.DirectorySeparatorChar))
                Dim progressTemplate As String = If(pkg("progressMessage") IsNot Nothing, pkg("progressMessage").ToObject(Of String)(), "")

                If Not effectiveOverwrite AndAlso filesToExtract.Count > 0 Then
                    Dim allExtracted As Boolean = filesToExtract.All(Function(rel) File.Exists(Path.Combine(targetDir, rel.Replace("/"c, Path.DirectorySeparatorChar))))
                    If allExtracted Then
                        Log($"✓ Package gia' pronto: {packageName}")
                        If Not String.IsNullOrWhiteSpace(packageVersion) AndAlso (Not packageVersions.ContainsKey(packageKey) OrElse Not packageVersions(packageKey).Equals(packageVersion, StringComparison.OrdinalIgnoreCase)) Then
                            packageVersions(packageKey) = packageVersion
                            packageVersionsDirty = True
                        End If
                        Continue For
                    End If
                End If

                Dim archiveStartPath As String = Nothing
                Dim packageFiles As JArray = Nothing
                If pkg("files") IsNot Nothing Then packageFiles = pkg("files").ToObject(Of JArray)()

                If pkg("parts") IsNot Nothing AndAlso packageFiles IsNot Nothing Then
                    Dim partEntries = pkg("parts").ToArray()
                    Dim totalParts As Integer = partEntries.Length
                    Dim currentPart As Integer = 0

                    For Each partEntry In partEntries
                        currentPart += 1
                        Dim partName As String = partEntry.ToObject(Of String)()
                        Dim matchingFile As JToken = packageFiles.FirstOrDefault(Function(pf)
                                                                                     Dim relPath As String = If(pf("path") IsNot Nothing, pf("path").ToObject(Of String)(), "")
                                                                                     Return Path.GetFileName(relPath).Equals(partName, StringComparison.OrdinalIgnoreCase)
                                                                                 End Function)
                        If matchingFile Is Nothing Then
                            Dim missingPartMessage As String = $"Parte non mappata in files: {partName} ({packageName})"
                            If isRequired Then Throw New Exception(missingPartMessage)
                            Log($"! {missingPartMessage}")
                            Continue For
                        End If

                        Dim relPartPath As String = matchingFile("path").ToObject(Of String)()
                        Dim localPartPath As String = Path.Combine(Paths.GameDir, relPartPath.Replace("/"c, Path.DirectorySeparatorChar))
                        Dim expectedSize As Long = If(matchingFile("size") IsNot Nothing, matchingFile("size").ToObject(Of Long)(), 0)
                        Dim expectedHash As String = If(matchingFile("sha256") IsNot Nothing, matchingFile("sha256").ToObject(Of String)(), "")

                        Dim partReady As Boolean = Await VerifyFileWithCacheAsync(localPartPath, expectedSize, expectedHash)
                        Dim percentage As Integer = CInt(Math.Round((currentPart * 100.0) / Math.Max(1, totalParts)))
                        Dim progressText As String = If(String.IsNullOrWhiteSpace(progressTemplate),
                                                        $"Package {packageName}: parte {currentPart}/{totalParts} ({percentage}%)",
                                                        progressTemplate.Replace("*currentPart*", currentPart.ToString()).Replace("*totalParts*", totalParts.ToString()).Replace("*percentage*", percentage.ToString()))

                        If Not partReady Then
                            Directory.CreateDirectory(Path.GetDirectoryName(localPartPath))
                            Log(progressText)
                            Await DownloadFileAsync(RepoBasePath & Uri.EscapeDataString(relPartPath).Replace("%2F", "/"), localPartPath, 3)
                            UpdateHashCacheEntry(localPartPath, expectedSize, expectedHash)
                        Else
                            Log(progressText & " (ok)")
                        End If

                        If archiveStartPath Is Nothing Then archiveStartPath = localPartPath
                    Next
                ElseIf packageFiles IsNot Nothing Then
                    For Each pkgFile In packageFiles
                        If pkgFile("path") IsNot Nothing Then
                            Dim candidate As String = Path.Combine(Paths.GameDir, pkgFile("path").ToObject(Of String)().Replace("/"c, Path.DirectorySeparatorChar))
                            If File.Exists(candidate) Then
                                archiveStartPath = candidate
                                Exit For
                            End If
                        End If
                    Next
                End If

                If String.IsNullOrEmpty(archiveStartPath) Then
                    Dim msg As String = $"File archive non trovato per package: {packageName}"
                    If isRequired Then Throw New Exception(msg)
                    Log($"! {msg}")
                    Continue For
                End If

                Log($"Estrazione package: {packageName}")

                Dim tempRoot As String = Path.Combine(Paths.DownloadDir, "packages_tmp")
                Dim safeName As String = String.Concat(packageName.Select(Function(ch) If(Path.GetInvalidFileNameChars().Contains(ch), "_"c, ch)))
                Dim tempExtract As String = Path.Combine(tempRoot, safeName)
                If Directory.Exists(tempExtract) Then Directory.Delete(tempExtract, True)
                Directory.CreateDirectory(tempExtract)

                Await ArchiveTools.Extract7zAsync(archiveStartPath, tempExtract, AddressOf Log)
                Log("Estrazione completata con successo.")

                Directory.CreateDirectory(targetDir)

                If filesToExtract.Count > 0 Then
                    For Each rel In filesToExtract
                        Dim relNormalized As String = rel.Replace("/"c, Path.DirectorySeparatorChar)
                        Dim sourcePath As String = Path.Combine(tempExtract, relNormalized)
                        Dim destinationPath As String = Path.Combine(targetDir, relNormalized)

                        If Not File.Exists(sourcePath) Then
                            If isRequired Then Throw New Exception($"File estratto mancante: {rel} ({packageName})")
                            Log($"! File package non trovato: {rel}")
                            Continue For
                        End If

                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath))
                        If File.Exists(destinationPath) AndAlso Not effectiveOverwrite Then Continue For
                        File.Copy(sourcePath, destinationPath, True)
                    Next
                Else
                    For Each extractedFile In Directory.GetFiles(tempExtract, "*", SearchOption.AllDirectories)
                        Dim relativePath As String = extractedFile.Substring(tempExtract.Length).TrimStart("\"c, "/"c)
                        Dim destinationPath As String = Path.Combine(targetDir, relativePath)
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath))
                        If File.Exists(destinationPath) AndAlso Not effectiveOverwrite Then Continue For
                        File.Copy(extractedFile, destinationPath, True)
                    Next
                End If

                Try
                    Directory.Delete(tempExtract, True)
                Catch
                End Try

                If Not String.IsNullOrWhiteSpace(packageVersion) AndAlso (Not packageVersions.ContainsKey(packageKey) OrElse Not packageVersions(packageKey).Equals(packageVersion, StringComparison.OrdinalIgnoreCase)) Then
                    packageVersions(packageKey) = packageVersion
                    packageVersionsDirty = True
                End If

                Log($"✓ Package estratto: {packageName}")

            Catch ex As Exception
                pkgError = ex
            End Try

            If pkgError IsNot Nothing Then
                If isRequired Then
                    Await host.AlertAsync("Errore pacchetto", $"Errore durante l'installazione del pacchetto {packageName}: {pkgError.Message}", AlertKind.Error)
                    Throw pkgError
                End If
                Log($"Errore package non obbligatorio ({packageName}): {pkgError.Message}")
            End If
        Next

        If packageVersionsDirty Then Await SavePackageVersionsAsync(packageVersions)
    End Function

    Private Function GetPackageVersionsFilePath() As String
        Return Path.Combine(Paths.DownloadDir, "package_versions.json")
    End Function

    Private Async Function LoadPackageVersionsAsync() As Task(Of Dictionary(Of String, String))
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim filePath = GetPackageVersionsFilePath()
            If Not File.Exists(filePath) Then Return result
            Dim json = Await File.ReadAllTextAsync(filePath)
            If String.IsNullOrWhiteSpace(json) Then Return result
            Dim parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(json)
            If parsed IsNot Nothing Then
                For Each kvp In parsed
                    result(kvp.Key) = kvp.Value
                Next
            End If
        Catch ex As Exception
            Log($"Impossibile leggere versioni package: {ex.Message}")
        End Try
        Return result
    End Function

    Private Async Function SavePackageVersionsAsync(versions As Dictionary(Of String, String)) As Task
        Try
            If versions Is Nothing Then Return
            Directory.CreateDirectory(Paths.DownloadDir)
            Await File.WriteAllTextAsync(GetPackageVersionsFilePath(), Newtonsoft.Json.JsonConvert.SerializeObject(versions, Newtonsoft.Json.Formatting.Indented))
        Catch ex As Exception
            Log($"Impossibile salvare versioni package: {ex.Message}")
        End Try
    End Function

    ' ====================================================================== STEP 2: FABRIC

    Private Async Function Step2Async() As Task
        Dim fabricInstalledMarker As String = Path.Combine(Paths.GameDir, "fabricInstalled")
        Dim fabricInst As New FabricInstaller()

        If fabricInst.IsForgeInstalled(Paths.GameDir) Then
            Log("Migrazione da Forge a Fabric in corso...")
            fabricInst.CleanupForgeInstallation(Paths.GameDir)
            Try
                Dim oldForgePath As String = Path.Combine(Paths.DownloadDir, "forge-installer.jar")
                If File.Exists(oldForgePath) Then File.Delete(oldForgePath)
            Catch
            End Try
            Log("Migrazione completata. Installazione Fabric...")
        End If

        Dim manifestLoaderVersion As String = manifest("fabricLoaderVersion").ToObject(Of String)().Trim()
        Dim manifestMcVersion As String = manifest("mcVersion").ToObject(Of String)().Trim()

        ' Entrambe le versioni (loader E minecraft) devono coincidere con il manifest
        If File.Exists(fabricInstalledMarker) AndAlso fabricInst.IsFabricInstalled(Settings.FabricLoaderVersion, Settings.McVersion, Paths.GameDir) Then
            If Settings.FabricLoaderVersion = manifestLoaderVersion AndAlso Settings.McVersion = manifestMcVersion Then
                Log($"✓ Fabric Loader {Settings.FabricLoaderVersion} gia' installato")
                SetProgress(65)
                Await Step3Async()
                Return
            End If
            Log($"Manifest richiede Fabric Loader {manifestLoaderVersion} per Minecraft {manifestMcVersion} (installato: {Settings.FabricLoaderVersion} / {Settings.McVersion}). Aggiornamento...")
        End If

        Dim step2Error As Exception = Nothing
        Try
            Settings.FabricLoaderVersion = manifestLoaderVersion
            Settings.McVersion = manifestMcVersion
            Settings.FabricVersionId = "fabric-loader-" & manifestLoaderVersion & "-" & manifestMcVersion
            SaveSettings()
            Try
                File.Delete(fabricInstalledMarker)
            Catch
            End Try

            Log("Installazione Fabric Loader...")
            SetProgress(45)
            Await Task.Delay(200)

            Dim success As Boolean = Await fabricInst.InstallFabric(Settings.FabricLoaderVersion, Settings.McVersion, Paths.GameDir)
            If success Then
                SetProgress(65)
                Log("Fabric Loader installato!")
                Await Step3Async()
            Else
                LogError("Errore durante l'installazione di Fabric Loader.")
                Await HandleFabricInstallationFailureAsync()
            End If
        Catch ex As Exception
            step2Error = ex
        End Try
        If step2Error IsNot Nothing Then
            LogError($" Errore durante installazione Fabric: {step2Error.Message}")
            Await host.AlertAsync("Errore", $"Si e' verificato un errore durante l'installazione di Fabric: {step2Error.Message}", AlertKind.Error)
            SetProgress(0)
        End If
    End Function

    Private Async Function HandleFabricInstallationFailureAsync() As Task
        Dim failure As Exception = Nothing
        Try
            Dim retry As Boolean = Await host.ConfirmAsync("Errore",
                "Si e' verificato un errore durante l'installazione di Fabric. Scegli Si per riprovare usando la safe-mode: questo reinstallera' Minecraft e ritentera' l'installazione.",
                AlertKind.Error)
            If retry Then
                Log("Avvio modalita' safe-mode...")
                SetProgress(60)
                Await RemoveAllNonManifestFilesAsync()
                Log("Reinstallazione Minecraft...")
                SetProgress(65)
                Try
                    Dim fabricMarker As String = Path.Combine(Paths.GameDir, "fabricInstalled")
                    If File.Exists(fabricMarker) Then File.Delete(fabricMarker)
                Catch
                End Try
                Await Step2Async()
            Else
                Log("Installazione Fabric fallita.")
                SetProgress(0)
            End If
        Catch ex As Exception
            failure = ex
        End Try
        If failure IsNot Nothing Then
            Await host.AlertAsync("Errore", $"Si e' verificato un errore durante la reinstallazione: {failure.Message}", AlertKind.Error)
            SetProgress(0)
        End If
    End Function

    ' ====================================================================== STEP 3/4: MINECRAFT

    Private Async Function Step3Async() As Task
        If String.IsNullOrEmpty(latestModpackVersion) Then latestModpackVersion = Await GetLatestModpackVersionAsync()
        Log("Installazione di GangDrogaCity " & latestModpackVersion & "...")
        SetProgress(75)

        Dim fabricInstalledMarker As String = Path.Combine(Paths.GameDir, "fabricInstalled")

        If Not File.Exists(fabricInstalledMarker) Then
            Dim installError As Exception = Nothing
            Try
                Log("Download Minecraft...")
                Await mcDownloader.DownloadMinecraftVersion(Settings.McVersion, Paths.GameDir)
                SetProgress(85)

                Dim javaPath As String = Await JavaManager.EnsureJavaAsync(Paths.GameDir, AddressOf Log)
                Log($" Usando Java: {javaPath}")
                If Not JavaManager.IsJavaValid(javaPath) Then
                    Throw New Exception("Java non valido o non funzionante")
                End If

                File.WriteAllText(Path.Combine(Paths.GameDir, "version.txt"), latestModpackVersion)
                File.WriteAllText(fabricInstalledMarker, "True")
                SetProgress(100)
                Log("Installazione Fabric completata.")
            Catch ex As Exception
                installError = ex
            End Try
            If installError IsNot Nothing Then
                LogError($" Errore durante installazione: {installError.Message}")
                Await host.AlertAsync("Errore", $"Si e' verificato un errore durante l'installazione: {installError.Message}", AlertKind.Error)
                SetProgress(0)
            End If
        End If

        Await Step4Async()
    End Function

    Private Async Function Step4Async() As Task
        Log("Finalizzazione...")
        Await VerifyAndFixCorruptedJarsAsync()

        Log("Verifica completezza Minecraft vanilla...")
        Await mcDownloader.DownloadMinecraftVersion(Settings.McVersion, Paths.GameDir)

        Log("Verifica librerie Fabric...")
        Await mcDownloader.DownloadVersionDependencies(Settings.FabricVersionId, Paths.GameDir)

        SetDoNotPowerOff(False)
        SetBusy(False)
        Phase = LauncherPhase.Ready
    End Function

    ' ====================================================================== JAVA

    Private Async Function CheckJavaStatusAsync() As Task(Of Boolean)
        Dim javaError As Exception = Nothing
        Try
            Log("Controllo disponibilita' Java...")
            Dim javaPath As String = JavaManager.FindExistingJava(Paths.GameDir)
            If javaPath Is Nothing Then
                ' Su Linux/macOS (o se il modpack non la fornisce) scarica la JRE
                javaPath = Await JavaManager.EnsureJavaAsync(Paths.GameDir, AddressOf Log)
            End If

            If Not String.IsNullOrEmpty(javaPath) AndAlso JavaManager.IsJavaValid(javaPath) Then
                Log($" Java trovato: {JavaManager.GetJavaVersion(javaPath)}")
                Log($" Percorso: {javaPath}")
                Return True
            End If
            Log("Java non trovato o non valido.")
            Return False
        Catch ex As Exception
            javaError = ex
        End Try
        Log($" Errore controllo Java: {javaError.Message}")
        Await host.AlertAsync("Errore", $"Si e' verificato un errore durante il controllo di Java: {javaError.Message}", AlertKind.Error)
        Return False
    End Function

    ' ====================================================================== PLAY

    ''' <summary>
    ''' Sincronizza il modpack e avvia Minecraft. Restituisce False se l'avvio non e' avvenuto.
    ''' </summary>
    Public Async Function PlayAsync(Optional reducedGraphics As Boolean = False) As Task(Of Boolean)
        If Phase <> LauncherPhase.Ready Then Return False
        Phase = LauncherPhase.Launching
        SetBusy(True, "Avvio in corso...")
        Dim playError As Exception = Nothing
        Try
            Await Step1Async(True)
            SetDoNotPowerOff(False)

            If Settings.Username Is Nothing OrElse Settings.Username.Trim().Length < 3 Then
                Await host.AlertAsync("Nome utente mancante", "Imposta un nome utente prima di giocare.", AlertKind.Warning)
                Return False
            End If

            Log("Verifica completezza Minecraft vanilla...")
            Await mcDownloader.DownloadMinecraftVersion(Settings.McVersion, Paths.GameDir)
            Log("Verifica librerie Fabric...")
            Await mcDownloader.DownloadVersionDependencies(Settings.FabricVersionId, Paths.GameDir)

            If reducedGraphics Then
                RemoveClientModsForReducedGraphics()
                ApplyReducedGraphicsOptions()
            End If

            Dim javaPath As String = Await JavaManager.EnsureJavaAsync(Paths.GameDir, AddressOf Log)
            Dim screen = host.GetScreenSize()
            mcProcess = Await mcLauncher.LaunchMinecraft(javaPath, Settings.Username.Trim(), Settings.FabricVersionId, Paths.GameDir, Settings.RamMB, screen.Width, screen.Height, Settings.DevMode)

            If mcProcess Is Nothing Then
                Await host.AlertAsync("Errore avvio", "Si e' verificato un errore durante l'avvio di Minecraft. Controlla i log per maggiori dettagli.", AlertKind.Error)
                Return False
            End If

            StartProcessMonitoring(mcProcess)
            Await Task.Delay(2000)
            Phase = LauncherPhase.Playing
            Return True
        Catch ex As Exception
            playError = ex
        Finally
            SetBusy(False)
            If Phase <> LauncherPhase.Playing Then Phase = LauncherPhase.Ready
        End Try
        LogError($"Errore avvio: {playError.Message}")
        Await host.AlertAsync("Errore avvio", $"Si e' verificato un errore durante l'avvio di Minecraft: {playError.Message}", AlertKind.Error)
        Return False
    End Function

    Public Sub StopGame()
        Try
            If mcProcess IsNot Nothing AndAlso Not mcProcess.HasExited Then
                mcProcess.Kill(True)
                mcProcess.WaitForExit(5000)
            End If
        Catch
        End Try
    End Sub

    Private Sub StartProcessMonitoring(p As Process)
        Log("Monitoraggio processo Minecraft attivato")
        Task.Run(Async Function()
                     Try
                         Await p.WaitForExitAsync()
                     Catch
                     End Try
                     Dim exitCode As Integer = 0
                     Try
                         exitCode = p.ExitCode
                     Catch
                     End Try
                     Log($"Minecraft chiuso (Exit Code: {exitCode})")

                     ' Salva subito impostazioni/keybind/waypoint modificati in partita
                     Try
                         UserData.Backup()
                     Catch
                     End Try

                     Try
                         p.Dispose()
                     Catch
                     End Try
                     mcProcess = Nothing
                     Phase = LauncherPhase.Ready
                     RaiseEvent GameExited(exitCode)
                 End Function)
    End Sub

    Private Sub RemoveClientModsForReducedGraphics()
        Dim modsDir As String = Path.Combine(Paths.GameDir, "mods")
        If Not Directory.Exists(modsDir) Then Return
        Dim clientModFiles() As String = Directory.GetFiles(modsDir, "*.jar", SearchOption.AllDirectories).
            Where(Function(modFile) Path.GetFileName(modFile).IndexOf("[client]", StringComparison.OrdinalIgnoreCase) >= 0).ToArray()
        For Each modFile As String In clientModFiles
            Try
                If modFile.IndexOf("drippyloadingscreen", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
                File.Delete(modFile)
                Log($"Rimuovo: {Path.GetFileName(modFile)}")
            Catch ex As Exception
                Log($"Errore rimuovendo la mod client {Path.GetFileName(modFile)}: {ex.Message}")
            End Try
        Next
    End Sub

    Private Sub ApplyReducedGraphicsOptions()
        Dim optionsPath As String = Path.Combine(Paths.GameDir, "options.txt")
        Dim optionsLines As New List(Of String)()
        If File.Exists(optionsPath) Then optionsLines.AddRange(File.ReadAllLines(optionsPath))

        UpdateOptionLine(optionsLines, "graphicsMode", "0")
        UpdateOptionLine(optionsLines, "renderDistance", "5")
        UpdateOptionLine(optionsLines, "enableVsync", "false")
        UpdateOptionLine(optionsLines, "entityShadows", "false")
        UpdateOptionLine(optionsLines, "simulationDistance", "5")
        UpdateOptionLine(optionsLines, "ao", "false")
        UpdateOptionLine(optionsLines, "biomeBlendRadius", "0")
        UpdateOptionLine(optionsLines, "particles", "0")
        UpdateOptionLine(optionsLines, "mipmapLevels", "1")
        UpdateOptionLine(optionsLines, "renderClouds", "false")
        UpdateOptionLine(optionsLines, "maxFps", "60")

        File.WriteAllLines(optionsPath, optionsLines)
    End Sub

    Private Sub UpdateOptionLine(optionsLines As List(Of String), optionName As String, optionValue As String)
        Dim prefix As String = optionName & ":"
        Dim existingIndex As Integer = optionsLines.FindIndex(Function(line) line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        Dim updatedLine As String = prefix & optionValue
        If existingIndex >= 0 Then
            optionsLines(existingIndex) = updatedLine
        Else
            optionsLines.Add(updatedLine)
        End If
    End Sub

    ' ====================================================================== MANUTENZIONE

    ''' <summary>Reinstalla Minecraft (file non nel manifest) conservando i dati utente.</summary>
    Public Async Function ReinstallMinecraftAsync() As Task
        If Phase = LauncherPhase.Playing OrElse Phase = LauncherPhase.Launching Then Return
        SetBusy(True, "Resetto Minecraft...")
        SetDoNotPowerOff(True)
        Await Task.Delay(500)
        If Not Directory.Exists(Paths.GameDir) Then
            SetBusy(False)
            Await host.AlertAsync("Errore", "La directory di gioco non esiste.", AlertKind.Error)
            Return
        End If
        Dim reinstallError As Exception = Nothing
        Try
            UserData.LoadRules(Paths.DownloadDir)
            Await Task.Run(Function() UserData.Backup())
            skipUserDataBackup = True
            pendingUserDataRestore = True

            Await RemoveAllNonManifestFilesAsync()
            For Each marker In {"version.txt", "fabricInstalled", "forgeInstalled", "forgeDownloaded"}
                Try
                    File.Delete(Path.Combine(Paths.GameDir, marker))
                Catch
                End Try
            Next
            For Each marker In {"forgeInstalled", "forgeDownloaded"}
                Try
                    File.Delete(Path.Combine(Paths.MinecraftDir, marker))
                Catch
                End Try
            Next
            SetBusy(False)
            Await BootAsync()
        Catch ex As Exception
            reinstallError = ex
        End Try
        If reinstallError IsNot Nothing Then
            SetBusy(False)
            Await host.AlertAsync("Errore", $"Si e' verificato un errore durante la reinstallazione: {reinstallError.Message}", AlertKind.Error)
        End If
    End Function

    ''' <summary>Reset completo: elimina game data, download e cache preservando la cartella userdata.</summary>
    Public Async Function ReinstallAllAsync() As Task
        If Phase = LauncherPhase.Playing OrElse Phase = LauncherPhase.Launching Then Return
        SetBusy(True, "Reset completo in corso...")
        SetDoNotPowerOff(True)
        Dim resetError As Exception = Nothing
        Try
            Await Task.Delay(300)
            Try
                UserData.LoadRules(Paths.DownloadDir)
                Await Task.Run(Function() UserData.Backup())
            Catch
            End Try
            skipUserDataBackup = True
            pendingUserDataRestore = True

            If Directory.Exists(Paths.MinecraftDir) Then
                Log("Rimozione dati di gioco precedenti...")
                Dim trashTasks As New List(Of Task)
                For Each entry As String In Directory.GetDirectories(Paths.MinecraftDir)
                    If IsUserDataOrTrashFolder(entry) Then Continue For
                    trashTasks.Add(DirectoryCleaner.FastDeleteDirectory(entry))
                Next
                For Each leftoverFile As String In Directory.GetFiles(Paths.MinecraftDir)
                    If Path.GetFileName(leftoverFile).Equals("settings.json", StringComparison.OrdinalIgnoreCase) Then Continue For
                    Try
                        File.Delete(leftoverFile)
                    Catch
                    End Try
                Next
                If Directory.GetDirectories(Paths.MinecraftDir).Any(Function(d) Not IsUserDataOrTrashFolder(d)) Then
                    Await Task.WhenAll(trashTasks)
                End If
            End If

            Paths.EnsureDirectories()
            fileHashCache.Clear()
            Log("Reinstallazione completa avviata.")
            SetBusy(False)
            Await BootAsync()
        Catch ex As Exception
            resetError = ex
        End Try
        If resetError IsNot Nothing Then
            SetBusy(False)
            SetDoNotPowerOff(False)
            Await host.AlertAsync("Errore", $"Si e' verificato un errore durante la reinstallazione completa: {resetError.Message}", AlertKind.Error)
        End If
    End Function

    Private Function IsUserDataOrTrashFolder(folderPath As String) As Boolean
        Dim name As String = Path.GetFileName(folderPath.TrimEnd("\"c, "/"c))
        Return name.Equals("userdata", StringComparison.OrdinalIgnoreCase) OrElse
               name.IndexOf(".trash-", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>Verifica forzata dell'installazione (sync completo ignorando il commit salvato).</summary>
    Public Async Function VerifyInstallationAsync() As Task
        If Phase = LauncherPhase.Playing OrElse Phase = LauncherPhase.Launching Then Return
        Phase = LauncherPhase.Installing
        SetBusy(True, "Verifica in corso...")
        Await Task.Delay(300)
        SetBusy(False)
        Dim verifyError As Exception = Nothing
        Try
            Await Step1Async(False, True)
        Catch ex As Exception
            verifyError = ex
        End Try
        If verifyError IsNot Nothing Then
            Await host.AlertAsync("Errore", $"Si e' verificato un errore durante la verifica: {verifyError.Message}", AlertKind.Error)
        End If
    End Function

    Public Async Function GetBranchesAsync() As Task(Of List(Of String))
        Dim github As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))
        Dim branches = Await github.Repository.Branch.GetAll(ModpackOwner, ModpackRepo)
        Return branches.Select(Function(b) b.Name).OrderBy(Function(n) n).ToList()
    End Function

    ''' <summary>Cambio branch del modpack (funzione DEV): chiede conferma, mostra la lista e riavvia il boot.</summary>
    Public Async Function ChangeBranchAsync() As Task
        If Phase = LauncherPhase.Playing OrElse Phase = LauncherPhase.Launching Then Return
        Dim ok = Await host.ConfirmAsync("Conferma",
            "Sei sicuro di voler cambiare branch? Potresti non poter giocare a GangDrogaCity Online o alcune feature potrebbero essere disattivate: procedi solo se sai cosa stai facendo.",
            AlertKind.Warning)
        If Not ok Then Return

        Dim branches As List(Of String)
        Try
            branches = Await GetBranchesAsync()
        Catch ex As Exception
            Log($"[DEV] Errore recupero branch: {ex.Message}")
            Return
        End Try
        If branches Is Nothing OrElse branches.Count = 0 Then
            Log("[DEV] Nessun branch trovato.")
            Return
        End If

        Dim selected = Await host.SelectBranchAsync(branches, RepoBranch)
        If String.IsNullOrEmpty(selected) Then
            Log("[DEV] Nessun branch selezionato.")
            Return
        End If

        Settings.CurrentBranch = selected
        SaveSettings()
        Log($"[DEV] Branch selezionato: {selected}")
        Await BootAsync()
    End Function

    ''' <summary>Carica l'ultimo log di gioco e restituisce l'URL pubblico, oppure lancia un'eccezione.</summary>
    Public Async Function UploadCrashReportAsync() As Task(Of String)
        Dim crashPath As String = Path.Combine(Paths.GameDir, "logs", "latest.log")
        If Not File.Exists(crashPath) Then Throw New FileNotFoundException("Log di gioco non trovato", crashPath)

        Using content As New ByteArrayContent(Await File.ReadAllBytesAsync(crashPath))
            Using req As New HttpRequestMessage(HttpMethod.Put, "https://drop.stefanodeblasi.it/upload/")
                req.Headers.Accept.ParseAdd("application/json")
                req.Headers.Add("Linx-Randomize", "yes")
                req.Content = content
                Using resp = Await http.SendAsync(req)
                    resp.EnsureSuccessStatusCode()
                    Dim json = JObject.Parse(Await resp.Content.ReadAsStringAsync())
                    Dim fileUrl = json("url").ToString()
                    Try
                        File.WriteAllText(Path.Combine(Paths.GameDir, "last_report_url.txt"), fileUrl)
                    Catch
                    End Try
                    Return fileUrl
                End Using
            End Using
        End Using
    End Function

    ' ====================================================================== VERIFICA JAR

    Private Async Function VerifyAndFixCorruptedJarsAsync() As Task
        Try
            Log("Verifica integrita' file JAR...")
            Dim manifestData As Dictionary(Of String, (Size As Long, Sha256 As String)) = Nothing
            Dim manifestPath As String = Path.Combine(Paths.DownloadDir, "manifest.json")
            If File.Exists(manifestPath) Then
                Try
                    Dim localManifest As JObject = JObject.Parse(Await File.ReadAllTextAsync(manifestPath))
                    manifestData = New Dictionary(Of String, (Size As Long, Sha256 As String))(StringComparer.OrdinalIgnoreCase)
                    For Each fileEntry In localManifest("files")
                        manifestData(fileEntry("path").ToObject(Of String)()) = (fileEntry("size").ToObject(Of Long)(), fileEntry("sha256").ToObject(Of String)())
                    Next
                Catch
                    manifestData = Nothing
                End Try
            End If

            Dim corrupted As New List(Of String)
            For Each folder In {"mods", "libraries"}
                Dim dir As String = Path.Combine(Paths.GameDir, folder)
                If Not Directory.Exists(dir) Then Continue For
                Dim jarFiles() As String = Directory.GetFiles(dir, "*.jar", SearchOption.AllDirectories)
                Log($" Verifica {jarFiles.Length} file in {folder}...")

                Dim found = Await Task.Run(Function()
                                               Dim bad As New List(Of String)
                                               Parallel.ForEach(jarFiles, Sub(jar)
                                                                              Dim rel = UserData.RelativePath(jar)
                                                                              Dim isCorrupted As Boolean
                                                                              If manifestData IsNot Nothing AndAlso manifestData.ContainsKey(rel) Then
                                                                                  Dim entry = manifestData(rel)
                                                                                  isCorrupted = Not IsJarValidWithHash(jar, entry.Size, entry.Sha256)
                                                                              Else
                                                                                  isCorrupted = Not IsJarValid(jar)
                                                                              End If
                                                                              If isCorrupted Then
                                                                                  SyncLock bad
                                                                                      bad.Add(jar)
                                                                                  End SyncLock
                                                                              End If
                                                                          End Sub)
                                               Return bad
                                           End Function)
                corrupted.AddRange(found)
            Next

            If corrupted.Count > 0 Then
                Log($" Trovati {corrupted.Count} file JAR corrotti: verranno riscaricati.")
                For Each f In corrupted
                    Try
                        File.Delete(f)
                        Log($" Rimosso JAR corrotto: {UserData.RelativePath(f)}")
                    Catch ex As Exception
                        Log($" Errore rimozione {Path.GetFileName(f)}: {ex.Message}")
                    End Try
                Next
                SaveHashCache()
                ' Forza il prossimo sync (altrimenti con lo stesso commit remoto verrebbe saltato
                ' e i file eliminati non verrebbero riscaricati)
                Try
                    File.Delete(Path.Combine(Paths.DownloadDir, "latest_modpack_commit.txt"))
                Catch
                End Try
            Else
                Log("Tutti i file JAR sono integri e validi!")
            End If
        Catch ex As Exception
            Log($" Errore durante verifica JAR: {ex.Message}")
        End Try
    End Function

    Private Function IsJarValidWithHash(jarPath As String, expectedSize As Long, expectedHash As String) As Boolean
        Try
            If Not IsJarValid(jarPath) Then Return False
            If New FileInfo(jarPath).Length <> expectedSize Then Return False

            Dim cacheKey As String = $"{jarPath}_{expectedSize}"
            SyncLock fileHashCache
                If fileHashCache.ContainsKey(cacheKey) Then
                    Return fileHashCache(cacheKey).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
                End If
            End SyncLock

            Dim actualHash As String = UserDataManager.ComputeSha256(jarPath)
            SyncLock fileHashCache
                fileHashCache(cacheKey) = actualHash
            End SyncLock
            Return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Un JAR e' considerato integro se e' un archivio zip leggibile con almeno una voce.
    ''' Niente euristiche su MANIFEST.MF o file .class: esistono mod di soli dati
    ''' (es. tru.e-ending) e librerie minuscole (blocklist-1.0.10.jar, 964 byte) che
    ''' il vecchio controllo scambiava per file corrotti, cancellandoli a ogni avvio.
    ''' Quando il manifest fornisce l'hash SHA256, e' quello a fare fede.
    ''' </summary>
    Private Function IsJarValid(jarPath As String) As Boolean
        Try
            If Not File.Exists(jarPath) Then Return False
            If New FileInfo(jarPath).Length < 22 Then Return False
            Using archive As ZipArchive = ZipFile.OpenRead(jarPath)
                Return archive.Entries.Count > 0
            End Using
        Catch
            Return False
        End Try
    End Function

    ' ====================================================================== HASH CACHE / DOWNLOAD

    Private Async Function VerifyFileWithCacheAsync(filePath As String, expectedSize As Long, expectedHash As String) As Task(Of Boolean)
        If Not File.Exists(filePath) Then Return False
        If New FileInfo(filePath).Length <> expectedSize Then Return False

        Dim cacheKey As String = $"{filePath}_{expectedSize}"
        SyncLock fileHashCache
            If fileHashCache.ContainsKey(cacheKey) AndAlso fileHashCache(cacheKey).Equals(expectedHash, StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
        End SyncLock

        Dim actualHash = Await Task.Run(Function() UserDataManager.ComputeSha256(filePath))
        SyncLock fileHashCache
            fileHashCache(cacheKey) = actualHash
        End SyncLock
        Return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub LoadHashCache()
        Try
            If File.Exists(Paths.HashCacheFile) Then
                Dim cache = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(File.ReadAllText(Paths.HashCacheFile))
                If cache IsNot Nothing Then fileHashCache = cache
            End If
        Catch
            fileHashCache = New Dictionary(Of String, String)()
        End Try
    End Sub

    Private Sub SaveHashCache()
        Try
            Dim json As String
            SyncLock fileHashCache
                json = Newtonsoft.Json.JsonConvert.SerializeObject(fileHashCache, Newtonsoft.Json.Formatting.None)
            End SyncLock
            File.WriteAllText(Paths.HashCacheFile, json)
        Catch
        End Try
    End Sub

    Private Sub UpdateHashCacheEntry(filePath As String, fileSize As Long, fileHash As String)
        SyncLock fileHashCache
            fileHashCache($"{filePath}_{fileSize}") = fileHash
        End SyncLock
    End Sub

    ''' <summary>Download di un file in modo atomico (file temporaneo poi rinominato), con retry.</summary>
    Private Async Function DownloadFileAsync(url As String, destination As String, Optional maxAttempts As Integer = 1) As Task
        Dim lastEx As Exception = Nothing
        Dim tmp As String = destination & ".tmp"
        For attempt As Integer = 1 To Math.Max(1, maxAttempts)
            Dim failed As Boolean = False
            Try
                Directory.CreateDirectory(Path.GetDirectoryName(destination))
                Using resp = Await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                    resp.EnsureSuccessStatusCode()
                    Using fs = New FileStream(tmp, IO.FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, True)
                        Await resp.Content.CopyToAsync(fs)
                    End Using
                End Using
                File.Move(tmp, destination, True)
                Return
            Catch ex As Exception
                lastEx = ex
                failed = True
                Try
                    If File.Exists(tmp) Then File.Delete(tmp)
                Catch
                End Try
            End Try
            If failed AndAlso attempt < maxAttempts Then Await Task.Delay(800 * attempt)
        Next
        Throw lastEx
    End Function

    ' ====================================================================== RETE / MODPACK INFO

    Private Sub StartInternetMonitor()
        If internetMonitorStarted Then Return
        internetMonitorStarted = True
        Task.Run(Async Function()
                     While True
                         Try
                             internet = Await CheckInternetConnectionAsync()
                         Catch
                             internet = False
                         End Try
                         Await Task.Delay(If(internet, 5000, 1500))
                     End While
                 End Function)
    End Sub

    Private Async Function CheckInternetConnectionAsync() As Task(Of Boolean)
        Try
            If Not NetworkInformation.NetworkInterface.GetIsNetworkAvailable() Then Return False

            Dim targets As String() = {"8.8.8.8", "1.1.1.1", "208.67.222.222"}
            Try
                Using ping As New NetworkInformation.Ping()
                    For Each target In targets
                        Try
                            Dim reply = Await ping.SendPingAsync(target, 2000)
                            If reply.Status = NetworkInformation.IPStatus.Success Then Return True
                        Catch
                        End Try
                    Next
                End Using
            Catch
                ' Il ping puo' richiedere privilegi su alcuni sistemi: passa al controllo HTTP
            End Try

            Try
                Using client As New HttpClient()
                    client.Timeout = TimeSpan.FromSeconds(4)
                    Dim response = Await client.GetAsync("https://www.google.com/generate_204", HttpCompletionOption.ResponseHeadersRead)
                    Return response.IsSuccessStatusCode
                End Using
            Catch
                Return False
            End Try
        Catch
            Return False
        End Try
    End Function

    Private Async Function GetRemoteModpackCommitIdAsync() As Task(Of String)
        Dim apiUrl As String = $"https://api.github.com/repos/{ModpackOwner}/{ModpackRepo}/commits/{RepoBranch}"
        Dim json As String = Await http.GetStringAsync(apiUrl)
        Dim commitObj As JObject = JObject.Parse(json)
        Return If(commitObj("sha") IsNot Nothing, commitObj("sha").ToObject(Of String)(), "")
    End Function

    Private Function GetSavedModpackCommitId() As String
        Try
            Dim commitFilePath As String = Path.Combine(Paths.DownloadDir, "latest_modpack_commit.txt")
            If Not File.Exists(commitFilePath) Then Return ""
            Return File.ReadAllText(commitFilePath).Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Sub SaveModpackCommitId(commitId As String)
        If String.IsNullOrWhiteSpace(commitId) Then Return
        Try
            Directory.CreateDirectory(Paths.DownloadDir)
            File.WriteAllText(Path.Combine(Paths.DownloadDir, "latest_modpack_commit.txt"), commitId.Trim())
        Catch ex As Exception
            Log($"Impossibile salvare latest commit id locale: {ex.Message}")
        End Try
    End Sub

    Private Async Function GetLatestModpackVersionAsync() As Task(Of String)
        Try
            Dim github As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))
            Dim releases = Await github.Repository.Release.GetAll(ModpackOwner, ModpackRepo)
            If releases.Count > 0 Then Return releases(0).TagName
        Catch
        End Try
        Return ""
    End Function

End Class
