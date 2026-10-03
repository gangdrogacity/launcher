Imports System.Diagnostics
Imports System.IO.Compression
Imports Newtonsoft.Json.Linq

''' <summary>
''' Avvio di Minecraft con Fabric Loader su Windows, Linux e macOS.
''' Costruisce il classpath (librerie Fabric + vanilla + client.jar) rispettando le regole
''' per sistema operativo dei version JSON, prepara i natives e lancia la JVM.
''' </summary>
Public Class MinecraftLauncher

    Public Async Function LaunchMinecraft(javaPath As String, username As String, version As String, minecraftDir As String,
                                          ramMB As Integer, screenWidth As Integer, screenHeight As Integer,
                                          Optional showConsole As Boolean = False) As Task(Of Process)
        Try
            If String.IsNullOrEmpty(javaPath) OrElse Not File.Exists(javaPath) Then
                Throw New Exception("Java non trovato")
            End If
            CoreLog.Log($"Usando Java: {javaPath}")
            CoreLog.Log($"Versione Java: {JavaManager.GetJavaVersion(javaPath)}")

            Dim versionDir As String = Path.Combine(minecraftDir, "versions", version)
            Dim versionJsonPath As String = Path.Combine(versionDir, $"{version}.json")
            If Not File.Exists(versionJsonPath) Then
                Throw New Exception($"File di configurazione versione non trovato: {versionJsonPath}")
            End If

            Dim versionData As JObject = JObject.Parse(File.ReadAllText(versionJsonPath))

            ' Dati della versione parent (vanilla) se presente inheritsFrom
            Dim parentVersion As String = If(versionData("inheritsFrom")?.ToString(), "1.20.1")
            Dim parentData As JObject = Nothing
            Dim parentJsonPath As String = Path.Combine(minecraftDir, "versions", parentVersion, $"{parentVersion}.json")
            If File.Exists(parentJsonPath) Then
                parentData = JObject.Parse(File.ReadAllText(parentJsonPath))
            End If

            ' Natives: su Windows arrivano dal manifest (natives\<versione>); su Linux/macOS
            ' vengono estratti dai jar natives-* delle librerie vanilla
            Dim nativesPath As String = Path.Combine(minecraftDir, "natives", parentVersion)
            Directory.CreateDirectory(nativesPath)
            If Not Platform.IsWindows Then
                Await Task.Run(Sub() ExtractNatives(parentData, versionData, minecraftDir, nativesPath))
            End If
            CoreLog.Log($"  Natives path: {nativesPath}")

            Dim psi As New ProcessStartInfo() With {
                .FileName = javaPath,
                .WorkingDirectory = minecraftDir,
                .UseShellExecute = False,
                .CreateNoWindow = Not showConsole
            }
            Dim args = psi.ArgumentList

            ' Memoria e GC
            args.Add($"-Xmx{ramMB}M")
            args.Add($"-Xms{ramMB}M")
            args.Add("-XX:+UseG1GC")
            args.Add("-Dsun.rmi.dgc.server.gcInterval=2147483646")
            args.Add("-XX:+UnlockExperimentalVMOptions")
            args.Add("-XX:G1NewSizePercent=20")
            args.Add("-XX:G1ReservePercent=20")
            args.Add("-XX:MaxGCPauseMillis=50")
            args.Add("-XX:G1HeapRegionSize=32M")

            If Platform.IsMacOS Then
                ' Obbligatorio per GLFW/LWJGL su macOS
                args.Add("-XstartOnFirstThread")
            End If

            args.Add($"-Djava.library.path={nativesPath}")
            args.Add($"-Dorg.lwjgl.librarypath={nativesPath}")
            args.Add($"-Dminecraft.launcher.brand=GangDrogaCity")
            args.Add($"-Dminecraft.launcher.version={Platform.AppVersionString}")

            ' Classpath
            Dim classpath As String = BuildClasspath(versionData, parentData, minecraftDir, parentVersion)
            args.Add("-cp")
            args.Add(classpath)

            ' Main class
            Dim mainClass As String = versionData("mainClass")?.ToString()
            If String.IsNullOrEmpty(mainClass) Then mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient"
            args.Add(mainClass)

            ' Argomenti di gioco (modalita' offline)
            args.Add("--username") : args.Add(username)
            args.Add("--version") : args.Add(version)
            args.Add("--gameDir") : args.Add(minecraftDir)
            args.Add("--assetsDir") : args.Add(Path.Combine(minecraftDir, "assets"))
            args.Add("--assetIndex") : args.Add(GetAssetIndexId(versionData, parentData))
            args.Add("--uuid") : args.Add("00000000-0000-0000-0000-000000000000")
            args.Add("--accessToken") : args.Add("0")
            args.Add("--userType") : args.Add("legacy")
            args.Add("--versionType") : args.Add("release")
            If screenWidth > 0 AndAlso screenHeight > 0 Then
                args.Add("--width") : args.Add(screenWidth.ToString())
                args.Add("--height") : args.Add(screenHeight.ToString())
            End If

            CoreLog.Log("Avvio Minecraft con Fabric...")
            WriteStartScript(javaPath, args, minecraftDir)

            Return Process.Start(psi)

        Catch ex As Exception
            CoreLog.Log($"Errore avvio Minecraft: {ex.Message}")
            Console.WriteLine($"Stack trace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function GetAssetIndexId(versionData As JObject, parentData As JObject) As String
        Try
            Dim idx = If(versionData("assetIndex"), parentData?("assetIndex"))
            If idx IsNot Nothing AndAlso idx("id") IsNot Nothing Then Return idx("id").ToString()
            Dim assets = If(versionData("assets"), parentData?("assets"))
            If assets IsNot Nothing Then Return assets.ToString()
        Catch
        End Try
        Return "5"
    End Function

    ''' <summary>Script di avvio per debug (start.bat su Windows, start.sh altrove).</summary>
    Private Sub WriteStartScript(javaPath As String, args As IList(Of String), minecraftDir As String)
        Try
            If Platform.IsWindows Then
                Dim line = """" & javaPath & """ " & String.Join(" ", args.Select(Function(a) """" & a & """"))
                File.WriteAllText(Path.Combine(minecraftDir, "start.bat"), line & vbCrLf & "pause")
            Else
                Dim line = "'" & javaPath.Replace("'", "'\''") & "' " & String.Join(" ", args.Select(Function(a) "'" & a.Replace("'", "'\''") & "'"))
                Dim scriptPath = Path.Combine(minecraftDir, "start.sh")
                File.WriteAllText(scriptPath, "#!/bin/sh" & vbLf & "cd '" & minecraftDir.Replace("'", "'\''") & "'" & vbLf & line & vbLf)
                Platform.MakeExecutable(scriptPath)
            End If
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Estrae le librerie native (.so/.dylib) dai jar "natives-*" compatibili con l'OS corrente.
    ''' Viene eseguito solo se la cartella natives non contiene gia' librerie.
    ''' </summary>
    Private Sub ExtractNatives(parentData As JObject, versionData As JObject, minecraftDir As String, nativesPath As String)
        Try
            Dim ext As String = If(Platform.IsMacOS, ".dylib", ".so")
            If Directory.EnumerateFiles(nativesPath).Any(Function(f) f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) Then Return

            Dim extracted As Integer = 0
            For Each versionJson In {parentData, versionData}
                If versionJson Is Nothing OrElse versionJson("libraries") Is Nothing Then Continue For
                For Each library As JObject In versionJson("libraries")
                    Dim name As String = library("name")?.ToString()
                    If String.IsNullOrEmpty(name) OrElse Not name.Contains(":natives-") Then Continue For
                    If Not IsLibraryAllowedForCurrentOS(library) Then Continue For

                    Dim jarPath = GetLibraryPath(library, minecraftDir)
                    If Not File.Exists(jarPath) Then Continue For

                    Using archive = ZipFile.OpenRead(jarPath)
                        For Each entry In archive.Entries
                            If String.IsNullOrEmpty(entry.Name) Then Continue For
                            If entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase) Then Continue For
                            Dim lower = entry.Name.ToLowerInvariant()
                            If lower.EndsWith(".so") OrElse lower.EndsWith(".dylib") OrElse lower.EndsWith(".jnilib") Then
                                Dim dest = Path.Combine(nativesPath, entry.Name)
                                entry.ExtractToFile(dest, True)
                                Platform.MakeExecutable(dest)
                                extracted += 1
                            End If
                        Next
                    End Using
                Next
            Next
            If extracted > 0 Then CoreLog.Log($"  Natives estratti: {extracted}")
        Catch ex As Exception
            CoreLog.Log($"  Avviso estrazione natives: {ex.Message}")
        End Try
    End Sub

    Private Function BuildClasspath(versionData As JObject, parentData As JObject, minecraftDir As String, parentVersion As String) As String
        Dim classpathList As New List(Of String)
        Dim addedPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Try
            AddLibrariesToClasspath(versionData, minecraftDir, classpathList, addedPaths)
            If parentData IsNot Nothing Then
                AddLibrariesToClasspath(parentData, minecraftDir, classpathList, addedPaths)
            End If

            Dim clientJar As String = Path.Combine(minecraftDir, "versions", parentVersion, $"{parentVersion}.jar")
            If File.Exists(clientJar) AndAlso Not addedPaths.Contains(clientJar) Then
                classpathList.Add(clientJar)
                addedPaths.Add(clientJar)
            End If
        Catch ex As Exception
            CoreLog.Log($"Errore costruzione classpath: {ex.Message}")
        End Try

        CoreLog.Log($"  Classpath costruito con {classpathList.Count} elementi")
        Return String.Join(Platform.ClasspathSeparator, classpathList)
    End Function

    Private Sub AddLibrariesToClasspath(versionData As JObject, minecraftDir As String, classpathList As List(Of String), addedPaths As HashSet(Of String))
        If versionData("libraries") Is Nothing Then Return

        For Each library As JObject In versionData("libraries")
            If Not IsLibraryAllowedForCurrentOS(library) Then Continue For
            Dim libName As String = library("name")?.ToString()
            If String.IsNullOrEmpty(libName) Then Continue For
            Dim libPath As String = GetLibraryPath(library, minecraftDir)
            If File.Exists(libPath) AndAlso Not addedPaths.Contains(libPath) Then
                classpathList.Add(libPath)
                addedPaths.Add(libPath)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Valuta le "rules" di una libreria del version JSON per l'OS/architettura corrente
    ''' (semantica Mojang: senza regole = permessa; con regole, vince l'ultima applicabile).
    ''' </summary>
    Public Shared Function IsLibraryAllowedForCurrentOS(library As JObject) As Boolean
        Try
            Dim rules = TryCast(library("rules"), JArray)
            If rules Is Nothing OrElse rules.Count = 0 Then Return True

            Dim allowed As Boolean = False
            For Each rule As JObject In rules
                Dim action As String = rule("action")?.ToString()
                Dim applies As Boolean = True
                Dim os = TryCast(rule("os"), JObject)
                If os IsNot Nothing Then
                    Dim osName = os("name")?.ToString()
                    If Not String.IsNullOrEmpty(osName) AndAlso Not osName.Equals(Platform.MojangOSName, StringComparison.OrdinalIgnoreCase) Then applies = False
                    Dim osArch = os("arch")?.ToString()
                    If applies AndAlso Not String.IsNullOrEmpty(osArch) Then
                        Dim currentArch = If(Platform.IsArm64, "arm64", "x64")
                        If Not osArch.Equals(currentArch, StringComparison.OrdinalIgnoreCase) Then applies = False
                    End If
                End If
                If applies Then allowed = (action = "allow")
            Next
            Return allowed
        Catch
            Return True
        End Try
    End Function

    Private Function GetLibraryPath(library As JObject, minecraftDir As String) As String
        Try
            Dim name As String = library("name").ToString()
            Dim parts() As String = name.Split(":"c)
            If parts.Length < 3 Then Return ""

            Dim group As String = parts(0).Replace("."c, Path.DirectorySeparatorChar)
            Dim artifact As String = parts(1)
            Dim ver As String = parts(2)
            Dim classifier As String = If(parts.Length >= 4, parts(3), "")
            Dim fileName As String = If(String.IsNullOrEmpty(classifier), $"{artifact}-{ver}.jar", $"{artifact}-{ver}-{classifier}.jar")

            Return Path.Combine(minecraftDir, "libraries", group, artifact, ver, fileName)
        Catch
            Return ""
        End Try
    End Function
End Class
