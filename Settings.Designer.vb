<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Settings
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Settings))
        Button1 = New Button()
        settingsPanel = New Panel()
        Button8 = New Button()
        Button6 = New Button()
        Button2 = New Button()
        settingsSavebtn = New Button()
        usernameTxt = New TextBox()
        Label2 = New Label()
        Label1 = New Label()
        Panel1 = New Panel()
        Button3 = New Button()
        settingsPanel.SuspendLayout()
        Panel1.SuspendLayout()
        SuspendLayout()
        ' 
        ' Button1
        ' 
        Button1.BackColor = Color.Firebrick
        Button1.FlatStyle = FlatStyle.Flat
        Button1.Font = New Font("Segoe UI", 9F, FontStyle.Bold)
        Button1.ForeColor = Color.White
        Button1.Location = New Point(271, 6)
        Button1.Margin = New Padding(0)
        Button1.Name = "Button1"
        Button1.Size = New Size(39, 23)
        Button1.TabIndex = 0
        Button1.Text = "X"
        Button1.UseVisualStyleBackColor = False
        ' 
        ' settingsPanel
        ' 
        settingsPanel.BackColor = Color.FromArgb(CByte(64), CByte(64), CByte(64))
        settingsPanel.Controls.Add(Button3)
        settingsPanel.Controls.Add(Button1)
        settingsPanel.Controls.Add(Button8)
        settingsPanel.Controls.Add(Button6)
        settingsPanel.Controls.Add(Button2)
        settingsPanel.Controls.Add(settingsSavebtn)
        settingsPanel.Controls.Add(usernameTxt)
        settingsPanel.Controls.Add(Label2)
        settingsPanel.Controls.Add(Label1)
        settingsPanel.Dock = DockStyle.Fill
        settingsPanel.Location = New Point(0, 0)
        settingsPanel.Name = "settingsPanel"
        settingsPanel.Size = New Size(318, 375)
        settingsPanel.TabIndex = 2
        ' 
        ' Button8
        ' 
        Button8.BackColor = Color.FromArgb(CByte(0), CByte(192), CByte(0))
        Button8.FlatStyle = FlatStyle.Flat
        Button8.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button8.ForeColor = Color.White
        Button8.Location = New Point(73, 224)
        Button8.Name = "Button8"
        Button8.Size = New Size(172, 28)
        Button8.TabIndex = 7
        Button8.Text = "Verifica installazione"
        Button8.UseVisualStyleBackColor = False
        ' 
        ' Button6
        ' 
        Button6.BackColor = Color.Red
        Button6.FlatStyle = FlatStyle.Flat
        Button6.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button6.ForeColor = Color.White
        Button6.Location = New Point(95, 292)
        Button6.Name = "Button6"
        Button6.Size = New Size(124, 28)
        Button6.TabIndex = 6
        Button6.Text = "Reinstalla tutto"
        Button6.UseVisualStyleBackColor = False
        ' 
        ' Button2
        ' 
        Button2.BackColor = Color.FromArgb(CByte(192), CByte(192), CByte(0))
        Button2.FlatStyle = FlatStyle.Flat
        Button2.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        Button2.ForeColor = Color.White
        Button2.Location = New Point(81, 258)
        Button2.Name = "Button2"
        Button2.Size = New Size(154, 28)
        Button2.TabIndex = 2
        Button2.Text = "Reinstalla Minecraft"
        Button2.UseVisualStyleBackColor = False
        ' 
        ' settingsSavebtn
        ' 
        settingsSavebtn.BackColor = Color.Black
        settingsSavebtn.FlatStyle = FlatStyle.Flat
        settingsSavebtn.Font = New Font("Segoe UI", 10F, FontStyle.Bold)
        settingsSavebtn.ForeColor = Color.White
        settingsSavebtn.Location = New Point(107, 137)
        settingsSavebtn.Name = "settingsSavebtn"
        settingsSavebtn.Size = New Size(104, 34)
        settingsSavebtn.TabIndex = 3
        settingsSavebtn.Text = "Salva"
        settingsSavebtn.UseVisualStyleBackColor = False
        ' 
        ' usernameTxt
        ' 
        usernameTxt.BackColor = Color.DimGray
        usernameTxt.BorderStyle = BorderStyle.FixedSingle
        usernameTxt.ForeColor = Color.White
        usernameTxt.Location = New Point(73, 89)
        usernameTxt.Name = "usernameTxt"
        usernameTxt.PlaceholderText = "Inserisci un username"
        usernameTxt.Size = New Size(171, 23)
        usernameTxt.TabIndex = 5
        ' 
        ' Label2
        ' 
        Label2.AutoSize = True
        Label2.Font = New Font("Segoe UI", 12F)
        Label2.ForeColor = Color.White
        Label2.Location = New Point(116, 59)
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
        ' Panel1
        ' 
        Panel1.Controls.Add(settingsPanel)
        Panel1.Dock = DockStyle.Fill
        Panel1.Location = New Point(0, 0)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(318, 375)
        Panel1.TabIndex = 5
        ' 
        ' Button3
        ' 
        Button3.BackColor = Color.Black
        Button3.FlatStyle = FlatStyle.Flat
        Button3.Font = New Font("Segoe UI", 7F, FontStyle.Bold)
        Button3.ForeColor = Color.White
        Button3.Location = New Point(222, 348)
        Button3.Name = "Button3"
        Button3.Size = New Size(88, 24)
        Button3.TabIndex = 8
        Button3.Text = "Cambia branch"
        Button3.UseVisualStyleBackColor = False
        ' 
        ' Settings
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = SystemColors.ActiveCaptionText
        ClientSize = New Size(318, 375)
        Controls.Add(Panel1)
        FormBorderStyle = FormBorderStyle.None
        Icon = CType(resources.GetObject("$this.Icon"), Icon)
        MaximizeBox = False
        MinimizeBox = False
        Name = "Settings"
        StartPosition = FormStartPosition.CenterScreen
        Text = "GangDrogaCity"
        settingsPanel.ResumeLayout(False)
        settingsPanel.PerformLayout()
        Panel1.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents Button1 As Button
    Friend WithEvents settingsPanel As Panel
    Friend WithEvents Button8 As Button
    Friend WithEvents Button6 As Button
    Friend WithEvents Button2 As Button
    Friend WithEvents settingsSavebtn As Button
    Friend WithEvents usernameTxt As TextBox
    Friend WithEvents Label2 As Label
    Friend WithEvents Label1 As Label
    Friend WithEvents Panel1 As Panel
    Friend WithEvents Button3 As Button

End Class
