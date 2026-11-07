Imports System
Public Module Logger
    Public Sub Info(m As String)
    End Sub
    Public Sub Warn(ex As Exception, m As String)
        Console.WriteLine("    [Warn] " & m & " : " & ex.Message)
    End Sub
    Public Sub Warn(m As String)
    End Sub
    Public Sub [Error](ex As Exception, m As String)
        Console.WriteLine("    [Error] " & m)
    End Sub
End Module
Public Module ModBase
    Public Const VersionBaseName As String = "1.0.0"
    Public Const VersionCode As Integer = 100
End Module