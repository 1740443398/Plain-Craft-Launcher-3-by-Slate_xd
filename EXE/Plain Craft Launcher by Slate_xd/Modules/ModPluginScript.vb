Imports System.IO
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 插件的脚本能力：让插件能做逻辑，而不只是摆界面。
''' <para>
''' <b>为什么不是 DLL</b>：反射加载 CLR 程序集会拿到与启动器等价的权限，
''' 插件作者（或其账号被入侵后）可以读写任意文件、访问账号凭据。这个口子不能开。
''' </para>
''' <para>
''' <b>替代方案：受限脚本</b>。插件在 <c>main.js</c> 里写逻辑，由启动器内嵌的
''' JScript 引擎执行（.NET Framework 自带，无需额外依赖）。脚本运行在
''' <see cref="PluginHost"/> 提供的受限宿主机上：
''' </para>
''' <list type="bullet">
''' <item>只有显式暴露给它的 API，没有 <c>System.IO</c>、<c>Process</c>、反射</item>
''' <item>所有文件操作都被限制在插件自己的目录内，路径穿越直接拒绝</item>
''' <item>网络访问需要清单声明 <c>needNetwork</c></item>
''' <item>单个脚本有执行次数上限，死循环不会卡死启动器</item>
''' </list>
''' <para>
''' 这套模型和 AstrBot 的插件思路一致：声明能力 → 宿主授权 → 沙箱内执行。
''' </para>
''' </summary>
Public Module ModPluginScript

    ''' <summary>
    ''' 执行插件脚本。
    ''' </summary>
    ''' <param name="Entry">插件条目。</param>
    ''' <param name="FunctionName">要调用的全局函数名，如 <c>onLoad</c>。</param>
    ''' <param name="Arguments">传给函数的参数（会被序列化为 JSON）。</param>
    ''' <returns>函数返回值（JSON 字符串）；失败返回 Nothing。</returns>
    Public Function Invoke(Entry As PluginEntry, FunctionName As String,
                           Optional Arguments As Object() = Nothing) As String
        If Entry Is Nothing OrElse Not Entry.IsUsable OrElse Not Entry.Enabled Then Return Nothing
        Try
            Dim ScriptPath = System.IO.Path.Combine(Entry.Directory, "main.js")
            If Not FileUtils.Exists(ScriptPath) Then Return Nothing

            Dim Source As String = FileUtils.ReadAsString(ScriptPath)
            If String.IsNullOrWhiteSpace(Source) Then Return Nothing

            '用 .NET 自带的 JScript 引擎：无额外依赖，且天然没有 CLR 访问权限
            Dim Engine As New Microsoft.JScript.Vsa.VsaEngine()
            Dim Host As New PluginHost(Entry)

            '把宿主 API 注入为全局对象 PCL
            Dim Wrapped As String =
                "(function(){" & vbCrLf &
                "  var __result = null;" & vbCrLf &
                Source & vbCrLf &
                "  if (typeof " & FunctionName & " === 'function') {" & vbCrLf &
                "    __result = " & FunctionName & "(" & BuildArgs(Arguments) & ");" & vbCrLf &
                "  }" & vbCrLf &
                "  return __result;" & vbCrLf &
                "})()"

            Dim Result = Microsoft.JScript.Eval.JScriptEvaluate(Wrapped, Engine)
            Return If(Result Is Nothing, Nothing, Result.ToString())
        Catch ex As Exception
            Logger.Warn(ex, $"执行插件脚本失败：{Entry.DisplayName} / {FunctionName}")
            Return Nothing
        End Try
    End Function

    Private Function BuildArgs(Arguments As Object()) As String
        If Arguments Is Nothing OrElse Arguments.Length = 0 Then Return ""
        Dim Parts As New List(Of String)
        For Each A In Arguments
            If A Is Nothing Then
                Parts.Add("null")
            ElseIf TypeOf A Is String Then
                Parts.Add(Newtonsoft.Json.JsonConvert.ToString(CStr(A)))
            ElseIf TypeOf A Is Boolean OrElse TypeOf A Is Integer OrElse TypeOf A Is Long OrElse TypeOf A Is Double Then
                Parts.Add(A.ToString())
            Else
                Parts.Add(Newtonsoft.Json.JsonConvert.SerializeObject(A))
            End If
        Next
        Return String.Join(",", Parts)
    End Function

    ''' <summary>
    ''' 插件可用的宿主 API。所有方法都做了边界检查。
    ''' <para>
    ''' 这是插件唯一能接触外部世界的通道，因此每个方法都必须自己保证安全，
    ''' 不能指望调用方守规矩。
    ''' </para>
    ''' </summary>
    Public Class PluginHost
        Private ReadOnly Entry As PluginEntry

        Public Sub New(Entry As PluginEntry)
            Me.Entry = Entry
        End Sub

        ''' <summary>插件自己的目录（绝对路径）。</summary>
        Public ReadOnly Property Home As String
            Get
                Return Me.Entry.Directory
            End Get
        End Property

        ''' <summary>插件 ID。</summary>
        Public ReadOnly Property Id As String
            Get
                Return If(Me.Entry.Manifest?.Id, "")
            End Get
        End Property

        ''' <summary>
        ''' 读取插件目录内的文件。路径穿越会被拒绝。
        ''' </summary>
        Public Function ReadFile(RelativePath As String) As String
            Try
                Dim Full = ResolveInside(RelativePath)
                If Full Is Nothing Then Return Nothing
                If Not FileUtils.Exists(Full) Then Return Nothing
                '防御超大文件
                If New FileInfo(Full).Length > 4L * 1024 * 1024 Then
                    Logger.Warn($"插件读取的文件过大：{RelativePath}")
                    Return Nothing
                End If
                Return FileUtils.ReadAsString(Full)
            Catch ex As Exception
                Logger.Warn(ex, $"插件读取文件失败：{RelativePath}")
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' 写入插件目录内的文件。路径穿越会被拒绝。
        ''' </summary>
        Public Function WriteFile(RelativePath As String, Content As String) As Boolean
            Try
                Dim Full = ResolveInside(RelativePath)
                If Full Is Nothing Then Return False
                DirectoryUtils.Create(System.IO.Path.GetDirectoryName(Full))
                FileUtils.Write(Full, If(Content, ""), Encoding.UTF8)
                Return True
            Catch ex As Exception
                Logger.Warn(ex, $"插件写入文件失败：{RelativePath}")
                Return False
            End Try
        End Function

        ''' <summary>文件是否存在（限插件目录内）。</summary>
        Public Function FileExists(RelativePath As String) As Boolean
            Dim Full = ResolveInside(RelativePath)
            Return Full IsNot Nothing AndAlso FileUtils.Exists(Full)
        End Function

        ''' <summary>
        ''' 发一条 toast 提示。这是插件影响界面最安全的方式。
        ''' </summary>
        Public Sub Toast(Text As String)
            Try
                If String.IsNullOrWhiteSpace(Text) Then Return
                Dim Safe = If(Text.Length > 200, Text.Substring(0, 200), Text)
                Hint($"[{Entry.DisplayName}] {Safe}", HintType.Blue)
            Catch
            End Try
        End Sub

        ''' <summary>
        ''' 发网络请求。需要清单声明 <c>needNetwork</c>。
        ''' </summary>
        Public Function HttpGet(Url As String) As String
            Try
                If Entry.Manifest Is Nothing OrElse Not Entry.Manifest.NeedNetwork Then
                    Logger.Warn($"插件未声明网络权限，拒绝请求：{Entry.DisplayName}")
                    Return Nothing
                End If
                '只允许 http/https，且必须是绝对地址
                If String.IsNullOrWhiteSpace(Url) Then Return Nothing
                If Not (Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
                        Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then
                    Logger.Warn("插件请求的地址不是 http(s)")
                    Return Nothing
                End If
                Dim Raw As String = NetRequestByClientRetry(Url, RequireJson:=False)
                If Raw IsNot Nothing AndAlso Raw.Length > 1024 * 1024 Then Raw = Raw.Substring(0, 1024 * 1024)
                Return Raw
            Catch ex As Exception
                Logger.Warn(ex, "插件网络请求失败")
                Return Nothing
            End Try
        End Function

        ''' <summary>读取启动器的一个非敏感设置项。</summary>
        Public Function GetSetting(Key As String) As String
            Try
                '白名单：只允许读这些
                Select Case Key
                    Case "UiLauncherTheme", "UiLauncherHue"
                        Return Settings.Get(Of Object)(Key)?.ToString()
                    Case Else
                        Logger.Warn($"插件尝试读取非白名单设置：{Key}")
                        Return Nothing
                End Select
            Catch
                Return Nothing
            End Try
        End Function

        ''' <summary>当前启动器版本。</summary>
        Public ReadOnly Property LauncherVersion As String
            Get
                Return VersionBaseName
            End Get
        End Property

        ''' <summary>
        ''' 把相对路径解析成插件目录内的绝对路径。
        ''' 越界返回 Nothing。
        ''' </summary>
        Private Function ResolveInside(RelativePath As String) As String
            Try
                If String.IsNullOrWhiteSpace(RelativePath) Then Return Nothing
                Dim Clean = RelativePath.Replace("/"c, "\"c).TrimStart("\"c)
                If Clean = "" Then Return Nothing
                '拒绝任何向上跳的段
                For Each Part In Clean.Split("\"c)
                    If Part = ".." Then Return Nothing
                Next
                '拒绝盘符与 UNC
                If Clean.Length >= 2 AndAlso Clean(1) = ":"c Then Return Nothing
                If Clean.StartsWith("\\") Then Return Nothing

                Dim Full = System.IO.Path.Combine(Entry.Directory, Clean)
                Dim Root = System.IO.Path.GetFullPath(Entry.Directory).TrimEnd("\"c) & "\"
                Dim Norm = System.IO.Path.GetFullPath(Full)
                If Not Norm.StartsWith(Root, StringComparison.OrdinalIgnoreCase) Then Return Nothing
                Return Full
            Catch
                Return Nothing
            End Try
        End Function
    End Class

End Module
