Imports System

Public Module Logger
    Public Sub Warn(ex As Exception, msg As String)
        '测试环境静默
    End Sub
    Public Sub Warn(msg As String)
    End Sub
End Module

Public Module FileUtils
    Public Function Exists(p As String) As Boolean
        Return System.IO.File.Exists(p)
    End Function
End Module