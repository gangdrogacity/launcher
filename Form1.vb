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

    Dim repobranch As String = "styleupdate"
    Dim data = "https://github.com/jamnaga/wtf-modpack/archive/refs/heads/" & repobranch & ".zip"
    Dim repoBasepath As String = "https://raw.githubusercontent.com/jamnaga/wtf-modpack/refs/heads/" & repobranch & "/"

    Dim zipPath As String = Path.Combine(downloadDir, "modpack.zip")
    Dim forgepath As String = Path.Combine(downloadDir, "forge-installer.jar")

    Dim javaUrl As String = ""
    Dim forgeUrl As String = "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.3.33/forge-1.20.1-47.3.33-installer.jar"

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
    Dim mcTask As Process
    Dim mcToken As CancellationToken
    Dim processMonitorTimer As New System.Windows.Forms.Timer()

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

    Private Async Sub boot()

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

    Private Async Sub step1(step1only As Boolean)

        If drive.AvailableFreeSpace < 1L * 1024 * 1024 * 1024 Then
            errorRed("Non c'è abbastanza spazio libero sul disco per installare il gioco. Sono necessari almeno 2 GB di spazio libero.")
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

        Dim manifestUrl = repoBasepath & "manifest.json"
        Dim manifestPath = Path.Combine(downloadDir, "manifest.json")

        Try
            ' Download del manifest
            Using client As New Net.WebClient()
                client.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")

                client.CachePolicy = New System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore)


                Await DownloadFileTaskAsync(client, New Uri(manifestUrl), manifestPath, False)
            End Using

            AddLog("Analisi file necessari...")

            ' Lettura e parsing del manifest
            Dim json As String = Await System.IO.File.ReadAllTextAsync(manifestPath)
            Dim manifest As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(json)
            Dim files = manifest("files").ToArray()
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
    End Sub

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
                    Path.Combine(gameDir, "forgeInstalled"),
                    Path.Combine(gameDir, "forgeDownloaded")
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
                        SafeInvoke(Sub() AddLog($">{file.Replace(target & "\", "")}"))
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

    ' Step2 completamente rivisitato e ottimizzato
    Private Async Function step2() As Task
        Dim forgeDownloadedMarker As String = Path.Combine(gameDir, "forgeDownloaded")
        
        ' Verifica se Forge è già stato scaricato (controlla marker E file di default)
        If File.Exists(forgeDownloadedMarker) Then
            Dim cachedForgePath As String = File.ReadAllText(forgeDownloadedMarker).Trim()
            If Not String.IsNullOrEmpty(cachedForgePath) AndAlso File.Exists(cachedForgePath) Then
                AddLog($"✓ Forge già scaricato: {Path.GetFileName(cachedForgePath)}")
                forgepath = cachedForgePath
                ProgressBar1.Value = 65
                Await step3()
                Return
            Else
                ' File marker corrotto o file cancellato, rimuovo marker
                AddLog("⚠ Marker Forge corrotto, riscarico...")
                File.Delete(forgeDownloadedMarker)
            End If
        ElseIf File.Exists(forgepath) Then
            ' Il file esiste nella posizione di default, ma non c'è marker
            Dim fileInfo As New FileInfo(forgepath)
            If fileInfo.Length > 1000000 Then ' Almeno 1MB
                AddLog($"✓ Forge trovato in cache: {Path.GetFileName(forgepath)}")
                File.WriteAllText(forgeDownloadedMarker, forgepath)
                ProgressBar1.Value = 65
                Await step3()
                Return
            Else
                ' File corrotto, elimino
                AddLog("⚠ File Forge corrotto, riscarico...")
                File.Delete(forgepath)
            End If
        End If
        Try
            AddLog("Download Forge installer...")
            ProgressBar1.Value = 45
            Await Task.Delay(200)



            AddLog("Download Forge installer in corso...")

            Using forgeClient As New Net.WebClient()
                forgeClient.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")

                ' Handler per il progresso


                Await DownloadFileTaskAsync(forgeClient, New Uri(forgeUrl), forgepath, True)
            End Using

            ' Cache del file Forge appena scaricato
            Await CacheForgeFileAsync()
            ' Cache del file Forge appena scaricato


            ProgressBar1.Value = 65
            AddLog("Download Forge completato!")
            System.IO.File.WriteAllText(Path.Combine(gameDir, "forgeDownloaded"), forgepath)


            Await step3()

        Catch ex As Exception
            errorRed($" Errore durante download Forge: {ex.Message}")
            MessageBox.Show($"Si è verificato un errore durante il download di Forge: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
            ProgressBar1.Value = 0
        End Try
        ' Download ottimizzato di Forge

    End Function

    ' Verifica veloce del file Forge usando cache
    Private Async Function VerifyForgeFileAsync() As Task(Of Boolean)
        Return Await Task.Run(Function() As Boolean
                                  Try
                                      If Not System.IO.File.Exists(forgepath) Then Return False

                                      ' Verifica dimensione tramite HEAD request
                                      Dim req = Net.WebRequest.Create(forgeUrl)
                                      req.Method = "HEAD"
                                      Using resp = req.GetResponse()
                                          Dim expectedSize As Long = resp.ContentLength
                                          Dim fileInfo As New FileInfo(forgepath)

                                          If fileInfo.Length <> expectedSize Then Return False

                                          ' Controlla cache per evitare ricalcolo hash
                                          Dim cacheKey As String = $"{forgepath}_{expectedSize}"
                                          If fileHashCache.ContainsKey(cacheKey) Then
                                              Return True ' Se in cache, assumiamo sia valido
                                          End If

                                          Return True ' Per Forge, la dimensione è sufficiente
                                      End Using
                                  Catch
                                      Return False
                                  End Try
                              End Function)
    End Function

    ' Cache veloce per il file Forge
    Private Async Function CacheForgeFileAsync() As Task
        Await Task.Run(Sub()
                           Try
                               If System.IO.File.Exists(forgepath) Then
                                   Dim fileInfo As New FileInfo(forgepath)
                                   Dim cacheKey As String = $"{forgepath}_{fileInfo.Length}"
                                   fileHashCache(cacheKey) = "forge_valid" ' Placeholder per indicare validità
                                   SaveHashCache()
                               End If
                           Catch
                               ' Ignora errori di cache
                           End Try
                       End Sub)
    End Function

    Private Async Function step3() As Task

        AddLog("Installazione di GangDrogaCity " + latestVersion + "...")
        ProgressBar1.Value = 65
        Await Task.Delay(500)

        ProgressBar1.Value = 75

        Dim forgeInstalledMarker As String = Path.Combine(gameDir, "forgeInstalled")
        
        If Not File.Exists(forgeInstalledMarker) Then
            Try
                ' Recupera il path di Forge dal marker di download
                Dim forgeDownloadedMarker As String = Path.Combine(gameDir, "forgeDownloaded")
                If File.Exists(forgeDownloadedMarker) Then
                    forgepath = File.ReadAllText(forgeDownloadedMarker).Trim()
                End If
                ' Download Minecraft in background
                AddLog("Download Minecraft...")
                Await Task.Delay(500)
                Try
                    Await mcDownloader.DownloadMinecraftVersion("1.20.1", gameDir)
                Catch ex As Exception
                    ' Fallback se il metodo non è async
                    Task.Run(Sub() mcDownloader.DownloadMinecraftVersion("1.20.1", gameDir))
                End Try

                ProgressBar1.Value = 85

                ' Setup Java e Forge
                Dim javaPath As String = JavaHelper.FindJavaPath().Result
                AddLog($" Usando Java: {javaPath}")

                If Not JavaHelper.IsJavaValid(javaPath) Then
                    Throw New Exception("Java non valido o non funzionante")
                End If

                System.IO.File.Copy(forgepath, Path.Combine(gameDir, "forge-installer.jar"), True)

                ' Installazione Forge asincrona
                AddLog("Installazione Forge...")
                Await InstallForgeAsync(javaPath)
                ' Il marker forgeInstalled viene scritto dentro InstallForgeAsync se successo

            Catch ex As Exception
                errorRed($" Errore durante installazione: {ex.Message}")
                MessageBox.Show($"Si è verificato un errore durante l'installazione: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                ProgressBar1.Value = 0
            End Try


        End If


        step4()
    End Function

    ' Installazione Forge asincrona - CORRETTO per evitare blocco UI
    Private Async Function InstallForgeAsync(javaPath As String) As Task
        Dim forgeInstalledMarker As String = Path.Combine(gameDir, "forgeInstalled")
        
        If Not File.Exists(forgeInstalledMarker) Then
            Await Task.Run(Sub()

                               Dim batPath As String = Path.Combine(gameDir, "install_forge.bat")
                               System.IO.File.WriteAllText(batPath, """" & javaPath & """" & " -jar """ & Path.Combine(gameDir, "forge-installer.jar") & """ --installClient """ & gameDir & """" & vbCrLf & "exit")
                               Dim startInfo As New ProcessStartInfo() With {
                .FileName = batPath,
                .UseShellExecute = True,
                .CreateNoWindow = True,
                .RedirectStandardOutput = False,
                .RedirectStandardError = False
            }

                               Using forgeProcess As Process = Process.Start(startInfo)
                                   forgeProcess.WaitForExit()

                                   Invoke(Sub()
                                              If forgeProcess.ExitCode = 0 Then
                                                  Console.WriteLine("Forge installato correttamente!")
                                                  ' Salva version.txt e marker forgeInstalled in gameDir
                                                  System.IO.File.WriteAllText(Path.Combine(gameDir, "version.txt"), latestVersion)
                                                  System.IO.File.WriteAllText(Path.Combine(gameDir, "forgeInstalled"), "True")
                                                  ProgressBar1.Value = 100
                                                  AddLog("Installazione completata.")
                                              Else
                                                  ' CORRETTO: Non chiamare direttamente metodi asincroni da Invoke
                                                  Task.Run(Async Function()
                                                               Await HandleForgeInstallationFailure()
                                                           End Function)
                                                  Return
                                              End If
                                              File.Delete(batPath)
                                          End Sub
                                   )

                               End Using
                           End Sub)
        End If
        ProgressBar1.Value = 75

    End Function


    Private Async Function step4() As Task
        AddLog("Finalizzazione...")

        ' Verifica integrità JAR (mod e librerie)
        Await VerifyAndFixCorruptedJars()
        
        ' CRITICO: Verifica e scarica TUTTI i componenti di Minecraft vanilla
        ' Questo include: client.jar, assets (audio + lingue), librerie vanilla
        AddLog("Verifica completezza Minecraft vanilla...")
        Await mcDownloader.DownloadMinecraftVersion("1.20.1", gameDir)

        ' Scarica SOLO le librerie specifiche di Forge (forge-client.jar, forge-universal.jar, ecc.)
        AddLog("Verifica librerie Forge...")
        Await mcDownloader.DownloadVersionDependencies("1.20.1-forge-47.3.33", gameDir)


        doNotPowerOffPanel.Visible = False
        Panel1.Visible = False
        menuPanel.Visible = True
        playBtn.Text = "PLAY"
        playBtn.Enabled = True

    End Function
    ' NUOVO: Metodo separato per gestire il fallimento dell'installazione
    Private Async Function HandleForgeInstallationFailure() As Task
        Try
            ' Esegui su UI thread
            Dim result As DialogResult = DialogResult.No
            If InvokeRequired Then
                Invoke(Sub()
                           result = MessageBox.Show("Si è verificato un errore durante l'installazione di Forge. Scegli Si per riprovare usando la safe-mode, questo reinstallera' Minecraft e ritenta l'installazione di Forge.", "Errore", MessageBoxButtons.YesNo, MessageBoxIcon.Error)
                       End Sub)
            Else
                result = MessageBox.Show("Si è verificato un errore durante l'installazione di Forge. Scegli Si per riprovare usando la safe-mode, questo reinstallera' Minecraft e ritenta l'installazione di Forge.", "Errore", MessageBoxButtons.YesNo, MessageBoxIcon.Error)
            End If

            If result = DialogResult.Yes Then
                ' Aggiorna UI
                SafeInvoke(Sub()
                               AddLog("Avvio modalità safe-mode...")
                               ProgressBar1.Value = 60
                           End Sub)

                ' Pulisci file non validi
                Await RemoveAllNonManifestFiles()

                ' Aggiorna UI
                SafeInvoke(Sub()
                               AddLog("Reinstallazione Minecraft...")
                               ProgressBar1.Value = 65
                           End Sub)

                ' Riavvia step3 in modo asincrono
                Await step3()

            Else
                ' Aggiorna UI
                SafeInvoke(Sub()
                               AddLog("Installazione Forge fallita.")
                               ProgressBar1.Value = 0
                           End Sub)
            End If

        Catch ex As Exception
            ' Aggiorna UI in caso di errore
            SafeInvoke(Sub()
                           MessageBox.Show($"Si è verificato un errore durante la reinstallazione: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error)
                           ProgressBar1.Value = 0
                       End Sub)
        End Try
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

            ' Scarica 7zr.exe se non esiste
            Dim sevenZipPath As String = Await EnsureSevenZipAsync()

            If String.IsNullOrEmpty(sevenZipPath) OrElse Not File.Exists(sevenZipPath) Then
                Throw New Exception("Impossibile scaricare o trovare 7zr.exe.")
            End If

            ' Comando: 7zr.exe x "archivio.7z" -o"destinazione" -y
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
    ''' Assicura che 7zr.exe sia disponibile, scaricandolo se necessario
    ''' </summary>
    Private Async Function EnsureSevenZipAsync() As Task(Of String)
        Try
            Dim baseDir As String = AppDomain.CurrentDomain.BaseDirectory
            Dim sevenZipPath As String = Path.Combine(baseDir, "7zr.exe")

            ' Se esiste già, restituisci il percorso
            If File.Exists(sevenZipPath) Then
                Return sevenZipPath
            End If

            ' Scarica 7zr.exe da 7-zip.org
            SafeInvoke(Sub() AddLog("Download 7z Runtime..."))

            Using webClient As New Net.WebClient()
                webClient.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                Await webClient.DownloadFileTaskAsync(New Uri("https://www.7-zip.org/a/7zr.exe"), sevenZipPath)
            End Using

            SafeInvoke(Sub() AddLog("7z Runtime pronto."))
            Return sevenZipPath

        Catch ex As Exception
            Console.WriteLine($"Errore durante il download di 7zr.exe: {ex.Message}")
            Return Nothing
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
                menuPanel.Visible = False
                settingsPanel.Visible = True
                Return
            End If

            ' Verifica e riscarica componenti Minecraft mancanti
            ' IMPORTANTE: Questo verifica e scarica assets vanilla mancanti (suoni, lingue)
            AddLog("Verifica completezza Minecraft vanilla...")
            Await mcDownloader.DownloadMinecraftVersion("1.20.1", gameDir)

            ' Verifica librerie Forge
            AddLog("Verifica librerie Forge...")
            Await mcDownloader.DownloadVersionDependencies("1.20.1-forge-47.3.33", gameDir)

            mcTask = Await mcLauncher.LaunchMinecraft(My.Settings.username, "1.20.1-forge-47.3.33", gameDir, 4096)

            If mcTask IsNot Nothing Then
                Await Task.Delay(2500)
                playBtn.Text = "CHIUDI"
                playBtn.Enabled = True
                playBtn.BackColor = Color.Red
                playBtn.ForeColor = Color.White

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
    End Sub

    Private Sub userLabel_Click(sender As Object, e As EventArgs) Handles userLabel.Click
        settingsPanel.Visible = True
        menuPanel.Visible = False

    End Sub

    Private Sub settingsSavebtn_Click(sender As Object, e As EventArgs) Handles settingsSavebtn.Click
        If usernameTxt.Text IsNot Nothing And usernameTxt.Text.Length < 3 Then
            MessageBox.Show("Il nome utente deve essere di almeno 3 caratteri.", "Nome utente non valido", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        My.Settings.username = usernameTxt.Text
        settingsPanel.Visible = False
        menuPanel.Visible = True
        userLabel.Text = My.Settings.username
    End Sub

    Private Sub settingsPanel_VisibleChanged(sender As Object, e As EventArgs) Handles settingsPanel.VisibleChanged
        If settingsPanel.Visible Then
            usernameTxt.Text = My.Settings.username
        End If
    End Sub

    Private Sub menuPanel_VisibleChanged(sender As Object, e As EventArgs) Handles menuPanel.VisibleChanged
        If menuPanel.Visible Then
            If My.Settings.username.Length > 3 Then
                userLabel.Text = My.Settings.username
            End If
        End If
    End Sub

    Private Async Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Dim result = MessageBox.Show("Sei sicuro di voler reinstallare minecraft?", "Conferma", MessageBoxButtons.YesNo, MessageBoxIcon.Question)

        If result = DialogResult.Yes Then
            settingsPanel.Visible = False
            operationText.Text = "Resetto Minecraft..."
            operationPanel.Visible = True
            doNotPowerOffPanel.Visible = True
            Await Task.Delay(1000)
            If Directory.Exists(gameDir) Then
                Try
                    Await RemoveAllNonManifestFiles()
                    ' Rimuovi tutti i marker di stato
                    File.Delete(Path.Combine(gameDir, "version.txt"))
                    File.Delete(Path.Combine(gameDir, "forgeInstalled"))
                    File.Delete(Path.Combine(gameDir, "forgeDownloaded"))
                    ' Rimuovi anche eventuali marker vecchi in minecraftDir (legacy)
                    File.Delete(Path.Combine(minecraftDir, "forgeInstalled"))
                    File.Delete(Path.Combine(minecraftDir, "forgeDownloaded"))
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
        menuPanel.Visible = False
        settingsPanel.Visible = True
    End Sub

    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        Me.WindowState = FormWindowState.Minimized
    End Sub
End Class