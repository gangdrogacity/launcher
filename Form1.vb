Imports System.IO
Imports System.Security.Policy
Imports Octokit
Imports System.IO.Compression
Imports System.Security.Cryptography
Imports Microsoft.VisualBasic.ApplicationServices
Imports System.Threading
Imports System.Net

Public Class Form1
    Dim minecraftDir As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".gangdrogacity")

    Dim drive As DriveInfo = New DriveInfo(Path.GetPathRoot(minecraftDir))

    Public gameDir As String = Path.Combine(minecraftDir, "game")
    Dim downloadDir As String = Path.Combine(minecraftDir, "downloads")

    Public devmode As Boolean = False
    Public repobranch As String = "main"
    Public data = "https://github.com/jamnaga/wtf-modpack/archive/refs/heads/" & repobranch & ".zip"
    Public repoBasepath As String = "https://raw.githubusercontent.com/jamnaga/wtf-modpack/refs/heads/" & repobranch & "/"

    Dim zipPath As String = Path.Combine(downloadDir, "modpack.zip")

    ' Costanti Fabric

    Dim javaUrl As String = ""

    Dim latestVersion As String

    Dim internet = False
    Dim internetTimer As New System.Windows.Forms.Timer()

    ' Variabili per tracking download
    Public Shared downloadProgress As Integer = 0
    Public Shared downloadTotal As Integer = 0
    Public Shared downloadStartTime As DateTime = DateTime.Now
    Public Shared downloadType As String = "" ' "assets" o "libraries"
    Dim downloadProgressTimer As New System.Windows.Forms.Timer()

    Dim WithEvents client As New Net.WebClient()
    Dim mcDownloader As New MinecraftDownloader()
    Dim mcLauncher As New MinecraftLauncher()
    Public mcTask As Process
    Dim mcToken As CancellationToken
    Dim processMonitorTimer As New System.Windows.Forms.Timer()
    Dim manifest As Newtonsoft.Json.Linq.JObject

    ' Cache per le verifiche hash
    Private fileHashCache As New Dictionary(Of String, String)()

    Dim drag As Boolean = False
    Dim dragOffset As Point



    Private Sub DownloadProgressTimer_Tick(sender As Object, e As EventArgs)
        If downloadType <> "" AndAlso downloadTotal > 0 Then
            Dim percentage = CInt((downloadProgress / downloadTotal) * 100)
            Dim progressBar = New String("█"c, CInt(percentage / 5))
            Dim emptyBar = New String("░"c, 20 - CInt(percentage / 5))

            ' Calcolo ETA
            Dim elapsed = (DateTime.Now - downloadStartTime).TotalSeconds
            Dim etaText As String = ""
            If downloadProgress > 0 And downloadProgress < downloadTotal Then
                Dim remaining = downloadTotal - downloadProgress
                Dim etaSeconds = (elapsed / downloadProgress) * remaining
                If etaSeconds >= 60 Then
                    Dim minutes = CInt(etaSeconds / 60)
                    Dim seconds = CInt(etaSeconds Mod 60)
                    etaText = $"ETA: {minutes}m {seconds}s"
                Else
                    etaText = $"ETA: {CInt(etaSeconds)}s"
                End If
            End If

            Dim typeEmoji = If(downloadType = "assets", "📦", "📚")
            statusText.Text = $"{typeEmoji} [{progressBar}{emptyBar}] {percentage}% ({downloadProgress}/{downloadTotal}) | {etaText}"
        End If
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Close()
    End Sub

    Dim downloading As Boolean = True

    Public Async Sub boot()
        menuPanel.Visible = False
        Panel1.Visible = True

        ProgressBar1.Value = 10
        Dim updater As New UpdateChecker()

        If Not Directory.Exists(downloadDir) Then
            Directory.CreateDirectory(downloadDir)
        End If


        AddLog("Avvio...")
        Await Task.Delay(1000)

        ' Controllo connessione internet iniziale
        AddLog("Verifica connessione internet...")
        Dim connectionCheckCount As Integer = 0
        While Not internet
            errorRed("Non sei connesso a internet. Attendo la rete...")
            Await Task.Delay(2000)
            connectionCheckCount += 1

            ' Forza un check manuale ogni 5 tentativi
            If connectionCheckCount Mod 5 = 0 Then
                internet = Await CheckInternetConnectionAsync()
            End If
        End While

        AddLog("Connessione a internet rilevata.")
        Await Task.Delay(1000)




        ProgressBar1.Value = 20
        Dim versionString As String = updater.getLatestversionString()
        If Await updater.CheckForUpdateAsync() Then
            AddLog("Aggiornamento del launcher alla versione " & versionString)


            Dim link As String = Await updater.GetLatestReleaseUrlAsync()

            ''' ottieni il link dell'asset di nome "GangDrogaCity.7z"


            Dim assetLink As String = Await updater.GetAssetDownloadUrlAsync("GangDrogaCity.7z")



            If assetLink IsNot Nothing Then
                '''scarica il file .7z nella stessa cartella del launcher
                '''

                Dim temp7zPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GangDrogaCity_update.7z")
                Dim tempExtractDir As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_temp")
                Dim tempPath As String = Path.Combine(tempExtractDir, "GangDrogaCity.exe")

                Using client As New Net.WebClient()
                    client.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                    client.CachePolicy = New System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore)
                    AddLog("Download archivio aggiornamento...")
                    Await DownloadFileTaskAsync(client, New Uri(assetLink), temp7zPath, True)
                End Using

                ' Estrai l'archivio 7z
                AddLog("Estrazione aggiornamento...")
                Await Extract7zAsync(temp7zPath, tempExtractDir)
                Dim zrPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7zr.exe")

                ' Verifica che l'eseguibile sia stato estratto
                If Not File.Exists(tempPath) Then
                    MessageBox.Show("Errore durante l'estrazione dell'aggiornamento: file GangDrogaCity.exe non trovato nell'archivio.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If

                ' Crea un file batch inline come risorsa embedded nel processo
                Dim currentExePath As String = Process.GetCurrentProcess().MainModule.FileName
                Dim currentPid As Integer = Process.GetCurrentProcess().Id
                Dim batchPath As String = Path.Combine(Path.GetTempPath(), "update_gdc_" & Guid.NewGuid().ToString("N").Substring(0, 8) & ".cmd")

                ' Crea batch che aspetta, sostituisce e riavvia
                Dim batchContent As String = String.Format(
                    "@echo off" & vbCrLf &
                    "title Aggiornamento GangDrogaCity Launcher" & vbCrLf &
                    "echo Attendo chiusura del launcher..." & vbCrLf &
                    ":WAIT" & vbCrLf &
                    "tasklist /FI ""PID eq {0}"" 2>NUL | find ""{0}"" >NUL" & vbCrLf &
                    "if not errorlevel 1 (" & vbCrLf &
                    "  timeout /t 1 /nobreak >NUL" & vbCrLf &
                    "  goto WAIT" & vbCrLf &
                    ")" & vbCrLf &
                    "echo Aggiornamento in corso..." & vbCrLf &
                    "timeout /t 2 /nobreak >NUL" & vbCrLf &
                    "del /f /q ""{1}""" & vbCrLf &
                    "timeout /t 1 /nobreak >NUL" & vbCrLf &
                    "move /y ""{2}"" ""{1}""" & vbCrLf &
                    "if errorlevel 1 (" & vbCrLf &
                    "  echo Errore durante l'aggiornamento!" & vbCrLf &
                    "  pause" & vbCrLf &
                    "  exit /b 1" & vbCrLf &
                    ")" & vbCrLf &
                    "echo Pulizia file temporanei..." & vbCrLf &
                    "timeout /t 1 /nobreak >NUL" & vbCrLf &
                    "del /f /q ""{4}""" & vbCrLf &
                    "rmdir /s /q ""{5}""" & vbCrLf &
                    "echo Avvio nuova versione..." & vbCrLf &
                    "timeout /t 1 /nobreak >NUL" & vbCrLf &
                    "start """" ""{1}""" & vbCrLf &
                    "timeout /t 2 /nobreak >NUL" & vbCrLf &
                    "del /f /q ""{3}""" & vbCrLf &
                    "del /f /q ""{6}""" & vbCrLf &
                    "exit",
                    currentPid,
                    currentExePath,
                    tempPath,
                    batchPath,
                    temp7zPath,
                    tempExtractDir,
                    zrPath
                )

                System.IO.File.WriteAllText(batchPath, batchContent)

                ' Avvia il batch in modo che continui dopo la chiusura
                Dim startInfo As New ProcessStartInfo() With {
                    .FileName = batchPath,
                    .UseShellExecute = True,
                    .WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    .WindowStyle = ProcessWindowStyle.Normal,
                    .CreateNoWindow = False
                }

                Process.Start(startInfo)

                ' Chiudi il launcher corrente
                AddLog("Riavvio per completare l'aggiornamento...")
                Await Task.Delay(1000)
                End




            Else
                MessageBox.Show("Impossibile recuperare il link per l'aggiornamento. Per favore visita la pagina GitHub del progetto per scaricare l'ultima versione.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If

        End If
        step1(False)




    End Sub

    Private Function ShouldSkipManifestFile(filePath As String) As Boolean
        Return Not String.IsNullOrEmpty(filePath) AndAlso filePath.IndexOf("[server]", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Async Function step1(step1only As Boolean, Optional forceSync As Boolean = False) As Task

        Dim remoteCommitId As String = ""

        If drive.AvailableFreeSpace < 1L * 1024 * 1024 * 1024 Then
            errorRed("Non c'è abbastanza spazio libero sul disco per continuare. Sono necessari almeno 2 GB di spazio libero.")
            Return
        End If
        doNotPowerOffPanel.Visible = True
        AddLog("Recupero manifest GDC...")
        Await Task.Delay(500)

        Try
            ' Pulisci solo il file zip del modpack (il resto viene gestito dai marker)
            System.IO.File.Delete(zipPath)
        Catch
            ' Ignora errori se il file non esiste
        End Try
        If My.Settings.currentBranch = "" Then
            My.Settings.currentBranch = repobranch
            data = "https://github.com/jamnaga/wtf-modpack/archive/refs/heads/" & repobranch & ".zip"
            repoBasepath = "https://raw.githubusercontent.com/jamnaga/wtf-modpack/refs/heads/" & repobranch & "/"
        Else
            repobranch = My.Settings.currentBranch
            data = "https://github.com/jamnaga/wtf-modpack/archive/refs/heads/" & repobranch & ".zip"
            repoBasepath = "https://raw.githubusercontent.com/jamnaga/wtf-modpack/refs/heads/" & repobranch & "/"
        End If
        Dim manifestUrl = repoBasepath & "manifest.json"
        Dim manifestPath = Path.Combine(downloadDir, "manifest.json")

        ' Download del manifest
        Using client As New Net.WebClient()
            client.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")

            client.CachePolicy = New System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore)


            Await DownloadFileTaskAsync(client, New Uri(manifestUrl), manifestPath, False)
        End Using
        Dim json As String = Await System.IO.File.ReadAllTextAsync(manifestPath)
        manifest = Newtonsoft.Json.Linq.JObject.Parse(json)
        Try
            Try
                remoteCommitId = Await GetRemoteModpackCommitIdAsync()
                If Not String.IsNullOrWhiteSpace(remoteCommitId) And forceSync = False Then
                    Dim localCommitId As String = GetSavedModpackCommitId()
                    If String.Equals(localCommitId, remoteCommitId, StringComparison.OrdinalIgnoreCase) Then
                        AddLog("Sync saltato.")
                        If Not step1only Then
                            If CheckJavaStatusAsync() Then
                                Await step2()
                            Else
                                errorRed(" Java Runtime non trovato o non valido.")
                            End If
                        End If
                        Return
                    End If
                End If
            Catch ex As Exception
                AddLog($"Impossibile verificare il commit remoto, continuo con il sync: {ex.Message}")
                Task.Delay(1000).Wait()

            End Try


            AddLog("Analisi file necessari...")

            ' Lettura e parsing del manifest


            Dim allFiles = manifest("files").ToArray()
            Dim files = allFiles.Where(Function(f) Not ShouldSkipManifestFile(f("path").ToObject(Of String)())).ToArray()
            Dim skippedServerFiles As Integer = allFiles.Length - files.Length
            If skippedServerFiles > 0 Then
                AddLog($"File server-only ignorati: {skippedServerFiles}")
            End If
            Dim totalSize As Long = files.Sum(Function(f) f("size").ToObject(Of Long)())

            ' Carica cache esistente
            LoadHashCache()

            ' Verifica parallela super veloce di tutti i file
            Dim filesToDownload As New List(Of Object)
            Dim alreadyDownloadedSize As Long = 0

            AddLog("Verifica file esistenti...")

            ' Partiziona i file per processamento parallelo massimo
            Dim maxConcurrency As Integer = Environment.ProcessorCount * 2
            Dim semaphore As New SemaphoreSlim(maxConcurrency)

            Dim verificationTasks = files.Select(Async Function(content)
                                                     Await semaphore.WaitAsync()
                                                     Try
                                                         Dim fileName As String = content("path").ToObject(Of String)()
                                                         Dim filePath As String = Path.Combine(gameDir, fileName)
                                                         Dim fileSize As Long = content("size").ToObject(Of Long)()
                                                         Dim fileHash As String = content("sha256").ToObject(Of String)()
                                                         Dim isOnce As Boolean = If(content("once") IsNot Nothing, content("once").ToObject(Of Boolean)(), False)
                                                         Dim isValid As Boolean
                                                         If isOnce Then
                                                             If Not System.IO.File.Exists(filePath & ".once") Then
                                                                 isValid = False
                                                             ElseIf Not System.IO.File.Exists(filePath) And System.IO.File.Exists(filePath & ".once") Then
                                                                 System.IO.File.Copy(filePath & ".once", filePath, True)
                                                             End If
                                                         Else
                                                             isValid = Await VerifyFileWithCacheAsync(filePath, fileSize, fileHash)

                                                         End If

                                                         Return New With {
                                                             .Content = content,
                                                             .FilePath = filePath,
                                                             .FileSize = fileSize,
                                                             .FileHash = fileHash,
                                                             .FileName = fileName,
                                                             .IsValid = isValid,
                                                             .IsOnce = isOnce
                                                         }
                                                     Finally
                                                         semaphore.Release()
                                                     End Try
                                                 End Function).ToArray()

            Dim verificationResults = Await Task.WhenAll(verificationTasks)

            ' Separa file validi da quelli da scaricare
            For Each result In verificationResults
                If result.IsValid Then
                    alreadyDownloadedSize += result.FileSize
                Else
                    filesToDownload.Add(result)
                End If
            Next

            AddLog($" File da scaricare: {filesToDownload.Count}/{files.Length}")
            Await Task.Delay(150)



            ' Download parallelo ultra veloce
            Dim downloadConcurrency As Integer = Math.Min(6, filesToDownload.Count)
            Dim downloadSemaphore As New SemaphoreSlim(downloadConcurrency)
            Dim downloadedSize As Long = alreadyDownloadedSize
            Dim lastProgressUpdate As DateTime = DateTime.Now

            'AddLog($" Avvio download parallelo ({downloadConcurrency} connessioni)...")
            Await Task.Delay(50)
            Dim startTime As DateTime = DateTime.Now

            Dim downloadTasks = filesToDownload.Select(Async Function(fileInfo)
                                                           Await downloadSemaphore.WaitAsync()
                                                           Try
                                                               Dim dir As String = Path.GetDirectoryName(fileInfo.FilePath)
                                                               If Not Directory.Exists(dir) Then
                                                                   Directory.CreateDirectory(dir)
                                                               End If

                                                               Dim fileUrl As String = repoBasepath & fileInfo.FileName
                                                               If fileInfo.IsOnce Then
                                                                   fileInfo.FilePath = fileInfo.FilePath & ".once"
                                                               End If
                                                               Using downloadClient As New Net.WebClient()
                                                                   Await DownloadFileTaskAsync(downloadClient, New Uri(fileUrl), fileInfo.FilePath, False)
                                                               End Using

                                                               UpdateHashCacheEntry(fileInfo.FilePath, fileInfo.FileSize, fileInfo.FileHash)
                                                               Interlocked.Add(downloadedSize, fileInfo.FileSize)

                                                               If DateTime.Now.Subtract(lastProgressUpdate).TotalMilliseconds > 2000 Then
                                                                   lastProgressUpdate = DateTime.Now
                                                                   Invoke(Sub()
                                                                              Dim progressValue As Integer = 25 + CInt((downloadedSize / totalSize) * 20)
                                                                              ProgressBar1.Value = Math.Min(progressValue, 45)
                                                                              Dim percentage = Math.Round((downloadedSize / totalSize) * 100, 1)
                                                                              'AddLog($" Download: {percentage}% completato")
                                                                              ''' se volessi aggiungere il tempo rimanente
                                                                              ''' 

                                                                              Dim elapsed As TimeSpan = DateTime.Now - startTime
                                                                              Dim estimatedTotalTime As TimeSpan = TimeSpan.FromTicks(elapsed.Ticks * totalSize / downloadedSize)
                                                                              Dim remainingTime As TimeSpan = estimatedTotalTime - elapsed
                                                                              AddLog($" Download: {percentage}% completato, ETA: ~{remainingTime.ToString("hh\:mm\:ss")}")
                                                                          End Sub)
                                                               End If

                                                           Finally
                                                               If fileInfo.IsOnce And Not System.IO.File.Exists(fileInfo.FilePath.ToString().Replace(".once", "")) Then
                                                                   Try
                                                                       System.IO.File.Copy(fileInfo.FilePath, fileInfo.FilePath.ToString().Replace(".once", ""), True)
                                                                   Catch
                                                                       ' Ignora errori di copia
                                                                   End Try
                                                               End If
                                                               downloadSemaphore.Release()
                                                           End Try
                                                       End Function).ToArray()

            Await Task.WhenAll(downloadTasks)
            SaveHashCache()

            ProgressBar1.Value = 45
            AddLog("Download completato!")
            Await RemoveAllNonManifestFiles(True)
            Await ProcessManifestPackagesAsync(manifest)

            If Not String.IsNullOrWhiteSpace(remoteCommitId) Then
                SaveModpackCommitId(remoteCommitId)
            End If

            ' Procedi al passo successivo
            If Not step1only Then
                If CheckJavaStatusAsync() Then



                    Await step2()

                    Else
                        errorRed(" Java Runtime non trovato o non valido.")
                End If

            End If


        Catch ex As Exception
            errorRed($" Errore durante step1: {ex.Message}")
            MessageBox.Show($"Si è verificato un errore: {ex.Message}.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            ProgressBar1.Value = 0
        End Try
    End Function

    Private Async Function RemoveAllNonManifestFiles(Optional excludeMinecraft As Boolean = False) As Task
        AddLog("Pulizia file obsoleti...")
        Dim target = gameDir
        If Not Directory.Exists(target) Then
            MessageBox.Show("La directory di gioco non esiste per la pulizia.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        Dim manifestPath = Path.Combine(downloadDir, "manifest.json")
        If Not System.IO.File.Exists(manifestPath) Then
            MessageBox.Show("Il manifest non esiste per la pulizia.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Try
            Dim json As String = Await System.IO.File.ReadAllTextAsync(manifestPath)
            Dim manifest As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(json)
            Dim files = manifest("files").ToArray()

            ' Crea HashSet con percorsi normalizzati (case-insensitive su Windows)
            Dim validFiles As New HashSet(Of String)(
                files.Select(Function(f) Path.GetFullPath(Path.Combine(target, f("path").ToObject(Of String)()))),
                StringComparer.OrdinalIgnoreCase
            )
            ''escludi i .once

            For Each file In files
                Dim filePath As String = Path.GetFullPath(Path.Combine(target, file("path").ToObject(Of String)()))
                If file("once") IsNot Nothing AndAlso file("once").ToObject(Of Boolean)() Then
                    validFiles.Add(filePath & ".once")
                End If
            Next

            ' Aggiunge i file estratti dai package per evitare che vengano rimossi dalla pulizia
            If manifest("packages") IsNot Nothing Then
                For Each pkg In manifest("packages")
                    Dim extractTo As String = ""
                    If pkg("extractTo") IsNot Nothing Then
                        extractTo = pkg("extractTo").ToObject(Of String)()
                    End If

                    If pkg("filesToExtract") IsNot Nothing Then
                        For Each extractEntry In pkg("filesToExtract")
                            Dim relPath As String = extractEntry.ToObject(Of String)()
                            Dim extractedPath As String = Path.GetFullPath(Path.Combine(target, extractTo, relPath))
                            validFiles.Add(extractedPath)
                        Next
                    End If
                Next
            End If

            '''escludi tutti i file nel .gitignore
            '''

            Dim gitignorePath As String = Path.Combine(downloadDir, ".gitignore")
            If System.IO.File.Exists(gitignorePath) Then
                Dim gitignoreLines = System.IO.File.ReadAllLines(gitignorePath)
                For Each line In gitignoreLines
                    Dim trimmedLine = line.Trim()
                    If Not String.IsNullOrEmpty(trimmedLine) AndAlso Not trimmedLine.StartsWith("#") Then
                        Dim ignorePath = Path.GetFullPath(Path.Combine(target, trimmedLine))
                        If Directory.Exists(ignorePath) Then
                            Dim allIgnoreFiles = Directory.GetFiles(ignorePath, "*", SearchOption.AllDirectories)
                            For Each file In allIgnoreFiles
                                Dim fullPath = Path.GetFullPath(file)
                                validFiles.Add(fullPath)
                            Next
                        ElseIf File.Exists(ignorePath) Then
                            validFiles.Add(ignorePath)
                        End If
                    End If
                Next
            End If

            If excludeMinecraft Then

                ''aggiunggi assets, versions, libraries e mods/mcef-libraries

                Dim excludeListFolder As String() = {
                    Path.Combine(gameDir, "assets"),
                    Path.Combine(gameDir, "versions"),
                    Path.Combine(gameDir, "libraries"),
                    Path.Combine(gameDir, "config"),
                    Path.Combine(gameDir, "mods", "mcef-libraries")
                }

                Dim excludeListFiles As String() = {
                    Path.Combine(gameDir, "version.txt"),
                    Path.Combine(gameDir, "fabricInstalled")
                    }

                For Each excludePath In excludeListFolder
                    If Directory.Exists(excludePath) Then
                        Dim allExcludeFiles = Directory.GetFiles(excludePath, "*", SearchOption.AllDirectories)
                        For Each file In allExcludeFiles
                            Dim fullPath = Path.GetFullPath(file)
                            validFiles.Add(fullPath)
                        Next
                    End If
                Next

                For Each excludePath In excludeListFiles
                    If File.Exists(excludePath) Then
                        validFiles.Add(excludePath)
                    End If
                Next

            End If

            ' Ottieni tutti i file nella gameDir
            Dim allFiles = Directory.GetFiles(target, "*", SearchOption.AllDirectories)

            ' Rimuovi file non mappati dal manifest
            For Each file In allFiles
                Dim fullPath = Path.GetFullPath(file)
                If Not validFiles.Contains(fullPath) Then
                    Try
                        System.IO.File.Delete(file)
                        SafeInvoke(Sub() AddLog($"Rimosso:{file.Replace(target & "\", "")}"))
                        'Await Task.Delay(10)
                    Catch ex As Exception
                        SafeInvoke(Sub() AddLog($" Errore rimozione {Path.GetFileName(file)}: {ex.Message}"))
                    End Try
                End If
            Next

            ' Rimuovi directory vuote (dalla più profonda alla radice)
            Dim allDirs = Directory.GetDirectories(target, "*", SearchOption.AllDirectories).OrderByDescending(Function(d) d.Length)
            For Each dirPath In allDirs
                Try
                    If Directory.Exists(dirPath) AndAlso Not Directory.EnumerateFileSystemEntries(dirPath).Any() Then
                        Directory.Delete(dirPath)
                        SafeInvoke(Sub() AddLog($" Rimossa directory vuota: {dirPath.Replace(target & "\", "")}"))
                    End If
                Catch ex As Exception
                    ' Ignora errori di cancellazione directory
                End Try
            Next

        Catch ex As Exception
            SafeInvoke(Sub() AddLog($" Errore durante pulizia file: {ex.Message}"))
        End Try
    End Function

    Private Async Function ProcessManifestPackagesAsync(manifest As Newtonsoft.Json.Linq.JObject) As Task
        If manifest Is Nothing OrElse manifest("packages") Is Nothing Then
            Return
        End If

        Dim packages = manifest("packages").ToArray()
        If packages.Length = 0 Then
            Return
        End If

        AddLog($"Verifica pacchetti opzionali/extra: {packages.Length}")

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
            If packageVersions.ContainsKey(packageKey) Then
                installedVersion = packageVersions(packageKey)
            End If
            Dim hasVersionChange As Boolean = (Not String.IsNullOrWhiteSpace(packageVersion)) AndAlso Not packageVersion.Equals(installedVersion, StringComparison.OrdinalIgnoreCase)
            Dim effectiveOverwrite As Boolean = overwrite OrElse hasVersionChange

            If hasVersionChange Then
                If String.IsNullOrWhiteSpace(installedVersion) Then
                    AddLog($"Package {packageName}: installazione versione {packageVersion}")
                Else
                    AddLog($"Package {packageName}: aggiornamento {installedVersion} -> {packageVersion}")
                End If
            End If

            Try
                If Not packageAction.Equals("extract", StringComparison.OrdinalIgnoreCase) Then
                    AddLog($"Package non gestito ({packageAction}): {packageName}")
                    Continue For
                End If

                Dim filesToExtract As New List(Of String)()
                If pkg("filesToExtract") IsNot Nothing Then
                    For Each entry In pkg("filesToExtract")
                        filesToExtract.Add(entry.ToObject(Of String)())
                    Next
                End If

                Dim targetDir As String = Path.Combine(gameDir, extractTo.Replace("/", "\\"))
                If String.IsNullOrWhiteSpace(targetDir) Then
                    targetDir = gameDir
                End If

                Dim progressTemplate As String = If(pkg("progressMessage") IsNot Nothing, pkg("progressMessage").ToObject(Of String)(), "")

                ' Se il package è già presente e non richiede sovrascrittura, salta
                If Not effectiveOverwrite AndAlso filesToExtract.Count > 0 Then
                    Dim allExtracted As Boolean = filesToExtract.All(Function(rel) File.Exists(Path.Combine(targetDir, rel.Replace("/", "\\"))))
                    If allExtracted Then
                        AddLog($"✓ Package già pronto: {packageName}")
                        If Not String.IsNullOrWhiteSpace(packageVersion) Then
                            If Not packageVersions.ContainsKey(packageKey) OrElse Not packageVersions(packageKey).Equals(packageVersion, StringComparison.OrdinalIgnoreCase) Then
                                packageVersions(packageKey) = packageVersion
                                packageVersionsDirty = True
                            End If
                        End If
                        Continue For
                    End If
                End If

                Dim archiveStartPath As String = Nothing

                Dim packageFiles As Newtonsoft.Json.Linq.JArray = Nothing
                If pkg("files") IsNot Nothing Then
                    packageFiles = pkg("files").ToObject(Of Newtonsoft.Json.Linq.JArray)()
                End If

                ' Gestione parti package + progressMessage con placeholder:
                ' *currentPart*, *totalParts*, *percentage*
                If pkg("parts") IsNot Nothing AndAlso packageFiles IsNot Nothing Then
                    Dim partEntries = pkg("parts").ToArray()
                    Dim totalParts As Integer = partEntries.Length
                    Dim currentPart As Integer = 0

                    For Each partEntry In partEntries
                        currentPart += 1
                        Dim partName As String = partEntry.ToObject(Of String)()
                        Dim matchingFile As Newtonsoft.Json.Linq.JToken = packageFiles.FirstOrDefault(Function(pf)
                                                                                                          Dim relPath As String = If(pf("path") IsNot Nothing, pf("path").ToObject(Of String)(), "")
                                                                                                          Return Path.GetFileName(relPath).Equals(partName, StringComparison.OrdinalIgnoreCase)
                                                                                                      End Function)

                        If matchingFile Is Nothing Then
                            Dim missingPartMessage As String = $"Parte non mappata in files: {partName} ({packageName})"
                            If isRequired Then
                                Throw New Exception(missingPartMessage)
                            End If
                            AddLog($"! {missingPartMessage}")
                            Continue For
                        End If

                        Dim relPartPath As String = matchingFile("path").ToObject(Of String)()
                        Dim localPartPath As String = Path.Combine(gameDir, relPartPath.Replace("/", "\\"))
                        Dim expectedSize As Long = If(matchingFile("size") IsNot Nothing, matchingFile("size").ToObject(Of Long)(), 0)
                        Dim expectedHash As String = If(matchingFile("sha256") IsNot Nothing, matchingFile("sha256").ToObject(Of String)(), "")

                        Dim partReady As Boolean = Await VerifyFileWithCacheAsync(localPartPath, expectedSize, expectedHash)
                        Dim percentage As Integer = CInt(Math.Round((currentPart * 100.0) / Math.Max(1, totalParts)))
                        Dim progressText As String = If(String.IsNullOrWhiteSpace(progressTemplate),
                                                        $"Package {packageName}: parte {currentPart}/{totalParts} ({percentage}%)",
                                                        progressTemplate.Replace("*currentPart*", currentPart.ToString()).Replace("*totalParts*", totalParts.ToString()).Replace("*percentage*", percentage.ToString()))

                        If Not partReady Then
                            Dim partUrl As String = repoBasepath & relPartPath
                            Dim localPartDir As String = Path.GetDirectoryName(localPartPath)
                            If Not Directory.Exists(localPartDir) Then
                                Directory.CreateDirectory(localPartDir)
                            End If

                            AddLog(progressText)
                            Await DownloadPackagePartWithRetryAsync(partUrl, localPartPath, progressText)

                            UpdateHashCacheEntry(localPartPath, expectedSize, expectedHash)
                        Else
                            AddLog(progressText & " (ok)")
                        End If

                        If archiveStartPath Is Nothing Then
                            archiveStartPath = localPartPath
                        End If
                    Next
                ElseIf packageFiles IsNot Nothing Then
                    For Each pkgFile In packageFiles
                        If pkgFile("path") IsNot Nothing Then
                            Dim candidate As String = Path.Combine(gameDir, pkgFile("path").ToObject(Of String)().Replace("/", "\\"))
                            If File.Exists(candidate) Then
                                archiveStartPath = candidate
                                Exit For
                            End If
                        End If
                    Next
                End If

                If String.IsNullOrEmpty(archiveStartPath) Then
                    Dim msg As String = $"File archive non trovato per package: {packageName}"
                    If isRequired Then
                        Throw New Exception(msg)
                    End If
                    AddLog($"! {msg}")
                    Continue For
                End If

                AddLog($"Estrazione package: {packageName}")

                Dim tempRoot As String = Path.Combine(downloadDir, "packages_tmp")
                Dim safeName As String = String.Concat(packageName.Select(Function(ch) If(Path.GetInvalidFileNameChars().Contains(ch), "_"c, ch)))
                Dim tempExtract As String = Path.Combine(tempRoot, safeName)

                If Directory.Exists(tempExtract) Then
                    Directory.Delete(tempExtract, True)
                End If
                Directory.CreateDirectory(tempExtract)

                Await Extract7zAsync(archiveStartPath, tempExtract)

                If Not Directory.Exists(targetDir) Then
                    Directory.CreateDirectory(targetDir)
                End If

                If filesToExtract.Count > 0 Then
                    For Each rel In filesToExtract
                        Dim relNormalized As String = rel.Replace("/", "\\")
                        Dim sourcePath As String = Path.Combine(tempExtract, relNormalized)
                        Dim destinationPath As String = Path.Combine(targetDir, relNormalized)

                        If Not File.Exists(sourcePath) Then
                            If isRequired Then
                                Throw New Exception($"File estratto mancante: {rel} ({packageName})")
                            End If
                            AddLog($"! File package non trovato: {rel}")
                            Continue For
                        End If

                        Dim destinationDir As String = Path.GetDirectoryName(destinationPath)
                        If Not Directory.Exists(destinationDir) Then
                            Directory.CreateDirectory(destinationDir)
                        End If

                        If File.Exists(destinationPath) AndAlso Not effectiveOverwrite Then
                            Continue For
                        End If

                        File.Copy(sourcePath, destinationPath, True)
                    Next
                Else
                    ' Se filesToExtract non è specificato, copia tutto il contenuto estratto
                    For Each extractedFile In Directory.GetFiles(tempExtract, "*", SearchOption.AllDirectories)
                        Dim relativePath As String = extractedFile.Substring(tempExtract.Length).TrimStart("\"c)
                        Dim destinationPath As String = Path.Combine(targetDir, relativePath)
                        Dim destinationDir As String = Path.GetDirectoryName(destinationPath)

                        If Not Directory.Exists(destinationDir) Then
                            Directory.CreateDirectory(destinationDir)
                        End If

                        If File.Exists(destinationPath) AndAlso Not effectiveOverwrite Then
                            Continue For
                        End If

                        File.Copy(extractedFile, destinationPath, True)
                    Next
                End If

                Try
                    Directory.Delete(tempExtract, True)
                Catch
                End Try

                If Not String.IsNullOrWhiteSpace(packageVersion) Then
                    If Not packageVersions.ContainsKey(packageKey) OrElse Not packageVersions(packageKey).Equals(packageVersion, StringComparison.OrdinalIgnoreCase) Then
                        packageVersions(packageKey) = packageVersion
                        packageVersionsDirty = True
                    End If
                End If

                AddLog($"✓ Package estratto: {packageName}")

            Catch ex As Exception
                If isRequired Then
                    Throw
                End If
                AddLog($"Errore package non obbligatorio ({packageName}): {ex.Message}")
            End Try
        Next

        If packageVersionsDirty Then
            Await SavePackageVersionsAsync(packageVersions)
        End If
    End Function

    Private Function GetPackageVersionsFilePath() As String
        Return Path.Combine(downloadDir, "package_versions.json")
    End Function

    Private Async Function LoadPackageVersionsAsync() As Task(Of Dictionary(Of String, String))
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        Try
            Dim filePath = GetPackageVersionsFilePath()
            If Not File.Exists(filePath) Then
                Return result
            End If

            Dim json = Await File.ReadAllTextAsync(filePath)
            If String.IsNullOrWhiteSpace(json) Then
                Return result
            End If

            Dim parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(json)
            If parsed IsNot Nothing Then
                For Each kvp In parsed
                    result(kvp.Key) = kvp.Value
                Next
            End If
        Catch ex As Exception
            AddLog($"Impossibile leggere versioni package: {ex.Message}")
        End Try

        Return result
    End Function

    Private Async Function SavePackageVersionsAsync(versions As Dictionary(Of String, String)) As Task
        Try
            If versions Is Nothing Then
                Return
            End If

            If Not Directory.Exists(downloadDir) Then
                Directory.CreateDirectory(downloadDir)
            End If

            Dim filePath = GetPackageVersionsFilePath()
            Dim json = Newtonsoft.Json.JsonConvert.SerializeObject(versions, Newtonsoft.Json.Formatting.Indented)
            Await File.WriteAllTextAsync(filePath, json)
        Catch ex As Exception
            AddLog($"Impossibile salvare versioni package: {ex.Message}")
        End Try
    End Function

    ' Step2: Installazione Fabric via Meta API
    Private Async Function step2() As Task
        Dim fabricInstalledMarker As String = Path.Combine(gameDir, "fabricInstalled")
        Dim fabricInst As New FabricInstaller()

        ' Migrazione da Forge: se l'utente aveva Forge, pulisci prima
        If fabricInst.IsForgeInstalled(gameDir) Then
            AddLog("Migrazione da Forge a Fabric in corso...")
            fabricInst.CleanupForgeInstallation(gameDir)
            ' Rimuovi anche i vecchi marker in downloadDir
            Try
                Dim oldForgePath As String = Path.Combine(downloadDir, "forge-installer.jar")
                If File.Exists(oldForgePath) Then File.Delete(oldForgePath)
            Catch
            End Try
            AddLog("Migrazione completata. Installazione Fabric...")
        End If

        ' Verifica se Fabric è già installato
        If File.Exists(fabricInstalledMarker) AndAlso fabricInst.IsFabricInstalled(My.Settings.fabricLoaderVersion, My.Settings.mcVersion, gameDir) Then
            If My.Settings.fabricLoaderVersion = manifest("fabricLoaderVersion").ToObject(Of String)() Or My.Settings.mcVersion = manifest("mcVersion").ToObject(Of String)() Then
                AddLog($"✓ Fabric Loader {My.Settings.fabricLoaderVersion} già installato")
                ProgressBar1.Value = 65
                Await step3()
                Return
            End If

        End If

        Try
            My.Settings.fabricLoaderVersion = manifest("fabricLoaderVersion").ToObject(Of String)()
            My.Settings.mcVersion = manifest("mcVersion").ToObject(Of String)()
            My.Settings.fabricVersionId = "fabric-loader-" & My.Settings.fabricLoaderVersion & "-" & My.Settings.mcVersion

            ''rimuovi marker

            My.Computer.FileSystem.DeleteFile(Path.Combine(gameDir, "fabricInstalled"))


            AddLog("Installazione Fabric Loader...")
            ProgressBar1.Value = 45
            Await Task.Delay(200)

            ' Installa Fabric tramite Meta API (scarica profilo JSON + librerie)
            Dim success As Boolean = Await fabricInst.InstallFabric(My.Settings.fabricLoaderVersion, My.Settings.mcVersion, gameDir)

            If success Then

                ProgressBar1.Value = 65
                AddLog("Fabric Loader installato!")
                ' Non serve più scrivere il marker qui, lo scriviamo dopo step3
                Await step3()
            Else
                errorRed("Errore durante l'installazione di Fabric Loader.")
                MessageBox.Show("Si è verificato un errore durante l'installazione di Fabric. Riprova.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                ProgressBar1.Value = 0
            End If

        Catch ex As Exception
            errorRed($" Errore durante installazione Fabric: {ex.Message}")
            MessageBox.Show($"Si è verificato un errore durante l'installazione di Fabric: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            ProgressBar1.Value = 0
        End Try
    End Function

    Private Async Function step3() As Task

        AddLog("Installazione di GangDrogaCity " + latestVersion + "...")
        ProgressBar1.Value = 65
        Await Task.Delay(500)

        ProgressBar1.Value = 75

        Dim fabricInstalledMarker As String = Path.Combine(gameDir, "fabricInstalled")

        If Not File.Exists(fabricInstalledMarker) Then
            Try
                ' Download Minecraft vanilla
                AddLog("Download Minecraft...")
                Await Task.Delay(500)
                Try
                    Await mcDownloader.DownloadMinecraftVersion(My.Settings.mcVersion, gameDir)
                Catch ex As Exception
                    ' Fallback se il metodo non è async
                    Task.Run(Sub() mcDownloader.DownloadMinecraftVersion(My.Settings.mcVersion, gameDir))
                End Try

                ProgressBar1.Value = 85

                ' Verifica che Java sia disponibile
                Dim javaPath As String = JavaHelper.FindJavaPath().Result
                AddLog($" Usando Java: {javaPath}")

                If Not JavaHelper.IsJavaValid(javaPath) Then
                    Throw New Exception("Java non valido o non funzionante")
                End If

                ' Scrivi marker e version.txt
                System.IO.File.WriteAllText(Path.Combine(gameDir, "version.txt"), latestVersion)
                System.IO.File.WriteAllText(fabricInstalledMarker, "True")
                ProgressBar1.Value = 100
                AddLog("Installazione Fabric completata.")

            Catch ex As Exception
                errorRed($" Errore durante installazione: {ex.Message}")
                MessageBox.Show($"Si è verificato un errore durante l'installazione: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                ProgressBar1.Value = 0
            End Try
        End If

        step4()
    End Function

    ' Gestione fallimento installazione Fabric
    Private Async Function HandleFabricInstallationFailure() As Task
        Try
            Dim result As DialogResult = DialogResult.No
            If InvokeRequired Then
                Invoke(Sub()
                           result = MessageBox.Show("Si è verificato un errore durante l'installazione di Fabric. Scegli Si per riprovare usando la safe-mode, questo reinstallerà Minecraft e ritenterà l'installazione.", "Errore", MessageBoxButtons.YesNo, MessageBoxIcon.Error)
                       End Sub)
            Else
                result = MessageBox.Show("Si è verificato un errore durante l'installazione di Fabric. Scegli Si per riprovare usando la safe-mode, questo reinstallerà Minecraft e ritenterà l'installazione.", "Errore", MessageBoxButtons.YesNo, MessageBoxIcon.Error)
            End If

            If result = DialogResult.Yes Then
                SafeInvoke(Sub()
                               AddLog("Avvio modalità safe-mode...")
                               ProgressBar1.Value = 60
                           End Sub)

                ' Pulisci file non validi
                Await RemoveAllNonManifestFiles()

                SafeInvoke(Sub()
                               AddLog("Reinstallazione Minecraft...")
                               ProgressBar1.Value = 65
                           End Sub)

                ' Rimuovi marker Fabric per forzare reinstallazione
                Try
                    Dim fabricMarker As String = Path.Combine(gameDir, "fabricInstalled")
                    If File.Exists(fabricMarker) Then File.Delete(fabricMarker)
                Catch
                End Try

                ' Riavvia da step2
                Await step2()

            Else
                SafeInvoke(Sub()
                               AddLog("Installazione Fabric fallita.")
                               ProgressBar1.Value = 0
                           End Sub)
            End If

        Catch ex As Exception
            SafeInvoke(Sub()
                           MessageBox.Show($"Si è verificato un errore durante la reinstallazione: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                           ProgressBar1.Value = 0
                       End Sub)
        End Try
    End Function

    Private Async Function step4() As Task
        AddLog("Finalizzazione...")

        ' Verifica integrità JAR (mod e librerie)
        Await VerifyAndFixCorruptedJars()

        ' CRITICO: Verifica e scarica TUTTI i componenti di Minecraft vanilla
        ' Questo include: client.jar, assets (audio + lingue), librerie vanilla
        AddLog("Verifica completezza Minecraft vanilla...")
        Await mcDownloader.DownloadMinecraftVersion(My.Settings.mcVersion, gameDir)

        ' Scarica le librerie specifiche di Fabric (fabric-loader, intermediary, ecc.)
        AddLog("Verifica librerie Fabric...")
        Await mcDownloader.DownloadVersionDependencies(My.Settings.fabricVersionId, gameDir)

        doNotPowerOffPanel.Visible = False
        Panel1.Visible = False
        menuPanel.Visible = True
        playBtn.Text = "PLAY"
        playBtn.Enabled = True
    End Function

    ' Verifica file con cache intelligente
    Private Async Function VerifyFileWithCacheAsync(filePath As String, expectedSize As Long, expectedHash As String) As Task(Of Boolean)
        If Not System.IO.File.Exists(filePath) Then Return False

        Dim fileInfo As New FileInfo(filePath)
        If fileInfo.Length <> expectedSize Then Return False

        Dim cacheKey As String = $"{filePath}_{expectedSize}"

        If fileHashCache.ContainsKey(cacheKey) Then
            Dim cachedHash = fileHashCache(cacheKey)
            If cachedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
        End If

        Dim actualHash = Await CalculateHashFastAsync(filePath)
        fileHashCache(cacheKey) = actualHash

        Return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Async Function CalculateHashFastAsync(filePath As String) As Task(Of String)
        Return Await Task.Run(Function() As String
                                  Try
                                      Const bufferSize As Integer = 4 * 1024 * 1024
                                      Using stream As New FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan)
                                          Using sha256 As SHA256 = SHA256.Create()
                                              Dim hashBytes As Byte() = sha256.ComputeHash(stream)
                                              Return BitConverter.ToString(hashBytes).Replace("-", "").ToLower()
                                          End Using
                                      End Using
                                  Catch
                                      Return String.Empty
                                  End Try
                              End Function)
    End Function

    Private Sub LoadHashCache()
        Try
            Dim cachePath As String = Path.Combine(minecraftDir, "hash_cache.json")
            If System.IO.File.Exists(cachePath) Then
                Dim cacheJson = System.IO.File.ReadAllText(cachePath)
                Dim cache = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(cacheJson)
                If cache IsNot Nothing Then
                    fileHashCache = cache
                End If
            End If
        Catch
            fileHashCache = New Dictionary(Of String, String)()
        End Try
    End Sub

    Private Sub SaveHashCache()
        Try
            Dim cachePath As String = Path.Combine(minecraftDir, "hash_cache.json")
            Dim cacheJson = Newtonsoft.Json.JsonConvert.SerializeObject(fileHashCache, Newtonsoft.Json.Formatting.None)
            System.IO.File.WriteAllText(cachePath, cacheJson)
        Catch
        End Try
    End Sub

    Private Sub UpdateHashCacheEntry(filePath As String, fileSize As Long, fileHash As String)
        Dim cacheKey As String = $"{filePath}_{fileSize}"
        fileHashCache(cacheKey) = fileHash
    End Sub

    Private Async Function DownloadPackagePartWithRetryAsync(url As String, destinationPath As String, progressText As String, Optional maxRetries As Integer = 3) As Task
        Dim lastEx As Exception = Nothing

        For attempt As Integer = 1 To maxRetries
            Dim shouldDelay As Boolean = False
            Try
                Using packageClient As New Net.WebClient()
                    packageClient.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                    packageClient.CachePolicy = New System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore)

                    Await DownloadFileTaskAsync(packageClient, New Uri(url), destinationPath, False)
                    Return
                End Using
            Catch ex As Exception
                lastEx = ex

                ' Rimuove eventuale file parziale dopo errore di download
                Try
                    If File.Exists(destinationPath) Then
                        File.Delete(destinationPath)
                    End If
                Catch
                End Try

                If attempt <maxRetries Then
                    AddLog($"{progressText} | retry {attempt + 1}/{maxRetries}")
                    shouldDelay = True
                End If
            End Try

            If shouldDelay Then
                Await Task.Delay(1000 * attempt)
            End If
        Next

        Throw New Exception($"Download parte package fallito dopo {maxRetries} tentativi: {url}", lastEx)
    End Function

    Private Async Function DownloadSevenZipRuntimeWithRetryAsync(url As String, destinationPath As String, Optional maxRetries As Integer = 3) As Task
        Dim lastEx As Exception = Nothing

        For attempt As Integer = 1 To maxRetries
            Dim shouldDelay As Boolean = False
            Try
                Using runtimeClient As New Net.WebClient()
                    runtimeClient.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                    runtimeClient.CachePolicy = New System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore)

                    Await DownloadFileTaskAsync(runtimeClient, New Uri(url), destinationPath, False)
                    Return
                End Using
            Catch ex As Exception
                lastEx = ex

                Try
                    If File.Exists(destinationPath) Then
                        File.Delete(destinationPath)
                    End If
                Catch
                End Try

                If attempt < maxRetries Then
                    AddLog($"Download 7z Runtime fallito, retry {attempt + 1}/{maxRetries}...")
                    shouldDelay = True
                End If
            End Try

            If shouldDelay Then
                Await Task.Delay(1000 * attempt)
            End If
        Next

        Throw New Exception($"Download 7z Runtime fallito dopo {maxRetries} tentativi: {url}", lastEx)
    End Function

    Public Function DownloadFileTaskAsync(client As Net.WebClient, uri As Uri, fileName As String, Optional showProgress As Boolean = True) As Task
        Dim tcs As New TaskCompletionSource(Of Boolean)()

        AddHandler client.DownloadFileCompleted, Sub(sender, e)
                                                     If e.Error IsNot Nothing Then
                                                         MessageBox.Show($"Si è verificato un errore durante il download di {uri}: {e.Error.Message}", "Errore download", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                                         tcs.SetException(e.Error)
                                                     ElseIf e.Cancelled Then
                                                         tcs.SetCanceled()
                                                     Else
                                                         tcs.SetResult(True)
                                                     End If
                                                 End Sub

        If showProgress Then
            AddHandler client.DownloadProgressChanged, Sub(sender, e)
                                                           If e.TotalBytesToReceive > 0 Then
                                                               SafeInvoke(Sub() AddLog($" Download: {e.ProgressPercentage}% ({e.BytesReceived \ 1024} KB di {e.TotalBytesToReceive \ 1024} KB)"))
                                                           End If
                                                       End Sub
        End If

        client.DownloadFileAsync(uri, fileName)
        Return tcs.Task
    End Function

    Private Async Function ExtractZipWithProgressAsync(zipFilePath As String, extractPath As String) As Task
        Await Task.Run(Sub()
                           Using archive As ZipArchive = ZipFile.OpenRead(zipFilePath)
                               Dim totalEntries As Integer = archive.Entries.Count
                               Dim extractedEntries As Integer = 0
                               Dim baseProgress As Integer = 70

                               For Each entry As ZipArchiveEntry In archive.Entries
                                   If String.IsNullOrEmpty(entry.Name) Then
                                       Continue For
                                   End If

                                   Dim destinationPath As String = Path.Combine(extractPath, entry.FullName)
                                   Dim destinationDir As String = Path.GetDirectoryName(destinationPath)

                                   If Not Directory.Exists(destinationDir) Then
                                       Directory.CreateDirectory(destinationDir)
                                   End If

                                   entry.ExtractToFile(destinationPath, True)
                                   extractedEntries += 1

                                   Dim progressPercentage As Integer = (extractedEntries * 100) \ totalEntries

                                   Invoke(Sub()
                                              ProgressBar1.Value = baseProgress + (progressPercentage * 5) \ 100
                                              AddLog($" Estrazione: {progressPercentage}% ({extractedEntries}/{totalEntries} file)")
                                          End Sub)
                               Next
                           End Using
                       End Sub)
    End Function

    ''' <summary>
    ''' Estrae un archivio 7z usando 7-Zip
    ''' </summary>
    Private Async Function Extract7zAsync(archivePath As String, extractPath As String) As Task
        Try
            ' Crea la directory di destinazione se non esiste
            If Not Directory.Exists(extractPath) Then
                Directory.CreateDirectory(extractPath)
            End If

            ' Recupera un runtime 7z valido (con fallback + validazione)
            Dim sevenZipPath As String = Await EnsureSevenZipAsync()

            If String.IsNullOrEmpty(sevenZipPath) OrElse Not File.Exists(sevenZipPath) Then
                Throw New Exception("Impossibile scaricare o trovare 7zr.exe.")
            End If

            ' Comando: 7z x "archivio.7z" -o"destinazione" -y
            Await Task.Run(Sub()
                               Dim startInfo As New ProcessStartInfo() With {
                                   .FileName = sevenZipPath,
                                   .Arguments = $"x ""{archivePath}"" -o""{extractPath}"" -y",
                                   .UseShellExecute = False,
                                   .CreateNoWindow = True,
                                   .RedirectStandardOutput = True,
                                   .RedirectStandardError = True
                               }

                               Using process As Process = Process.Start(startInfo)
                                   process.WaitForExit()

                                   If process.ExitCode <> 0 Then
                                       Dim errorOutput As String = process.StandardError.ReadToEnd()
                                       Throw New Exception($"Errore durante l'estrazione 7z (Exit Code: {process.ExitCode}). Dettagli: {errorOutput}")
                                   End If
                               End Using
                           End Sub)

            SafeInvoke(Sub() AddLog("Estrazione completata con successo."))

        Catch ex As Exception
            SafeInvoke(Sub()
                           MessageBox.Show($"Errore durante l'estrazione dell'archivio: {ex.Message}", "Errore estrazione", MessageBoxButtons.OK, MessageBoxIcon.Error)
                       End Sub)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Assicura che un runtime 7z valido sia disponibile, scaricandolo se necessario
    ''' </summary>
    Private Async Function EnsureSevenZipAsync() As Task(Of String)
        Try
            Dim baseDir As String = AppDomain.CurrentDomain.BaseDirectory
            Dim local7zr As String = Path.Combine(baseDir, "7zr.exe")

            ' 1) Prova runtime locali/comuni già installati
            Dim candidates As New List(Of String) From {
                local7zr,
                Path.Combine(baseDir, "7za.exe"),
                "C:\\Program Files\\7-Zip\\7z.exe",
                "C:\\Program Files (x86)\\7-Zip\\7z.exe"
            }

            For Each candidate In candidates
                If File.Exists(candidate) AndAlso IsSevenZipExecutableValid(candidate) Then
                    Return candidate
                End If
            Next

            ' 2) Se 7zr locale esiste ma è invalido, elimina e riscarica in modo atomico
            If File.Exists(local7zr) Then
                Try
                    File.Delete(local7zr)
                Catch
                    ' Se non eliminabile, si prosegue usando un file temporaneo e poi move
                End Try
            End If

            SafeInvoke(Sub() AddLog("Download 7z Runtime..."))

            Dim tempDownload As String = local7zr & ".tmp"
            Await DownloadSevenZipRuntimeWithRetryAsync("https://www.7-zip.org/a/7zr.exe", tempDownload)

            If Not IsSevenZipExecutableValid(tempDownload) Then
                Throw New Exception("Il file 7z scaricato non è un eseguibile valido.")
            End If

            If File.Exists(local7zr) Then
                File.Delete(local7zr)
            End If
            File.Move(tempDownload, local7zr)

            SafeInvoke(Sub() AddLog("7z Runtime pronto."))
            Return local7zr

        Catch ex As Exception
            Console.WriteLine($"Errore durante il download di 7zr.exe: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Function IsSevenZipExecutableValid(exePath As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(exePath) OrElse Not File.Exists(exePath) Then
                Return False
            End If

            ' Verifica header PE: deve iniziare con "MZ"
            Using fs As New FileStream(exePath, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read)
                If fs.Length < 2 Then Return False
                Dim b1 As Integer = fs.ReadByte()
                Dim b2 As Integer = fs.ReadByte()
                If b1 <> AscW("M"c) OrElse b2 <> AscW("Z"c) Then
                    Return False
                End If
            End Using

            ' Verifica esecuzione reale del binario
            Dim psi As New ProcessStartInfo() With {
                .FileName = exePath,
                .Arguments = "i",
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }

            Using p As Process = Process.Start(psi)
                If p Is Nothing Then
                    Return False
                End If
                If Not p.WaitForExit(5000) Then
                    Try
                        p.Kill()
                    Catch
                    End Try
                    Return False
                End If
                Return p.ExitCode = 0
            End Using

        Catch
            Return False
        End Try
    End Function

    Private Sub errorRed(text)
        SafeInvoke(Sub()
                       Dim originalColor As Color = statusText.BackColor
                       Dim blinkColor As Color = Color.Red
                       statusText.BackColor = blinkColor
                       statusText.Text = text
                   End Sub)
    End Sub

    ''' <summary>
    ''' Recupera i branch dal repo GitHub e mostra un dialog di selezione.
    ''' Restituisce il nome del branch selezionato, oppure Nothing se annullato.
    ''' </summary>
    Public Async Function ShowBranchSelectionAsync() As Task(Of String)
        Try
            Dim github As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))
            Dim branches = Await github.Repository.Branch.GetAll("jamnaga", "wtf-modpack")

            If branches Is Nothing OrElse branches.Count = 0 Then
                AddLog("[DEV] Nessun branch trovato.")
                Return repobranch
            End If

            Dim branchNames = branches.Select(Function(b) b.Name).OrderBy(Function(n) n).ToList()

            ' Mostra il dialog di selezione sul thread UI
            Dim result As String = Nothing
            Dim tcs As New TaskCompletionSource(Of String)()

            Invoke(Sub()
                       Using dlg As New Form()
                           dlg.Text = "[DEV] Seleziona Branch"
                           dlg.Size = New Size(380, 420)
                           dlg.StartPosition = FormStartPosition.CenterParent
                           dlg.FormBorderStyle = FormBorderStyle.FixedDialog
                           dlg.MaximizeBox = False
                           dlg.MinimizeBox = False
                           dlg.BackColor = Color.FromArgb(30, 30, 30)
                           dlg.ForeColor = Color.White

                           Dim lbl As New System.Windows.Forms.Label() With {
                               .Text = "Seleziona il branch da utilizzare:",
                               .Location = New Point(12, 12),
                               .Size = New Size(340, 22),
                               .Font = New Font("Segoe UI", 10)
                           }
                           dlg.Controls.Add(lbl)

                           Dim lst As New ListBox() With {
                               .Location = New Point(12, 40),
                               .Size = New Size(340, 290),
                               .Font = New Font("Consolas", 10),
                               .BackColor = Color.FromArgb(45, 45, 45),
                               .ForeColor = Color.LightGreen,
                               .BorderStyle = BorderStyle.FixedSingle
                           }
                           For Each brName As String In branchNames
                               lst.Items.Add(brName)
                           Next
                           ' Preseleziona il branch corrente
                           Dim idx = branchNames.IndexOf(repobranch)
                           If idx >= 0 Then lst.SelectedIndex = idx
                           dlg.Controls.Add(lst)

                           Dim btnOk As New Button() With {
                               .Text = "Conferma",
                               .Location = New Point(170, 340),
                               .Size = New Size(90, 32),
                               .FlatStyle = FlatStyle.Flat,
                               .BackColor = Color.FromArgb(0, 120, 215),
                               .ForeColor = Color.White,
                               .DialogResult = DialogResult.OK
                           }
                           dlg.Controls.Add(btnOk)
                           dlg.AcceptButton = btnOk

                           Dim btnCancel As New Button() With {
                               .Text = "Annulla",
                               .Location = New Point(265, 340),
                               .Size = New Size(90, 32),
                               .FlatStyle = FlatStyle.Flat,
                               .BackColor = Color.FromArgb(80, 80, 80),
                               .ForeColor = Color.White,
                               .DialogResult = DialogResult.Cancel
                           }
                           dlg.Controls.Add(btnCancel)
                           dlg.CancelButton = btnCancel

                           ' Doppio click conferma direttamente
                           AddHandler lst.DoubleClick, Sub(s, ev)
                                                           If lst.SelectedIndex >= 0 Then
                                                               dlg.DialogResult = DialogResult.OK
                                                               dlg.Close()
                                                           End If
                                                       End Sub

                           Dim dlgResult = dlg.ShowDialog(Me)
                           If dlgResult = DialogResult.OK AndAlso lst.SelectedIndex >= 0 Then
                               tcs.SetResult(lst.SelectedItem.ToString())
                           Else
                               tcs.SetResult(Nothing)
                           End If
                       End Using
                   End Sub)

            Return Await tcs.Task

        Catch ex As Exception
            AddLog($"[DEV] Errore recupero branch: {ex.Message}")
            Return repobranch
        End Try
    End Function

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        statusText.Text = "Avvio..."
        versionLabel.Text = "v" & My.Settings.version
        downloadProgressTimer.Interval = 100 ' Aggiorna ogni 100ms
        AddHandler downloadProgressTimer.Tick, AddressOf DownloadProgressTimer_Tick
        Dim timer As New System.Windows.Forms.Timer()
        timer.Interval = 1000
        AddHandler timer.Tick, Sub(s, ev)
                                   boot()
                                   timer.Stop()
                               End Sub
        timer.Start()
        AddHandler internetTimer.Tick, Sub(s, ev)
                                           internetTimer.Stop()
                                           Task.Run(Async Function()
                                                        Dim isConnected = Await CheckInternetConnectionAsync()
                                                        internet = isConnected

                                                        ' Imposta intervallo in base al risultato
                                                        internetTimer.Interval = If(isConnected, 5000, 1000)
                                                        internetTimer.Start()
                                                        Return Nothing
                                                    End Function)
                                       End Sub
        internetTimer.Interval = 100
        internetTimer.Start()

        Dim timer2 As New System.Windows.Forms.Timer()
        timer2.Interval = 1
        AddHandler timer2.Tick, Sub(s, ev)
                                    If drag Then
                                        Me.Location = New Point(MousePosition.X - dragOffset.X, MousePosition.Y - dragOffset.Y)
                                    End If
                                End Sub
        timer2.Start()

    End Sub

    ''' <summary>
    ''' Verifica la connessione internet con strategia multi-livello
    ''' </summary>
    Private Async Function CheckInternetConnectionAsync() As Task(Of Boolean)
        Try
            ' Livello 1: Verifica interfaccia di rete attiva
            If Not Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() Then
                Return False
            End If

            ' Livello 2: Ping multiplo a server DNS pubblici affidabili
            Dim targets As String() = {"8.8.8.8", "1.1.1.1", "208.67.222.222"}
            Using ping As New Net.NetworkInformation.Ping()
                For Each target In targets
                    Try
                        Dim reply = Await ping.SendPingAsync(target, 2000)
                        If reply.Status = Net.NetworkInformation.IPStatus.Success Then
                            Return True
                        End If
                    Catch
                        ' Prova il prossimo target
                    End Try
                Next
            End Using

            ' Livello 3: HTTP HEAD request come ultima risorsa
            Try
                Using client As New Net.Http.HttpClient()
                    client.Timeout = TimeSpan.FromSeconds(3)
                    Dim response = Await client.GetAsync("https://www.google.com/generate_204", Net.Http.HttpCompletionOption.ResponseHeadersRead)
                    Return response.IsSuccessStatusCode
                End Using
            Catch
                Return False
            End Try

        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function CheckJavaStatusAsync()
        Try
            Invoke(Sub() AddLog("Controllo disponibilità Java..."))

            Dim javaPath As String = JavaHelper.FindExistingJava()

            If Not String.IsNullOrEmpty(javaPath) AndAlso JavaHelper.IsJavaValid(javaPath) Then
                Dim version As String = JavaDownloader.GetJavaVersion(javaPath)

                AddLog($" Java trovato: {version}")
                AddLog($" Percorso: {javaPath}")

                Return True
            Else
                AddLog("Java non trovato o non valido.")
                Return False
            End If
        Catch ex As Exception
            MessageBox.Show($"Si è verificato un errore durante il controllo di Java: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            AddLog($" Errore controllo Java: {ex.Message}")
            Return False
        End Try
    End Function

    Private Function CheckModpackVersion()
        AddLog("Controllo versione modpack...")
        Dim github As New GitHubClient(New ProductHeaderValue("WTF-Modpack-Launcher"))
        latestVersion = github.Repository.Release.GetAll("jamnaga", "wtf-modpack").Result(0).TagName

        ' Debug logging per verificare le directory
        AddLog($" Directory gameDir: {gameDir}")
        AddLog($" Directory minecraftDir: {minecraftDir}")

        ' Controlla se la directory gameDir esiste
        If Not Directory.Exists(gameDir) Then
            AddLog("GameDir non esiste - modpack non installato.")
            Return False
        End If

        ' Controlla se la cartella mods esiste in gameDir (non in minecraftDir)
        Dim modsDir As String = Path.Combine(gameDir, "mods")
        If Not Directory.Exists(modsDir) Then
            AddLog("Cartella mods non trovata in gameDir - modpack non installato.")
            Return False
        End If

        ' Verifica che ci siano effettivamente dei file mod
        Dim modFiles() As String = Directory.GetFiles(modsDir, "*.jar")
        If modFiles.Length = 0 Then
            AddLog("Nessun file mod trovato - modpack non installato.")
            Return False
        End If

        AddLog($" Trovati {modFiles.Length} file mod in gameDir/mods")

        ' Controlla il file version.txt in gameDir (per coerenza)
        Dim versionFilePath As String = Path.Combine(gameDir, "version.txt")
        If System.IO.File.Exists(versionFilePath) Then
            Dim version As String = System.IO.File.ReadAllText(versionFilePath).Trim()
            AddLog($" Versione locale trovata: {version}")
            AddLog($" Versione remota: {latestVersion}")

            If version <> latestVersion Then
                AddLog($" Modpack obsoleto (local: {version}, latest: {latestVersion})")
                Return False
            Else
                AddLog("Modpack già aggiornato!")
                Return True
            End If
        Else
            ' Fallback: controlla anche in minecraftDir per compatibilità con versioni precedenti
            Dim fallbackVersionPath As String = Path.Combine(minecraftDir, "version.txt")
            If System.IO.File.Exists(fallbackVersionPath) Then
                Dim version As String = System.IO.File.ReadAllText(fallbackVersionPath).Trim()
                AddLog($" Versione locale (fallback) trovata: {version}")
                AddLog($" Versione remota: {latestVersion}")

                If version <> latestVersion Then
                    AddLog($" Modpack obsoleto (local: {version}, latest: {latestVersion})")
                    Return False
                Else
                    ' Sposta il file version.txt nella posizione corretta
                    Try
                        System.IO.File.Copy(fallbackVersionPath, versionFilePath, True)
                        AddLog("File version.txt spostato in gameDir per coerenza")
                    Catch ex As Exception
                        AddLog($" Errore spostamento version.txt: {ex.Message}")
                    End Try
                    AddLog("Modpack già aggiornato!")
                    Return True
                End If
            Else
                AddLog("File version.txt non trovato - impossibile verificare versione.")
                Return False
            End If
        End If
    End Function

    Public Sub AddLog(message As String)
        SafeInvoke(Sub()
                       Dim timestamp As String = DateTime.Now.ToString("HH:mm:ss")
                       Dim logMessage As String = $"[{timestamp}] {message}"
                       statusText.Text = message
                       operationText.Text = message
                       statusText.BackColor = Color.Transparent
                       statusText.Refresh()
                   End Sub)
    End Sub

    Private Async Function GetRemoteModpackCommitIdAsync() As Task(Of String)
        Dim apiUrl As String = $"https://api.github.com/repos/jamnaga/wtf-modpack/commits/{repobranch}"

        Using wc As New Net.WebClient()
            wc.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
            wc.CachePolicy = New System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore)

            Dim json As String = Await wc.DownloadStringTaskAsync(apiUrl)
            Dim commitObj As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(json)
            Return If(commitObj("sha") IsNot Nothing, commitObj("sha").ToObject(Of String)(), "")
        End Using
    End Function

    Private Function GetSavedModpackCommitId() As String
        Try
            Dim commitFilePath As String = Path.Combine(downloadDir, "latest_modpack_commit.txt")
            If Not File.Exists(commitFilePath) Then
                Return ""
            End If

            Return File.ReadAllText(commitFilePath).Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Sub SaveModpackCommitId(commitId As String)
        If String.IsNullOrWhiteSpace(commitId) Then
            Return
        End If

        Try
            If Not Directory.Exists(downloadDir) Then
                Directory.CreateDirectory(downloadDir)
            End If

            Dim commitFilePath As String = Path.Combine(downloadDir, "latest_modpack_commit.txt")
            File.WriteAllText(commitFilePath, commitId.Trim())
        Catch ex As Exception
            AddLog($"Impossibile salvare latest commit id locale: {ex.Message}")
        End Try
    End Sub

    Private Sub ApplyReducedGraphicsOptions()
        Dim optionsPath As String = Path.Combine(gameDir, "options.txt")
        Dim optionsLines As New List(Of String)()

        If File.Exists(optionsPath) Then
            optionsLines.AddRange(File.ReadAllLines(optionsPath))
        End If

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

    Private Function isCached(filePath, size)
        Dim cachePath As String = Path.Combine(minecraftDir, "cache.json")
        If Not System.IO.File.Exists(cachePath) Then
            Return False
        End If

        Try
            Dim cacheJson As String = System.IO.File.ReadAllText(cachePath)
            Dim cache As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(cacheJson)
            Dim fileName As String = Path.GetFileName(filePath)

            If cache("files") IsNot Nothing Then
                Dim filesObj As Newtonsoft.Json.Linq.JObject = cache("files")

                If filesObj(fileName) IsNot Nothing Then
                    Dim cachedFile As Newtonsoft.Json.Linq.JObject = filesObj(fileName)
                    Dim cachedSize As Long = cachedFile("size").ToObject(Of Long)()

                    If cachedSize = size Then
                        Dim fileInfo As New FileInfo(filePath)
                        If fileInfo.Exists Then
                            Dim hash As String
                            Using stream As FileStream = fileInfo.OpenRead()
                                hash = SHA256.Create().ComputeHash(stream).Aggregate("", Function(s, b) s & b.ToString("x2"))
                            End Using

                            Dim cachedHash As String = cachedFile("sha256").ToObject(Of String)()
                            If cachedHash = hash Then
                                Return True
                            End If
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            AddLog($" Errore lettura cache: {ex.Message}")
            Return False
        End Try

        Return False
    End Function

    Private Function cacheFile(ByVal filePath) As Boolean
        Try
            Dim cachePath As String = Path.Combine(minecraftDir, "cache.json")
            Dim cache As Newtonsoft.Json.Linq.JObject

            If System.IO.File.Exists(cachePath) Then
                Dim cacheJson As String = System.IO.File.ReadAllText(cachePath)
                cache = Newtonsoft.Json.Linq.JObject.Parse(cacheJson)
            Else
                cache = New Newtonsoft.Json.Linq.JObject()
            End If

            If cache("files") Is Nothing Then
                cache("files") = New Newtonsoft.Json.Linq.JObject()
            End If

            Dim fileInfo As New FileInfo(filePath)
            If Not fileInfo.Exists Then
                Return False
            End If

            Dim hash As String
            Using stream As FileStream = fileInfo.OpenRead()
                hash = SHA256.Create().ComputeHash(stream).Aggregate("", Function(s, b) s & b.ToString("x2"))
            End Using

            Dim filesObj As Newtonsoft.Json.Linq.JObject = cache("files")
            filesObj(fileInfo.Name) = New Newtonsoft.Json.Linq.JObject From {
                {"size", fileInfo.Length},
                {"sha256", hash}
            }

            Dim updatedCacheJson As String = cache.ToString(Newtonsoft.Json.Formatting.Indented)
            System.IO.File.WriteAllText(cachePath, updatedCacheJson)
            Return True

        Catch ex As Exception
            AddLog($" Errore salvataggio cache: {ex.Message}")
            Return False
        End Try
    End Function

    ' Add thread-safe helper method
    Public Sub SafeInvoke(action As Action)
        If InvokeRequired Then
            Invoke(action)
        Else
            action()
        End If
    End Sub

    ' NUOVO: Verifica l'integrità di tutti i JAR (mod e librerie) e riscarica quelli corrotti
    Private Async Function VerifyAndFixCorruptedJars() As Task
        Try
            AddLog("Verifica integrità file JAR...")
            Await Task.Delay(300)

            Dim corruptedFiles As New List(Of Object)
            Dim manifestData As Dictionary(Of String, Object) = Nothing

            ' Carica il manifest per confronti SHA256 se disponibile
            Dim manifestPath As String = Path.Combine(downloadDir, "manifest.json")
            If File.Exists(manifestPath) Then
                Try
                    Dim json As String = Await File.ReadAllTextAsync(manifestPath)
                    Dim manifest As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(json)
                    Dim files = manifest("files").ToArray()

                    ' Crea dizionario per lookup veloce
                    manifestData = New Dictionary(Of String, Object)
                    For Each fileEntry In files
                        Dim filePath As String = fileEntry("path").ToObject(Of String)()
                        manifestData(filePath) = New With {
                            .Size = fileEntry("size").ToObject(Of Long)(),
                            .Sha256 = fileEntry("sha256").ToObject(Of String)()
                        }
                    Next
                    AddLog("Manifest caricato per verifica avanzata.")
                Catch
                    ' Se il manifest non è valido, continua con verifica base
                    manifestData = Nothing
                End Try
            End If

            ' 1. Verifica le MOD
            Dim modsDir As String = Path.Combine(gameDir, "mods")
            If Directory.Exists(modsDir) Then
                Dim modFiles() As String = Directory.GetFiles(modsDir, "*.jar", SearchOption.AllDirectories)
                AddLog($" Verifica {modFiles.Length} mod...")

                Dim corruptedMods = Await Task.Run(Function()
                                                       Dim corrupted As New List(Of String)
                                                       Parallel.ForEach(modFiles, Sub(modFile)
                                                                                      Dim relativePath = modFile.Replace(gameDir & "\", "")
                                                                                      Dim isCorrupted As Boolean = False

                                                                                      ' Verifica con manifest se disponibile
                                                                                      If manifestData IsNot Nothing AndAlso manifestData.ContainsKey(relativePath) Then
                                                                                          Dim manifestEntry = manifestData(relativePath)
                                                                                          If Not IsJarValidWithHash(modFile, manifestEntry.Size, manifestEntry.Sha256) Then
                                                                                              isCorrupted = True
                                                                                          End If
                                                                                      Else
                                                                                          ' Verifica base senza hash
                                                                                          If Not IsJarValid(modFile) Then
                                                                                              isCorrupted = True
                                                                                          End If
                                                                                      End If

                                                                                      If isCorrupted Then
                                                                                          SyncLock corrupted
                                                                                              corrupted.Add(modFile)
                                                                                          End SyncLock
                                                                                      End If
                                                                                  End Sub)
                                                       Return corrupted
                                                   End Function)

                If corruptedMods.Count > 0 Then
                    AddLog($" Trovate {corruptedMods.Count} mod corrotte!")
                    For Each corruptedMod In corruptedMods
                        Dim relativePath = corruptedMod.Replace(gameDir & "\", "")
                        corruptedFiles.Add(New With {.Path = corruptedMod, .RelativePath = relativePath, .Type = "mod"})
                    Next
                Else
                    AddLog("Tutte le mod sono integre.")
                End If
            End If

            ' 2. Verifica le LIBRERIE
            Dim librariesDir As String = Path.Combine(gameDir, "libraries")
            If Directory.Exists(librariesDir) Then
                Dim libFiles() As String = Directory.GetFiles(librariesDir, "*.jar", SearchOption.AllDirectories)
                AddLog($" Verifica {libFiles.Length} librerie...")

                Dim corruptedLibs = Await Task.Run(Function()
                                                       Dim corrupted As New List(Of String)
                                                       Parallel.ForEach(libFiles, Sub(libFile)
                                                                                      Dim relativePath = libFile.Replace(gameDir & "\", "")
                                                                                      Dim isCorrupted As Boolean = False

                                                                                      ' Verifica con manifest se disponibile
                                                                                      If manifestData IsNot Nothing AndAlso manifestData.ContainsKey(relativePath) Then
                                                                                          Dim manifestEntry = manifestData(relativePath)
                                                                                          If Not IsJarValidWithHash(libFile, manifestEntry.Size, manifestEntry.Sha256) Then
                                                                                              isCorrupted = True
                                                                                          End If
                                                                                      Else
                                                                                          ' Verifica base senza hash
                                                                                          If Not IsJarValid(libFile) Then
                                                                                              isCorrupted = True
                                                                                          End If
                                                                                      End If

                                                                                      If isCorrupted Then
                                                                                          SyncLock corrupted
                                                                                              corrupted.Add(libFile)
                                                                                          End SyncLock
                                                                                      End If
                                                                                  End Sub)
                                                       Return corrupted
                                                   End Function)

                If corruptedLibs.Count > 0 Then
                    AddLog($" Trovate {corruptedLibs.Count} librerie corrotte!")
                    For Each corruptedLib In corruptedLibs
                        Dim relativePath = corruptedLib.Replace(gameDir & "\", "")
                        corruptedFiles.Add(New With {.Path = corruptedLib, .RelativePath = relativePath, .Type = "libreria"})
                    Next
                Else
                    AddLog("Tutte le librerie sono integre.")
                End If
            End If

            ' 3. Riscarica i file corrotti
            If corruptedFiles.Count > 0 Then
                AddLog($" Inizio riparazione di {corruptedFiles.Count} file corrotti...")
                Await Task.Delay(500)

                Dim redownloadedCount As Integer = 0
                Dim failedCount As Integer = 0

                ' Scarica i file corrotti dal repository
                For Each corruptedFile In corruptedFiles
                    Try
                        ' Elimina il file corrotto
                        If File.Exists(corruptedFile.Path) Then
                            File.Delete(corruptedFile.Path)
                            AddLog($" Eliminato {corruptedFile.Type} corrotto: {Path.GetFileName(corruptedFile.Path)}")
                        End If

                    Catch ex As Exception
                        AddLog($" Errore riparazione {corruptedFile.RelativePath}: {ex.Message}")

                    End Try
                Next

                ' Salva cache aggiornata
                SaveHashCache()

                If failedCount > 0 Then
                    AddLog($" Riparazione completata con errori: {redownloadedCount} OK, {failedCount} falliti")
                Else
                    AddLog($" Riparazione completata: {redownloadedCount} file riparati con successo!")
                End If
            Else
                AddLog("Tutti i file JAR sono integri e validi!")
            End If

        Catch ex As Exception
            AddLog($" Errore durante verifica JAR: {ex.Message}")
        End Try
    End Function

    ' NUOVO: Verifica JAR con hash SHA256 dal manifest
    Private Function IsJarValidWithHash(jarPath As String, expectedSize As Long, expectedHash As String) As Boolean
        Try
            ' Prima verifica base
            If Not IsJarValid(jarPath) Then
                Return False
            End If

            ' Verifica dimensione
            Dim fileInfo As New FileInfo(jarPath)
            If fileInfo.Length <> expectedSize Then
                Return False
            End If

            ' Usa la cache se disponibile
            Dim cacheKey As String = $"{jarPath}_{expectedSize}"
            If fileHashCache.ContainsKey(cacheKey) Then
                Dim cachedHash = fileHashCache(cacheKey)
                Return cachedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
            End If

            ' Calcola hash SHA256
            Using stream As New FileStream(jarPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read)
                Using sha256 As SHA256 = SHA256.Create()
                    Dim hashBytes As Byte() = sha256.ComputeHash(stream)
                    Dim actualHash As String = BitConverter.ToString(hashBytes).Replace("-", "").ToLower()

                    ' Aggiorna cache
                    fileHashCache(cacheKey) = actualHash

                    Return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
                End Using
            End Using

        Catch
            Return False
        End Try
    End Function

    ' NUOVO: Verifica se un file JAR è valido (non corrotto)
    Private Function IsJarValid(jarPath As String) As Boolean
        Try
            ' Controlla se il file esiste
            If Not File.Exists(jarPath) Then
                Return False
            End If

            ' Controlla dimensione minima (un JAR deve essere almeno 1KB)
            Dim fileInfo As New FileInfo(jarPath)
            If fileInfo.Length < 1024 Then
                Return False
            End If

            ' Verifica che sia un file ZIP valido (i JAR sono file ZIP)
            ' Tenta di aprire come archivio ZIP
            Try
                Using archive As ZipArchive = ZipFile.OpenRead(jarPath)
                    ' Verifica che abbia almeno un entry
                    If archive.Entries.Count = 0 Then
                        Return False
                    End If

                    ' Verifica che abbia META-INF/MANIFEST.MF (caratteristica dei JAR)
                    Dim hasManifest As Boolean = archive.Entries.Any(Function(e) e.FullName.ToLower().Contains("meta-inf/manifest.mf"))

                    ' Per le librerie, potremmo essere più permissivi
                    ' Considera valido anche se non ha manifest ma ha almeno file .class
                    If Not hasManifest Then
                        Dim hasClassFiles As Boolean = archive.Entries.Any(Function(e) e.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                        Return hasClassFiles
                    End If

                    Return True
                End Using
            Catch
                ' Se non riesce ad aprire come ZIP, è corrotto
                Return False
            End Try

        Catch
            ' In caso di errore, considera il file corrotto
            Return False
        End Try
    End Function

    Private Async Sub playBtn_Click(sender As Object, e As EventArgs) Handles playBtn.Click
        If playBtn.Text = "PLAY" Then
            playBtn.Text = "ATTENDI"
            playBtn.Enabled = False
            playBtn.BackColor = Color.Yellow
            playBtn.ForeColor = Color.Black
            crashPanel.Visible = False
            operationPanel.Visible = True
            Await Task.Delay(420)

            step1(True)


            doNotPowerOffPanel.Visible = False



            If My.Settings.username.Length < 3 Then
                playBtn.Text = "PLAY"
                playBtn.Enabled = True
                playBtn.BackColor = Color.Green
                playBtn.ForeColor = Color.White
                MessageBox.Show("Imposta un nome utente prima di giocare.", "Nome utente mancante", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Settings.Show()
                Return
            End If

            ' Verifica e riscarica componenti Minecraft mancanti
            ' IMPORTANTE: Questo verifica e scarica assets vanilla mancanti (suoni, lingue)
            AddLog("Verifica completezza Minecraft vanilla...")
            Await mcDownloader.DownloadMinecraftVersion(My.Settings.mcVersion, gameDir)

            ' Verifica librerie Fabric
            AddLog("Verifica librerie Fabric...")
            Await mcDownloader.DownloadVersionDependencies(My.Settings.fabricVersionId, gameDir)

            mcTask = Await mcLauncher.LaunchMinecraft(My.Settings.username, My.Settings.fabricVersionId, gameDir, 4096)

            If mcTask IsNot Nothing Then
                Await Task.Delay(2500)
                playBtn.Text = "CHIUDI"
                playBtn.Enabled = True
                playBtn.BackColor = Color.Red
                playBtn.ForeColor = Color.White
                'doNotPowerOffPanel.Visible = True

                ' Avvia monitoraggio del processo
                StartProcessMonitoring()
                Await Task.Delay(420)
                operationPanel.Visible = False
                Await Task.Delay(420)
            Else
                playBtn.Text = "PLAY"
                playBtn.Enabled = True
                playBtn.BackColor = Color.Green
                playBtn.ForeColor = Color.White
                MessageBox.Show("Si è verificato un errore durante l'avvio di Minecraft. Controlla i log per maggiori dettagli.", "Errore avvio", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
        ElseIf playBtn.Text = "CHIUDI" Then
            If mcTask IsNot Nothing AndAlso Not mcTask.HasExited Then
                mcTask.Kill()
                mcTask.WaitForExit(5000) ' Attendi max 5 secondi
            End If

            ' Ferma monitoraggio
            StopProcessMonitoring()
            doNotPowerOffPanel.Visible = False
            ' Reset pulsante
            ResetPlayButton()
        End If
    End Sub

    ''' <summary>
    ''' Avvia il monitoraggio del processo Minecraft
    ''' </summary>
    Private Sub StartProcessMonitoring()
        ' Ferma timer esistente se attivo
        StopProcessMonitoring()

        ' Configura e avvia timer
        processMonitorTimer.Interval = 1000 ' Controlla ogni secondo
        AddHandler processMonitorTimer.Tick, AddressOf ProcessMonitorTimer_Tick
        processMonitorTimer.Start()

        AddLog("🔍 Monitoraggio processo Minecraft attivato")
    End Sub

    ''' <summary>
    ''' Ferma il monitoraggio del processo
    ''' </summary>
    Private Sub StopProcessMonitoring()
        If processMonitorTimer.Enabled Then
            processMonitorTimer.Stop()
            RemoveHandler processMonitorTimer.Tick, AddressOf ProcessMonitorTimer_Tick
            AddLog("🔍 Monitoraggio processo Minecraft disattivato")
        End If
    End Sub

    ''' <summary>
    ''' Timer tick - controlla se il processo è ancora attivo
    ''' </summary>
    ''' 
    Dim crashPath As String = Path.Combine(gameDir, "logs", "latest.log")
    Private Sub ProcessMonitorTimer_Tick(sender As Object, e As EventArgs)
        Try
            If mcTask IsNot Nothing Then
                ' Controlla se il processo è uscito
                If mcTask.HasExited Then
                    AddLog($"🎮 Minecraft chiuso (Exit Code: {mcTask.ExitCode})")

                    ' Ferma monitoraggio
                    StopProcessMonitoring()

                    ' Reset pulsante
                    ResetPlayButton()

                    If mcTask.ExitCode <> 0 Then

                        crashPanel.Visible = True
                    End If


                    ' Pulisci riferimento processo
                    mcTask.Dispose()
                    mcTask = Nothing
                End If
            Else
                ' Nessun processo da monitorare
                StopProcessMonitoring()
            End If
        Catch ex As Exception
            ' Errore durante il check (processo probabilmente non più accessibile)
            AddLog($"⚠ Errore monitoraggio processo: {ex.Message}")
            StopProcessMonitoring()
            ResetPlayButton()
            mcTask = Nothing
        End Try
    End Sub

    ''' <summary>
    ''' Resetta il pulsante Play allo stato iniziale
    ''' </summary>
    Private Sub ResetPlayButton()
        If InvokeRequired Then
            Invoke(Sub() ResetPlayButton())
            Return
        End If

        playBtn.Text = "PLAY"
        playBtn.Enabled = True
        playBtn.BackColor = Color.Green
        playBtn.ForeColor = Color.White
        Button7.Enabled = True
    End Sub

    Private Sub userLabel_Click(sender As Object, e As EventArgs) Handles userLabel.Click
        Settings.Show()

    End Sub



    Private Sub menuPanel_VisibleChanged(sender As Object, e As EventArgs) Handles menuPanel.VisibleChanged
        If menuPanel.Visible Then
            If My.Settings.username.Length > 3 Then
                userLabel.Text = My.Settings.username
            End If
        End If
    End Sub

    Public Async Sub MCReinstall()
        operationText.Text = "Resetto Minecraft..."
        operationPanel.Visible = True
        doNotPowerOffPanel.Visible = True
        Await Task.Delay(1000)
        If Directory.Exists(gameDir) Then
            Try
                Await RemoveAllNonManifestFiles()
                ' Rimuovi tutti i marker di stato
                File.Delete(Path.Combine(gameDir, "version.txt"))
                File.Delete(Path.Combine(gameDir, "fabricInstalled"))
                ' Rimuovi anche eventuali marker vecchi Forge (legacy/migrazione)
                Try : File.Delete(Path.Combine(gameDir, "forgeInstalled")) : Catch : End Try
                Try : File.Delete(Path.Combine(gameDir, "forgeDownloaded")) : Catch : End Try
                Try : File.Delete(Path.Combine(minecraftDir, "forgeInstalled")) : Catch : End Try
                Try : File.Delete(Path.Combine(minecraftDir, "forgeDownloaded")) : Catch : End Try
                menuPanel.Visible = False
                operationPanel.Visible = False
                Panel1.Visible = True
                boot()
            Catch ex As Exception
                MessageBox.Show($"Si è verificato un errore durante la reinstallazione: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Close()
            End Try
        Else
            MessageBox.Show("La directory di gioco non esiste.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If

    End Sub

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        Button3.Enabled = False
        Button3.Text = "ATTENDI"
        playBtn.Enabled = False

        '''To upload a file, make a PUT request to https://drop.stefanodeblasi.it/upload/ and you will get the url of your upload back.

        '''Optional headers with the request

        '''     Randomize the filename
        '''Linx-Randomize(): yes

        '''Specify a custom deletion key
        '''Linx-Delete - Key: mysecret

        '''    Protect File with password
        '''Linx-Access - Key: mysecret

        '''   Specify an expiration time (in seconds)
        '''Linx-Expiry:  60

        '''Get a json response
        '''Accept: Application/ json

        '''The json response will then contain: 

        ''' “url” the publicly available upload url
        '''“direct_url”: the Url to access the file directly
        '''“filename”: the(optionally generated) filename
        ''' “delete_key”: the(optionally generated) deletion key,
        ''' “access_key”: the(optionally supplied) access key,
        ''' “expiry”: the unix timestamp at which the file will expire (0 if never)
        ''' “size”: the Size in bytes of the file
        '''  “mimetype”: the guessed mimetype of the file
        ''' “sha256sum”: the sha256sum Of the file,

        ''' Examples

        '''Uploading myphoto.jpg

        '''$ curl -T myphoto.jpg https//drop.stefanodeblasi.it/upload/  
        '''https://drop.stefanodeblasi.it/myphoto.jpg
        '''
        '''carica il crashpath
        '''

        Dim client As New WebClient
        Try

            ''set PUT request


            client.Headers(HttpRequestHeader.Accept) = "application/json"
            client.Headers("Linx-Randomize") = "yes"



            Dim response = client.UploadData("https://drop.stefanodeblasi.it/upload/", "PUT", File.ReadAllBytes(crashPath))

            Dim responseString = System.Text.Encoding.UTF8.GetString(response)
            Dim jsonResponse = Newtonsoft.Json.Linq.JObject.Parse(responseString)
            Dim fileUrl = jsonResponse("url").ToString
            File.WriteAllText(Path.Combine(gameDir, "last_report_url.txt"), fileUrl)

            Dim result = MessageBox.Show($"Log caricato con successo! Copiare il link negli appunti?{Environment.NewLine}{fileUrl}", "Upload completato", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
            If result = DialogResult.Yes Then
                Clipboard.SetText(fileUrl)
            End If
            playBtn.Enabled = True
        Catch ex As Exception
            MessageBox.Show($"Si è verificato un errore durante l'upload del log: {ex.Message}", "Errore upload", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            playBtn.Enabled = True
            Button3.Enabled = True
            Button3.Text = "INVIA"
        End Try






    End Sub



    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles MyBase.MouseDown
        If e.Button = MouseButtons.Left Then
            drag = True
            dragOffset = New Point(e.X, e.Y)
        End If
    End Sub
    Private Sub Form1_MouseUp(sender As Object, e As MouseEventArgs) Handles MyBase.MouseUp
        drag = False
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles Button4.Click
        Settings.Show()
    End Sub

    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        Me.WindowState = FormWindowState.Minimized
    End Sub

    Public Sub ReinstallAll()



        menuPanel.Visible = False
        operationText.Text = "Reset completo in corso..."
        operationPanel.Visible = True
        doNotPowerOffPanel.Visible = True

        Task.Run(Async Function()
                     Try
                         Await Task.Delay(300)

                         ' Pulisce ogni dato gestito dal launcher per forzare una reinstallazione completa
                         If Directory.Exists(minecraftDir) Then
                             Directory.Delete(minecraftDir, True)
                         End If

                         If Not Directory.Exists(minecraftDir) Then
                             Directory.CreateDirectory(minecraftDir)
                         End If

                         fileHashCache.Clear()

                         SafeInvoke(Sub()
                                        operationPanel.Visible = False
                                        Panel1.Visible = True
                                        AddLog("Reinstallazione completa avviata.")
                                        boot()
                                    End Sub)

                     Catch ex As Exception
                         SafeInvoke(Sub()
                                        operationPanel.Visible = False
                                        doNotPowerOffPanel.Visible = False
                                        menuPanel.Visible = True
                                        MessageBox.Show($"Si è verificato un errore durante la reinstallazione completa: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                    End Sub)
                     End Try
                 End Function)
    End Sub

    Private Async Sub Button7_Click(sender As Object, e As EventArgs) Handles Button7.Click
        Dim result = MessageBox.Show("Vuoi avviare GangDrogaCity in grafica ridotta? Verranno disabilitati shaders, animazioni e dettagli per migliorare le prestazioni. Puoi sempre modificare questa impostazione in seguito dalle opzioni grafiche di Minecraft.", "Modalità grafica ridotta", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If result = DialogResult.Yes Then

            If playBtn.Text = "PLAY" Then
                playBtn.Text = "ATTENDI"
                playBtn.Enabled = False
                playBtn.BackColor = Color.Yellow
                playBtn.ForeColor = Color.Black
                crashPanel.Visible = False
                operationPanel.Visible = True
                Await Task.Delay(420)

                Await step1(True)


                doNotPowerOffPanel.Visible = False



                If My.Settings.username.Length < 3 Then
                    playBtn.Text = "PLAY"
                    playBtn.Enabled = True
                    playBtn.BackColor = Color.Green
                    playBtn.ForeColor = Color.White
                    MessageBox.Show("Imposta un nome utente prima di giocare.", "Nome utente mancante", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Settings.Show()
                    Return
                End If

                ' Verifica e riscarica componenti Minecraft mancanti
                ' IMPORTANTE: Questo verifica e scarica assets vanilla mancanti (suoni, lingue)
                AddLog("Verifica Minecraft vanilla...")
                Await mcDownloader.DownloadMinecraftVersion(My.Settings.mcVersion, gameDir)

                ' Verifica librerie Fabric
                AddLog("Verifica librerie Fabric...")
                Await mcDownloader.DownloadVersionDependencies(My.Settings.fabricVersionId, gameDir)

                ' Rimuove le mod client-side per la modalità grafica ridotta
                Dim modsDir As String = Path.Combine(gameDir, "mods")
                If Directory.Exists(modsDir) Then
                    Dim clientModFiles() As String = Directory.GetFiles(modsDir, "*.jar", SearchOption.AllDirectories).
                        Where(Function(modFile) modFile.IndexOf("[client]", StringComparison.OrdinalIgnoreCase) >= 0 OrElse Path.GetFileName(modFile).IndexOf("[client]", StringComparison.OrdinalIgnoreCase) >= 0).
                        ToArray()

                    For Each modFile As String In clientModFiles
                        Try
                            If modFile.IndexOf("drippyloadingscreen", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                Continue For
                            End If
                            File.Delete(modFile)
                            AddLog($"Rimuovo: {Path.GetFileName(modFile)}")
                        Catch ex As Exception
                            AddLog($"Errore rimuovendo la mod client {Path.GetFileName(modFile)}: {ex.Message}")
                        End Try
                    Next
                End If

                ApplyReducedGraphicsOptions()



                mcTask = Await mcLauncher.LaunchMinecraft(My.Settings.username, My.Settings.fabricVersionId, gameDir, 4096)

                If mcTask IsNot Nothing Then
                    Await Task.Delay(2500)
                    playBtn.Text = "CHIUDI"
                    playBtn.Enabled = True
                    playBtn.BackColor = Color.Red
                    playBtn.ForeColor = Color.White
                    'doNotPowerOffPanel.Visible = True

                    ' Avvia monitoraggio del processo
                    StartProcessMonitoring()
                    Await Task.Delay(420)
                    operationPanel.Visible = False
                    Await Task.Delay(420)
                Else
                    playBtn.Text = "PLAY"
                    playBtn.Enabled = True
                    playBtn.BackColor = Color.Green
                    playBtn.ForeColor = Color.White
                    MessageBox.Show("Si è verificato un errore durante l'avvio di Minecraft. Controlla i log per maggiori dettagli.", "Errore avvio", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End If
            ElseIf playBtn.Text = "CHIUDI" Then
                If mcTask IsNot Nothing AndAlso Not mcTask.HasExited Then
                    mcTask.Kill()
                    mcTask.WaitForExit(5000) ' Attendi max 5 secondi
                End If

                ' Ferma monitoraggio
                StopProcessMonitoring()
                doNotPowerOffPanel.Visible = False
                ' Reset pulsante
                ResetPlayButton()
            End If
        Else

        End If
    End Sub

    Private Sub playBtn_EnabledChanged(sender As Object, e As EventArgs) Handles playBtn.EnabledChanged
        Button7.Enabled = playBtn.Enabled
        If playBtn.Text = "CHIUDI" Then
            Button7.Enabled = False
        End If
    End Sub

    Public Sub VerifyInstallation()
        menuPanel.Visible = False
        operationText.Text = "Verifica in corso..."
        operationPanel.Visible = True
        doNotPowerOffPanel.Visible = True

        Task.Run(Async Function()
                     Try
                         Await Task.Delay(300)

                         SafeInvoke(Async Sub()
                                        operationPanel.Visible = False
                                        Panel1.Visible = True
                                        Await step1(False, True)
                                    End Sub)

                     Catch ex As Exception
                         SafeInvoke(Sub()
                                        operationPanel.Visible = False
                                        doNotPowerOffPanel.Visible = False
                                        menuPanel.Visible = True
                                        MessageBox.Show($"Si è verificato un errore durante la reinstallazione completa: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                    End Sub)
                     End Try
                 End Function)

    End Sub
End Class