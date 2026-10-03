Imports System.Diagnostics
Imports System.Formats.Tar
Imports System.IO.Compression
Imports System.Net.Http
Imports SharpCompress.Archives
Imports SharpCompress.Common

''' <summary>
''' Estrazione archivi su tutte le piattaforme.
'''  - zip e tar.gz: librerie di base .NET
'''  - 7z (anche in volumi .7z.001...): eseguibile 7-Zip se disponibile (su Windows viene
'''    scaricato 7zr.exe come in passato; su Linux/macOS si usa 7zz/7z/7za se installato),
'''    altrimenti SharpCompress in puro .NET (i volumi vengono prima concatenati).
''' </summary>
Public Module ArchiveTools

    Private ReadOnly http As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(5)}

    Public Sub ExtractZip(archivePath As String, destination As String)
        Directory.CreateDirectory(destination)
        ZipFile.ExtractToDirectory(archivePath, destination, True)
    End Sub

    Public Sub ExtractTarGz(archivePath As String, destination As String)
        Directory.CreateDirectory(destination)
        Using fs As New FileStream(archivePath, FileMode.Open, FileAccess.Read)
            Using gz As New GZipStream(fs, CompressionMode.Decompress)
                TarFile.ExtractToDirectory(gz, destination, True)
            End Using
        End Using
    End Sub

    ''' <summary>Estrae zip o tar.gz in base all'estensione.</summary>
    Public Sub ExtractAuto(archivePath As String, destination As String)
        Dim lower = archivePath.ToLowerInvariant()
        If lower.EndsWith(".tar.gz") OrElse lower.EndsWith(".tgz") Then
            ExtractTarGz(archivePath, destination)
        ElseIf lower.EndsWith(".zip") Then
            ExtractZip(archivePath, destination)
        Else
            Throw New NotSupportedException($"Formato archivio non supportato: {archivePath}")
        End If
    End Sub

    ''' <summary>
    ''' Estrae un archivio 7z (archivePath puo' essere il primo volume .7z.001).
    ''' </summary>
    Public Async Function Extract7zAsync(archivePath As String, destination As String, Optional log As Action(Of String) = Nothing) As Task
        Directory.CreateDirectory(destination)

        Dim tool As String = Await EnsureSevenZipToolAsync(log)
        If Not String.IsNullOrEmpty(tool) Then
            Await Task.Run(Sub() RunSevenZip(tool, archivePath, destination))
            Return
        End If

        log?.Invoke("7-Zip non disponibile: uso l'estrattore integrato...")
        Await Task.Run(Sub() ExtractSevenZipManaged(archivePath, destination))
    End Function

    Private Sub RunSevenZip(tool As String, archivePath As String, destination As String)
        Dim psi As New ProcessStartInfo() With {
            .FileName = tool,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        psi.ArgumentList.Add("x")
        psi.ArgumentList.Add(archivePath)
        psi.ArgumentList.Add("-o" & destination)
        psi.ArgumentList.Add("-y")

        Using p As Process = Process.Start(psi)
            Dim stdout = p.StandardOutput.ReadToEndAsync()
            Dim stderr = p.StandardError.ReadToEndAsync()
            p.WaitForExit()
            If p.ExitCode <> 0 Then
                Throw New Exception($"Errore durante l'estrazione 7z (Exit Code: {p.ExitCode}). Dettagli: {stderr.Result}")
            End If
        End Using
    End Sub

    ''' <summary>
    ''' Estrazione 7z in puro .NET. I volumi (.7z.001, .7z.002...) sono semplici spezzoni
    ''' consecutivi: vengono concatenati in un file temporaneo e poi estratti.
    ''' </summary>
    Private Sub ExtractSevenZipManaged(archivePath As String, destination As String)
        Dim source As String = archivePath
        Dim tempJoined As String = Nothing
        Try
            If IsSplitVolume(archivePath) Then
                tempJoined = Path.Combine(Path.GetTempPath(), "gdc_" & Guid.NewGuid().ToString("N") & ".7z")
                Using output As New FileStream(tempJoined, FileMode.Create, FileAccess.Write)
                    For Each part In EnumerateVolumes(archivePath)
                        Using input As New FileStream(part, FileMode.Open, FileAccess.Read)
                            input.CopyTo(output)
                        End Using
                    Next
                End Using
                source = tempJoined
            End If

            ArchiveFactory.WriteToDirectory(source, destination, New ExtractionOptions() With {.ExtractFullPath = True, .Overwrite = True})
        Finally
            If tempJoined IsNot Nothing Then
                Try
                    File.Delete(tempJoined)
                Catch
                End Try
            End If
        End Try
    End Sub

    Private Function IsSplitVolume(archivePath As String) As Boolean
        Dim ext = Path.GetExtension(archivePath)
        Dim n As Integer
        Return ext.Length = 4 AndAlso Integer.TryParse(ext.Substring(1), n)
    End Function

    Private Iterator Function EnumerateVolumes(firstVolume As String) As IEnumerable(Of String)
        Dim baseName = Path.Combine(Path.GetDirectoryName(firstVolume), Path.GetFileNameWithoutExtension(firstVolume))
        Dim index As Integer = 1
        While True
            Dim part = $"{baseName}.{index:000}"
            If Not File.Exists(part) Then Exit While
            Yield part
            index += 1
        End While
    End Function

    ''' <summary>
    ''' Restituisce il percorso di un eseguibile 7-Zip utilizzabile, oppure Nothing.
    ''' Su Windows scarica 7zr.exe accanto al launcher se non presente (come in passato).
    ''' </summary>
    Public Async Function EnsureSevenZipToolAsync(Optional log As Action(Of String) = Nothing) As Task(Of String)
        Try
            Dim baseDir As String = AppContext.BaseDirectory

            If Platform.IsWindows Then
                Dim local7zr As String = Path.Combine(baseDir, "7zr.exe")
                Dim candidates As New List(Of String) From {
                    local7zr,
                    Path.Combine(baseDir, "7za.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
                }
                For Each candidate In candidates
                    If File.Exists(candidate) AndAlso IsSevenZipExecutableValid(candidate) Then Return candidate
                Next

                Try
                    If File.Exists(local7zr) Then File.Delete(local7zr)
                Catch
                End Try

                log?.Invoke("Download 7z Runtime...")
                Dim tempDownload As String = local7zr & ".tmp"
                Await DownloadWithRetryAsync("https://www.7-zip.org/a/7zr.exe", tempDownload)
                If Not IsSevenZipExecutableValid(tempDownload) Then
                    Throw New Exception("Il file 7z scaricato non e' un eseguibile valido.")
                End If
                File.Move(tempDownload, local7zr, True)
                log?.Invoke("7z Runtime pronto.")
                Return local7zr
            Else
                Dim fromPath = Platform.FindInPath("7zz", "7z", "7za", "7zr")
                If fromPath IsNot Nothing AndAlso IsSevenZipExecutableValid(fromPath) Then Return fromPath
                Return Nothing
            End If
        Catch ex As Exception
            log?.Invoke($"7-Zip non disponibile: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Async Function DownloadWithRetryAsync(url As String, destination As String, Optional maxRetries As Integer = 3) As Task
        Dim lastEx As Exception = Nothing
        For attempt As Integer = 1 To maxRetries
            Dim failed As Boolean = False
            Try
                Using resp = Await http.GetAsync(url)
                    resp.EnsureSuccessStatusCode()
                    Using fs = File.Create(destination)
                        Await resp.Content.CopyToAsync(fs)
                    End Using
                End Using
                Return
            Catch ex As Exception
                lastEx = ex
                failed = True
                Try
                    If File.Exists(destination) Then File.Delete(destination)
                Catch
                End Try
            End Try
            If failed AndAlso attempt < maxRetries Then Await Task.Delay(1000 * attempt)
        Next
        Throw New Exception($"Download fallito dopo {maxRetries} tentativi: {url}", lastEx)
    End Function

    Private Function IsSevenZipExecutableValid(exePath As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(exePath) OrElse Not File.Exists(exePath) Then Return False

            If Platform.IsWindows Then
                Using fs As New FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    If fs.Length < 2 Then Return False
                    If fs.ReadByte() <> AscW("M"c) OrElse fs.ReadByte() <> AscW("Z"c) Then Return False
                End Using
            End If

            Dim psi As New ProcessStartInfo() With {
                .FileName = exePath,
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }
            psi.ArgumentList.Add("i")
            Using p As Process = Process.Start(psi)
                If p Is Nothing Then Return False
                p.StandardOutput.ReadToEndAsync()
                p.StandardError.ReadToEndAsync()
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

End Module
