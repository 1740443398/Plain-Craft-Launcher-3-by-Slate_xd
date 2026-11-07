Imports System.Security.Principal

'Windows 系统相关工具
'开源版中 WindowsUtils 来自外部程序集，源码里并不存在，
'调用它会在运行时抛异常，导致「内存优化」等功能直接失效。
'这里用纯 .NET 实现同样的能力，不依赖任何外部类型。
Public Module ModWindows

    ''' <summary>
    ''' 当前进程是否以管理员身份运行。
    ''' </summary>
    Public Function HasAdminRole() As Boolean
        Try
            Using Identity As WindowsIdentity = WindowsIdentity.GetCurrent()
                Dim Principal As New WindowsPrincipal(Identity)
                Return Principal.IsInRole(WindowsBuiltInRole.Administrator)
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "检测管理员权限失败，按无权限处理")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 当前系统可用物理内存（字节）。
    ''' </summary>
    Public Function AvailablePhysicalMemory() As Long
        Try
            Return CLng(My.Computer.Info.AvailablePhysicalMemory)
        Catch ex As Exception
            Logger.Warn(ex, "读取可用内存失败")
            Return 0
        End Try
    End Function

End Module
