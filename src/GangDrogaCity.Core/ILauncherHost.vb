''' <summary>
''' Tutto cio' che il Core chiede all'interfaccia utente. L'implementazione (Avalonia)
''' si occupa di eseguire le chiamate sul thread UI: il Core puo' chiamare da qualsiasi thread.
''' </summary>
Public Interface ILauncherHost

    ''' <summary>Messaggio di stato corrente (riga sotto la barra di avanzamento).</summary>
    Sub Log(message As String)

    ''' <summary>Messaggio di stato evidenziato come errore.</summary>
    Sub LogError(message As String)

    ''' <summary>Avanzamento 0-100 della fase di installazione.</summary>
    Sub SetProgress(value As Integer)

    ''' <summary>Finestra informativa con solo OK.</summary>
    Function AlertAsync(title As String, message As String, Optional kind As AlertKind = AlertKind.Info) As Task

    ''' <summary>Domanda Si/No.</summary>
    Function ConfirmAsync(title As String, message As String, Optional kind As AlertKind = AlertKind.Question) As Task(Of Boolean)

    ''' <summary>Selezione di un branch da una lista. Nothing se annullato.</summary>
    Function SelectBranchAsync(branches As IList(Of String), current As String) As Task(Of String)

    ''' <summary>Dimensione dello schermo principale in pixel (per --width/--height di Minecraft).</summary>
    Function GetScreenSize() As (Width As Integer, Height As Integer)

    ''' <summary>Chiude il launcher (usato dopo aver avviato l'aggiornamento).</summary>
    Sub ExitApplication()

End Interface

Public Enum AlertKind
    Info
    Warning
    [Error]
    Question
End Enum

''' <summary>
''' Hub statico di log usato dai componenti che non hanno un riferimento all'host
''' (downloader, installer Fabric...). L'engine lo collega all'host.
''' </summary>
Public Module CoreLog
    Public Event Message(text As String)

    Public Sub Log(text As String)
        RaiseEvent Message(text)
    End Sub
End Module

''' <summary>
''' Stato condiviso dei download massivi (assets/librerie) per il testo di avanzamento.
''' </summary>
Public Module DownloadProgress
    Public Property Current As Integer = 0
    Public Property Total As Integer = 0
    Public Property StartTime As DateTime = DateTime.Now
    Public Property Kind As String = ""
End Module
