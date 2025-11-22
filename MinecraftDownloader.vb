Imports System.Net.Http
Imports System.IO
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports System.Threading
Imports System.Security.Cryptography
Imports System.Linq
Imports System.Net

''' <summary>
''' Downloader robusto per Minecraft vanilla - Gestisce download completo di client.jar, assets (suoni/lingue) e librerie
''' Architettura fail-safe con retry automatici, verifica SHA1, e ripristino da errori
''' </summary>
Public Class MinecraftDownloader
    Private Shared ReadOnly httpClient As New HttpClient() With {
        .Timeout = TimeSpan.FromMinutes(10)
    }

    ' Cache per evitare re-download di manifest
    Private Shared manifestCache As JObject = Nothing
    Private Shared lastManifestUpdate As DateTime = DateTime.MinValue
    Private Const MANIFEST_CACHE_HOURS As Integer = 1

    ' Configurazione retry e download
    Private Const MAX_RETRIES As Integer = 3
    Private Const MAX_CONCURRENT_DOWNLOADS As Integer = 8

#Region "API Pubblica"

    ''' <summary>
    ''' Scarica e verifica una versione completa di Minecraft vanilla
    ''' Gestisce automaticamente: client.jar, assets (suoni/lingue), librerie
    ''' </summary>
    Public Async Function DownloadMinecraftVersion(version As String, minecraftDir As String) As Task(Of Boolean)
        Try
            Log($"🔍 Verifica installazione Minecraft {version}...")

            ' FASE 1: Verifica rapida file base
            Log("  📋 Controllo file base...")
            Dim hasBaseFiles = QuickVerifyBaseFiles(version, minecraftDir)
            
            If Not hasBaseFiles Then
                Log("  ⚠ File base mancanti")
            End If

            ' FASE 2: Ottieni metadati versione
            Dim versionData As JObject = Await GetVersionMetadataAsync(version, minecraftDir)
            If versionData Is Nothing Then
                LogError("✗ Impossibile ottenere metadati versione")
                Return False
            End If

            ' FASE 3: Verifica dettagliata di TUTTI i componenti
            Log("  🔎 Verifica dettagliata componenti...")
            Dim verificationResult = Await VerifyAllComponentsAsync(versionData, version, minecraftDir)

            ' FASE 4: Scarica solo componenti mancanti
            If verificationResult.AllComplete Then
                Log("✅ Minecraft completamente installato e verificato!")
                Return True
            End If

            Log($"  📥 Download necessario - Jar={Not verificationResult.JarValid}, Assets={verificationResult.MissingAssets}, Lib={verificationResult.MissingLibraries}")

            ' Prepara task di download
            Dim downloadTasks As New List(Of Task(Of Boolean))

            ' Download client.jar se necessario
            If Not verificationResult.JarValid Then
                downloadTasks.Add(DownloadClientJarAsync(versionData, version, minecraftDir))
            End If

            ' Download assets se necessario
            If verificationResult.MissingAssets > 0 Then
                downloadTasks.Add(DownloadAllAssetsAsync(versionData, minecraftDir))
            End If

            ' Download librerie se necessario
            If verificationResult.MissingLibraries > 0 Then
                downloadTasks.Add(DownloadAllLibrariesAsync(versionData, minecraftDir))
            End If

            ' Esegui tutti i download in parallelo
            If downloadTasks.Count > 0 Then
                Log($"📥 Avvio {downloadTasks.Count} task di download...")
                Dim results = Await Task.WhenAll(downloadTasks)

                If results.All(Function(r) r) Then
                    Log("✅ Download Minecraft completato con successo!")
                    Return True
                Else
                    LogError("❌ Alcuni download sono falliti")
                    Return False
                End If
            Else
                Log("✅ Nessun componente da scaricare")
                Return True
            End If

        Catch ex As Exception
            LogError($"❌ Errore critico: {ex.Message}")
            Console.WriteLine($"Stack: {ex.StackTrace}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Scarica solo le dipendenze di una versione (es. Forge)
    ''' </summary>
    Public Async Function DownloadVersionDependencies(version As String, gamedir As String) As Task
        Try
            Log($"📚 Verifica dipendenze {version}...")

            Dim versionData As JObject = Await GetVersionMetadataAsync(version, gamedir, False)
            If versionData Is Nothing Then
                LogError("✗ Impossibile ottenere metadati")
                Return
            End If

            ' Download librerie critiche Forge prima
            If version.Contains("-forge-") Then
                Await DownloadForgeCriticalLibrariesAsync(version, gamedir)
            End If

            ' Download tutte le librerie
            Await DownloadAllLibrariesAsync(versionData, gamedir)

            Log("✅ Verifica dipendenze completata")

        Catch ex As Exception
            LogError($"❌ Errore dipendenze: {ex.Message}")
        End Try
    End Function

#End Region

#Region "Verifica Componenti"

    ''' <summary>
    ''' Verifica rapida file base
    ''' </summary>
    Private Function QuickVerifyBaseFiles(version As String, minecraftDir As String) As Boolean
        Try
            Dim versionDir = Path.Combine(minecraftDir, "versions", version)
            Dim clientJar = Path.Combine(versionDir, $"{version}.jar")
            Dim versionJson = Path.Combine(versionDir, $"{version}.json")
            Return File.Exists(clientJar) AndAlso File.Exists(versionJson)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Verifica dettagliata TUTTI i componenti
    ''' </summary>
    Private Async Function VerifyAllComponentsAsync(versionData As JObject, version As String, minecraftDir As String) As Task(Of VerificationResult)
        Dim result As New VerificationResult()

        Try
            ' Verifica client.jar
            result.JarValid = Await IsClientJarValidAsync(versionData, version, minecraftDir)
            If Not result.JarValid Then Log("  ⚠ Client.jar invalido")

            ' Verifica TUTTI gli assets
            Dim assetsResult = Await VerifyAllAssetsAsync(versionData, minecraftDir)
            result.MissingAssets = assetsResult.MissingCount
            result.TotalAssets = assetsResult.TotalCount

            If result.MissingAssets > 0 Then
                Log($"  ⚠ Assets: {result.MissingAssets}/{result.TotalAssets} mancanti")
            Else If result.TotalAssets > 0 Then
                Log($"  ✓ Assets: {result.TotalAssets} completi")
            End If

            ' Verifica librerie
            Dim libResult = Await VerifyAllLibrariesAsync(versionData, minecraftDir)
            result.MissingLibraries = libResult.MissingCount
            result.TotalLibraries = libResult.TotalCount

            If result.MissingLibraries > 0 Then
                Log($"  ⚠ Librerie: {result.MissingLibraries}/{result.TotalLibraries} mancanti")
            Else If result.TotalLibraries > 0 Then
                Log($"  ✓ Librerie: {result.TotalLibraries} complete")
            End If

            result.AllComplete = result.JarValid AndAlso result.MissingAssets = 0 AndAlso result.MissingLibraries = 0

        Catch ex As Exception
            LogError($"  ✗ Errore verifica: {ex.Message}")
            result.AllComplete = False
        End Try

        Return result
    End Function

    ''' <summary>
    ''' Verifica validità client.jar con SHA1
    ''' </summary>
    Private Async Function IsClientJarValidAsync(versionData As JObject, version As String, minecraftDir As String) As Task(Of Boolean)
        Return Await Task.Run(Function() As Boolean
            Try
                Dim clientPath = Path.Combine(minecraftDir, "versions", version, $"{version}.jar")
                If Not File.Exists(clientPath) Then Return False

                Dim clientInfo = versionData("downloads")?.Item("client")
                If clientInfo IsNot Nothing Then
                    Dim expectedSize As Long = clientInfo("size").ToObject(Of Long)()
                    Dim expectedSha1 = clientInfo("sha1").ToString()

                    Dim fileInfo As New FileInfo(clientPath)
                    If fileInfo.Length <> expectedSize Then Return False

                    Using stream As New FileStream(clientPath, FileMode.Open, FileAccess.Read)
                        Using sha1 As SHA1 = SHA1.Create()
                            Dim hashBytes = sha1.ComputeHash(stream)
                            Dim actualSha1 = BitConverter.ToString(hashBytes).Replace("-", "").ToLower()
                            Return actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase)
                        End Using
                    End Using
                Else
                    Dim fileInfo As New FileInfo(clientPath)
                    Return fileInfo.Length > 1000000
                End If
            Catch
                Return False
            End Try
        End Function)
    End Function

    ''' <summary>
    ''' Verifica TUTTI gli assets - Critico per suoni e lingue
    ''' </summary>
    Private Async Function VerifyAllAssetsAsync(versionData As JObject, minecraftDir As String) As Task(Of CountResult)
        Return Await Task.Run(Function() As CountResult
            Dim result As New CountResult()

            Try
                Dim assetIndexInfo = versionData("assetIndex")
                If assetIndexInfo Is Nothing Then
                    Log("  ℹ Nessun assetIndex")
                    Return result
                End If

                Dim assetIndexId = assetIndexInfo("id").ToString()
                Dim assetIndexPath = Path.Combine(minecraftDir, "assets", "indexes", $"{assetIndexId}.json")

                If Not File.Exists(assetIndexPath) Then
                    Log($"  ⚠ Asset index mancante")
                    result.MissingCount = Integer.MaxValue
                    Return result
                End If

                Dim assetIndexJson = File.ReadAllText(assetIndexPath)
                Dim assetIndexData = JsonConvert.DeserializeObject(Of JObject)(assetIndexJson)
                Dim objects = CType(assetIndexData("objects"), JObject)

                If objects Is Nothing OrElse objects.Count = 0 Then
                    Log("  ⚠ Asset index vuoto/corrotto")
                    result.MissingCount = Integer.MaxValue
                    Return result
                End If

                result.TotalCount = objects.Count
                Log($"  🔎 Verifica {result.TotalCount} assets...")

                Dim missingCount = 0
                Dim checkedCount = 0

                For Each assetProp In objects.Properties()
                    Dim hash = assetProp.Value("hash").ToString()
                    Dim assetPath = Path.Combine(minecraftDir, "assets", "objects", hash.Substring(0, 2), hash)

                    If Not File.Exists(assetPath) Then missingCount += 1

                    checkedCount += 1
                    If checkedCount Mod 1000 = 0 Then
                        Log($"    {checkedCount}/{result.TotalCount} ({missingCount} mancanti)")
                    End If
                Next

                result.MissingCount = missingCount

            Catch ex As Exception
                LogError($"  ✗ Errore verifica assets: {ex.Message}")
                result.MissingCount = Integer.MaxValue
            End Try

            Return result
        End Function)
    End Function

    ''' <summary>
    ''' Verifica librerie
    ''' </summary>
    Private Async Function VerifyAllLibrariesAsync(versionData As JObject, minecraftDir As String) As Task(Of CountResult)
        Return Await Task.Run(Function() As CountResult
            Dim result As New CountResult()

            Try
                Dim libraries = CType(versionData("libraries"), JArray)
                If libraries Is Nothing Then Return result

                Dim missingCount = 0
                Dim totalCount = 0

                For Each library As JObject In libraries
                    If IsLibraryCompatible(library) Then
                        totalCount += 1
                        Dim libraryName = library("name").ToObject(Of String)()
                        Dim libraryPath = GetLibraryPath(libraryName, minecraftDir)

                        If Not File.Exists(libraryPath) OrElse New FileInfo(libraryPath).Length < 100 Then
                            missingCount += 1
                        End If
                    End If
                Next

                result.TotalCount = totalCount
                result.MissingCount = missingCount

            Catch ex As Exception
                LogError($"  ✗ Errore verifica librerie: {ex.Message}")
                result.MissingCount = Integer.MaxValue
            End Try

            Return result
        End Function)
    End Function

