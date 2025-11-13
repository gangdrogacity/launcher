<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Form1))
        Button1 = New Button()
        PictureBox1 = New PictureBox()
        ProgressBar1 = New ProgressBar()
        statusText = New Label()
        Panel1 = New Panel()
        menuPanel = New Panel()
        userLabel = New Label()
        playBtn = New Button()
        Button2 = New Button()
        settingsPanel = New Panel()
        settingsSavebtn = New Button()
        usernameTxt = New TextBox()
        Label2 = New Label()
        Label1 = New Label()
        operationPanel = New Panel()
        operationText = New Label()
        doNotPowerOffPanel = New Panel()
        Label3 = New Label()
        crashPanel = New Panel()
        Button3 = New Button()
        Label5 = New Label()
        Label4 = New Label()
        versionLabel = New Label()
        CType(PictureBox1, ComponentModel.ISupportInitialize).BeginInit()
        Panel1.SuspendLayout()
        menuPanel.SuspendLayout()
        settingsPanel.SuspendLayout()
        operationPanel.SuspendLayout()
        doNotPowerOffPanel.SuspendLayout()
        crashPanel.SuspendLayout()
        SuspendLayout()
        ' 
        ' Button1
        ' 
        Button1.BackColor = Color.Firebrick
        Button1.FlatStyle = FlatStyle.Flat
        Button1.Font = New Font("Segoe UI", 9F, FontStyle.Bold)
        Button1.ForeColor = Color.White
        Button1.Location = New Point(1294, 9)
        Button1.Margin = New Padding(0)
        Button1.Name = "Button1"
        Button1.Size = New Size(39, 23)
        Button1.TabIndex = 0
        Button1.Text = "X"
        Button1.UseVisualStyleBackColor = False
        ' 
        ' PictureBox1
        ' 
        PictureBox1.BackgroundImage = My.Resources.Resources.logo
        PictureBox1.BackgroundImageLayout = ImageLayout.Stretch
        PictureBox1.Location = New Point(368, -5)
        PictureBox1.Name = "PictureBox1"
        PictureBox1.Size = New Size(578, 587)
        PictureBox1.TabIndex = 1
        PictureBox1.TabStop = False
        ' 
        ' ProgressBar1
        ' 
        ProgressBar1.ForeColor = Color.White
        ProgressBar1.Location = New Point(0, 581)
        ProgressBar1.Name = "ProgressBar1"
        ProgressBar1.Size = New Size(1338, 19)
        ProgressBar1.TabIndex = 2
        ' 
        ' statusText
        ' 
        statusText.AutoSize = True
        statusText.Font = New Font("Segoe UI", 15F)
        statusText.ForeColor = Color.White
        statusText.ImageAlign = ContentAlignment.TopCenter
        statusText.Location = New Point(0, 550)
        statusText.Name = "statusText"
        statusText.Size = New Size(24, 28)
        statusText.TabIndex = 3
        statusText.Text = "..."
        statusText.TextAlign = ContentAlignment.TopCenter
        ' 
        ' Panel1
        ' 
        Panel1.Controls.Add(ProgressBar1)
        Panel1.Controls.Add(statusText)
        Panel1.Controls.Add(PictureBox1)
        Panel1.Location = New Point(4, 45)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(1338, 602)
        Panel1.TabIndex = 5
        ' 
        ' menuPanel
        ' 
        menuPanel.Controls.Add(userLabel)
        menuPanel.Controls.Add(playBtn)
        menuPanel.Location = New Point(50, 0)
        menuPanel.Name = "menuPanel"
        menuPanel.Size = New Size(1213, 368)
        menuPanel.TabIndex = 6
        menuPanel.Visible = False
        ' 
        ' userLabel
        ' 
        userLabel.AutoSize = True
        userLabel.Font = New Font("Segoe UI", 15F)
        userLabel.ForeColor = Color.White
        userLabel.Location = New Point(3, 9)
        userLabel.Name = "userLabel"
        userLabel.Size = New Size(242, 56)
        userLabel.TabIndex = 1
        userLabel.Text = " Username non impostato." & vbCrLf & "Clicca qui per impostarlo"
        userLabel.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' playBtn
        ' 
        playBtn.BackColor = Color.Green
        playBtn.Enabled = False
        playBtn.FlatStyle = FlatStyle.Flat
        playBtn.Font = New Font("Segoe UI", 15F, FontStyle.Bold)
        playBtn.ForeColor = Color.White
        playBtn.Location = New Point(399, 184)
        playBtn.Name = "playBtn"
        playBtn.Size = New Size(420, 100)
        playBtn.TabIndex = 0
        playBtn.Text = "..."
        playBtn.UseVisualStyleBackColor = False
        ' 
        ' Button2
        ' 
        Button2.BackColor = Color.FromArgb(CByte(192), CByte(192), CByte(0))
        Button2.FlatStyle = FlatStyle.Flat
        Button2.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button2.ForeColor = Color.White
        Button2.Location = New Point(317, 106)
        Button2.Name = "Button2"
        Button2.Size = New Size(103, 28)
        Button2.TabIndex = 2
        Button2.Text = "MC Reset"
        Button2.UseVisualStyleBackColor = False
        ' 
        ' settingsPanel
        ' 
        settingsPanel.BackColor = Color.FromArgb(CByte(64), CByte(64), CByte(64))
        settingsPanel.Controls.Add(Button2)
        settingsPanel.Controls.Add(settingsSavebtn)
        settingsPanel.Controls.Add(usernameTxt)
        settingsPanel.Controls.Add(Label2)
        settingsPanel.Controls.Add(Label1)
        settingsPanel.Location = New Point(443, 125)
        settingsPanel.Name = "settingsPanel"
        settingsPanel.Size = New Size(437, 146)
        settingsPanel.TabIndex = 2
        settingsPanel.Visible = False
        ' 
        ' settingsSavebtn
        ' 
        settingsSavebtn.BackColor = Color.Black
        settingsSavebtn.FlatStyle = FlatStyle.Flat
        settingsSavebtn.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        settingsSavebtn.ForeColor = Color.White
        settingsSavebtn.Location = New Point(338, 15)
        settingsSavebtn.Name = "settingsSavebtn"
        settingsSavebtn.Size = New Size(82, 30)
        settingsSavebtn.TabIndex = 3
        settingsSavebtn.Text = "Salva"
        settingsSavebtn.UseVisualStyleBackColor = False
        ' 
        ' usernameTxt
        ' 
        usernameTxt.Location = New Point(106, 64)
        usernameTxt.Name = "usernameTxt"
        usernameTxt.Size = New Size(171, 23)
        usernameTxt.TabIndex = 5
        ' 
        ' Label2
        ' 
        Label2.AutoSize = True
        Label2.Font = New Font("Segoe UI", 12F)
        Label2.ForeColor = Color.White
        Label2.Location = New Point(19, 62)
        Label2.Name = "Label2"
        Label2.Size = New Size(81, 21)
        Label2.TabIndex = 4
        Label2.Text = "Username"
        Label2.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' Label1
        ' 
        Label1.AutoSize = True
        Label1.Font = New Font("Segoe UI", 15F)
        Label1.ForeColor = Color.White
        Label1.Location = New Point(17, 13)
        Label1.Name = "Label1"
        Label1.Size = New Size(125, 28)
        Label1.TabIndex = 3
        Label1.Text = "Impostazioni"
        Label1.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' operationPanel
        ' 
        operationPanel.BackColor = Color.Olive
        operationPanel.Controls.Add(operationText)
        operationPanel.Location = New Point(368, 6)
        operationPanel.Name = "operationPanel"
        operationPanel.Size = New Size(585, 68)
        operationPanel.TabIndex = 6
        operationPanel.Visible = False
        ' 
        ' operationText
        ' 
        operationText.AutoSize = True
        operationText.Font = New Font("Consolas", 15F, FontStyle.Bold)
        operationText.ForeColor = Color.White
        operationText.Location = New Point(19, 24)
        operationText.Name = "operationText"
        operationText.Size = New Size(54, 23)
        operationText.TabIndex = 3
        operationText.Text = "ABCD"
        operationText.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' doNotPowerOffPanel
        ' 
        doNotPowerOffPanel.BackColor = Color.Teal
        doNotPowerOffPanel.Controls.Add(Label3)
        doNotPowerOffPanel.Location = New Point(368, 539)
        doNotPowerOffPanel.Name = "doNotPowerOffPanel"
        doNotPowerOffPanel.Size = New Size(585, 38)
        doNotPowerOffPanel.TabIndex = 7
        doNotPowerOffPanel.Visible = False
        ' 
        ' Label3
        ' 
        Label3.AutoSize = True
        Label3.Font = New Font("Consolas", 12F, FontStyle.Bold)
        Label3.ForeColor = Color.White
        Label3.Location = New Point(34, 10)
        Label3.Name = "Label3"
        Label3.Size = New Size(513, 19)
        Label3.TabIndex = 3
        Label3.Text = "NON PREMERE IL TASTO POWER O RESET DURANTE LE OPERAZIONI"
        Label3.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' crashPanel
        ' 
        crashPanel.BackColor = Color.FromArgb(CByte(192), CByte(0), CByte(0))
        crashPanel.Controls.Add(Button3)
        crashPanel.Controls.Add(Label5)
        crashPanel.Controls.Add(Label4)
        crashPanel.Location = New Point(368, 374)
        crashPanel.Name = "crashPanel"
        crashPanel.Size = New Size(585, 123)
        crashPanel.TabIndex = 7
        crashPanel.Visible = False
        ' 
        ' Button3
        ' 
        Button3.BackColor = Color.Navy
        Button3.FlatStyle = FlatStyle.Flat
        Button3.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button3.ForeColor = Color.White
        Button3.Location = New Point(243, 83)
        Button3.Name = "Button3"
        Button3.Size = New Size(82, 30)
        Button3.TabIndex = 6
        Button3.Text = "Invia"
        Button3.UseVisualStyleBackColor = False
        ' 
        ' Label5
        ' 
        Label5.AutoSize = True
        Label5.Font = New Font("Consolas", 10F, FontStyle.Bold)
        Label5.ForeColor = Color.White
        Label5.Location = New Point(28, 37)
        Label5.Name = "Label5"
        Label5.Size = New Size(528, 34)
        Label5.TabIndex = 4
        Label5.Text = "Sembra che il gioco abbia avuto un problema, " & vbCrLf & "invia il crash report per farlo esaminare e risolvere il problema"
        Label5.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' Label4
        ' 
        Label4.AutoSize = True
        Label4.Font = New Font("Consolas", 15F, FontStyle.Bold)
        Label4.ForeColor = Color.White
        Label4.Location = New Point(6, 6)
        Label4.Name = "Label4"
        Label4.Size = New Size(98, 23)
        Label4.TabIndex = 3
        Label4.Text = "Uh Oh :/"
        Label4.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' versionLabel
        ' 
        versionLabel.AutoSize = True
        versionLabel.ForeColor = Color.White
        versionLabel.Location = New Point(6, 6)
        versionLabel.Name = "versionLabel"
        versionLabel.Size = New Size(37, 15)
        versionLabel.TabIndex = 8
        versionLabel.Text = "v0.0.0"
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = SystemColors.ActiveCaptionText
        ClientSize = New Size(1345, 652)
        Controls.Add(versionLabel)
        Controls.Add(settingsPanel)
        Controls.Add(crashPanel)
        Controls.Add(operationPanel)
        Controls.Add(doNotPowerOffPanel)
        Controls.Add(Button1)
        Controls.Add(menuPanel)
        Controls.Add(Panel1)
        FormBorderStyle = FormBorderStyle.None
        Icon = CType(resources.GetObject("$this.Icon"), Icon)
        MaximizeBox = False
        Name = "Form1"
        StartPosition = FormStartPosition.CenterScreen
        Text = "GangDrogaCity"
        CType(PictureBox1, ComponentModel.ISupportInitialize).EndInit()
        Panel1.ResumeLayout(False)
        Panel1.PerformLayout()
        menuPanel.ResumeLayout(False)
        menuPanel.PerformLayout()
        settingsPanel.ResumeLayout(False)
        settingsPanel.PerformLayout()
        operationPanel.ResumeLayout(False)
        operationPanel.PerformLayout()
        doNotPowerOffPanel.ResumeLayout(False)
        doNotPowerOffPanel.PerformLayout()
        crashPanel.ResumeLayout(False)
        crashPanel.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents Button1 As Button
    Friend WithEvents PictureBox1 As PictureBox
    Friend WithEvents ProgressBar1 As ProgressBar
    Friend WithEvents statusText As Label
    Friend WithEvents Panel1 As Panel
    Friend WithEvents menuPanel As Panel
    Friend WithEvents playBtn As Button
    Friend WithEvents settingsPanel As Panel
    Friend WithEvents userLabel As Label
    Friend WithEvents settingsSavebtn As Button
    Friend WithEvents usernameTxt As TextBox
    Friend WithEvents Label2 As Label
    Friend WithEvents Label1 As Label
    Friend WithEvents Button2 As Button
    Friend WithEvents operationPanel As Panel
    Friend WithEvents operationText As Label
    Friend WithEvents doNotPowerOffPanel As Panel
    Friend WithEvents Label3 As Label
    Friend WithEvents crashPanel As Panel
    Friend WithEvents Label4 As Label
    Friend WithEvents Label5 As Label
    Friend WithEvents Button4 As Button
    Friend WithEvents Button3 As Button
    Friend WithEvents versionLabel As Label

End Class
