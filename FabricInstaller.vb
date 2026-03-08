Imports System.IO
Imports System.Net.Http
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Installer per Fabric Loader - Utilizza il Fabric Meta API per scaricare
''' il profilo di gioco e le librerie necessarie senza bisogno di un installer JAR esterno.
''' Gestisce anche la migrazione da una precedente installazione Forge.
''' </summary>
Public Class FabricInstaller
    Private Shared ReadOnly httpClient As New HttpClient() With {
        .Timeout = TimeSpan.FromMinutes(5)
    }

    Private Const FABRIC_META_BASE As String = "https://meta.fabricmc.net/v2/versions/loader"

    ''' <summary>
    ''' Installa Fabric scaricando il profilo dal Fabric Meta API e le librerie necessarie
    ''' </summary>
    Public Async Function InstallFabric(loaderVersion As String, mcVersion As String, minecraftDir As String) As Task(Of Boolean)
        Try
            Dim fabricVersionId As String = $"fabric-loader-{loaderVersion}-{mcVersion}"
            Dim versionDir As String = Path.Combine(minecraftDir, "versions", fabricVersionId)
            Dim versionJsonPath As String = Path.Combine(versionDir, $"{fabricVersionId}.json")

            ' 1. Scarica il profilo JSON da Fabric Meta API
            Form1.SafeInvoke(Sub() Form1.AddLog("Download profilo Fabric..."))

            Dim profileUrl As String = $"{FABRIC_META_BASE}/{mcVersion}/{loaderVersion}/profile/json"
            Dim profileJson As String

            httpClient.DefaultRequestHeaders.Clear()
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GangDrogaCity-Launcher/1.0")

            Using response = Await httpClient.GetAsync(profileUrl)
                response.EnsureSuccessStatusCode()
                profileJson = Await response.Content.ReadAsStringAsync()
            End Using

            ' Salva il profilo JSON
            Directory.CreateDirectory(versionDir)
            File.WriteAllText(versionJsonPath, profileJson)
            Form1.SafeInvoke(Sub() Form1.AddLog($"Profilo Fabric salvato: {fabricVersionId}"))

            ' 2. Scarica tutte le librerie Fabric
            Dim versionData As JObject = JObject.Parse(profileJson)
            Dim libraries As JArray = CType(versionData("libraries"), JArray)

            If libraries IsNot Nothing AndAlso libraries.Count > 0 Then
                Form1.SafeInvoke(Sub() Form1.AddLog($"Download {libraries.Count} librerie Fabric..."))

                Dim successCount As Integer = 0
                Dim failCount As Integer = 0

                For Each fabricLib As JObject In libraries
                    Try
                        Dim libName As String = fabricLib("name")?.ToString()
                        If String.IsNullOrEmpty(libName) Then Continue For

                        Dim libPath As String = GetLibraryPath(libName, minecraftDir)

                        If Not File.Exists(libPath) OrElse New FileInfo(libPath).Length < 100 Then
                            Directory.CreateDirectory(Path.GetDirectoryName(libPath))

                            ' Prova diversi repository Maven
                            Dim urls As New List(Of String)

                            ' URL dal JSON (Fabric Maven)
                            If fabricLib("url") IsNot Nothing Then
                                urls.Add(BuildMavenUrl(fabricLib("url").ToString().TrimEnd("/"c), libName))
                            End If

                            ' Fabric Maven come fallback
                            urls.Add(BuildMavenUrl("https://maven.fabricmc.net", libName))

                            ' Maven Central come ultimo fallback
                            urls.Add(BuildMavenUrl("https://repo1.maven.org/maven2", libName))

                            Dim downloaded As Boolean = False
                            For Each url In urls
                                If downloaded Then Exit For
                                For retry As Integer = 1 To 3
                                    Try
                                        Using dlResponse = Await httpClient.GetAsync(url)
                                            If dlResponse.IsSuccessStatusCode Then
                                                Using fs = File.Create(libPath)
                                                    Await dlResponse.Content.CopyToAsync(fs)
                                                End Using
                                                downloaded = True
                                                Exit For
                                            End If
                                        End Using
                                    Catch
                                        Dim shouldDelay As Boolean = (retry < 3)
                                    End Try
                                    If Not downloaded AndAlso retry < 3 Then
                                        Threading.Thread.Sleep(1000 * retry)
                                    End If
                                Next
                            Next

                            If downloaded Then
                                successCount += 1
                            Else
                                failCount += 1
                                Dim displayName = libName
                                Form1.SafeInvoke(Sub() Form1.AddLog($"  ✗ Libreria mancante: {displayName}"))
                            End If
                        Else
                            successCount += 1
                        End If

                    Catch ex As Exception
                        failCount += 1
                    End Try
                Next

                Form1.SafeInvoke(Sub() Form1.AddLog($"Librerie Fabric: {successCount} OK, {failCount} fallite"))

                If failCount > 0 Then
                    Return False
                End If
            End If

            Form1.SafeInvoke(Sub() Form1.AddLog("Fabric installato correttamente!"))
            Return True

        Catch ex As Exception
            Form1.SafeInvoke(Sub() Form1.AddLog($"Errore installazione Fabric: {ex.Message}"))
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Rimuove una precedente installazione di Forge per migrare a Fabric
    ''' </summary>
    Public Sub CleanupForgeInstallation(minecraftDir As String)
        Try
            ' Rimuovi la cartella della versione Forge
            Dim forgeVersionDir As String = Path.Combine(minecraftDir, "versions", "1.20.1-forge-47.3.33")
            If Directory.Exists(forgeVersionDir) Then
                Directory.Delete(forgeVersionDir, True)
                Form1.SafeInvoke(Sub() Form1.AddLog("Rimossa versione Forge precedente"))
            End If

            ' Rimuovi i marker Forge
            Dim markers() As String = {
                Path.Combine(minecraftDir, "forgeInstalled"),
                Path.Combine(minecraftDir, "forgeDownloaded")
            }
            For Each marker In markers
                If File.Exists(marker) Then File.Delete(marker)
            Next

            ' Rimuovi file installer e batch Forge
            Dim filesToDelete() As String = {
                Path.Combine(minecraftDir, "forge-installer.jar"),
                Path.Combine(minecraftDir, "install_forge.bat")
            }
            For Each f In filesToDelete
                If File.Exists(f) Then File.Delete(f)
            Next

            Form1.SafeInvoke(Sub() Form1.AddLog("Pulizia Forge completata"))
        Catch ex As Exception
            Form1.SafeInvoke(Sub() Form1.AddLog($"Avviso pulizia Forge: {ex.Message}"))
        End Try
    End Sub

    ''' <summary>
    ''' Verifica se Fabric è già installato correttamente
    ''' </summary>
    Public Function IsFabricInstalled(loaderVersion As String, mcVersion As String, minecraftDir As String) As Boolean
        Dim fabricVersionId As String = $"fabric-loader-{loaderVersion}-{mcVersion}"
        Dim versionJsonPath As String = Path.Combine(minecraftDir, "versions", fabricVersionId, $"{fabricVersionId}.json")
        Return File.Exists(versionJsonPath)
    End Function

    ''' <summary>
    ''' Verifica se è presente una precedente installazione di Forge
    ''' </summary>
    Public Function IsForgeInstalled(minecraftDir As String) As Boolean
        Dim forgeVersionDir As String = Path.Combine(minecraftDir, "versions", "1.20.1-forge-47.3.33")
        Dim forgeMarker As String = Path.Combine(minecraftDir, "forgeInstalled")
        Return Directory.Exists(forgeVersionDir) OrElse File.Exists(forgeMarker)
    End Function

    Private Function BuildMavenUrl(baseUrl As String, mavenName As String) As String
        Dim parts() As String = mavenName.Split(":"c)
        If parts.Length < 3 Then Return ""

        Dim groupId As String = parts(0).Replace(".", "/")
        Dim artifactId As String = parts(1)
        Dim version As String = parts(2)
        Dim classifier As String = If(parts.Length > 3, "-" & parts(3), "")
        Dim fileName As String = $"{artifactId}-{version}{classifier}.jar"

        Return $"{baseUrl}/{groupId}/{artifactId}/{version}/{fileName}"
    End Function

    Private Function GetLibraryPath(libraryName As String, minecraftDir As String) As String
        Dim parts() As String = libraryName.Split(":"c)
        If parts.Length < 3 Then Throw New ArgumentException($"Nome libreria invalido: {libraryName}")

        Dim groupId As String = parts(0).Replace("."c, Path.DirectorySeparatorChar)
        Dim artifactId As String = parts(1)
        Dim version As String = parts(2)
        Dim classifier As String = If(parts.Length > 3, "-" & parts(3), "")
        Dim fileName As String = $"{artifactId}-{version}{classifier}.jar"

        Return Path.Combine(minecraftDir, "libraries", groupId, artifactId, version, fileName)
    End Function
End Class
