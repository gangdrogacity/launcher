Imports Octokit
Imports System.Reflection

Public Class UpdateChecker
    Private Const REPO_OWNER As String = "gangdrogacity"
    Private Const REPO_NAME As String = "launcher"

    ''' <summary>
    ''' Verifica se è disponibile un aggiornamento controllando l'ultima release su GitHub
    ''' </summary>
    ''' <returns>True se l'ultima release ha una versione superiore a quella corrente</returns>
    Public Async Function CheckForUpdateAsync() As Task(Of Boolean)
        Try
            Dim client As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))

            ' Ottieni tutte le release (incluse pre-release e draft)
            Dim allReleases = Await client.Repository.Release.GetAll(REPO_OWNER, REPO_NAME)

            Console.WriteLine($"Numero di release trovate: {allReleases.Count}")

            Dim latestRelease As Release = Nothing

            If allReleases.Count > 0 Then
                ' Prendi la prima release (la più recente)
                latestRelease = allReleases(0)
                Console.WriteLine($"Release trovata: {latestRelease.TagName}, Draft: {latestRelease.Draft}, PreRelease: {latestRelease.Prerelease}")
            Else
                ' Nessuna release trovata
                Console.WriteLine("Nessuna release trovata nella repository")
                Return False
            End If

            If latestRelease Is Nothing Then
                Return False
            End If

            ' Ottieni la versione corrente dell'applicazione
            Dim currentVersion = New Version(My.Settings.version)

            ' Rimuovi il prefisso "v" dal tag se presente
            Dim latestVersionString = latestRelease.TagName.TrimStart("v"c)

            ' Converte la versione della release in un oggetto Version
            Dim latestVersion As Version = Nothing

            If Version.TryParse(latestVersionString, latestVersion) Then
                ' Confronta le versioni
                Return latestVersion > currentVersion
            Else
                ' Se non riesce a parsare la versione, restituisce False
                Return False
            End If

        Catch ex As Exception
            ' In caso di errore (es. nessuna connessione, repository non trovata, ecc.)
            ' Restituisce False per evitare di bloccare l'applicazione
            Console.WriteLine($"Errore durante il controllo degli aggiornamenti: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Ottiene l'URL dell'ultima release disponibile su GitHub
    ''' </summary>
    ''' <returns>L'URL della release o Nothing se non disponibile</returns>
    Public Async Function GetLatestReleaseUrlAsync() As Task(Of String)
        Try
            Dim client As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))

            Dim allReleases = Await client.Repository.Release.GetAll(REPO_OWNER, REPO_NAME)

            Dim latestRelease As Release = Nothing

            If allReleases.Count > 0 Then
                latestRelease = allReleases(0)
            End If

            If latestRelease IsNot Nothing Then
                Return latestRelease.HtmlUrl
            Else
                Return Nothing
            End If
        Catch ex As Exception
            Console.WriteLine($"Errore durante il recupero dell'URL della release: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Function getLatestversionString()
        Try
            Dim client As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))

            Dim allReleases = client.Repository.Release.GetAll(REPO_OWNER, REPO_NAME).Result

            Dim latestRelease As Release = Nothing

            If allReleases.Count > 0 Then
                latestRelease = allReleases(0)
            End If

            If latestRelease IsNot Nothing Then
                Dim latestVersionString = latestRelease.TagName.TrimStart("v"c)
                Return latestVersionString
            Else
                Return Nothing
            End If
        Catch ex As Exception
            Console.WriteLine($"Errore durante il recupero della versione della release: {ex.Message}")
            Return Nothing
        End Try
    End Function


    Public Async Function GetAssetDownloadUrlAsync(ByVal assetName As String) As Task(Of String)
        Try
            Dim client As New GitHubClient(New ProductHeaderValue("GangDrogaCity-Launcher"))
            Dim allReleases = Await client.Repository.Release.GetAll(REPO_OWNER, REPO_NAME)
            Dim latestRelease As Release = Nothing
            If allReleases.Count > 0 Then
                latestRelease = allReleases(0)
            End If
            If latestRelease IsNot Nothing Then
                For Each asset In latestRelease.Assets
                    If asset.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase) Then
                        Return asset.BrowserDownloadUrl
                    End If
                Next
            End If
            Return Nothing
        Catch ex As Exception
            Console.WriteLine($"Errore durante il recupero dell'URL di download dell'asset: {ex.Message}")
            Return Nothing
        End Try
    End Function


End Class
