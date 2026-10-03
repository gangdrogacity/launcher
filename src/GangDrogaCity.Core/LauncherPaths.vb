''' <summary>
''' Percorsi usati dal launcher. Tutto vive dentro la cartella .gangdrogacity nella
''' cartella dati dell'utente (%APPDATA% su Windows, ~/.config su Linux e macOS):
'''   game\       dati di gioco gestiti dal manifest
'''   downloads\  manifest, regole, pacchetti e cache
'''   userdata\   dati utente (impostazioni, keybind, waypoint) - vedi UserDataManager
''' </summary>
Public Class LauncherPaths

    Public ReadOnly Property MinecraftDir As String
    Public ReadOnly Property GameDir As String
    Public ReadOnly Property DownloadDir As String
    Public ReadOnly Property SettingsFile As String
    Public ReadOnly Property HashCacheFile As String

    Public Sub New()
        Me.New(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".gangdrogacity"))
    End Sub

    Public Sub New(root As String)
        MinecraftDir = root
        GameDir = Path.Combine(root, "game")
        DownloadDir = Path.Combine(root, "downloads")
        SettingsFile = Path.Combine(root, "settings.json")
        HashCacheFile = Path.Combine(root, "hash_cache.json")
    End Sub

    Public Sub EnsureDirectories()
        Directory.CreateDirectory(MinecraftDir)
        Directory.CreateDirectory(DownloadDir)
    End Sub

    ''' <summary>Spazio libero (byte) sul volume che contiene la cartella del launcher.</summary>
    Public Function AvailableFreeSpace() As Long
        Try
            Dim probe As String = MinecraftDir
            While Not Directory.Exists(probe)
                Dim parent = Path.GetDirectoryName(probe)
                If String.IsNullOrEmpty(parent) OrElse parent = probe Then Exit While
                probe = parent
            End While
            Return New DriveInfo(probe).AvailableFreeSpace
        Catch
            Return Long.MaxValue
        End Try
    End Function

End Class
