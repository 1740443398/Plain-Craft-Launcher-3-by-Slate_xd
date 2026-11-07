Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' 插件权限系统：把「插件能做什么」变成用户可见、可拒绝的东西。
''' <para>
''' <b>为什么要授权而不是直接放开</b>：插件生态的自由度和安全性是一对矛盾。
''' 完全放开等于把机器交给第三方代码；完全锁死等于没人愿意写插件。
''' 折中办法是：能力按项声明，首次启用时明确列出，用户点同意才生效。
''' </para>
''' <para>
''' <b>为什么默认拒绝</b>：如果默认允许，用户根本不会看那个弹窗。
''' 只有默认拒绝、必须显式同意，授权才有意义。
''' </para>
''' </summary>
Public Module ModPluginPermission

    ''' <summary>插件可以申请的权限。</summary>
    Public Enum Perm
        ''' <summary>发网络请求（任何域名）。</summary>
        Network = 0
        ''' <summary>读插件自己目录以外的文件（只读）。</summary>
        FileRead = 1
        ''' <summary>写插件自己目录以外的文件。</summary>
        FileWrite = 2
        ''' <summary>读取启动器设置。</summary>
        ReadSettings = 3
        ''' <summary>修改启动器设置（含主题）。</summary>
        WriteSettings = 4
        ''' <summary>读游戏实例列表与基本信息。</summary>
        ReadInstances = 5
        ''' <summary>弹通知与提示。</summary>
        Notify = 6
        ''' <summary>读写剪贴板。</summary>
        Clipboard = 7
        ''' <summary>在标题栏加入口。</summary>
        Titlebar = 8
    End Enum

    ''' <summary>权限的展示信息。</summary>
    Public Class PermInfo
        Public Property Perm As Perm
        Public Property Key As String = ""
        Public Property Title As String = ""
        Public Property Detail As String = ""
        ''' <summary>风险等级 0=低 1=中 2=高。</summary>
        Public Property Risk As Integer = 0
    End Class

    ''' <summary>全部权限定义。</summary>
    Public ReadOnly AllPerms As New List(Of PermInfo) From {
        New PermInfo With {.Perm = Perm.Network, .Key = "network", .Risk = 1,
            .Title = "网络访问", .Detail = "向任意网址发送请求。插件可能把你的信息发到第三方服务器。"},
        New PermInfo With {.Perm = Perm.FileRead, .Key = "fileRead", .Risk = 1,
            .Title = "读取文件", .Detail = "读取启动器目录下的文件。账号令牌与 API Key 会被自动屏蔽。"},
        New PermInfo With {.Perm = Perm.FileWrite, .Key = "fileWrite", .Risk = 2,
            .Title = "写入文件", .Detail = "修改启动器目录下的文件。写错可能导致启动器无法启动。"},
        New PermInfo With {.Perm = Perm.ReadSettings, .Key = "readSettings", .Risk = 0,
            .Title = "读取设置", .Detail = "读取主题、界面偏好等设置项。"},
        New PermInfo With {.Perm = Perm.WriteSettings, .Key = "writeSettings", .Risk = 1,
            .Title = "修改设置", .Detail = "修改主题色、界面偏好等设置项。"},
        New PermInfo With {.Perm = Perm.ReadInstances, .Key = "readInstances", .Risk = 0,
            .Title = "读取实例列表", .Detail = "读取你有哪些游戏实例及其版本号。"},
        New PermInfo With {.Perm = Perm.Notify, .Key = "notify", .Risk = 0,
            .Title = "弹出通知", .Detail = "在界面上弹出提示消息。"},
        New PermInfo With {.Perm = Perm.Clipboard, .Key = "clipboard", .Risk = 1,
            .Title = "剪贴板", .Detail = "读取和修改剪贴板内容。"},
        New PermInfo With {.Perm = Perm.Titlebar, .Key = "titlebar", .Risk = 0,
            .Title = "标题栏入口", .Detail = "在启动器标题栏添加自己的按钮。"}
    }

    ''' <summary>按 key 找定义。</summary>
    Public Function FindByKey(Key As String) As PermInfo
        If String.IsNullOrWhiteSpace(Key) Then Return Nothing
        Return AllPerms.FirstOrDefault(Function(p) String.Equals(p.Key, Key, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' 从清单里解析出申请的权限。清单里写 <c>permissions: ["network", "fileRead"]</c>。
    ''' </summary>
    Public Function Parse(Manifest As PluginManifest) As List(Of PermInfo)
        Dim Out As New List(Of PermInfo)
        Try
            If Manifest Is Nothing Then Return Out
            For Each Key In Manifest.Permissions
                Dim Info = FindByKey(Key)
                If Info IsNot Nothing AndAlso Not Out.Any(Function(x) x.Perm = Info.Perm) Then
                    Out.Add(Info)
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析插件权限失败")
        End Try
        Return Out
    End Function

    ''' <summary>用户已经同意的权限（按插件 id）。</summary>
    Private Function GetGranted(PluginId As String) As HashSet(Of String)
        Try
            Dim Raw = Settings.Get(Of String)("PluginGranted." & PluginId)
            If String.IsNullOrWhiteSpace(Raw) Then Return New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Return New HashSet(Of String)(Raw.Split(","c).Select(Function(s) s.Trim()), StringComparer.OrdinalIgnoreCase)
        Catch
            Return New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        End Try
    End Function

    ''' <summary>保存用户同意的权限。</summary>
    Private Sub SetGranted(PluginId As String, Keys As IEnumerable(Of String))
        Try
            Settings.Set("PluginGranted." & PluginId, String.Join(",", Keys))
        Catch ex As Exception
            Logger.Warn(ex, "保存插件授权失败")
        End Try
    End Sub

    ''' <summary>某个权限是否已被用户同意。</summary>
    Public Function IsGranted(PluginId As String, P As Perm) As Boolean
        Try
            Dim Info = AllPerms.FirstOrDefault(Function(x) x.Perm = P)
            If Info Is Nothing Then Return False
            Return GetGranted(PluginId).Contains(Info.Key)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>同意全部申请的权限。</summary>
    Public Sub GrantAll(Entry As PluginEntry)
        Try
            If Entry Is Nothing OrElse Entry.Manifest Is Nothing Then Return
            SetGranted(Entry.Manifest.Id, Parse(Entry.Manifest).Select(Function(p) p.Key))
        Catch ex As Exception
            Logger.Error(ex, "授予插件权限失败")
        End Try
    End Sub

    ''' <summary>撤销全部权限。</summary>
    Public Sub RevokeAll(PluginId As String)
        Try
            SetGranted(PluginId, Enumerable.Empty(Of String)())
        Catch ex As Exception
            Logger.Error(ex, "撤销插件权限失败")
        End Try
    End Sub

    ''' <summary>
    ''' 检查插件是否已获得全部所需权限。
    ''' </summary>
    Public Function IsFullyGranted(Entry As PluginEntry) As Boolean
        Try
            If Entry Is Nothing OrElse Entry.Manifest Is Nothing Then Return False
            Dim Need = Parse(Entry.Manifest)
            If Need.Count = 0 Then Return True
            Dim Got = GetGranted(Entry.Manifest.Id)
            Return Need.All(Function(p) Got.Contains(p.Key))
        Catch
            Return False
        End Try
    End Function

    ''' <summary>列出还没同意的权限，用于弹窗。</summary>
    Public Function GetMissing(Entry As PluginEntry) As List(Of PermInfo)
        Dim Out As New List(Of PermInfo)
        Try
            If Entry Is Nothing OrElse Entry.Manifest Is Nothing Then Return Out
            Dim Got = GetGranted(Entry.Manifest.Id)
            For Each P In Parse(Entry.Manifest)
                If Not Got.Contains(P.Key) Then Out.Add(P)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "列出未授权权限失败")
        End Try
        Return Out
    End Function

    ''' <summary>生成给用户看的权限清单文本。</summary>
    Public Function Describe(Perms As List(Of PermInfo)) As String
        If Perms Is Nothing OrElse Perms.Count = 0 Then Return "这个插件不需要特殊权限。"
        Dim Sb As New StringBuilder()
        For Each P In Perms
            Dim Mark = If(P.Risk >= 2, "【高风险】", If(P.Risk = 1, "【需注意】", ""))
            Sb.AppendLine($"· {Mark}{P.Title}")
            Sb.AppendLine($"  {P.Detail}")
        Next
        Return Sb.ToString().TrimEnd()
    End Function

End Module
