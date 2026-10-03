Imports System.Diagnostics
Imports System.Net.Http

''' <summary>
''' Aggiornamento automatico del launcher su Windows, Linux e macOS.
''' Scarica l'asset della release per la piattaforma corrente (vedi Platform.UpdateAssetName),
''' lo estrae e avvia uno script che attende la chiusura del launcher, sostituisce i file
''' e riavvia la nuova versione:
'''  - Windows: file .cmd (come in passato, con richiesta di elevazione)
'''  - Linux:   script sh che sostituisce l'eseguibile
'''  - macOS:   script sh che sostituisce l'intero bundle .app (o l'eseguibile se fuori bundle)
''' </summary>
Public Class SelfUpdater

    Private ReadOnly http As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(15)}

    ''' <summary>
    ''' Scarica e avvia l'aggiornamento. Restituisce True se l'aggiornamento e' stato avviato
    ''' e il launcher deve chiudersi, False se non c'e' nulla da fare o qualcosa e' andato storto.
    ''' </summary>
    Public Async Function TryUpdateAsync(host As ILauncherHost, checker As UpdateChecker, latestVersion As String) As Task(Of Boolean)
        Dim assetName As String = Platform.UpdateAssetName
        Dim assetUrl As String = Await checker.GetAssetDownloadUrlAsync(assetName)
        If String.IsNullOrEmpty(assetUrl) Then
            host.Log($"Aggiornamento {latestVersion} non disponibile per {Platform.OSTag} {Platform.ArchTag} ({assetName}).")
            Return False
        End If

        Dim workDir As String = Path.Combine(Path.GetTempPath(), "gdc_update_" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Dim archivePath As String = Path.Combine(workDir, assetName)
        Dim extractDir As String = Path.Combine(workDir, "extract")
        Directory.CreateDirectory(extractDir)

        Dim updateError As Exception = Nothing
        Try
            host.Log("Download aggiornamento launcher...")
            Await DownloadAsync(assetUrl, archivePath, Sub(pct) host.Log($"Download aggiornamento: {pct}%"))

            host.Log("Estrazione aggiornamento...")
            Await Task.Run(Sub() ArchiveTools.ExtractAuto(archivePath, extractDir))

            If Platform.IsWindows Then
                Return ApplyWindows(host, extractDir, archivePath, workDir)
            ElseIf Platform.IsMacOS Then
                Return ApplyMacOS(host, extractDir, workDir)
            Else
                Return ApplyLinux(host, extractDir, workDir)
            End If
        Catch ex As Exception
            updateError = ex
        End Try

        host.LogError($"Aggiornamento non riuscito: {updateError.Message}")
        Await host.AlertAsync("Aggiornamento", $"Impossibile aggiornare il launcher: {updateError.Message}{Environment.NewLine}Scarica manualmente l'ultima versione da GitHub.", AlertKind.Error)
        Try
            Directory.Delete(workDir, True)
        Catch
        End Try
        Return False
    End Function

    Private Async Function DownloadAsync(url As String, destination As String, progress As Action(Of Integer)) As Task
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.UserAgent.ParseAdd("GangDrogaCity-Launcher/1.0")
            Using resp = Await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                resp.EnsureSuccessStatusCode()
                Dim total As Long = If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value, 0L)
                Using fs = File.Create(destination)
                    Using stream = Await resp.Content.ReadAsStreamAsync()
                        Dim buffer(1024 * 256 - 1) As Byte
                        Dim done As Long = 0
                        Dim lastPct As Integer = -1
                        While True
                            Dim n = Await stream.ReadAsync(buffer, 0, buffer.Length)
                            If n <= 0 Then Exit While
                            Await fs.WriteAsync(buffer, 0, n)
                            done += n
                            If total > 0 Then
                                Dim pct = CInt(done * 100 \ total)
                                If pct <> lastPct AndAlso pct Mod 5 = 0 Then
                                    lastPct = pct
                                    progress?.Invoke(pct)
                                End If
                            End If
                        End While
                    End Using
                End Using
            End Using
        End Using
    End Function

    Private Function FindFile(root As String, fileName As String) As String
        Return Directory.GetFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault()
    End Function

    Private Function FindAppBundle(root As String) As String
        Return Directory.GetDirectories(root, "*.app", SearchOption.AllDirectories).FirstOrDefault()
    End Function

    ' ---------------------------------------------------------------- Windows

    Private Function ApplyWindows(host As ILauncherHost, extractDir As String, archivePath As String, workDir As String) As Boolean
        Dim newExe As String = FindFile(extractDir, "GangDrogaCity.exe")
        If newExe Is Nothing Then Throw New Exception("GangDrogaCity.exe non trovato nell'archivio di aggiornamento")

        Dim currentExePath As String = Platform.CurrentExecutablePath()
        Dim currentPid As Integer = Process.GetCurrentProcess().Id
        Dim batchPath As String = Path.Combine(Path.GetTempPath(), "update_gdc_" & Guid.NewGuid().ToString("N").Substring(0, 8) & ".cmd")

        Dim batch As String = String.Join(vbCrLf, {
            "@echo off",
            "title Aggiornamento GangDrogaCity Launcher",
            "echo Attendo chiusura del launcher...",
            ":WAIT",
            $"tasklist /FI ""PID eq {currentPid}"" 2>NUL | find ""{currentPid}"" >NUL",
            "if not errorlevel 1 (",
            "  timeout /t 1 /nobreak >NUL",
            "  goto WAIT",
            ")",
            "echo Aggiornamento in corso...",
            "timeout /t 2 /nobreak >NUL",
            $"del /f /q ""{currentExePath}""",
            "timeout /t 1 /nobreak >NUL",
            $"move /y ""{newExe}"" ""{currentExePath}""",
            "if errorlevel 1 (",
            "  echo Errore durante l'aggiornamento!",
            "  pause",
            "  exit /b 1",
            ")",
            "echo Avvio nuova versione...",
            "timeout /t 1 /nobreak >NUL",
            $"start """" ""{currentExePath}""",
            "timeout /t 2 /nobreak >NUL",
            $"rmdir /s /q ""{workDir}""",
            $"del /f /q ""{batchPath}""",
            "exit"
        })
        File.WriteAllText(batchPath, batch)

        Dim psi As New ProcessStartInfo() With {
            .FileName = batchPath,
            .UseShellExecute = True,
            .WorkingDirectory = Path.GetDirectoryName(currentExePath),
            .WindowStyle = ProcessWindowStyle.Normal,
            .CreateNoWindow = False,
            .Verb = "runas"
        }
        Process.Start(psi)
        host.Log("Aggiornamento avviato, il launcher si chiude...")
        Return True
    End Function

    ' ---------------------------------------------------------------- Linux

    Private Function ApplyLinux(host As ILauncherHost, extractDir As String, workDir As String) As Boolean
        Dim newExe As String = FindFile(extractDir, "GangDrogaCity")
        If newExe Is Nothing Then Throw New Exception("Eseguibile GangDrogaCity non trovato nell'archivio di aggiornamento")

        Dim currentExePath As String = Platform.CurrentExecutablePath()
        Dim script As String = String.Join(vbLf, {
            "#!/bin/sh",
            $"PID={Process.GetCurrentProcess().Id}",
            $"TARGET={Sh(currentExePath)}",
            $"NEW={Sh(newExe)}",
            $"WORK={Sh(workDir)}",
            "while kill -0 ""$PID"" 2>/dev/null; do sleep 1; done",
            "sleep 1",
            "cp -f ""$NEW"" ""$TARGET.new"" && mv -f ""$TARGET.new"" ""$TARGET""",
            "chmod +x ""$TARGET""",
            "nohup ""$TARGET"" >/dev/null 2>&1 &",
            "sleep 2",
            "rm -rf ""$WORK""",
            "rm -f ""$0"""
        })
        Return RunUnixScript(host, script, workDir)
    End Function

    ' ---------------------------------------------------------------- macOS

    Private Function ApplyMacOS(host As ILauncherHost, extractDir As String, workDir As String) As Boolean
        Dim currentBundle As String = Platform.CurrentAppBundlePath()
        Dim newBundle As String = FindAppBundle(extractDir)

        If currentBundle IsNot Nothing AndAlso newBundle IsNot Nothing Then
            Dim script As String = String.Join(vbLf, {
                "#!/bin/sh",
                $"PID={Process.GetCurrentProcess().Id}",
                $"TARGET={Sh(currentBundle)}",
                $"NEW={Sh(newBundle)}",
                $"WORK={Sh(workDir)}",
                "while kill -0 ""$PID"" 2>/dev/null; do sleep 1; done",
                "sleep 1",
                "rm -rf ""$TARGET.old""",
                "mv ""$TARGET"" ""$TARGET.old"" && mv ""$NEW"" ""$TARGET""",
                "chmod -R +x ""$TARGET/Contents/MacOS""",
                "xattr -dr com.apple.quarantine ""$TARGET"" 2>/dev/null",
                "open -n ""$TARGET""",
                "sleep 2",
                "rm -rf ""$TARGET.old"" ""$WORK""",
                "rm -f ""$0"""
            })
            Return RunUnixScript(host, script, workDir)
        End If

        ' Eseguibile nudo (fuori da un bundle) oppure archivio senza .app: sostituisci il binario
        Dim newExe As String = If(newBundle IsNot Nothing,
                                  FindFile(Path.Combine(newBundle, "Contents", "MacOS"), "GangDrogaCity"),
                                  FindFile(extractDir, "GangDrogaCity"))
        If newExe Is Nothing Then Throw New Exception("Eseguibile GangDrogaCity non trovato nell'archivio di aggiornamento")
        Return ApplyLinuxLike(host, newExe, workDir)
    End Function

    Private Function ApplyLinuxLike(host As ILauncherHost, newExe As String, workDir As String) As Boolean
        Dim currentExePath As String = Platform.CurrentExecutablePath()
        Dim script As String = String.Join(vbLf, {
            "#!/bin/sh",
            $"PID={Process.GetCurrentProcess().Id}",
            $"TARGET={Sh(currentExePath)}",
            $"NEW={Sh(newExe)}",
            $"WORK={Sh(workDir)}",
            "while kill -0 ""$PID"" 2>/dev/null; do sleep 1; done",
            "sleep 1",
            "cp -f ""$NEW"" ""$TARGET.new"" && mv -f ""$TARGET.new"" ""$TARGET""",
            "chmod +x ""$TARGET""",
            "xattr -d com.apple.quarantine ""$TARGET"" 2>/dev/null",
            "nohup ""$TARGET"" >/dev/null 2>&1 &",
            "sleep 2",
            "rm -rf ""$WORK""",
            "rm -f ""$0"""
        })
        Return RunUnixScript(host, script, workDir)
    End Function

    Private Function RunUnixScript(host As ILauncherHost, script As String, workDir As String) As Boolean
        Dim scriptPath As String = Path.Combine(workDir, "update_gdc.sh")
        File.WriteAllText(scriptPath, script & vbLf)
        Platform.MakeExecutable(scriptPath)

        Dim psi As New ProcessStartInfo() With {
            .FileName = "/bin/sh",
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .WorkingDirectory = workDir
        }
        psi.ArgumentList.Add(scriptPath)
        Process.Start(psi)
        host.Log("Aggiornamento avviato, il launcher si chiude...")
        Return True
    End Function

    ''' <summary>Quota una stringa per sh con apici singoli.</summary>
    Private Shared Function Sh(value As String) As String
        Return "'" & value.Replace("'", "'\''") & "'"
    End Function

End Class
