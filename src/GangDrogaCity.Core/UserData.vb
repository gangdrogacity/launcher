Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' Separa i DATI UTENTE (impostazioni, keybind, waypoint, server list...) dai DATI DI GIOCO
''' gestiti dal manifest del modpack. Struttura dentro %APPDATA%\.gangdrogacity\:
'''
'''   game\               game data: ricostruibile dal manifest, puo' essere cancellato
'''   userdata\           user data: di proprieta' del giocatore, mai toccato da sync/reinstall
'''      files\           mirror dei file utente (backup; ripristinato dopo un reset)
'''      defaults\        versione "di default" fornita dal pack per i file utente (ex *.once)
'''
''' Un file utente presente nel manifest viene trattato cosi':
'''   - la versione del pack viene scaricata in userdata\defaults\, MAI direttamente in game\
'''   - se in game\ il file manca, viene copiato dal default
'''   - se il pack aggiorna il default e il giocatore non aveva mai modificato il file,
'''     il file viene aggiornato; altrimenti la versione del giocatore vince sempre
'''
''' Le regole (pattern in stile .gitignore) vengono da una lista integrata nel launcher
''' piu' i file facoltativi del repo del modpack: .userdata, .oncelist (dati utente),
''' .gitignore e .manifestignore (file da non cancellare mai).
''' </summary>
Public Class UserDataManager

    Public ReadOnly Property GameDir As String
    Public ReadOnly Property RootDir As String
    Public ReadOnly Property MirrorDir As String
    Public ReadOnly Property DefaultsDir As String

    ''' <summary>File di regole scaricati dal repo del modpack (tutti facoltativi).</summary>
    Public Shared ReadOnly RuleFileNames As String() = {".userdata", ".oncelist", ".gitignore", ".manifestignore"}

    ''' <summary>
    ''' Dati utente integrati: salvati nel mirror, mai sovrascritti ne' cancellati dal sync.
    ''' Estendibili dal repo del modpack con il file .userdata (stessa sintassi).
    ''' </summary>
    Public Shared ReadOnly BuiltInUserPatterns As String() = {
        "options.txt",
        "optionsof.txt",
        "optionsshaders.txt",
        "servers.dat",
        "hotbar.nbt",
        "xaero/",
        "XaeroWaypoints/",
        "XaeroWorldMap/",
        "config/xaero/",
        "config/xaerohud.txt",
        "config/xaeropatreon.txt",
        "config/xaerominimap.txt",
        "config/xaeroworldmap.txt",
        "config/sodium-options.json",
        "config/sodium-extra-options.json",
        "config/iris.properties",
        "config/DistantHorizons.toml",
        "config/voicechat/voicechat-client.properties",
        "config/voicechat/player-volumes.properties",
        "config/voicechat/category-volumes.properties",
        "config/skinlayers.json",
        "config/entity_model_features.json",
        "config/entity_texture_features.json",
        "config/continuity.json",
        "config/modmenu.json",
        "config/jei/jei-client.ini",
        "config/jei/world/",
        "config/roughlyenoughitems/favorites.json5",
        "config/inventoryprofilesnext/",
        "shaderpacks/*.txt"
    }

    ''' <summary>
    ''' Dati che non vanno mai cancellati dal sync ma che non ha senso copiare nel mirror
    ''' (mondi, screenshot, log...). Estesi da .gitignore e .manifestignore del repo.
    ''' </summary>
    Public Shared ReadOnly BuiltInProtectedPatterns As String() = {
        "saves/",
        "screenshots/",
        "logs/",
        "crash-reports/",
        "backups/",
        "schematics/",
        "replay_recordings/",
        ".replay_cache/",
        "Distant_Horizons_server_data/",
        "usercache.json",
        "usernamecache.json",
        "XaeroWaypoints_BACKUP*/"
    }

    Private ReadOnly userRules As New List(Of Regex)
    Private ReadOnly protectedRules As New List(Of Regex)
    Private ReadOnly rulesLock As New Object()

    Public Sub New(minecraftDir As String, gameDirectory As String)
        GameDir = gameDirectory
        RootDir = Path.Combine(minecraftDir, "userdata")
        MirrorDir = Path.Combine(RootDir, "files")
        DefaultsDir = Path.Combine(RootDir, "defaults")
        LoadRules(Nothing)
    End Sub

    ''' <summary>
    ''' Ricarica le regole: lista integrata piu' i file di regole presenti in rulesDir.
    ''' </summary>
    Public Sub LoadRules(rulesDir As String)
        SyncLock rulesLock
            userRules.Clear()
            protectedRules.Clear()

            For Each p In BuiltInUserPatterns
                AddRule(userRules, p)
            Next
            For Each p In BuiltInProtectedPatterns
                AddRule(protectedRules, p)
            Next

            ' Su Linux/macOS la JRE e i natives non arrivano dal manifest (che e' Windows-only):
            ' vengono installati dal launcher e non devono essere cancellati dalla pulizia
            If Not Platform.IsWindows Then
                AddRule(protectedRules, "java/")
                AddRule(protectedRules, "natives/")
            End If

            If Not String.IsNullOrEmpty(rulesDir) AndAlso Directory.Exists(rulesDir) Then
                AddRulesFromFile(userRules, Path.Combine(rulesDir, ".userdata"))
                AddRulesFromFile(userRules, Path.Combine(rulesDir, ".oncelist"))
                AddRulesFromFile(protectedRules, Path.Combine(rulesDir, ".gitignore"))
                AddRulesFromFile(protectedRules, Path.Combine(rulesDir, ".manifestignore"))
            End If
        End SyncLock
    End Sub

    Private Sub AddRulesFromFile(target As List(Of Regex), filePath As String)
        Try
            If Not File.Exists(filePath) Then Return
            For Each line In File.ReadAllLines(filePath)
                AddRule(target, line)
            Next
        Catch
            ' File di regole non leggibile: ignora
        End Try
    End Sub

    Private Sub AddRule(target As List(Of Regex), pattern As String)
        Dim rx = GlobToRegex(pattern)
        If rx IsNot Nothing Then target.Add(rx)
    End Sub

    ''' <summary>
    ''' Converte un pattern in stile .gitignore in una Regex sul percorso relativo (separatore /).
    ''' - "cartella/"  tutto il contenuto della cartella
    ''' - "/nome"      ancorato alla radice; un pattern con / interno e' anch'esso ancorato
    ''' - "nome"       senza / corrisponde a qualsiasi profondita'
    ''' - "*" e "?"    wildcard entro un segmento, "**" attraversa le cartelle
    ''' Righe vuote, commenti (#) e negazioni (!) vengono ignorate.
    ''' </summary>
    Public Shared Function GlobToRegex(pattern As String) As Regex
        If pattern Is Nothing Then Return Nothing
        Dim p As String = pattern.Trim().Replace("\"c, "/"c)
        If p.Length = 0 OrElse p.StartsWith("#") OrElse p.StartsWith("!") Then Return Nothing

        Dim anchored As Boolean = p.StartsWith("/")
        Dim isDir As Boolean = p.EndsWith("/")
        p = p.Trim("/"c)
        If p.Length = 0 Then Return Nothing
        If p.Contains("/") Then anchored = True

        Dim sb As New StringBuilder()
        Dim i As Integer = 0
        While i < p.Length
            Dim ch As Char = p(i)
            If ch = "*"c Then
                If i + 1 < p.Length AndAlso p(i + 1) = "*"c Then
                    ' "**/" oppure "**"
                    If i + 2 < p.Length AndAlso p(i + 2) = "/"c Then
                        sb.Append("(.*/)?")
                        i += 3
                    Else
                        sb.Append(".*")
                        i += 2
                    End If
                    Continue While
                End If
                sb.Append("[^/]*")
            ElseIf ch = "?"c Then
                sb.Append("[^/]")
            Else
                sb.Append(Regex.Escape(ch.ToString()))
            End If
            i += 1
        End While

        Dim prefix As String = If(anchored, "^", "^(.*/)?")
        Dim suffix As String = If(isDir, "/.*$", "(/.*)?$")
        Try
            Return New Regex(prefix & sb.ToString() & suffix, RegexOptions.IgnoreCase Or RegexOptions.Compiled)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Percorso relativo a game\ con separatore /.</summary>
    Public Function RelativePath(fullPath As String) As String
        Dim gameFull As String = Path.GetFullPath(GameDir).TrimEnd("\"c, "/"c)
        Dim full As String = Path.GetFullPath(fullPath)
        If full.StartsWith(gameFull, StringComparison.OrdinalIgnoreCase) Then
            full = full.Substring(gameFull.Length)
        End If
        Return full.TrimStart("\"c, "/"c).Replace("\"c, "/"c)
    End Function

    Private Shared Function Normalize(relPath As String) As String
        If relPath Is Nothing Then Return ""
        Return relPath.Replace("\"c, "/"c).TrimStart("/"c)
    End Function

    ''' <summary>Il percorso (relativo a game\) e' un dato utente: mirror, mai sovrascritto, mai cancellato.</summary>
    Public Function IsUserData(relPath As String) As Boolean
        Dim rel As String = Normalize(relPath)
        If rel.Length = 0 Then Return False
        SyncLock rulesLock
            Return userRules.Any(Function(rx) rx.IsMatch(rel))
        End SyncLock
    End Function

    ''' <summary>Il percorso (relativo a game\) non deve mai essere cancellato dalla pulizia.</summary>
    Public Function IsProtected(relPath As String) As Boolean
        Dim rel As String = Normalize(relPath)
        If rel.Length = 0 Then Return False
        SyncLock rulesLock
            If userRules.Any(Function(rx) rx.IsMatch(rel)) Then Return True
            Return protectedRules.Any(Function(rx) rx.IsMatch(rel))
        End SyncLock
    End Function

    ''' <summary>Percorso in userdata\defaults\ della versione del pack di un file utente.</summary>
    Public Function DefaultPath(relPath As String) As String
        Return Path.Combine(DefaultsDir, Normalize(relPath).Replace("/"c, Path.DirectorySeparatorChar))
    End Function

    Private Function GamePath(relPath As String) As String
        Return Path.Combine(GameDir, Normalize(relPath).Replace("/"c, Path.DirectorySeparatorChar))
    End Function

    Private Function MirrorPath(relPath As String) As String
        Return Path.Combine(MirrorDir, Normalize(relPath).Replace("/"c, Path.DirectorySeparatorChar))
    End Function

    ''' <summary>
    ''' Hash SHA256 della vecchia versione del default (userdata\defaults\ oppure il legacy *.once in game\),
    ''' da passare ad ApplyDefault dopo aver scaricato il nuovo default. Vuoto se non esiste.
    ''' </summary>
    Public Function PreviousDefaultHash(relPath As String) As String
        Dim defPath As String = DefaultPath(relPath)
        If File.Exists(defPath) Then Return ComputeSha256(defPath)
        Dim legacyOnce As String = GamePath(relPath) & ".once"
        If File.Exists(legacyOnce) Then Return ComputeSha256(legacyOnce)
        Return ""
    End Function

    ''' <summary>
    ''' Applica il default del pack a un file utente:
    '''  - migra ed elimina l'eventuale legacy *.once
    '''  - se il file in game\ manca, lo copia dal default
    '''  - se il default e' cambiato e il file del giocatore coincide con il VECCHIO default
    '''    (mai modificato), lo aggiorna al nuovo default
    '''  - altrimenti lascia intatta la versione del giocatore
    ''' Restituisce una descrizione dell'azione svolta oppure Nothing.
    ''' </summary>
    Public Function ApplyDefault(relPath As String, previousDefaultHash As String) As String
        Dim defPath As String = DefaultPath(relPath)
        Dim gamePathStr As String = GamePath(relPath)
        Dim legacyOnce As String = gamePathStr & ".once"

        Try
            If File.Exists(legacyOnce) Then
                If String.IsNullOrEmpty(previousDefaultHash) Then previousDefaultHash = ComputeSha256(legacyOnce)
                If Not File.Exists(defPath) Then
                    Directory.CreateDirectory(Path.GetDirectoryName(defPath))
                    File.Copy(legacyOnce, defPath, True)
                End If
                File.Delete(legacyOnce)
            End If
        Catch
        End Try

        If Not File.Exists(defPath) Then Return Nothing

        Try
            If Not File.Exists(gamePathStr) Then
                Directory.CreateDirectory(Path.GetDirectoryName(gamePathStr))
                File.Copy(defPath, gamePathStr, True)
                Return $"Impostazioni predefinite create: {Normalize(relPath)}"
            End If

            If Not String.IsNullOrEmpty(previousDefaultHash) Then
                Dim newDefaultHash As String = ComputeSha256(defPath)
                If Not newDefaultHash.Equals(previousDefaultHash, StringComparison.OrdinalIgnoreCase) Then
                    Dim userHash As String = ComputeSha256(gamePathStr)
                    If userHash.Equals(previousDefaultHash, StringComparison.OrdinalIgnoreCase) Then
                        File.Copy(defPath, gamePathStr, True)
                        Return $"Impostazioni predefinite aggiornate (non personalizzate): {Normalize(relPath)}"
                    End If
                End If
            End If
        Catch
        End Try

        Return Nothing
    End Function

    ''' <summary>
    ''' Copia i dati utente da game\ nel mirror userdata\files\ (aggiunge/aggiorna/rimuove).
    ''' Restituisce il numero di file copiati. Se game\ manca o e' vuoto non fa nulla.
    ''' </summary>
    Public Function Backup() As Integer
        If Not Directory.Exists(GameDir) Then Return 0

        Dim copied As Integer = 0
        Dim present As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Dim allFiles As String()
        Try
            allFiles = Directory.GetFiles(GameDir, "*", SearchOption.AllDirectories)
        Catch
            Return 0
        End Try
        If allFiles.Length = 0 Then Return 0

        Directory.CreateDirectory(MirrorDir)

        For Each src In allFiles
            Dim rel As String = RelativePath(src)
            If rel.EndsWith(".once", StringComparison.OrdinalIgnoreCase) Then Continue For
            If Not IsUserData(rel) Then Continue For

            present.Add(rel)
            Dim dst As String = MirrorPath(rel)
            Try
                If Not SameFile(src, dst) Then
                    Directory.CreateDirectory(Path.GetDirectoryName(dst))
                    File.Copy(src, dst, True)
                    copied += 1
                End If
            Catch
                ' File in uso o non leggibile: riprovera' al prossimo backup
            End Try
        Next

        ' Rimuovi dal mirror i file che il giocatore ha eliminato
        Try
            For Each mirrored In Directory.GetFiles(MirrorDir, "*", SearchOption.AllDirectories)
                Dim rel As String = Path.GetFullPath(mirrored).Substring(Path.GetFullPath(MirrorDir).TrimEnd("\"c).Length).TrimStart("\"c, "/"c).Replace("\"c, "/"c)
                If Not present.Contains(rel) Then
                    Try
                        File.Delete(mirrored)
                    Catch
                    End Try
                End If
            Next
            RemoveEmptyDirectories(MirrorDir)
        Catch
        End Try

        Return copied
    End Function

    ''' <summary>
    ''' Ripristina in game\ i file utente presenti nel mirror ma mancanti in game\ (dopo un reset).
    ''' Restituisce il numero di file ripristinati.
    ''' </summary>
    Public Function Restore() As Integer
        If Not Directory.Exists(MirrorDir) Then Return 0
        Dim restored As Integer = 0
        Try
            For Each mirrored In Directory.GetFiles(MirrorDir, "*", SearchOption.AllDirectories)
                Dim rel As String = Path.GetFullPath(mirrored).Substring(Path.GetFullPath(MirrorDir).TrimEnd("\"c).Length).TrimStart("\"c, "/"c).Replace("\"c, "/"c)
                Dim dst As String = GamePath(rel)
                If Not File.Exists(dst) Then
                    Try
                        Directory.CreateDirectory(Path.GetDirectoryName(dst))
                        File.Copy(mirrored, dst, False)
                        restored += 1
                    Catch
                    End Try
                End If
            Next
        Catch
        End Try
        Return restored
    End Function

    Private Shared Function SameFile(a As String, b As String) As Boolean
        If Not File.Exists(b) Then Return False
        Dim fa As New FileInfo(a)
        Dim fb As New FileInfo(b)
        Return fa.Length = fb.Length AndAlso fa.LastWriteTimeUtc = fb.LastWriteTimeUtc
    End Function

    Private Shared Sub RemoveEmptyDirectories(root As String)
        Try
            For Each d In Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(Function(x) x.Length)
                Try
                    If Not Directory.EnumerateFileSystemEntries(d).Any() Then Directory.Delete(d)
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub

    Public Shared Function ComputeSha256(filePath As String) As String
        Try
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using sha As SHA256 = SHA256.Create()
                    Return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLower()
                End Using
            End Using
        Catch
            Return ""
        End Try
    End Function

End Class
