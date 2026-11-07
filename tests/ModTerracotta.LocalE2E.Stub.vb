Imports System
Imports System.IO
Public Module Logger
    Public Sub Warn(ex As Exception, msg As String)
        Console.WriteLine("    [Warn] " & msg)
    End Sub
    Public Sub Warn(msg As String)
        Console.WriteLine("    [Warn] " & msg)
    End Sub
    Public Sub Info(msg As String)
        Console.WriteLine("    [Info] " & msg)
    End Sub
    Public Sub [Error](ex As Exception, msg As String)
        Console.WriteLine("    [Error] " & msg)
    End Sub
End Module
Public Module FileUtils
    Public Function Exists(p As String) As Boolean
        Return File.Exists(p)
    End Function
    Public Sub Delete(p As String, Optional recycle As Boolean = False)
        If File.Exists(p) Then File.Delete(p)
    End Sub
    Public Sub Move(a As String, b As String)
        File.Move(a, b)
    End Sub
    Public Function ReadAsString(p As String) As String
        Return File.ReadAllText(p)
    End Function
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
    Public Function RemoveLastPart(p As String) As String
        Dim d = IO.Path.GetDirectoryName(p)
        Return If(d Is Nothing, "", d)
    End Function
    Public Function GetLastPart(p As String) As String
        Return IO.Path.GetFileName(p)
    End Function
End Module
Public Module Globals
    Public ReadOnly Property PathTemp As String
        Get
            Dim p = IO.Path.Combine(IO.Path.GetTempPath(), "pcltest-temp") & "\"
            Directory.CreateDirectory(p)
            Return p
        End Get
    End Property
End Module