Imports System.IO
Imports System.Threading.Tasks

''' <summary>
''' Eliminazione rapida di cartelle grandi (assets, libraries, downloads...).
''' Invece di cancellare file per file bloccando l'interfaccia, la cartella viene
''' rinominata all'istante in una cartella "cestino" accanto a quella originale e
''' poi eliminata in background su piu' thread. Se la rinomina fallisce (file in uso)
''' si ricade sull'eliminazione parallela diretta.
''' </summary>
Public Module DirectoryCleaner

    Private Const TrashSuffix As String = ".trash-"

    ''' <summary>
    ''' Rimuove la cartella dal percorso originale il piu' velocemente possibile e
    ''' restituisce il Task che completa l'eliminazione fisica in background.
    ''' Al ritorno della funzione il percorso originale e' gia' libero.
    ''' </summary>
    Public Function FastDeleteDirectory(path As String) As Task
        If String.IsNullOrWhiteSpace(path) OrElse Not Directory.Exists(path) Then
            Return Task.CompletedTask
        End If

        Dim trashPath As String = path.TrimEnd("\"c, "/"c) & TrashSuffix & DateTime.UtcNow.Ticks.ToString()
        Try
            Directory.Move(path, trashPath)
        Catch
            ' Rinomina non riuscita (es. file bloccati): elimina direttamente in parallelo
            Return Task.Run(Sub() DeleteTreeParallel(path))
        End Try

        Return Task.Run(Sub() DeleteTreeParallel(trashPath))
    End Function

    ''' <summary>
    ''' Elimina in background le cartelle "cestino" rimaste da sessioni precedenti
    ''' (ad esempio se il launcher e' stato chiuso prima della fine dell'eliminazione).
    ''' </summary>
    Public Function CleanupLeftoverTrash(path As String) As Task
        Try
            Dim parent As String = IO.Path.GetDirectoryName(path.TrimEnd("\"c, "/"c))
            Dim name As String = IO.Path.GetFileName(path.TrimEnd("\"c, "/"c))
            If String.IsNullOrEmpty(parent) OrElse Not Directory.Exists(parent) Then Return Task.CompletedTask

            Dim leftovers = Directory.GetDirectories(parent, name & TrashSuffix & "*")
            If leftovers.Length = 0 Then Return Task.CompletedTask

            Return Task.Run(Sub()
                                For Each trashDir As String In leftovers
                                    DeleteTreeParallel(trashDir)
                                Next
                            End Sub)
        Catch
            Return Task.CompletedTask
        End Try
    End Function

    ''' <summary>
    ''' Elimina in background le cartelle "cestino" (*.trash-*) rimaste DENTRO la cartella indicata.
    ''' </summary>
    Public Function CleanupLeftoverTrashIn(parentDir As String) As Task
        Try
            If String.IsNullOrEmpty(parentDir) OrElse Not Directory.Exists(parentDir) Then Return Task.CompletedTask
            Dim leftovers = Directory.GetDirectories(parentDir, "*" & TrashSuffix & "*")
            If leftovers.Length = 0 Then Return Task.CompletedTask
            Return Task.Run(Sub()
                                For Each trashDir As String In leftovers
                                    DeleteTreeParallel(trashDir)
                                Next
                            End Sub)
        Catch
            Return Task.CompletedTask
        End Try
    End Function

    ''' <summary>
    ''' Elimina un albero di cartelle usando piu' thread sulle sottocartelle di primo livello.
    ''' Gli errori sui singoli elementi vengono ignorati: cio' che resta viene ripulito
    ''' al prossimo avvio da CleanupLeftoverTrash.
    ''' </summary>
    Public Sub DeleteTreeParallel(root As String)
        If Not Directory.Exists(root) Then Return
        Try
            Dim subDirs = Directory.GetDirectories(root)
            Parallel.ForEach(subDirs, New ParallelOptions With {.MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)},
                             Sub(d)
                                 Try
                                     Directory.Delete(d, True)
                                 Catch
                                     ' Riprova rimuovendo gli attributi di sola lettura
                                     Try
                                         ClearReadOnly(d)
                                         Directory.Delete(d, True)
                                     Catch
                                     End Try
                                 End Try
                             End Sub)

            For Each f As String In Directory.GetFiles(root)
                Try
                    File.SetAttributes(f, FileAttributes.Normal)
                    File.Delete(f)
                Catch
                End Try
            Next

            Directory.Delete(root, True)
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Elimina in parallelo un elenco di file. Restituisce il numero di file eliminati.
    ''' </summary>
    Public Function DeleteFilesParallel(files As IEnumerable(Of String), Optional onError As Action(Of String, Exception) = Nothing) As Integer
        Dim deleted As Integer = 0
        Parallel.ForEach(files, New ParallelOptions With {.MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)},
                         Sub(f)
                             Try
                                 File.SetAttributes(f, FileAttributes.Normal)
                                 File.Delete(f)
                                 Threading.Interlocked.Increment(deleted)
                             Catch ex As Exception
                                 If onError IsNot Nothing Then onError(f, ex)
                             End Try
                         End Sub)
        Return deleted
    End Function

    Private Sub ClearReadOnly(folder As String)
        For Each f As String In Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            Try
                File.SetAttributes(f, FileAttributes.Normal)
            Catch
            End Try
        Next
    End Sub

End Module
