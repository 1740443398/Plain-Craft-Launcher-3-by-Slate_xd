Imports System
Public Module Logger
    Public Sub Warn(ex As Exception, msg As String)
    End Sub
    Public Sub Warn(msg As String)
    End Sub
    Public Sub [Error](ex As Exception, msg As String)
    End Sub
End Module
Public Module FileUtils
    Public Function Exists(p As String) As Boolean
        Return System.IO.File.Exists(p)
    End Function
End Module
Public Module DirectoryUtils
    Public Function Exists(p As String) As Boolean
        Return System.IO.Directory.Exists(p)
    End Function
End Module
Public Module PathUtils
    Public Function GetLastPart(p As String) As String
        Return System.IO.Path.GetFileName(p)
    End Function
    Public Function GetExtension(p As String) As String
        Return System.IO.Path.GetExtension(p)
    End Function
End Module