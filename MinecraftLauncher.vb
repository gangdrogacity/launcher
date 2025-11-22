Imports System.IO
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

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

            '' Costruisci argomenti JVM completi per Forge
            Dim jvmArgsList As New List(Of String)

            '' Memoria
            jvmArgsList.Add($"-Xmx{ramMB}M")
            jvmArgsList.Add($"-Xms{ramMB}M")

            ' aggiungi -XX:+UseG1GC -Dsun.rmi.dgc.server.gcInterval=2147483646 -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M 

            jvmArgsList.Add("-XX:+UseG1GC")
            jvmArgsList.Add("-Dsun.rmi.dgc.server.gcInterval=2147483646")
            jvmArgsList.Add("-XX:+UnlockExperimentalVMOptions")
            jvmArgsList.Add("-XX:G1NewSizePercent=20")
            jvmArgsList.Add("-XX:G1ReservePercent=20")
            jvmArgsList.Add("-XX:MaxGCPauseMillis=50")
            jvmArgsList.Add("-XX:G1HeapRegionSize=32M")

            '' Natives path - Per Forge usa {mcVersion}-{forgeVersion}, NON il nome completo
            '' Per Forge 1.20.1-forge-47.3.33 sarà: C:\...\game\natives\1.20.1-47.3.33\
            Dim nativesVersionName As String = version
            Dim forgeParts As String() = version.Split("-"c)
            If forgeParts.Length >= 3 AndAlso forgeParts(1) = "forge" Then
                '' Per Forge: rimuovi "forge" dal nome (1.20.1-forge-47.3.33 -> 1.20.1-47.3.33)
                nativesVersionName = $"{forgeParts(0)}-{forgeParts(2)}"
            End If
            Dim nativesPath As String = Path.Combine(minecraftDir, "natives", nativesVersionName)
            jvmArgsList.Add($"""-Djava.library.path={nativesPath}""")
            Form1.AddLog($"  Natives path: {nativesPath}")

            '' Parametri di sistema Forge
            jvmArgsList.Add("-Djava.net.preferIPv4Stack=system")
            '' ignoreList: bootstrap libs (nel module path) e language mods (caricati dai mod)
            jvmArgsList.Add("-DignoreList=bootstraplauncher,securejarhandler,asm-commons,asm-util,asm-analysis,asm-tree,asm,JarJarFileSystems,javafmllanguage,lowcodelanguage,mclanguage")
            jvmArgsList.Add($"-DlibraryDirectory={Path.Combine(minecraftDir, "libraries").Replace("\", "/")}")

            '' Costruisci classpath PRIMA (serve per legacyClassPath)
            Dim classpath As String = BuildClasspath(versionData, minecraftDir, version)

            '' FONDAMENTALE: legacyClassPath = TUTTO il classpath!
            '' Bootstrap usa questo per caricare modlauncher e tutti i moduli nel runtime layer
            jvmArgsList.Add($"-DlegacyClassPath={classpath.Replace("\", "/")}")
            Form1.AddLog($"  legacyClassPath configurato con classpath completo")

            '' Il classpath normale può rimanere vuoto o minimale
            jvmArgsList.Add("-cp")
            jvmArgsList.Add(""".""")  '' Classpath dummy

            '' Costruisci module path per Forge
            Dim modulePath As String = BuildModulePath(versionData, minecraftDir, version)
            If Not String.IsNullOrEmpty(modulePath) Then
                jvmArgsList.Add($"-p")
                jvmArgsList.Add(modulePath)
            End If

            '' Aggiungi moduli Java
            jvmArgsList.Add("--add-modules")
            jvmArgsList.Add("ALL-MODULE-PATH")
            jvmArgsList.Add("--add-opens")
            jvmArgsList.Add("java.base/java.util.jar=cpw.mods.securejarhandler")
            jvmArgsList.Add("--add-opens")
            jvmArgsList.Add("java.base/java.lang.invoke=cpw.mods.securejarhandler")
            jvmArgsList.Add("--add-exports")
            jvmArgsList.Add("java.base/sun.security.util=cpw.mods.securejarhandler")
            jvmArgsList.Add("--add-exports")
            jvmArgsList.Add("jdk.naming.dns/com.sun.jndi.dns=java.naming")

            '' Main class
            Dim mainClass As String = versionData("mainClass")?.ToString()
            If String.IsNullOrEmpty(mainClass) Then
                mainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher"
            End If
            jvmArgsList.Add(mainClass)

            '' Game arguments
            Dim gameArgsList As New List(Of String)
            gameArgsList.Add("--username")
            gameArgsList.Add(username)
            gameArgsList.Add("--version")
            gameArgsList.Add("1.20.1")
            gameArgsList.Add("--gameDir")
            gameArgsList.Add(minecraftDir)

            '' FONDAMENTALE: Assets directory e index (per suoni/lingue/texture)
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

            '' Argomenti Forge specifici
            gameArgsList.Add("--launchTarget")
            gameArgsList.Add("forgeclient")
            gameArgsList.Add("--fml.forgeVersion")
            gameArgsList.Add("47.3.33")
            gameArgsList.Add("--fml.mcVersion")
            gameArgsList.Add("1.20.1")
            gameArgsList.Add("--fml.forgeGroup")
            gameArgsList.Add("net.minecraftforge")
            gameArgsList.Add("--fml.mcpVersion")
            gameArgsList.Add("20230612.114412")

            '' Combina tutti gli argomenti
            Dim allArgs As New List(Of String)
            allArgs.AddRange(jvmArgsList)
            allArgs.AddRange(gameArgsList)

            Dim arguments As String = String.Join(" ", allArgs)

            Form1.AddLog("Avvio Minecraft...")

            '' Salva script di avvio per debug
            Dim batContent As String = $"""{javaPath}"" {arguments}{vbCrLf}pause"
            File.WriteAllText(Path.Combine(minecraftDir, "start.bat"), batContent)

            Dim startInfo As New ProcessStartInfo() With {
                .FileName = javaPath,
                .Arguments = arguments,
                .WorkingDirectory = minecraftDir,
                .UseShellExecute = False,
                .CreateNoWindow = True
            }

            Return Process.Start(startInfo)

        Catch ex As Exception
            Form1.AddLog($"Errore avvio Minecraft: {ex.Message}")
            Console.WriteLine($"Stack trace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    '' Costruisce il module path per Forge (librerie che devono essere nel module path)
    '' BASATO SU: https://github.com/alexivkin/minecraft-launcher/blob/main/get-forge-client.sh
    '' Forge 39+ (Minecraft 1.19+) usa il Java Module System invece del classpath tradizionale
    Private Function BuildModulePath(versionData As JObject, minecraftDir As String, version As String) As String
        Dim moduleList As New List(Of String)

        '' Librerie che vanno nel module path per Forge (SOLO Bootstrap)
        '' NON includere modlauncher qui! Viene caricato da Bootstrap dal classpath
        Dim moduleLibraries As String() = {
            "cpw/mods/bootstraplauncher/1.1.2/bootstraplauncher-1.1.2.jar",
            "cpw/mods/securejarhandler/2.1.10/securejarhandler-2.1.10.jar",
            "org/ow2/asm/asm-commons/9.7.1/asm-commons-9.7.1.jar",
            "org/ow2/asm/asm-util/9.7.1/asm-util-9.7.1.jar",
            "org/ow2/asm/asm-analysis/9.7.1/asm-analysis-9.7.1.jar",
            "org/ow2/asm/asm-tree/9.7.1/asm-tree-9.7.1.jar",
            "org/ow2/asm/asm/9.7.1/asm-9.7.1.jar",
            "net/minecraftforge/JarJarFileSystems/0.3.19/JarJarFileSystems-0.3.19.jar"
        }

        For Each libPath In moduleLibraries
            Dim fullPath As String = Path.Combine(minecraftDir, "libraries", libPath.Replace("/", "\"))
            If File.Exists(fullPath) Then
                moduleList.Add(fullPath.Replace("\", "/"))
            End If
        Next

        Form1.AddLog($"  Module path costruito con {moduleList.Count} librerie")
        Return String.Join(";", moduleList)
    End Function

    Private Function BuildClasspath(versionData As JObject, minecraftDir As String, version As String) As String
        Dim classpathList As New List(Of String)
        Dim addedPaths As New HashSet(Of String)() '' Per evitare duplicati

        '' Librerie escluse dal classpath (vanno nel module path o vengono caricate dai mod)
        '' IMPORTANTE: 
        '' 1. Bootstrap libraries: caricate dal module path (-p)
        '' 2. forge-universal: escluso, usiamo solo i JAR separati (fmlloader, fmlearlydisplay, fmlcore)
        '' 3. forge-client: escluso sempre
        Dim excludedFromClasspath As New HashSet(Of String) From {
            "bootstraplauncher", "securejarhandler", "asm-commons", "asm-util",
            "asm-analysis", "asm-tree", "JarJarFileSystems",
            "javafmllanguage", "lowcodelanguage", "mclanguage",
            "forge-1.20.1-47.3.33-universal.jar",
            "forge-1.20.1-47.3.33-client.jar"
        }

        Try
            '' Aggiungi librerie dalla versione corrente
            If versionData("libraries") IsNot Nothing Then
                For Each library As JObject In versionData("libraries")
                    If IsLibraryAllowed(library) Then
                        Dim libName As String = library("name")?.ToString()
                        If Not String.IsNullOrEmpty(libName) Then
                            Dim libPath As String = GetLibraryPath(library, minecraftDir)
                            Dim fileName As String = Path.GetFileName(libPath)

                            '' Verifica che non sia esclusa
                            Dim shouldExclude As Boolean = False
                            For Each excluded In excludedFromClasspath
                                If fileName.Contains(excluded) OrElse libName.Contains(excluded) Then
                                    shouldExclude = True
                                    Exit For
                                End If
                            Next

                            If Not shouldExclude AndAlso File.Exists(libPath) AndAlso Not addedPaths.Contains(libPath) Then
                                classpathList.Add(libPath)
                                addedPaths.Add(libPath)
                            End If
                        End If
                    End If
                Next
            End If

            '' Se ha inheritsFrom, aggiungi librerie dalla versione parent
            If versionData("inheritsFrom") IsNot Nothing Then
                Dim parentVersion As String = versionData("inheritsFrom").ToString()
                Dim parentVersionDir As String = Path.Combine(minecraftDir, "versions", parentVersion)
                Dim parentJsonPath As String = Path.Combine(parentVersionDir, $"{parentVersion}.json")

                If File.Exists(parentJsonPath) Then
                    Dim parentVersionJson As String = File.ReadAllText(parentJsonPath)
                    Dim parentVersionData As JObject = JObject.Parse(parentVersionJson)

                    If parentVersionData("libraries") IsNot Nothing Then
                        For Each library As JObject In parentVersionData("libraries")
                            If IsLibraryAllowed(library) Then
                                Dim libName As String = library("name")?.ToString()
                                If Not String.IsNullOrEmpty(libName) Then
                                    Dim libPath As String = GetLibraryPath(library, minecraftDir)
                                    Dim fileName As String = Path.GetFileName(libPath)

                                    Dim shouldExclude As Boolean = False
                                    For Each excluded In excludedFromClasspath
                                        If fileName.Contains(excluded) OrElse libName.Contains(excluded) Then
                                            shouldExclude = True
                                            Exit For
                                        End If
                                    Next

                                    If Not shouldExclude AndAlso File.Exists(libPath) AndAlso Not addedPaths.Contains(libPath) Then
                                        classpathList.Add(libPath)
                                        addedPaths.Add(libPath)
                                    End If
                                End If
                            End If
                        Next
                    End If
                End If
            End If

            '' Aggiungi TUTTE le librerie Forge al classpath (inclusi client e universal)
            '' IMPORTANTE: NON usare forge-universal.jar, ma solo i JAR separati (fmlloader, fmlcore, fmlearlydisplay)
            '' Questi vengono aggiunti automaticamente tramite la libreria Forge nel JSON
            '' Non serve aggiungere manualmente nulla qui!
            Dim forgeParts As String() = version.Split("-"c)
            If forgeParts.Length >= 3 Then
                Dim mcVersion As String = forgeParts(0)
                Dim forgeVersion As String = forgeParts(2)
                Dim fullForgeVersion As String = $"{mcVersion}-{forgeVersion}"

                '' Nessun JAR manuale da aggiungere - tutto viene dal libraries array
            End If

        Catch ex As Exception
            Form1.AddLog($"Errore costruzione classpath: {ex.Message}")
        End Try

        '' NON aggiungere il client jar per Forge - viene caricato automaticamente da ModLauncher
        '' Il client jar contiene le classi Minecraft che sono già in forge-universal.jar
        '' Dim clientJar As String = Path.Combine(minecraftDir, "versions", version, $"{version}.jar")
        '' If File.Exists(clientJar) AndAlso Not addedPaths.Contains(clientJar) Then
        ''     classpathList.Add(clientJar)
        '' End If

        Form1.AddLog($"  Classpath costruito con {classpathList.Count} librerie")
        Return String.Join(";", classpathList)
    End Function

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
                Dim version As String = parts(2)

                '' Gestione del classificatore (es. :api, :natives-windows)
                Dim classifier As String = If(parts.Length >= 4, parts(3), "")

                Dim fileName As String
                If Not String.IsNullOrEmpty(classifier) Then
                    fileName = $"{artifact}-{version}-{classifier}.jar"
                Else
                    fileName = $"{artifact}-{version}.jar"
                End If

                Return Path.Combine(minecraftDir, "libraries", group, artifact, version, fileName)
            End If

            Return ""
        Catch
            Return ""
        End Try
    End Function
End Class
