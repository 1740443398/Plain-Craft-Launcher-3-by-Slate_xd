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
    Public Sub Hint(m As String, Optional t As HintType = HintType.Blue)
        Console.WriteLine("    [Hint] " & m)
    End Sub
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
End Module
Public Module FileUtils
    Public Function Exists(p As String) As Boolean
        Return File.Exists(p)
    End Function
    Public Sub Delete(p As String, Optional r As Boolean = False)
        If File.Exists(p) Then File.Delete(p)
    End Sub
End Module
Public Module ModBase
    Public Const VersionBaseName As String = "1.0.0"
End Module