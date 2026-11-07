Imports System
Imports System.IO
Public Module Logger
    Public Sub Info(m As String)
    End Sub
    Public Sub Warn(ex As Exception, m As String)
        Console.WriteLine("    [Warn] " & m)
    End Sub
    Public Sub Warn(m As String)
    End Sub
    Public Sub [Error](ex As Exception, m As String)
        Console.WriteLine("    [Error] " & m & " : " & ex.Message)
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
    Public Sub Write(p As String, c As String, e As Text.Encoding)
        Dim d = Path.GetDirectoryName(p)
        If Not String.IsNullOrEmpty(d) Then Directory.CreateDirectory(d)
        File.WriteAllText(p, c, e)
    End Sub
    Public Sub Delete(p As String, Optional recursive As Boolean = False)
        If File.Exists(p) Then File.Delete(p)
    End Sub
    Public Sub Move(a As String, b As String)
        If File.Exists(b) Then File.Delete(b)
        File.Move(a, b)
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
    Public Function [Get](Of T)(k As String) As T
        If GetType(T) Is GetType(String) Then Return CType(CObj("[]"), T)
        Return CType(CObj(0), T)
    End Function
    Public Sub [Set](k As String, v As Object)
    End Sub
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
Public Module StringUtils
    Public Function FormatByteSize(b As Long) As String
        Return Math.Round(b / 1048576.0, 1).ToString() & " MB"
    End Function
End Module
Public Module ModBase
    Public Const VersionCode As Integer = 100
    Public Const VersionBaseName As String = "1.0.0"
End Module
Public Class LoaderBase
    Public Property Progress As Double
End Class