Imports Newtonsoft.Json

''' <summary>
''' Impostazioni del launcher salvate in settings.json dentro la cartella .gangdrogacity.
''' Sostituisce My.Settings (WinForms) ed e' identico su tutte le piattaforme.
''' Al primo avvio su Windows migra i valori dal vecchio user.config di My.Settings.
''' </summary>
Public Class LauncherSettings

    Public Const DefaultBranch As String = "main"

    Public Property Username As String = ""
    Public Property CurrentBranch As String = DefaultBranch
    Public Property FabricLoaderVersion As String = ""
    Public Property McVersion As String = ""
    Public Property FabricVersionId As String = ""
    Public Property DevMode As Boolean = False
    Public Property RamMB As Integer = 4096

    <JsonIgnore>
    Private filePath As String

    Public Shared Function Load(filePath As String) As LauncherSettings
        Dim s As LauncherSettings = Nothing
        Try
            If File.Exists(filePath) Then
                s = JsonConvert.DeserializeObject(Of LauncherSettings)(File.ReadAllText(filePath))
            End If
        Catch
            s = Nothing
        End Try

        Dim migrated As Boolean = False
        If s Is Nothing Then
            s = New LauncherSettings()
            migrated = s.MigrateFromLegacyUserConfig()
        End If

        s.filePath = filePath
        If String.IsNullOrWhiteSpace(s.CurrentBranch) Then s.CurrentBranch = DefaultBranch
        If s.Username Is Nothing Then s.Username = ""
        If migrated Then s.Save()
        Return s
    End Function

    Public Sub Save()
        Try
            If String.IsNullOrEmpty(filePath) Then Return
            Directory.CreateDirectory(Path.GetDirectoryName(filePath))
            Dim tmp = filePath & ".tmp"
            File.WriteAllText(tmp, JsonConvert.SerializeObject(Me, Formatting.Indented))
            File.Copy(tmp, filePath, True)
            File.Delete(tmp)
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Migrazione best-effort dal vecchio My.Settings (WinForms):
    ''' %LOCALAPPDATA%\GangDrogaCity\GangDrogaCity_Url_xxx\&lt;versione&gt;\user.config
    ''' </summary>
    Private Function MigrateFromLegacyUserConfig() As Boolean
        If Not Platform.IsWindows Then Return False
        Try
            Dim localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If String.IsNullOrEmpty(localAppData) OrElse Not Directory.Exists(localAppData) Then Return False

            Dim candidates As New List(Of String)
            For Each appDir In Directory.GetDirectories(localAppData, "GangDrogaCity*")
                For Each urlDir In Directory.GetDirectories(appDir)
                    For Each verDir In Directory.GetDirectories(urlDir)
                        Dim cfg = Path.Combine(verDir, "user.config")
                        If File.Exists(cfg) Then candidates.Add(cfg)
                    Next
                Next
            Next
            If candidates.Count = 0 Then Return False

            ' Usa il user.config piu' recente
            Dim newest = candidates.OrderByDescending(Function(c) File.GetLastWriteTimeUtc(c)).First()
            Dim doc = New Xml.XmlDocument()
            doc.Load(newest)
            Dim found As Boolean = False
            For Each node As Xml.XmlNode In doc.SelectNodes("//setting")
                Dim name = node.Attributes("name")?.Value
                Dim valueNode = node.SelectSingleNode("value")
                If name Is Nothing OrElse valueNode Is Nothing Then Continue For
                Dim value = valueNode.InnerText
                Select Case name.ToLowerInvariant()
                    Case "username" : Username = value : found = True
                    Case "currentbranch" : If Not String.IsNullOrWhiteSpace(value) Then CurrentBranch = value
                    Case "fabricloaderversion" : FabricLoaderVersion = value
                    Case "mcversion" : McVersion = value
                    Case "fabricversionid" : FabricVersionId = value
                End Select
            Next
            Return found
        Catch
            Return False
        End Try
    End Function

End Class
