Imports System.IO
Imports System.Net.Http
Imports System.IO.Compression
Imports System.Threading.Tasks

Public Module JavaDownloader
    Private Const JAVA_DOWNLOAD_URL As String = "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jdk/hotspot/normal/eclipse"
    Private Const JAVA_DIR As String = "java"
    
    Public Async Function EnsureJavaInstalled() As Task(Of String)
        Try
            '' Prima prova a trovare Java esistente
            Dim existingJava As String = TryFindExistingJava()
            If Not String.IsNullOrEmpty(existingJava) Then
                Console.WriteLine("Java trovato: " & existingJava)
                Return existingJava
            End If
            
            '' Se non trovato, scarica e installa
            Console.WriteLine("Java non trovato. Scaricamento in corso...")
            Return Await DownloadAndInstallJava()
            
        Catch ex As Exception
            Throw New Exception($"Errore nel garantire Java: {ex.Message}")
        End Try
    End Function
    
    Private Function TryFindExistingJava() As String
        Try
            Return JavaHelper.FindExistingJava()
        Catch
            Return Nothing
        End Try
    End Function
    
    Private Async Function DownloadAndInstallJava() As Task(Of String)
        Try
            Dim tempDir As String = Path.GetTempPath()
            Dim javaZipPath As String = Path.Combine(tempDir, "openjdk.zip")
            Dim currentDir As String = Environment.CurrentDirectory
            Dim extractDir As String = Path.Combine(currentDir, JAVA_DIR)
            
            '' Scarica Java
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(10)
                Console.WriteLine("Scaricamento Java JDK 17...")
                
                Using response = Await client.GetAsync(JAVA_DOWNLOAD_URL)
                    response.EnsureSuccessStatusCode()
                    
                    Using fileStream = File.Create(javaZipPath)
                        Await response.Content.CopyToAsync(fileStream)
                    End Using
                End Using
            End Using
            
            '' Estrai Java
            Console.WriteLine("Estrazione Java...")
            If Directory.Exists(extractDir) Then
                Directory.Delete(extractDir, True)
            End If
            
            ZipFile.ExtractToDirectory(javaZipPath, extractDir)
            
            '' Trova la directory JDK estratta
            Dim jdkDir As String = FindJdkDirectory(extractDir)
            If String.IsNullOrEmpty(jdkDir) Then
                Throw New Exception("Directory JDK non trovata dopo estrazione")
            End If
            
            '' Percorso Java executable
            Dim javaExe As String = Path.Combine(jdkDir, "bin", "java.exe")
            If Not File.Exists(javaExe) Then
                Throw New Exception("Java.exe non trovato dopo installazione")
            End If
            
            '' Pulizia
            If File.Exists(javaZipPath) Then
                File.Delete(javaZipPath)
            End If
            
            Console.WriteLine($"Java installato correttamente in: {javaExe}")
            Return javaExe
            
        Catch ex As Exception
            Throw New Exception($"Errore durante download/installazione Java: {ex.Message}")
        End Try
    End Function
    
    Private Function FindJdkDirectory(extractDir As String) As String
        Try
            '' Cerca directory che contiene bin/java.exe
            For Each subDir In Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories)
                Dim javaExe As String = Path.Combine(subDir, "bin", "java.exe")
                If File.Exists(javaExe) Then
                    Return subDir
                End If
            Next
            
            '' Se non trovato, prova nella directory root
            Dim rootJavaExe As String = Path.Combine(extractDir, "bin", "java.exe")
            If File.Exists(rootJavaExe) Then
                Return extractDir
            End If
            
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function
    
    Public Function GetJavaVersion(javaPath As String) As String
        Try
            Dim startInfo As New ProcessStartInfo() With {
                .FileName = javaPath,
                .Arguments = "-version",
                .UseShellExecute = False,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }
            
            Using process As Process = Process.Start(startInfo)
                Dim output As String = process.StandardError.ReadToEnd()
                process.WaitForExit()
                
                '' Estrai versione dalla prima riga
                Dim lines() As String = output.Split(vbCrLf)
                If lines.Length > 0 Then
                    Return lines(0).Trim()
                End If
            End Using
        Catch
        End Try
        
        Return "Versione sconosciuta"
    End Function
End Module