#End Region

#Region "Download Componenti"

    ''' <summary>
    ''' Download client.jar con retry
    ''' </summary>
    Private Async Function DownloadClientJarAsync(versionData As JObject, version As String, minecraftDir As String) As Task(Of Boolean)
        Try
            Log("📦 Download client.jar...")

            Dim clientInfo = versionData("downloads")("client")
            Dim clientUrl = clientInfo("url").ToString()
            Dim expectedSize As Long = clientInfo("size").ToObject(Of Long)()
            Dim expectedSha1 = clientInfo("sha1").ToString()

            Dim clientPath = Path.Combine(minecraftDir, "versions", version, $"{version}.jar")
            Directory.CreateDirectory(Path.GetDirectoryName(clientPath))

            For retry = 1 To MAX_RETRIES
                Try
                    If retry > 1 Then
                        Log($"  Retry {retry}/{MAX_RETRIES}...")
                        Await Task.Delay(1000 * retry)
                    End If

                    Dim tempPath = clientPath & ".tmp"
                    
                    Using client As New Net.WebClient()
                        client.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                        Await Form1.DownloadFileTaskAsync(client, New Uri(clientUrl), tempPath, True)
                    End Using

                    If VerifyFileSHA1(tempPath, expectedSha1, expectedSize) Then
                        If File.Exists(clientPath) Then File.Delete(clientPath)
                        File.Move(tempPath, clientPath)
                        Log("  ✓ Client.jar OK")
                        Return True
                    Else
                        Log("  ✗ Hash invalido")
                        If File.Exists(tempPath) Then File.Delete(tempPath)
                    End If

                Catch ex As Exception
                    Log($"  ✗ Errore: {ex.Message}")
                End Try
            Next

            LogError($"  ✗ Fallito dopo {MAX_RETRIES} tentativi")
            Return False

        Catch ex As Exception
            LogError($"✗ Errore critico client.jar: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Download TUTTI gli assets
    ''' </summary>
    Private Async Function DownloadAllAssetsAsync(versionData As JObject, minecraftDir As String) As Task(Of Boolean)
        Try
            Log("📦 Download assets...")

            Dim assetIndexInfo = versionData("assetIndex")
            Dim assetIndexId = assetIndexInfo("id").ToString()
            Dim assetIndexUrl = assetIndexInfo("url").ToString()
            Dim assetIndexPath = Path.Combine(minecraftDir, "assets", "indexes", $"{assetIndexId}.json")

            Directory.CreateDirectory(Path.GetDirectoryName(assetIndexPath))

            If Not File.Exists(assetIndexPath) Then
                Log("  📄 Download asset index...")
                Await DownloadFileWithRetryAsync(assetIndexUrl, assetIndexPath)
            End If

            Dim assetIndexJson = Await File.ReadAllTextAsync(assetIndexPath)
            Dim assetIndexData = JsonConvert.DeserializeObject(Of JObject)(assetIndexJson)
            Dim objects = CType(assetIndexData("objects"), JObject)

            If objects Is Nothing OrElse objects.Count = 0 Then
                LogError("  ✗ Asset index vuoto")
                Return False
            End If

            Dim missingAssets = objects.Properties().Where(Function(prop)
                Dim hash = prop.Value("hash").ToString()
                Dim assetPath = Path.Combine(minecraftDir, "assets", "objects", hash.Substring(0, 2), hash)
                Return Not File.Exists(assetPath)
            End Function).ToList()

            If missingAssets.Count = 0 Then
                Log("  ✓ Assets già presenti")
                Return True
            End If

            Log($"  📥 {missingAssets.Count} assets da scaricare...")

            Form1.downloadType = "assets"
            Form1.downloadProgress = 0
            Form1.downloadTotal = missingAssets.Count
            Form1.downloadStartTime = DateTime.Now

            Dim semaphore As New SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS)
            Dim successCount = 0
            Dim failedCount = 0
            Dim progressCounter = 0

            Dim tasks = missingAssets.Select(Async Function(assetProp)
                Await semaphore.WaitAsync()
                Try
                    Dim hash = assetProp.Value("hash").ToString()
                    Dim assetPath = Path.Combine(minecraftDir, "assets", "objects", hash.Substring(0, 2), hash)
                    Dim assetUrl = $"https://resources.download.minecraft.net/{hash.Substring(0, 2)}/{hash}"

                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath))

                    Dim downloaded = False
                    For retry = 0 To 2
                        Dim shouldDelay = False
                        Try
                            Using client As New Net.WebClient()
                                client.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                                Await client.DownloadFileTaskAsync(New Uri(assetUrl), assetPath)
                            End Using
                            downloaded = True
                            Exit For
                        Catch
                            If retry < 2 Then shouldDelay = True
                        End Try
                        
                        If shouldDelay Then
                            Await Task.Delay(500 * (retry + 1))
                        End If
                    Next

                    If downloaded Then
                        Interlocked.Increment(successCount)
                    Else
                        Interlocked.Increment(failedCount)
                    End If

                    Dim currentProgress = Interlocked.Increment(progressCounter)
                    Form1.downloadProgress = currentProgress
                    
                    ' Log ogni 100 assets per mostrare progresso
                    If currentProgress Mod 100 = 0 OrElse currentProgress = missingAssets.Count Then
                        Log($"  📦 Assets: {currentProgress}/{missingAssets.Count} ({CInt(currentProgress * 100 / missingAssets.Count)}%)")
                    End If

                Finally
                    semaphore.Release()
                End Try
            End Function).ToArray()

            Await Task.WhenAll(tasks)

            Form1.downloadType = ""
            Log($"  ✅ Assets: {successCount} OK, {failedCount} falliti")

            Return failedCount = 0

        Catch ex As Exception
            Form1.downloadType = ""
            LogError($"✗ Errore assets: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Download librerie
    ''' </summary>
    Private Async Function DownloadAllLibrariesAsync(versionData As JObject, minecraftDir As String) As Task(Of Boolean)
        Try
            Log("📚 Download librerie...")

            Dim libraries = CType(versionData("libraries"), JArray)
            If libraries Is Nothing OrElse libraries.Count = 0 Then
                Return True
            End If

            Dim missingLibs = New List(Of JObject)
            For Each libToken In libraries
                Dim library = CType(libToken, JObject)
                If IsLibraryCompatible(library) Then
                    Dim libName = library("name").ToObject(Of String)()
                    Dim libPath = GetLibraryPath(libName, minecraftDir)
                    If Not File.Exists(libPath) OrElse New FileInfo(libPath).Length < 100 Then
                        missingLibs.Add(library)
                    End If
                End If
            Next

            If missingLibs.Count = 0 Then
                Log("  ✓ Librerie già presenti")
                Return True
            End If

            Log($"  📥 {missingLibs.Count} librerie da scaricare...")

            Form1.downloadType = "libraries"
            Form1.downloadProgress = 0
            Form1.downloadTotal = missingLibs.Count
            Form1.downloadStartTime = DateTime.Now

            Dim semaphore As New SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS)
            Dim successCount = 0
            Dim failedCount = 0
            Dim progressCounter = 0

            Dim tasks = missingLibs.Select(Async Function(library)
                Await semaphore.WaitAsync()
                Try
                    Dim libraryName = library("name").ToObject(Of String)()
                    Dim libraryPath = GetLibraryPath(libraryName, minecraftDir)
                    Dim libraryUrl = GetLibraryDownloadUrl(library)

                    If String.IsNullOrEmpty(libraryUrl) Then
                        Interlocked.Increment(failedCount)
                        Return Nothing
                    End If

                    Directory.CreateDirectory(Path.GetDirectoryName(libraryPath))

                    Dim downloaded = False
                    For retry = 0 To 2
                        Dim shouldDelay = False
                        Try
                            Using client As New Net.WebClient()
                                client.Headers.Add("User-Agent", "GangDrogaCity-Launcher/1.0")
                                Await client.DownloadFileTaskAsync(New Uri(libraryUrl), libraryPath)
                            End Using
                            downloaded = True
                            Exit For
                        Catch
                            If retry < 2 Then shouldDelay = True
                        End Try
                        
                        If shouldDelay Then
                            Await Task.Delay(1000 * (retry + 1))
                        End If
                    Next

                    If downloaded Then
                        Interlocked.Increment(successCount)
                    Else
                        Interlocked.Increment(failedCount)
                    End If

                    Dim currentProgress = Interlocked.Increment(progressCounter)
                    Form1.downloadProgress = currentProgress
                    
                    ' Log ogni 10 librerie per mostrare progresso
                    If currentProgress Mod 10 = 0 OrElse currentProgress = missingLibs.Count Then
                        Log($"  📚 Librerie: {currentProgress}/{missingLibs.Count} ({CInt(currentProgress * 100 / missingLibs.Count)}%)")
                    End If

                Finally
                    semaphore.Release()
                End Try
            End Function).ToArray()

            Await Task.WhenAll(tasks)

            Form1.downloadType = ""
            Log($"  ✅ Librerie: {successCount} OK, {failedCount} falliti")

            Return failedCount <= missingLibs.Count * 0.05

        Catch ex As Exception
            Form1.downloadType = ""
            LogError($"✗ Errore librerie: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Download librerie Forge critiche
    ''' </summary>
    Private Async Function DownloadForgeCriticalLibrariesAsync(version As String, gamedir As String) As Task
        Try
            Dim parts = version.Split("-"c)
            If parts.Length < 3 Then Return

            Dim mcVer = parts(0)
            Dim forgeVer = parts(2)
            Dim fullVer = $"{mcVer}-{forgeVer}"

            Log($"  🔥 Librerie Forge...")

            Dim baseUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge"
            Dim libs As New Dictionary(Of String, String) From {
                {"client", "forge"},
                {"universal", "forge"}
            }

            For Each entry In libs
                Dim classifier = entry.Key
                Dim name = entry.Value
                Dim fileName = $"{name}-{fullVer}-{classifier}.jar"
                Dim libPath = Path.Combine(gamedir, "libraries", "net", "minecraftforge", "forge", fullVer, fileName)
                Dim url = $"{baseUrl}/{fullVer}/{fileName}"

                If Not File.Exists(libPath) OrElse New FileInfo(libPath).Length < 1024 Then
                    Try
                        Directory.CreateDirectory(Path.GetDirectoryName(libPath))
                        Await DownloadFileWithRetryAsync(url, libPath)
                        Log($"    ✓ {fileName}")
                    Catch ex As Exception
                        LogError($"    ✗ {fileName}: {ex.Message}")
                    End Try
                End If
            Next

        Catch ex As Exception
            LogError($"  ✗ Errore Forge: {ex.Message}")
        End Try
    End Function

#End Region

#Region "Utilità"

    Private Async Function GetVersionMetadataAsync(version As String, minecraftDir As String, Optional useManifest As Boolean = True) As Task(Of JObject)
        Try
            Dim versionJsonPath = Path.Combine(minecraftDir, "versions", version, $"{version}.json")

            If File.Exists(versionJsonPath) Then
                Try
                    Dim json = File.ReadAllText(versionJsonPath)
                    Dim parsed = JsonConvert.DeserializeObject(Of JObject)(json)
                    If parsed("downloads") IsNot Nothing OrElse parsed("libraries") IsNot Nothing Then
                        Return parsed
                    End If
                Catch
                    Try : File.Delete(versionJsonPath) : Catch : End Try
                End Try
            End If

            If useManifest Then
                Dim manifest = Await GetVersionManifestAsync()
                Dim versionInfo = FindVersionInfo(manifest, version)
                If versionInfo Is Nothing Then Return Nothing

                Dim url = versionInfo("url").ToString()
                Dim json = Await httpClient.GetStringAsync(url)

                Directory.CreateDirectory(Path.GetDirectoryName(versionJsonPath))
                File.WriteAllText(versionJsonPath, json)

                Return JsonConvert.DeserializeObject(Of JObject)(json)
            End If

            Return Nothing

        Catch ex As Exception
            LogError($"Errore metadati: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Async Function GetVersionManifestAsync() As Task(Of JObject)
        If manifestCache IsNot Nothing AndAlso DateTime.Now.Subtract(lastManifestUpdate).TotalHours < MANIFEST_CACHE_HOURS Then
            Return manifestCache
        End If

        Dim url = "https://launchermeta.mojang.com/mc/game/version_manifest.json"
        Dim json = Await httpClient.GetStringAsync(url)

        manifestCache = JsonConvert.DeserializeObject(Of JObject)(json)
        lastManifestUpdate = DateTime.Now

        Return manifestCache
    End Function

    Private Function FindVersionInfo(manifest As JObject, targetVersion As String) As JObject
        Try
            Dim versions = CType(manifest("versions"), JArray)
            For Each version As JObject In versions
                If version("id").ToString().Equals(targetVersion, StringComparison.OrdinalIgnoreCase) Then
                    Return version
                End If
            Next
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

    Private Async Function DownloadFileWithRetryAsync(url As String, dest As String) As Task
        For retry = 1 To MAX_RETRIES
            Dim shouldDelay = False
            Dim lastException As Exception = Nothing
            
            Try
                Using response = Await httpClient.GetAsync(url)
                    response.EnsureSuccessStatusCode()
                    Using fileStream = File.Create(dest)
                        Await response.Content.CopyToAsync(fileStream)
                    End Using
                End Using
                Return
            Catch ex As Exception
                lastException = ex
                If retry < MAX_RETRIES Then
                    shouldDelay = True
                End If
            End Try
            
            If shouldDelay Then
                Await Task.Delay(1000 * retry)
            ElseIf lastException IsNot Nothing Then
                Throw lastException
            End If
        Next
    End Function

    Private Function VerifyFileSHA1(filePath As String, expectedSha1 As String, expectedSize As Long) As Boolean
        Try
            Dim fileInfo As New FileInfo(filePath)
            If fileInfo.Length <> expectedSize Then Return False

            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read)
                Using sha1 As SHA1 = SHA1.Create()
                    Dim hashBytes = sha1.ComputeHash(stream)
                    Dim actualSha1 = BitConverter.ToString(hashBytes).Replace("-", "").ToLower()
                    Return actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase)
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Function IsLibraryCompatible(library As JObject) As Boolean
        If library("rules") Is Nothing Then Return True

        Dim rules = CType(library("rules"), JArray)
        Dim allowed = False

        For Each rule As JObject In rules
            Dim action = rule("action").ToObject(Of String)()
            If rule("os") Is Nothing Then
                allowed = (action = "allow")
            Else
                Dim osName = rule("os")("name").ToObject(Of String)()
                If osName = GetCurrentOS() Then
                    allowed = (action = "allow")
                End If
            End If
        Next

        Return allowed
    End Function

    Private Function GetCurrentOS() As String
        Select Case Environment.OSVersion.Platform
            Case PlatformID.Win32NT : Return "windows"
            Case PlatformID.Unix : Return "linux"
            Case PlatformID.MacOSX : Return "osx"
            Case Else : Return "unknown"
        End Select
    End Function

    Private Function GetLibraryPath(libraryName As String, minecraftDir As String) As String
        Dim parts = libraryName.Split(":"c)
        If parts.Length < 3 Then Throw New ArgumentException($"Nome libreria invalido: {libraryName}")

        Dim groupId = parts(0).Replace("."c, Path.DirectorySeparatorChar)
        Dim artifactId = parts(1)
        Dim version = parts(2)
        Dim classifier = If(parts.Length > 3, "-" & parts(3), "")
        Dim fileName = $"{artifactId}-{version}{classifier}.jar"

        Return Path.Combine(minecraftDir, "libraries", groupId, artifactId, version, fileName)
    End Function

    Private Function GetLibraryDownloadUrl(library As JObject) As String
        If library("downloads")?.Item("artifact")?.Item("url") IsNot Nothing Then
            Return library("downloads")("artifact")("url").ToObject(Of String)()
        End If

        Dim libraryName = library("name").ToObject(Of String)()
        Dim parts = libraryName.Split(":"c)
        If parts.Length < 3 Then Return ""

        Dim groupId = parts(0).Replace("."c, "/")
        Dim artifactId = parts(1)
        Dim version = parts(2)
        Dim classifier = If(parts.Length > 3, "-" & parts(3), "")
        Dim fileName = $"{artifactId}-{version}{classifier}.jar"

        Return $"https://repo1.maven.org/maven2/{groupId}/{artifactId}/{version}/{fileName}"
    End Function

    Private Sub Log(msg As String)
        Form1.SafeInvoke(Sub() Form1.AddLog(msg))
    End Sub

    Private Sub LogError(msg As String)
        Form1.SafeInvoke(Sub() Form1.AddLog(msg))
    End Sub

#End Region

#Region "Classi Supporto"

    Private Class VerificationResult
        Public Property JarValid As Boolean = False
        Public Property MissingAssets As Integer = 0
        Public Property TotalAssets As Integer = 0
        Public Property MissingLibraries As Integer = 0
        Public Property TotalLibraries As Integer = 0
        Public Property AllComplete As Boolean = False
    End Class

    Private Class CountResult
        Public Property TotalCount As Integer = 0
        Public Property MissingCount As Integer = 0
    End Class

#End Region

End Class
