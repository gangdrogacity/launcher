Imports System.Runtime.InteropServices

''' <summary>
''' Informazioni sulla piattaforma corrente (OS, architettura) e convenzioni dipendenti
''' dal sistema operativo: nome dell'eseguibile Java, separatore del classpath,
''' nome dell'asset di release per il self-update, ecc.
''' </summary>
Public Module Platform

    Public ReadOnly Property IsWindows As Boolean
        Get
            Return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        End Get
    End Property

    Public ReadOnly Property IsMacOS As Boolean
        Get
            Return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        End Get
    End Property

    Public ReadOnly Property IsLinux As Boolean
        Get
            Return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        End Get
    End Property

    Public ReadOnly Property IsArm64 As Boolean
        Get
            Return RuntimeInformation.OSArchitecture = Architecture.Arm64
        End Get
    End Property

    ''' <summary>Nome OS secondo la convenzione dei version JSON di Mojang: windows, linux, osx.</summary>
    Public ReadOnly Property MojangOSName As String
        Get
            If IsWindows Then Return "windows"
            If IsMacOS Then Return "osx"
            Return "linux"
        End Get
    End Property

    ''' <summary>Nome OS secondo la convenzione dell'API Adoptium: windows, linux, mac.</summary>
    Public ReadOnly Property AdoptiumOSName As String
        Get
            If IsWindows Then Return "windows"
            If IsMacOS Then Return "mac"
            Return "linux"
        End Get
    End Property

    ''' <summary>Architettura secondo la convenzione Adoptium: x64, aarch64.</summary>
    Public ReadOnly Property AdoptiumArch As String
        Get
            Return If(IsArm64, "aarch64", "x64")
        End Get
    End Property

    ''' <summary>Nome OS "umano" usato nei tag del manifest e negli asset di release: windows, linux, macos.</summary>
    Public ReadOnly Property OSTag As String
        Get
            If IsWindows Then Return "windows"
            If IsMacOS Then Return "macos"
            Return "linux"
        End Get
    End Property

    Public ReadOnly Property ArchTag As String
        Get
            Return If(IsArm64, "arm64", "x64")
        End Get
    End Property

    ''' <summary>Runtime identifier .NET della piattaforma corrente (win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64).</summary>
    Public ReadOnly Property RuntimeIdentifier As String
        Get
            Dim os As String = If(IsWindows, "win", If(IsMacOS, "osx", "linux"))
            Return os & "-" & ArchTag
        End Get
    End Property

    ''' <summary>Nome dell'asset della release GitHub per il self-update di questa piattaforma.</summary>
    Public ReadOnly Property UpdateAssetName As String
        Get
            If IsWindows Then Return $"GangDrogaCity-windows-{ArchTag}.zip"
            If IsMacOS Then Return $"GangDrogaCity-macos-{ArchTag}.zip"
            Return $"GangDrogaCity-linux-{ArchTag}.tar.gz"
        End Get
    End Property

    Public ReadOnly Property JavaExecutableName As String
        Get
            Return If(IsWindows, "java.exe", "java")
        End Get
    End Property

    Public ReadOnly Property ExecutableExtension As String
        Get
            Return If(IsWindows, ".exe", "")
        End Get
    End Property

    ''' <summary>Separatore delle voci del classpath Java (";" su Windows, ":" altrove).</summary>
    Public ReadOnly Property ClasspathSeparator As String
        Get
            Return If(IsWindows, ";", ":")
        End Get
    End Property

    ''' <summary>Versione dell'applicazione (dall'assembly di ingresso).</summary>
    Public ReadOnly Property AppVersion As Version
        Get
            Try
                Dim asm = Reflection.Assembly.GetEntryAssembly()
                If asm Is Nothing Then asm = Reflection.Assembly.GetExecutingAssembly()
                Dim v = asm.GetName().Version
                If v IsNot Nothing Then Return v
            Catch
            End Try
            Return New Version(0, 0, 0, 0)
        End Get
    End Property

    Public ReadOnly Property AppVersionString As String
        Get
            Dim v = AppVersion
            Return $"{v.Major}.{v.Minor}.{v.Build}.{Math.Max(0, v.Revision)}"
        End Get
    End Property

    ''' <summary>Percorso dell'eseguibile del launcher in esecuzione.</summary>
    Public Function CurrentExecutablePath() As String
        Try
            Dim p = Environment.ProcessPath
            If Not String.IsNullOrEmpty(p) Then Return p
        Catch
        End Try
        Return Diagnostics.Process.GetCurrentProcess().MainModule.FileName
    End Function

    ''' <summary>
    ''' Su macOS, se il launcher gira dentro un bundle .app, restituisce il percorso del bundle; altrimenti Nothing.
    ''' </summary>
    Public Function CurrentAppBundlePath() As String
        If Not IsMacOS Then Return Nothing
        Try
            Dim exe = CurrentExecutablePath()
            ' .../GangDrogaCity.app/Contents/MacOS/GangDrogaCity
            Dim macosDir = Path.GetDirectoryName(exe)
            Dim contentsDir = Path.GetDirectoryName(macosDir)
            Dim bundle = Path.GetDirectoryName(contentsDir)
            If Path.GetFileName(macosDir) = "MacOS" AndAlso Path.GetFileName(contentsDir) = "Contents" AndAlso bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) Then
                Return bundle
            End If
        Catch
        End Try
        Return Nothing
    End Function

    ''' <summary>Rende eseguibile un file su Unix (no-op su Windows).</summary>
    Public Sub MakeExecutable(filePath As String)
        If IsWindows Then Return
        Try
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead Or UnixFileMode.UserWrite Or UnixFileMode.UserExecute Or
                                           UnixFileMode.GroupRead Or UnixFileMode.GroupExecute Or
                                           UnixFileMode.OtherRead Or UnixFileMode.OtherExecute)
        Catch
        End Try
    End Sub

    ''' <summary>Cerca un eseguibile nel PATH (e in alcune cartelle note su Unix).</summary>
    Public Function FindInPath(ParamArray names As String()) As String
        Dim dirs As New List(Of String)
        Dim pathVar = Environment.GetEnvironmentVariable("PATH")
        If Not String.IsNullOrEmpty(pathVar) Then
            dirs.AddRange(pathVar.Split(Path.PathSeparator))
        End If
        If Not IsWindows Then
            dirs.AddRange({"/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/snap/bin"})
        End If
        For Each n In names
            For Each d In dirs
                Try
                    Dim candidate = Path.Combine(d, n)
                    If File.Exists(candidate) Then Return candidate
                Catch
                End Try
            Next
        Next
        Return Nothing
    End Function

End Module
