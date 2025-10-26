Imports System.IO
Imports System.Net.Http

Public Class ForgeInstaller
    Public Async Function InstallForge(forgeVersion As String, minecraftDir As String) As Task(Of Boolean)
        Dim installerPath As String = Path.Combine(Path.GetTempPath(), "forge-installer.jar")

        Try
            '' URL per scaricare Forge installer
            Dim forgeUrl As String = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar"

            '' Scarica installer
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(5)
                Console.WriteLine("Scaricamento Forge installer...")

                Using response = Await client.GetAsync(forgeUrl)
                    response.EnsureSuccessStatusCode()
                    Using fileStream = File.Create(installerPath)
                        Await response.Content.CopyToAsync(fileStream)
                    End Using
                End Using
            End Using

            '' Assicurati che Java sia disponibile
            Dim javaPath As String = Await JavaHelper.FindJavaPath()
            Console.WriteLine($"Usando Java: {javaPath}")

            '' Verifica che Java sia valido
            If Not JavaHelper.IsJavaValid(javaPath) Then
                Throw New Exception("Java non valido o non funzionante")
            End If

            '' Esegui installer
            Console.WriteLine("Installazione Forge in corso...")
            Dim startInfo As New ProcessStartInfo() With {
                .FileName = javaPath,
                .Arguments = $"-jar ""{installerPath}"" --installClient --mcDir ""{minecraftDir}""",
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }

            Using forgeProcess As Process = Process.Start(startInfo)
                Await forgeProcess.WaitForExitAsync()

                If forgeProcess.ExitCode = 0 Then
                    Console.WriteLine("Forge installato correttamente!")
                    Return True
                Else
                    Dim errorOutput As String = forgeProcess.StandardError.ReadToEnd()
                    Console.WriteLine($"Errore installazione Forge: {errorOutput}")
                    Return False
                End If
            End Using

        Catch ex As Exception
            Console.WriteLine($"Errore installazione Forge: {ex.Message}")
            Return False
        Finally
            '' Pulizia file temporaneo
            Try
                If File.Exists(installerPath) Then
                    File.Delete(installerPath)
                End If
            Catch
                '' Ignora errori di pulizia
            End Try
        End Try
    End Function
End Class
