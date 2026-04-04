Imports System.ComponentModel

Public Class Settings

    Dim drag As Boolean = False
    Dim dragOffset As Point
    Private Sub settingsSavebtn_Click(sender As Object, e As EventArgs) Handles settingsSavebtn.Click
        If usernameTxt.Text IsNot Nothing And usernameTxt.Text.Length < 3 Then
            MessageBox.Show("Il nome utente deve essere di almeno 3 caratteri.", "Nome utente non valido", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        My.Settings.username = usernameTxt.Text
        settingsPanel.Visible = False
        Form1.userLabel.Text = My.Settings.username
        Close()
    End Sub

    Private Sub Button8_Click(sender As Object, e As EventArgs) Handles Button8.Click
        If Form1.mcTask IsNot Nothing Then
            Try
                If Not Form1.mcTask.HasExited Then
                    MessageBox.Show("Chiudi prima Minecraft, poi riprova.", "Minecraft in esecuzione", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return
                End If
            Catch
                ' Ignora eventuali errori di stato processo
            End Try
        End If
        Me.Close()
        Form1.VerifyInstallation()

    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Dim result = MessageBox.Show("Sei sicuro di voler reinstallare minecraft?", "Conferma", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If Not Form1.mcTask.HasExited Then
            MessageBox.Show("Chiudi prima Minecraft, poi riprova.", "Minecraft in esecuzione", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If result = DialogResult.Yes Then
            Form1.MCReinstall()
        End If

    End Sub

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles Button6.Click
        Dim result = MessageBox.Show("Sei sicuro di voler reinstallare tutto? Verranno rimossi Minecraft, cache, download e dati pacchetti.", "Conferma", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)

        If result <> DialogResult.Yes Then
            If Form1.mcTask IsNot Nothing Then
                Try
                    If Not Form1.mcTask.HasExited Then
                        MessageBox.Show("Chiudi prima Minecraft, poi riprova.", "Minecraft in esecuzione", MessageBoxButtons.OK, MessageBoxIcon.Information)
                        Return
                    End If
                Catch
                    ' Ignora eventuali errori di stato processo
                End Try
            End If
            Form1.ReinstallAll()
            Return
        End If
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Me.Close()
    End Sub

    Private Sub Settings_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        usernameTxt.Text = My.Settings.username
        Dim timer2 As New System.Windows.Forms.Timer()
        timer2.Interval = 1
        AddHandler timer2.Tick, Sub(s, ev)
                                    If drag Then
                                        Me.Location = New Point(MousePosition.X - dragOffset.X, MousePosition.Y - dragOffset.Y)
                                    End If
                                End Sub
        timer2.Start()
    End Sub

    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles settingsPanel.MouseDown
        If e.Button = MouseButtons.Left Then
            drag = True
            dragOffset = New Point(e.X, e.Y)
        End If
    End Sub
    Private Sub Form1_MouseUp(sender As Object, e As MouseEventArgs) Handles settingsPanel.MouseUp
        drag = False
    End Sub

    Private Async Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        Dim result As DialogResult = MessageBox.Show("Sei sicuro di voler cambiare branch? Potresti non poter giocare a GangDrogaCity Online o alcune feature potrebbero essere disattivate: procedi solo se sai cosa stai facendo.", "Conferma", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
        If result <> DialogResult.Yes Then
            Return
        End If
        Dim selectedBranch = Await Form1.ShowBranchSelectionAsync()
        If selectedBranch Is Nothing Then
            Form1.AddLog("[DEV] Nessun branch selezionato, chiusura.")
            Return
        End If
        Form1.repobranch = selectedBranch
        My.Settings.currentBranch = Form1.repobranch
        Form1.data = "https://github.com/jamnaga/wtf-modpack/archive/refs/heads/" & Form1.repobranch & ".zip"
        Form1.repoBasepath = "https://raw.githubusercontent.com/jamnaga/wtf-modpack/refs/heads/" & Form1.repobranch & "/"
        Form1.AddLog($"[DEV] Branch selezionato: {Form1.repobranch}")
        Form1.boot()
        Me.Close()
    End Sub
End Class