Imports System
Imports System.IO
Public Module Logger
    Public Sub Info(m As String) : End Sub
    Public Sub Warn(ex As Exception, m As String) : End Sub
    Public Sub Warn(m As String) : End Sub
    Public Sub [Error](ex As Exception, m As String) : End Sub
End Module
Public Enum HintType
    Blue
    Green
    Red
End Enum
Public Module HintModule
    Public Sub Hint(m As String, Optional t As HintType = HintType.Blue) : End Sub
End Module
Public Module Paths
    Public ReadOnly Property Base As String
        Get
            Return Environment.GetEnvironmentVariable("PCL_TEST_BASE") & "\"
        End Get
    End Property
End Module
Public Module DirectoryUtils
    Public Sub Create(p As String)
        If Not String.IsNullOrEmpty(p) Then Directory.CreateDirectory(p)
    End Sub
    Public Function Exists(p As String) As Boolean
        Return Directory.Exists(p)
    End Function
End Module
Public Module FileUtils
    Public Function Exists(p As String) As Boolean
        Return File.Exists(p)
    End Function
    Public Function ReadAsString(p As String) As String
        Return File.ReadAllText(p)
    End Function
    Public Function TryReadAsString(p As String) As String
        Try
            If File.Exists(p) Then Return File.ReadAllText(p)
        Catch
        End Try
        Return Nothing
    End Function
    Public Sub Delete(p As String, Optional r As Boolean = False)
        If File.Exists(p) Then File.Delete(p)
    End Sub
    Public Sub Write(p As String, c As String, e As Text.Encoding)
        Dim d = Path.GetDirectoryName(p)
        If Not String.IsNullOrEmpty(d) Then Directory.CreateDirectory(d)
        File.WriteAllText(p, c, e)
    End Sub
End Module
Public Module Settings
    Private D As New Collections.Generic.Dictionary(Of String, Object)
    Public Function [Get](Of T)(k As String) As T
        If D.ContainsKey(k) Then Return CType(D(k), T)
        Return CType(CObj(""), T)
    End Function
    Public Sub [Set](k As String, v As Object)
        D(k) = v
    End Sub
End Module
Public Module ModBase
    Public Const VersionBaseName As String = "1.0.0"
    Public Const VersionCode As Integer = 100
End Module
Public Module Threading
    Public Sub RunInUi(a As Action)
        a()
    End Sub
    Public Sub RunInUiWait(a As Action)
        a()
    End Sub
End Module
Public Module ModServer
    Public Function FindJava() As String
        Return "java"
    End Function
End Module
Public Module PathUtils
    Public Function GetLastPart(p As String) As String
        Return IO.Path.GetFileName(p.TrimEnd("\"c))
    End Function
End Module
Public Module ThemeModule
    Public Sub ThemeRefresh() : End Sub
End Module
Public Module McInstanceModule
    Public McInstanceList As New Collections.Generic.Dictionary(Of String, Collections.Generic.List(Of Object))
End Module