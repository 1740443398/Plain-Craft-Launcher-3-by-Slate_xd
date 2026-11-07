Imports System
Imports System.IO
Public Module Logger
    Public Sub Info(msg As String)
    End Sub
    Public Sub Warn(ex As Exception, msg As String)
        Console.WriteLine("    [Warn] " & msg)
    End Sub
    Public Sub Warn(msg As String)
    End Sub
    Public Sub [Error](ex As Exception, msg As String)
        Console.WriteLine("    [Error] " & msg & " : " & ex.Message)
    End Sub
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
    Public Sub Write(p As String, content As String, enc As Text.Encoding)
        Directory.CreateDirectory(Path.GetDirectoryName(p))
        File.WriteAllText(p, content, enc)
    End Sub
End Module
Public Module DirectoryUtils
    Public Sub Create(p As String)
        If Not String.IsNullOrEmpty(p) Then Directory.CreateDirectory(p)
    End Sub
    Public Function Exists(p As String) As Boolean
        Return Directory.Exists(p)
    End Function
End Module
Public Module PathUtils
    Public Function GetLastPart(p As String) As String
        Return Path.GetFileName(p.TrimEnd("\"c))
    End Function
End Module
Public Module Paths
    Public ReadOnly Property Base As String
        Get
            Return Environment.GetEnvironmentVariable("PCL_TEST_BASE") & "\"
        End Get
    End Property
End Module
Public Module Settings
    Public Function [Get](Of T)(key As String) As T
        Return CType(CObj(True), T)
    End Function
    Public Sub [Set](key As String, value As Object)
    End Sub
End Module
Public Enum HintType
    Blue
    Green
    Red
End Enum
Public Module HintModule
    Public Sub Hint(msg As String, Optional t As HintType = HintType.Blue)
        Console.WriteLine("    [Hint] " & msg)
    End Sub
End Module
Public Module ModBase
    Public Const VersionCode As Integer = 100
End Module