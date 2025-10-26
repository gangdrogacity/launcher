Imports System.IO

Public Module JavaHelper
    Public Async Function FindJavaPath() As Task(Of String)
        '' Prima prova con il metodo esistente (cercando Java 17)
        Dim existingJava As String = FindExistingJava(preferredVersion:=17)
        If Not String.IsNullOrEmpty(existingJava) Then
            Return existingJava
        End If

        '' Se non trovato, usa il downloader automatico
        Return Nothing
    End Function

    Public Function FindExistingJava(Optional preferredVersion As Integer = 17) As String
        '' Controlla in gameDir/java se ci sono cartelle che contengono bin e dentro java

        Dim gameDir As String = Form1.gameDir
        Dim javaDir As String = Path.Combine(gameDir, "java")


        If Directory.Exists(javaDir) Then
            Dim subDirs As String() = Directory.GetDirectories(javaDir)
            For Each dir As String In subDirs
                Dim javaBinPath As String = Path.Combine(dir, "bin", "java.exe")
                If File.Exists(javaBinPath) Then
                    If IsJavaValid(javaBinPath) Then
                        Return javaBinPath
                    End If
                End If
            Next
        End If





    End Function

    Public Function IsJavaValid(javaPath As String) As Boolean
        Try
            If String.IsNullOrEmpty(javaPath) OrElse Not File.Exists(javaPath) Then
                Return False
            End If

            Dim startInfo As New ProcessStartInfo() With {
                .FileName = javaPath,
                .Arguments = "-version",
                .UseShellExecute = False,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }

            Using process As Process = Process.Start(startInfo)
                process.WaitForExit(5000) '' Timeout di 5 secondi
                Return process.ExitCode = 0
            End Using
        Catch
            Return False
        End Try
    End Function
End Module
