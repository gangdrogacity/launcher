Imports System.IO
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Launcher Minecraft con supporto Fabric Loader.
''' Costruisce classpath standard con librerie Fabric + vanilla + client.jar.
''' Nessun module system necessario (a differenza di Forge).
''' </summary>
Public Class MinecraftLauncher
    Public Async Function LaunchMinecraft(username As String, version As String, minecraftDir As String, ramMB As Integer) As Task(Of Process)
        Try
            '' Assicurati che Java sia disponibile
            Dim javaPath As String = Await JavaHelper.FindJavaPath()
            Form1.AddLog($"Usando Java: {javaPath}")
            Form1.AddLog($"Versione Java: {JavaDownloader.GetJavaVersion(javaPath)}")

            '' Verifica che Java sia valido
            If Not JavaHelper.IsJavaValid(javaPath) Then
                Throw New Exception("Java non valido o non funzionante")
            End If

            Dim versionDir As String = Path.Combine(minecraftDir, "versions", version)
            Dim versionJsonPath As String = Path.Combine(versionDir, $"{version}.json")

            If Not File.Exists(versionJsonPath) Then
                Throw New Exception($"File di configurazione versione non trovato: {versionJsonPath}")
            End If

            Dim versionJson As String = File.ReadAllText(versionJsonPath)
            Dim versionData As JObject = JObject.Parse(versionJson)

            '' Carica dati versione parent (vanilla) se presente inheritsFrom
            Dim parentData As JObject = Nothing
            If versionData("inheritsFrom") IsNot Nothing Then
                Dim parentVersion As String = versionData("inheritsFrom").ToString()
                Dim parentJsonPath As String = Path.Combine(minecraftDir, "versions", parentVersion, $"{parentVersion}.json")
                If File.Exists(parentJsonPath) Then
                    parentData = JObject.Parse(File.ReadAllText(parentJsonPath))
                End If
            End If

            '' Costruisci argomenti JVM per Fabric
            Dim jvmArgsList As New List(Of String)

            '' Memoria
            jvmArgsList.Add($"-Xmx{ramMB}M")
            jvmArgsList.Add($"-Xms{ramMB}M")

            '' GC tuning
            jvmArgsList.Add("-XX:+UseG1GC")
            jvmArgsList.Add("-Dsun.rmi.dgc.server.gcInterval=2147483646")
            jvmArgsList.Add("-XX:+UnlockExperimentalVMOptions")
            jvmArgsList.Add("-XX:G1NewSizePercent=20")
            jvmArgsList.Add("-XX:G1ReservePercent=20")
            jvmArgsList.Add("-XX:MaxGCPauseMillis=50")
            jvmArgsList.Add("-XX:G1HeapRegionSize=32M")

            '' Natives path - Per Fabric usa la versione vanilla di MC
            Dim nativesPath As String = Path.Combine(minecraftDir, "natives", "1.20.1")
            Directory.CreateDirectory(nativesPath)
            jvmArgsList.Add($"""-Djava.library.path={nativesPath}""")
            Form1.AddLog($"  Natives path: {nativesPath}")

            '' Costruisci classpath completo per Fabric (librerie Fabric + vanilla + client.jar)
            Dim classpath As String = BuildClasspath(versionData, parentData, minecraftDir, version)
            jvmArgsList.Add("-cp")
            jvmArgsList.Add($"""{classpath}""")

            '' Main class dalla versione Fabric
            Dim mainClass As String = versionData("mainClass")?.ToString()
            If String.IsNullOrEmpty(mainClass) Then
                mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient"
            End If
            jvmArgsList.Add(mainClass)

            '' Game arguments
            Dim gameArgsList As New List(Of String)
            gameArgsList.Add("--username")
            gameArgsList.Add(username)
            gameArgsList.Add("--version")
            gameArgsList.Add(version)
            gameArgsList.Add("--gameDir")
            gameArgsList.Add(minecraftDir)

            '' Assets directory e index (per suoni/lingue/texture)
            Dim assetsDir As String = Path.Combine(minecraftDir, "assets")
            gameArgsList.Add("--assetsDir")
            gameArgsList.Add(assetsDir)
            gameArgsList.Add("--assetIndex")
            gameArgsList.Add("5")  '' Asset index per Minecraft 1.20.1

            '' Parametri di autenticazione (modalità offline)
            gameArgsList.Add("--uuid")
            gameArgsList.Add("00000000-0000-0000-0000-000000000000")
            gameArgsList.Add("--accessToken")
            gameArgsList.Add("0")
            gameArgsList.Add("--userType")
            gameArgsList.Add("legacy")

            '' Risoluzione schermo
            gameArgsList.Add("--width")
            gameArgsList.Add(Screen.PrimaryScreen.Bounds.Width.ToString())
            gameArgsList.Add("--height")
            gameArgsList.Add(Screen.PrimaryScreen.Bounds.Height.ToString())

            '' Combina tutti gli argomenti
            Dim allArgs As New List(Of String)
            allArgs.AddRange(jvmArgsList)
            allArgs.AddRange(gameArgsList)

            Dim arguments As String = String.Join(" ", allArgs)

            Form1.AddLog("Avvio Minecraft con Fabric...")

            '' Salva script di avvio per debug
            Dim batContent As String = $"""{javaPath}"" {arguments}{vbCrLf}pause"
            File.WriteAllText(Path.Combine(minecraftDir, "start.bat"), batContent)

            Dim startInfo As New ProcessStartInfo() With {
                .FileName = javaPath,
                .Arguments = arguments,
                .WorkingDirectory = minecraftDir,
                .UseShellExecute = False,
                .CreateNoWindow = Not Form1.devmode
            }

            Return Process.Start(startInfo)

        Catch ex As Exception
            Form1.AddLog($"Errore avvio Minecraft: {ex.Message}")
            Console.WriteLine($"Stack trace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Costruisce il classpath per Fabric: librerie Fabric + librerie vanilla + client.jar
    ''' Fabric non usa il Java Module System, tutto va nel classpath tradizionale.
    ''' </summary>
    Private Function BuildClasspath(versionData As JObject, parentData As JObject, minecraftDir As String, version As String) As String
        Dim classpathList As New List(Of String)
        Dim addedPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Try
            '' Aggiungi librerie dalla versione Fabric
            AddLibrariesToClasspath(versionData, minecraftDir, classpathList, addedPaths)

            '' Aggiungi librerie dalla versione parent (vanilla)
            If parentData IsNot Nothing Then
                AddLibrariesToClasspath(parentData, minecraftDir, classpathList, addedPaths)
            End If

            '' Aggiungi client.jar vanilla
            Dim parentVersion As String = If(versionData("inheritsFrom")?.ToString(), "1.20.1")
            Dim clientJar As String = Path.Combine(minecraftDir, "versions", parentVersion, $"{parentVersion}.jar")
            If File.Exists(clientJar) AndAlso Not addedPaths.Contains(clientJar) Then
                classpathList.Add(clientJar)
                addedPaths.Add(clientJar)
            End If

        Catch ex As Exception
            Form1.AddLog($"Errore costruzione classpath: {ex.Message}")
        End Try

        Form1.AddLog($"  Classpath costruito con {classpathList.Count} elementi")
        Return String.Join(";", classpathList)
    End Function

    ''' <summary>
    ''' Aggiunge le librerie di un version JSON al classpath
    ''' </summary>
    Private Sub AddLibrariesToClasspath(versionData As JObject, minecraftDir As String, classpathList As List(Of String), addedPaths As HashSet(Of String))
        If versionData("libraries") Is Nothing Then Return

        For Each library As JObject In versionData("libraries")
            If IsLibraryAllowed(library) Then
                Dim libName As String = library("name")?.ToString()
                If Not String.IsNullOrEmpty(libName) Then
                    Dim libPath As String = GetLibraryPath(library, minecraftDir)
                    If File.Exists(libPath) AndAlso Not addedPaths.Contains(libPath) Then
                        classpathList.Add(libPath)
                        addedPaths.Add(libPath)
                    End If
                End If
            End If
        Next
    End Sub

    Private Function IsLibraryAllowed(library As JObject) As Boolean
        Try
            '' Controlla se ci sono regole di compatibilità
            If library("rules") IsNot Nothing Then
                For Each rule In library("rules")
                    Dim action As String = rule("action").ToString()
                    If rule("os") IsNot Nothing Then
                        Dim osName As String = rule("os")("name").ToString()
                        If action = "allow" And osName = "windows" Then
                            Return True
                        ElseIf action = "disallow" And osName = "windows" Then
                            Return False
                        End If
                    ElseIf action = "allow" Then
                        Return True
                    End If
                Next
                Return False '' Se ci sono regole ma nessuna permette Windows
            End If

            Return True '' Nessuna regola = permessa
        Catch
            Return True '' In caso di errore, includi la libreria
        End Try
    End Function

    Private Function GetLibraryPath(library As JObject, minecraftDir As String) As String
        Try
            Dim name As String = library("name").ToString()
            Dim parts() As String = name.Split(":"c)

            If parts.Length >= 3 Then
                Dim group As String = parts(0).Replace(".", "\")
                Dim artifact As String = parts(1)
                Dim ver As String = parts(2)

                '' Gestione del classificatore (es. :api, :natives-windows)
                Dim classifier As String = If(parts.Length >= 4, parts(3), "")

                Dim fileName As String
                If Not String.IsNullOrEmpty(classifier) Then
                    fileName = $"{artifact}-{ver}-{classifier}.jar"
                Else
                    fileName = $"{artifact}-{ver}.jar"
                End If

                Return Path.Combine(minecraftDir, "libraries", group, artifact, ver, fileName)
            End If

            Return ""
        Catch
            Return ""
        End Try
    End Function
End Class
