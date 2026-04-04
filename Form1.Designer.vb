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
        PictureBox2 = New PictureBox()
        menuPanel = New Panel()
        Button7 = New Button()
        Button4 = New Button()
        userLabel = New Label()
        playBtn = New Button()
        operationPanel = New Panel()
        operationText = New Label()
        doNotPowerOffPanel = New Panel()
        Label3 = New Label()
        crashPanel = New Panel()
        Button3 = New Button()
        Label5 = New Label()
        Label4 = New Label()
        versionLabel = New Label()
        Button5 = New Button()
        CType(PictureBox1, ComponentModel.ISupportInitialize).BeginInit()
        Panel1.SuspendLayout()
        CType(PictureBox2, ComponentModel.ISupportInitialize).BeginInit()
        menuPanel.SuspendLayout()
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
        ProgressBar1.ForeColor = Color.Green
        ProgressBar1.Location = New Point(7, 588)
        ProgressBar1.Name = "ProgressBar1"
        ProgressBar1.Size = New Size(1347, 21)
        ProgressBar1.TabIndex = 2
        ' 
        ' statusText
        ' 
        statusText.AutoSize = True
        statusText.Font = New Font("Segoe UI", 15F)
        statusText.ForeColor = Color.White
        statusText.ImageAlign = ContentAlignment.TopCenter
        statusText.Location = New Point(99, 553)
        statusText.Name = "statusText"
        statusText.Size = New Size(24, 28)
        statusText.TabIndex = 3
        statusText.Text = "..."
        statusText.TextAlign = ContentAlignment.TopCenter
        ' 
        ' Panel1
        ' 
        Panel1.BackColor = Color.Transparent
        Panel1.Controls.Add(PictureBox2)
        Panel1.Controls.Add(ProgressBar1)
        Panel1.Controls.Add(statusText)
        Panel1.Controls.Add(PictureBox1)
        Panel1.Location = New Point(-7, 45)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(1357, 625)
        Panel1.TabIndex = 5
        ' 
        ' PictureBox2
        ' 
        PictureBox2.Image = CType(resources.GetObject("PictureBox2.Image"), Image)
        PictureBox2.Location = New Point(9, 503)
        PictureBox2.Name = "PictureBox2"
        PictureBox2.Size = New Size(88, 81)
        PictureBox2.SizeMode = PictureBoxSizeMode.Zoom
        PictureBox2.TabIndex = 4
        PictureBox2.TabStop = False
        ' 
        ' menuPanel
        ' 
        menuPanel.BackColor = Color.Transparent
        menuPanel.Controls.Add(Button7)
        menuPanel.Controls.Add(Button4)
        menuPanel.Controls.Add(userLabel)
        menuPanel.Controls.Add(playBtn)
        menuPanel.Location = New Point(4, 35)
        menuPanel.Name = "menuPanel"
        menuPanel.Size = New Size(1346, 498)
        menuPanel.TabIndex = 6
        menuPanel.Visible = False
        ' 
        ' Button7
        ' 
        Button7.BackColor = Color.FromArgb(CByte(128), CByte(64), CByte(0))
        Button7.Enabled = False
        Button7.FlatStyle = FlatStyle.Flat
        Button7.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button7.ForeColor = Color.White
        Button7.Location = New Point(440, 326)
        Button7.Name = "Button7"
        Button7.Size = New Size(420, 30)
        Button7.TabIndex = 8
        Button7.Text = "Gioca con la grafica di merda"
        Button7.UseVisualStyleBackColor = False
        ' 
        ' Button4
        ' 
        Button4.BackColor = Color.FromArgb(CByte(64), CByte(64), CByte(64))
        Button4.FlatStyle = FlatStyle.Flat
        Button4.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button4.ForeColor = Color.White
        Button4.Location = New Point(1219, 9)
        Button4.Name = "Button4"
        Button4.Size = New Size(110, 30)
        Button4.TabIndex = 7
        Button4.Text = "Impostazioni"
        Button4.UseVisualStyleBackColor = False
        ' 
        ' userLabel
        ' 
        userLabel.AutoSize = True
        userLabel.Font = New Font("Segoe UI", 15F)
        userLabel.ForeColor = Color.White
        userLabel.Location = New Point(2, 0)
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
        playBtn.Location = New Point(440, 223)
        playBtn.Name = "playBtn"
        playBtn.Size = New Size(420, 100)
        playBtn.TabIndex = 0
        playBtn.Text = "..."
        playBtn.UseVisualStyleBackColor = False
        ' 
        ' operationPanel
        ' 
        operationPanel.BackColor = Color.Olive
        operationPanel.Controls.Add(operationText)
        operationPanel.Location = New Point(368, 35)
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
        doNotPowerOffPanel.Location = New Point(368, 531)
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
        crashPanel.Location = New Point(368, 397)
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
        versionLabel.BackColor = Color.Transparent
        versionLabel.ForeColor = Color.White
        versionLabel.Location = New Point(6, 6)
        versionLabel.Name = "versionLabel"
        versionLabel.Size = New Size(37, 15)
        versionLabel.TabIndex = 8
        versionLabel.Text = "v0.0.0"
        ' 
        ' Button5
        ' 
        Button5.BackColor = Color.Teal
        Button5.FlatStyle = FlatStyle.Flat
        Button5.Font = New Font("Segoe UI", 8F, FontStyle.Bold)
        Button5.ForeColor = Color.White
        Button5.Location = New Point(1246, 9)
        Button5.Margin = New Padding(0)
        Button5.Name = "Button5"
        Button5.Size = New Size(39, 23)
        Button5.TabIndex = 9
        Button5.Text = "__"
        Button5.UseVisualStyleBackColor = False
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = SystemColors.ActiveCaptionText
        BackgroundImage = CType(resources.GetObject("$this.BackgroundImage"), Image)
        BackgroundImageLayout = ImageLayout.Stretch
        ClientSize = New Size(1345, 652)
        Controls.Add(Button5)
        Controls.Add(Button1)
        Controls.Add(versionLabel)
        Controls.Add(doNotPowerOffPanel)
        Controls.Add(operationPanel)
        Controls.Add(crashPanel)
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
        CType(PictureBox2, ComponentModel.ISupportInitialize).EndInit()
        menuPanel.ResumeLayout(False)
        menuPanel.PerformLayout()
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
    Friend WithEvents userLabel As Label
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
    Friend WithEvents PictureBox2 As PictureBox
    Friend WithEvents Button5 As Button
    Friend WithEvents Button7 As Button

End Class
