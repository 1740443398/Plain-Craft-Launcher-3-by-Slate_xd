Imports System
Public Module Logger
    Public Sub Warn(ex As Exception, msg As String)
        Console.WriteLine("  [Warn] " & msg & " : " & ex.Message)
    End Sub
    Public Sub Warn(msg As String)
        Console.WriteLine("  [Warn] " & msg)
    End Sub
End Module