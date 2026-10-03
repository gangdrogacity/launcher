Imports System.Diagnostics
Imports System.Net.Http

''' <summary>
''' Individuazione e installazione di Java 17 per la piattaforma corrente.
'''  - Windows: il modpack fornisce una JRE Zulu in game\java (manifest); viene trovata qui.
'''  - Linux/macOS: la cartella java\ del manifest e' Windows-only e viene ignorata dal sync;
'''    il launcher scarica una JRE Temurin 17 da Adoptium per OS e architettura in game\java.
''' </summary>
Public Module JavaManager

    Private ReadOnly http As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(15)}

    ''' <summary>Cerca un eseguibile Java valido dentro game\java (qualsiasi layout: bin\java, Contents/Home/bin/java).</summary>
    Public Function FindExistingJava(gameDir As String) As String
        Dim javaDir As String = Path.Combine(gameDir, "java")
        If Not Directory.Exists(javaDir) Then Return Nothing

        For Each candidate In EnumerateJavaCandidates(javaDir)
            If IsJavaValid(candidate) Then Return candidate
        Next
        Return Nothing
    End Function

    Private Iterator Function EnumerateJavaCandidates(javaDir As String) As IEnumerable(Of String)
        Dim exe = Platform.JavaExecutableName
        Dim relLayouts As String() = {
            Path.Combine("bin", exe),
            Path.Combine("Contents", "Home", "bin", exe),
            Path.Combine("jre", "bin", exe)
        }
        For Each level1 In SafeGetDirectories(javaDir)
            For Each rel In relLayouts
                Dim p = Path.Combine(level1, rel)
                If File.Exists(p) Then Yield p
            Next
            ' Un livello piu' in profondita' (es. java\adoptium\jdk-17.x-jre\bin\java)
            For Each level2 In SafeGetDirectories(level1)
                For Each rel In relLayouts
                    Dim p = Path.Combine(level2, rel)
                    If File.Exists(p) Then Yield p
                Next
            Next
        Next
    End Function

    Private Function SafeGetDirectories(dir As String) As String()
        Try
            Return Directory.GetDirectories(dir)
        Catch
            Return Array.Empty(Of String)()
        End Try
    End Function

    Public Function IsJavaValid(javaPath As String) As Boolean
        Try
            If String.IsNullOrEmpty(javaPath) OrElse Not File.Exists(javaPath) Then Return False
            Platform.MakeExecutable(javaPath)

            Dim psi As New ProcessStartInfo() With {
                .FileName = javaPath,
                .UseShellExecute = False,
                .RedirectStandardError = True,
                .RedirectStandardOutput = True,
                .CreateNoWindow = True
            }
            psi.ArgumentList.Add("-version")

            Using p As Process = Process.Start(psi)
                p.StandardError.ReadToEndAsync()
                p.StandardOutput.ReadToEndAsync()
                If Not p.WaitForExit(10000) Then
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

    Public Function GetJavaVersion(javaPath As String) As String
        Try
            Dim psi As New ProcessStartInfo() With {
                .FileName = javaPath,
                .UseShellExecute = False,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }
            psi.ArgumentList.Add("-version")
            Using p As Process = Process.Start(psi)
                Dim output As String = p.StandardError.ReadToEnd()
                p.WaitForExit()
                Dim lines = output.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                If lines.Length > 0 Then Return lines(0).Trim()
            End Using
        Catch
        End Try
        Return "Versione sconosciuta"
    End Function

    ''' <summary>
    ''' Garantisce una JRE 17 in game\java: restituisce il percorso di java, scaricandola
    ''' da Adoptium se manca. Lancia un'eccezione se non riesce.
    ''' </summary>
    Public Async Function EnsureJavaAsync(gameDir As String, Optional log As Action(Of String) = Nothing) As Task(Of String)
        Dim existing = FindExistingJava(gameDir)
        If existing IsNot Nothing Then Return existing

        log?.Invoke($"Java non trovato: download JRE 17 per {Platform.OSTag} {Platform.ArchTag}...")

        Dim url As String = $"https://api.adoptium.net/v3/binary/latest/17/ga/{Platform.AdoptiumOSName}/{Platform.AdoptiumArch}/jre/hotspot/normal/eclipse"
        Dim archiveExt As String = If(Platform.IsWindows, ".zip", ".tar.gz")
        Dim javaRoot As String = Path.Combine(gameDir, "java")
        Dim targetDir As String = Path.Combine(javaRoot, $"temurin-17-jre-{Platform.OSTag}-{Platform.ArchTag}")
        Dim tempArchive As String = Path.Combine(Path.GetTempPath(), "gdc_java_" & Guid.NewGuid().ToString("N") & archiveExt)

        Try
            Using req As New HttpRequestMessage(HttpMethod.Get, url)
                req.Headers.UserAgent.ParseAdd("GangDrogaCity-Launcher/1.0")
                Using resp = Await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                    resp.EnsureSuccessStatusCode()
                    Dim total As Long = If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value, 0L)
                    Using fs = File.Create(tempArchive)
                        Using stream = Await resp.Content.ReadAsStreamAsync()
                            Dim buffer(1024 * 256 - 1) As Byte
                            Dim readTotal As Long = 0
                            Dim lastLog As DateTime = DateTime.MinValue
                            While True
                                Dim n = Await stream.ReadAsync(buffer, 0, buffer.Length)
                                If n <= 0 Then Exit While
                                Await fs.WriteAsync(buffer, 0, n)
                                readTotal += n
                                If total > 0 AndAlso (DateTime.Now - lastLog).TotalMilliseconds > 1500 Then
                                    lastLog = DateTime.Now
                                    log?.Invoke($"Download Java: {CInt(readTotal * 100 / total)}%")
                                End If
                            End While
                        End Using
                    End Using
                End Using
            End Using

            log?.Invoke("Estrazione Java...")
            If Directory.Exists(targetDir) Then Directory.Delete(targetDir, True)
            Directory.CreateDirectory(targetDir)
            Await Task.Run(Sub() ArchiveTools.ExtractAuto(tempArchive, targetDir))

            Dim javaPath = FindExistingJava(gameDir)
            If javaPath Is Nothing Then Throw New Exception("Java non trovato dopo l'estrazione")
            Platform.MakeExecutable(javaPath)
            log?.Invoke($"Java installato: {javaPath}")
            Return javaPath
        Finally
            Try
                If File.Exists(tempArchive) Then File.Delete(tempArchive)
            Catch
            End Try
        End Try
    End Function

End Module
