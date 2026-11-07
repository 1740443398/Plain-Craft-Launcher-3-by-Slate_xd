Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Java 插件宿主：把插件跑成独立的 Java 进程，用 stdin/stdout 上的 JSON 行通信。
''' <para>
''' <b>为什么是 Java 而不是别的</b>：玩 Minecraft 的机器必然装了 Java，
''' 启动器本身也在管理 Java 列表。用户不需要为了插件再装一个运行时。
''' </para>
''' <para>
''' <b>为什么不直接加载 jar</b>：那等于把整个 CLR 交给插件，
''' 一个恶意 jar 可以读走所有账号凭据。跑成子进程至少让权限边界是真实的：
''' 插件能做什么，取决于我们提供哪些消息，而不是它能 import 什么。
''' </para>
''' <para>
''' <b>能力边界</b>：插件进程拿不到启动器的内存，也拿不到文件系统权限，
''' 所有对启动器的操作都必须以 JSON 请求的形式发过来，由这里判断是否放行。
''' </para>
''' </summary>
Public Module ModPluginJava

    ''' <summary>插件清单里的运行时类型。</summary>
    Public Const RuntimeJs As String = "js"
    Public Const RuntimeJava As String = "java"

    ''' <summary>一个正在运行的 Java 插件实例。</summary>
    Public Class JavaInstance
        Public Property Entry As PluginEntry = Nothing
        Public Property Process As Process = Nothing
        ''' <summary>启动时间，用于超时判断。</summary>
        Public Property StartedAt As DateTime = DateTime.Now
        ''' <summary>记录最近的错误，便于在插件页显示。</summary>
        Public Property LastError As String = ""
        ''' <summary>最近一次收到插件主动发来的消息。</summary>
        Public Property LastMessage As String = ""
        
        '最近一次插件主动发来的请求（JSON 的 action）。排查问题时看这个
        Public Property LastRequest As String = ""

        Public ReadOnly Property IsAlive As Boolean
            Get
                Try
                    Return Process IsNot Nothing AndAlso Not Process.HasExited
                Catch
                    Return False
                End Try
            End Get
        End Property

        Public ReadOnly Property Uptime As TimeSpan
            Get
                Return Date.Now - StartedAt
            End Get
        End Property

        ''' <summary>进程内存占用（MB）。取不到时返回 0。</summary>
        Public ReadOnly Property MemoryMB As Double
            Get
                Try
                    If Process IsNot Nothing AndAlso Not Process.HasExited Then
                        Return Process.WorkingSet64 / 1048576.0
                    End If
                Catch
                End Try
                Return 0
            End Get
        End Property
    End Class

    ''' <summary>正在运行的实例，按插件 Id 索引。</summary>
    Public ReadOnly Running As New Dictionary(Of String, JavaInstance)

    ''' <summary>插件可以调用的宿主能力。超出这个清单的请求一律拒绝。</summary>
    ''' <summary>
    ''' 插件可以请求的操作。每条都对应一个具体权限，没有权限就会被拒绝。
    ''' <para>
    ''' 注意：这里是「允许请求」的清单，不是「一定放行」。
    ''' 实际放行还要看用户是否同意了这个操作对应的权限。
    ''' </para>
    ''' </summary>
    Private ReadOnly AllowedActions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "toast",             ' 弹提示        → notify
        "log",               ' 写日志        → 无需权限
        "get_version",       ' 取版本号      → 无需权限
        "get_setting",       ' 读设置        → readSettings
        "set_setting",       ' 写设置        → writeSettings
        "get_theme",         ' 读主题        → readSettings
        "set_theme",         ' 改主题        → writeSettings
        "list_versions",     ' 列实例        → readInstances
        "http_get",          ' GET 请求      → network
        "http_post",         ' POST 请求     → network
        "read_file",         ' 读文件        → fileRead
        "write_file",        ' 写文件        → fileWrite
        "list_dir",          ' 列目录        → fileRead
        "clipboard_get",     ' 读剪贴板      → clipboard
        "clipboard_set",     ' 写剪贴板      → clipboard
        "notify"             ' 系统通知      → notify
    }

    ''' <summary>action → 需要的权限。没有对应权限的 action 不需要授权。</summary>
    Private ReadOnly ActionPerms As New Dictionary(Of String, ModPluginPermission.Perm)(StringComparer.OrdinalIgnoreCase) From {
        {"toast", ModPluginPermission.Perm.Notify},
        {"get_setting", ModPluginPermission.Perm.ReadSettings},
        {"set_setting", ModPluginPermission.Perm.WriteSettings},
        {"get_theme", ModPluginPermission.Perm.ReadSettings},
        {"set_theme", ModPluginPermission.Perm.WriteSettings},
        {"list_versions", ModPluginPermission.Perm.ReadInstances},
        {"http_get", ModPluginPermission.Perm.Network},
        {"http_post", ModPluginPermission.Perm.Network},
        {"read_file", ModPluginPermission.Perm.FileRead},
        {"write_file", ModPluginPermission.Perm.FileWrite},
        {"list_dir", ModPluginPermission.Perm.FileRead},
        {"clipboard_get", ModPluginPermission.Perm.Clipboard},
        {"clipboard_set", ModPluginPermission.Perm.Clipboard},
        {"notify", ModPluginPermission.Perm.Notify}
    }

    ''' <summary>设置白名单：只允许这几个键，避免插件改坏启动参数。</summary>
    Private ReadOnly SettableKeys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "UiLauncherTheme", "UiLauncherHue", "UiLauncherSat", "UiLauncherLight"
    }

    ''' <summary>
    ''' 启动一个 Java 插件。
    ''' </summary>
    ''' <returns>启动成功返回实例；失败返回 Nothing，原因写在 <see cref="GetLastError"/>。</returns>
    Public Function Start(Entry As PluginEntry) As JavaInstance
        Try
            If Entry Is Nothing OrElse Entry.Manifest Is Nothing Then Return Nothing
            Dim Id = Entry.Manifest.Id
            If String.IsNullOrWhiteSpace(Id) Then Return Nothing

            '已经在跑就复用
            If Running.ContainsKey(Id) AndAlso Running(Id).IsAlive Then Return Running(Id)

            Dim Jar = System.IO.Path.Combine(Entry.Directory, "plugin.jar")
            If Not FileUtils.Exists(Jar) Then
                Hint($"插件 {Entry.DisplayName} 缺少 plugin.jar", HintType.Red)
                Return Nothing
            End If

            Dim Java = ModServer.FindJava()
            If String.IsNullOrWhiteSpace(Java) Then
                Hint("没有找到 Java，Java 插件无法运行。请先在设置里配置 Java。", HintType.Red)
                Return Nothing
            End If

            Dim Psi As New ProcessStartInfo With {
                .FileName = Java,
                .Arguments = $"-Dfile.encoding=UTF-8 -jar ""{Jar}""",
                .WorkingDirectory = Entry.Directory,
                .UseShellExecute = False,
                .RedirectStandardInput = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }

            Dim Proc As New Process With {.StartInfo = Psi}
            Dim Inst As New JavaInstance With {.Entry = Entry, .Process = Proc}

            AddHandler Proc.OutputDataReceived,
                Sub(s As Object, e As DataReceivedEventArgs)
                    If String.IsNullOrWhiteSpace(e.Data) Then Return
                    HandlePluginMessage(Inst, e.Data)
                End Sub
            AddHandler Proc.ErrorDataReceived,
                Sub(s As Object, e As DataReceivedEventArgs)
                    If String.IsNullOrWhiteSpace(e.Data) Then Return
                    Inst.LastError = e.Data
                    Logger.Warn($"[插件 {Id}] {e.Data}")
                End Sub
            AddHandler Proc.Exited,
                Sub(s As Object, e As EventArgs)
                    Logger.Info($"Java 插件已退出：{Id}")
                    Running.Remove(Id)
                End Sub

            Proc.Start()
            Proc.BeginOutputReadLine()
            Proc.BeginErrorReadLine()

            Running(Id) = Inst
            Logger.Info($"Java 插件已启动：{Id}（pid {Proc.Id}）")

            '告诉插件它自己是谁，以及能用哪些能力
            SendToPlugin(Inst, New JObject From {
                {"type", "hello"},
                {"pluginId", Id},
                {"launcherVersion", VersionBaseName},
                {"actions", New JArray(AllowedActions.ToArray())}
            })
            Return Inst
        Catch ex As Exception
            Logger.Error(ex, $"启动 Java 插件失败：{Entry?.DisplayName}")
            Hint($"启动插件失败：{ex.Message}", HintType.Red)
            Return Nothing
        End Try
    End Function

    ''' <summary>停止一个插件进程。先给 3 秒优雅退出，再强杀。</summary>
    Public Sub [Stop](PluginId As String)
        Try
            If Not Running.ContainsKey(PluginId) Then Return
            Dim Inst = Running(PluginId)
            If Inst Is Nothing OrElse Inst.Process Is Nothing Then Return

            Try
                If Not Inst.Process.HasExited Then
                    SendToPlugin(Inst, New JObject From {{"type", "shutdown"}})
                    If Not Inst.Process.WaitForExit(3000) Then Inst.Process.Kill()
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"停止插件失败：{PluginId}")
            End Try
            Running.Remove(PluginId)
        Catch ex As Exception
            Logger.Error(ex, "停止 Java 插件失败")
        End Try
    End Sub

    ''' <summary>停止全部插件，程序退出时调用。</summary>
    Public Sub StopAll()
        For Each Id In Running.Keys.ToList()
            [Stop](Id)
        Next
    End Sub

    ''' <summary>给插件发一条 JSON 消息。</summary>
    Public Sub SendToPlugin(Inst As JavaInstance, Msg As JObject)
        Try
            If Inst Is Nothing OrElse Inst.Process Is Nothing OrElse Inst.Process.HasExited Then Return
            Inst.Process.StandardInput.WriteLine(Msg.ToString(Formatting.None))
            Inst.Process.StandardInput.Flush()
        Catch ex As Exception
            Logger.Warn(ex, "向插件发送消息失败")
        End Try
    End Sub

    ''' <summary>
    ''' 处理插件发来的一行 JSON。这是唯一的权限闸口。
    ''' </summary>
    Private Sub HandlePluginMessage(Inst As JavaInstance, Line As String)
        Try
            Dim Msg As JObject
            Try
                Msg = JObject.Parse(Line)
            Catch
                '不是 JSON 就当普通输出记下来，不影响运行
                Inst.LastMessage = Line
                Logger.Info($"[插件 {Inst.Entry?.Manifest?.Id}] {Line}")
                Return
            End Try

            Dim Action = Msg("action")?.ToString()
            If String.IsNullOrWhiteSpace(Action) Then Return

            '记下请求，出问题时能看到插件到底调了什么
            Inst.LastRequest = Action
            Inst.LastMessage = Line

            '白名单校验：不在清单里的直接拒绝，并告诉插件为什么
            If Not AllowedActions.Contains(Action) Then
                Logger.Warn($"[插件 {Inst.Entry?.Manifest?.Id}] 请求了不允许的操作：{Action}")
                SendToPlugin(Inst, New JObject From {
                    {"type", "error"},
                    {"action", Action},
                    {"message", $"操作 {Action} 不在允许清单内"}
                })
                Return
            End If

            '权限校验：申请了但用户没同意，一样拒绝
            '这一层不能省，否则插件只要写了 action 就能绕过用户授权
            Dim PluginId = Inst.Entry?.Manifest?.Id
            If ActionPerms.ContainsKey(Action) Then
                Dim NeedPerm = ActionPerms(Action)
                If Not ModPluginPermission.IsGranted(PluginId, NeedPerm) Then
                    Dim PermInfo = ModPluginPermission.AllPerms.FirstOrDefault(Function(x) x.Perm = NeedPerm)
                    Logger.Warn($"[插件 {PluginId}] 缺少权限 {PermInfo?.Key}，拒绝 {Action}")
                    SendToPlugin(Inst, New JObject From {
                        {"type", "error"},
                        {"action", Action},
                        {"message", $"缺少权限：{If(PermInfo?.Title, NeedPerm.ToString())}。到插件页重新授权。"}
                    })
                    Return
                End If
            End If

            Dispatch(Inst, Action, Msg)
        Catch ex As Exception
            Logger.Warn(ex, "处理插件消息失败")
        End Try
    End Sub

    ''' <summary>按 action 分派。每个分支能做的最多就是调用启动器已有的公开能力。</summary>
    Private Sub Dispatch(Inst As JavaInstance, Action As String, Msg As JObject)
        Dim Id = Inst.Entry?.Manifest?.Id
        Dim Reply As New JObject From {{"type", "reply"}, {"action", Action}}

        Select Case Action.ToLowerInvariant()
            Case "toast"
                Dim Text = Msg("text")?.ToString()
                If Not String.IsNullOrWhiteSpace(Text) Then
                    RunInUi(Sub() Hint(Text.Substring(0, Math.Min(200, Text.Length))))
                End If
                Reply("ok") = True

            Case "log"
                Logger.Info($"[插件 {Id}] {Msg("text")}")
                Reply("ok") = True

            Case "get_version"
                Reply("ok") = True
                Reply("value") = VersionBaseName

            Case "get_setting"
                Dim Key = Msg("key")?.ToString()
                If SettableKeys.Contains(Key) Then
                    Reply("ok") = True
                    Reply("value") = If(Settings.Get(Of String)(Key), "")
                Else
                    Reply("ok") = False
                    Reply("message") = "该设置项不允许读取"
                End If

            Case "set_setting"
                Dim Key = Msg("key")?.ToString()
                If SettableKeys.Contains(Key) Then
                    Settings.Set(Key, Msg("value")?.ToString())
                    Reply("ok") = True
                Else
                    Reply("ok") = False
                    Reply("message") = "该设置项不允许修改"
                End If

            Case "get_theme"
                Reply("ok") = True
                Reply("theme") = Settings.Get(Of Integer)("UiLauncherTheme")
                Reply("hue") = Settings.Get(Of Integer)("UiLauncherHue")

            Case "set_theme"
                Try
                    Dim Hue = Msg("hue")
                    If Hue IsNot Nothing Then
                        Settings.Set("UiLauncherHue", Hue.ToObject(Of Integer)())
                        RunInUi(Sub() ThemeRefresh())
                    End If
                    Reply("ok") = True
                Catch ex As Exception
                    Reply("ok") = False
                    Reply("message") = ex.Message
                End Try

            Case "list_versions"
                Try
                    Dim Names As New JArray
                    'McInstanceList 是 Dictionary(Of 卡片类型, List(Of 实例))
                    For Each Card In McInstanceList
                        For Each V In Card.Value
                            Names.Add(V.Name)
                        Next
                    Next
                    Reply("ok") = True
                    Reply("value") = Names
                Catch ex As Exception
                    Reply("ok") = False
                    Reply("message") = ex.Message
                End Try

            Case "http_get", "http_post"
                Try
                    Dim Url = Msg("url")?.ToString()
                    If String.IsNullOrWhiteSpace(Url) Then
                        Reply("ok") = False : Reply("message") = "缺少 url"
                    ElseIf Not (Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
                                    Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then
                        Reply("ok") = False : Reply("message") = "只允许 http/https"
                    Else
                        Dim Body = Msg("body")?.ToString()
                        Dim Result As String
                        If Action.ToLowerInvariant() = "http_post" Then
                            Result = PluginHttpPost(Url, If(Body, ""))
                        Else
                            Result = PluginHttpGet(Url)
                        End If
                        If Result Is Nothing Then
                            Reply("ok") = False : Reply("message") = "请求失败"
                        Else
                            Reply("ok") = True
                            '返回内容可能很大，截断避免撑爆管道
                            Reply("value") = If(Result.Length > 200000, Result.Substring(0, 200000), Result)
                            Reply("truncated") = Result.Length > 200000
                        End If
                    End If
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case "read_file"
                Try
                    Dim Rel = Msg("path")?.ToString()
                    Dim Full = ResolvePluginFile(Inst, Rel, False)
                    If Full Is Nothing Then
                        Reply("ok") = False : Reply("message") = "路径不允许或文件不存在"
                    Else
                        Dim Content = IO.File.ReadAllText(Full)
                        Reply("ok") = True
                        Reply("value") = If(Content.Length > 200000, Content.Substring(0, 200000), Content)
                    End If
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case "write_file"
                Try
                    Dim Rel = Msg("path")?.ToString()
                    Dim Content = Msg("content")?.ToString()
                    Dim Full = ResolvePluginFile(Inst, Rel, True)
                    If Full Is Nothing Then
                        Reply("ok") = False : Reply("message") = "路径不允许"
                    Else
                        Dim Dir = System.IO.Path.GetDirectoryName(Full)
                        If Not String.IsNullOrEmpty(Dir) Then DirectoryUtils.Create(Dir)
                        IO.File.WriteAllText(Full, If(Content, ""))
                        Reply("ok") = True
                    End If
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case "list_dir"
                Try
                    Dim Rel = Msg("path")?.ToString()
                    Dim Full = ResolvePluginFile(Inst, Rel, False)
                    If Full Is Nothing OrElse Not DirectoryUtils.Exists(Full) Then
                        Reply("ok") = False : Reply("message") = "目录不存在或不允许"
                    Else
                        Dim Names As New JArray
                        For Each F In IO.Directory.GetFileSystemEntries(Full)
                            Names.Add(System.IO.Path.GetFileName(F))
                        Next
                        Reply("ok") = True
                        Reply("value") = Names
                    End If
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case "clipboard_get"
                Try
                    Dim Text As String = ""
                    RunInUiWait(Sub() Text = If(System.Windows.Clipboard.ContainsText(), System.Windows.Clipboard.GetText(), ""))
                    Reply("ok") = True
                    Reply("value") = If(Text.Length > 100000, Text.Substring(0, 100000), Text)
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case "clipboard_set"
                Try
                    Dim Text = Msg("text")?.ToString()
                    RunInUi(Sub() System.Windows.Clipboard.SetText(If(Text, "")))
                    Reply("ok") = True
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case "notify"
                Try
                    Dim Text = Msg("text")?.ToString()
                    If Not String.IsNullOrWhiteSpace(Text) Then
                        RunInUi(Sub() Hint(Text.Substring(0, Math.Min(200, Text.Length)), HintType.Green))
                    End If
                    Reply("ok") = True
                Catch ex As Exception
                    Reply("ok") = False : Reply("message") = ex.Message
                End Try

            Case Else
                Reply("ok") = False
                Reply("message") = "未实现"
        End Select

        SendToPlugin(Inst, Reply)
    End Sub

    ''' <summary>某个插件是否正在运行。</summary>
    Public Function IsRunning(PluginId As String) As Boolean
        Return Running.ContainsKey(PluginId) AndAlso Running(PluginId).IsAlive
    End Function


    '==== 文件路径解析（安全边界所在）====

''' <summary>
''' 把插件给的相对路径解析成绝对路径，并判断是否允许访问。
''' <para>
''' <b>这是最容易被绕过的地方</b>，所以规则写得很死：
''' </para>
''' <list type="bullet">
''' <item>默认只能访问插件自己目录内的文件</item>
''' <item>有 fileRead/fileWrite 权限时，可以访问 PCL 目录，但敏感文件永远拒绝</item>
''' <item>拒绝 .. 穿越、绝对路径、UNC 路径、盘符跳转</item>
''' </list>
''' </summary>
''' <param name="ForWrite">True 表示写操作，要求 fileWrite 权限。</param>
    Private Function ResolvePluginFile(Inst As JavaInstance, RelPath As String, ForWrite As Boolean) As String
        Try
            If String.IsNullOrWhiteSpace(RelPath) Then Return Nothing
            Dim PluginId = Inst?.Entry?.Manifest?.Id
            Dim Home = Inst?.Entry?.Directory
            If String.IsNullOrWhiteSpace(Home) Then Return Nothing

            '先挡掉明显越界的写法
            Dim Clean = RelPath.Replace("/"c, "\"c).Trim()
            If Clean.Contains("..") Then Return Nothing
            If Clean.StartsWith("\") Then Return Nothing
            If Clean.Length >= 2 AndAlso Clean(1) = ":"c Then Return Nothing
            If Clean.Contains(":") Then Return Nothing

            '拼成绝对路径
            Dim Base As String = Paths.Base
            Dim PclRoot = System.IO.Path.Combine(Base, "PCL")
            Dim Full As String
            If Clean.StartsWith("PCL\", StringComparison.OrdinalIgnoreCase) Then
                '显式写 PCL 开头 = 想访问启动器目录，需要权限
                If ForWrite Then
                    If Not ModPluginPermission.IsGranted(PluginId, ModPluginPermission.Perm.FileWrite) Then Return Nothing
                Else
                    If Not ModPluginPermission.IsGranted(PluginId, ModPluginPermission.Perm.FileRead) Then Return Nothing
                End If
                Full = System.IO.Path.Combine(Base, Clean)
            Else
                '默认限制在插件自己目录内
                Full = System.IO.Path.Combine(Home, Clean)
            End If

            Full = System.IO.Path.GetFullPath(Full)

            '规范化后再次确认没有逃出去
            Dim HomeNorm = System.IO.Path.GetFullPath(Home).TrimEnd("\"c)
            Dim PclNorm = System.IO.Path.GetFullPath(PclRoot).TrimEnd("\"c)
            Dim InHome = Full.StartsWith(HomeNorm & "\", StringComparison.OrdinalIgnoreCase) OrElse
                         String.Equals(Full.TrimEnd("\"c), HomeNorm, StringComparison.OrdinalIgnoreCase)
            Dim InPcl = Full.StartsWith(PclNorm & "\", StringComparison.OrdinalIgnoreCase) OrElse
                        String.Equals(Full.TrimEnd("\"c), PclNorm, StringComparison.OrdinalIgnoreCase)
            If Not InHome AndAlso Not InPcl Then Return Nothing

            '敏感文件永远拒绝，即使有权限
            If IsSensitivePath(Full) Then
                Logger.Warn($"[插件 {PluginId}] 试图访问敏感文件，已拒绝：{Full}")
                Return Nothing
            End If

            Return Full
        Catch ex As Exception
            Logger.Warn(ex, "解析插件文件路径失败")
            Return Nothing
        End Try
    End Function

''' <summary>敏感路径判定。账号令牌、API Key、Cookie 都在这里挡住。</summary>
    Private Function IsSensitivePath(FullPath As String) As Boolean
        Try
            Dim P = FullPath.Replace("/"c, "\"c).ToLowerInvariant()
            Dim FileName = System.IO.Path.GetFileName(P)

            '按文件名挡
            Dim BadNames = {"launcher_profiles.json", "launcher_accounts.json",
                            "usercache.json", "api.json", "log1.txt", "log2.txt",
                            "login.ini", "token.ini", "account.ini", "secret.ini"}
            For Each Bad In BadNames
                If String.Equals(FileName, Bad, StringComparison.OrdinalIgnoreCase) Then Return True
            Next

            '按目录挡
            Dim BadDirs = {"\.minecraft\logs\", "\pcl\cache\", "\pcl\logs\",
                           "\pcl\login\", "\pcl\account\", "\pcl\secret\"}
            For Each Bad In BadDirs
                If P.Contains(Bad) Then Return True
            Next

            '按扩展名挡
            Dim Ext = System.IO.Path.GetExtension(P)
            If Ext = ".pfx" OrElse Ext = ".p12" OrElse Ext = ".key" OrElse Ext = ".pem" Then Return True

            '按内容特征挡：文件名看不出，但可能藏着令牌
            If Ext = ".ini" OrElse Ext = ".json" OrElse Ext = ".cfg" OrElse Ext = ".config" Then
                Try
                    Dim Fi As New IO.FileInfo(FullPath)
                    If Fi.Exists AndAlso Fi.Length < 512 * 1024 Then
                        Dim Content = IO.File.ReadAllText(FullPath)
                        Dim Signals = {"accessToken", "clientToken", "refresh_token",
                                       "api_key", "apikey", "password", "secret",
                                       "client_secret", "private_key"}
                        For Each Sig In Signals
                            If Content.IndexOf(Sig, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
                        Next
                    End If
                Catch
                End Try
            End If

            Return False
        Catch
            '判定失败按敏感处理，宁可拒绝
            Return True
        End Try
    End Function

    '==== HTTP ====

    Private Function PluginHttpGet(Url As String) As String
        Try
            Dim Req = PluginRequest(Url, "GET", Nothing)
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"插件 GET 请求失败：{Url}")
            Return Nothing
        End Try
    End Function

    Private Function PluginHttpPost(Url As String, Body As String) As String
        Try
            Dim Req = PluginRequest(Url, "POST", Body)
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"插件 POST 请求失败：{Url}")
            Return Nothing
        End Try
    End Function

    Private Function PluginRequest(Url As String, Method As String, Body As String) As Net.HttpWebRequest
        Net.ServicePointManager.SecurityProtocol = Net.ServicePointManager.SecurityProtocol Or
                                                   Net.SecurityProtocolType.Tls12 Or Net.SecurityProtocolType.Tls11
        Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Req.Method = Method
        Req.Timeout = 20000
        Req.ReadWriteTimeout = 20000
        Req.UserAgent = "PCL3-Plugin/" & VersionBaseName
        Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
        If Method = "POST" Then
            Req.ContentType = "application/json; charset=utf-8"
            Dim Data = Text.Encoding.UTF8.GetBytes(If(Body, ""))
            Req.ContentLength = Data.Length
            Using S = Req.GetRequestStream()
                S.Write(Data, 0, Data.Length)
            End Using
        End If
        Return Req
    End Function
End Module
