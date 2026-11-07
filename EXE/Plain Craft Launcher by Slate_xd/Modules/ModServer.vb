Imports System.IO
Imports System.Diagnostics
Imports System.Text
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

''' <summary>
''' 一键开服器：列出本地服务端版本、下载官方服务端、按版本隔离启动。
''' <para>
''' <b>为什么只下载服务端</b>：Mojang 的版本清单里，客户端与服务端是两个独立条目
''' （<c>downloads.client</c> 与 <c>downloads.server</c>）。开服器只取 server，
''' 界面上也不出现任何「客户端下载」入口——避免玩家误装成客户端。
''' </para>
''' <para>
''' <b>版本隔离</b>：每个服务端版本放在独立的 <c>PCL\Servers\&lt;版本&gt;\</c> 下，
''' 各自有 server.properties / world / 模组，互不干扰。
''' </para>
''' </summary>
Public Module ModServer

    ''' <summary>服务器根目录。</summary>
    Public ReadOnly Property ServerDir As String
        Get
            Return Paths.Base & "PCL\Servers\"
        End Get
    End Property

    ''' <summary>一个服务端版本（对应一个隔离目录）。</summary>
    Public Class ServerVersion
        ''' <summary>版本名，也就是目录名，如 1.20.1。</summary>
        Public Property Name As String = ""
        ''' <summary>是否已下载 server.jar。</summary>
        Public Property Installed As Boolean = False
        ''' <summary>server.jar 的字节数（未安装时 0）。</summary>
        Public Property JarSize As Long = 0
        ''' <summary>已同意 EULA。</summary>
        Public Property EulaAccepted As Boolean = False
        ''' <summary>端口。</summary>
        Public Property Port As Integer = 25565
        ''' <summary>分配内存（MB）。</summary>
        Public Property MemoryMB As Integer = 2048
        ''' <summary>正版验证。</summary>
        Public Property OnlineMode As Boolean = True
        ''' <summary>motd。</summary>
        Public Property Motd As String = ""
        ''' <summary>是否启用（用于控制列表里的启动按钮）。</summary>
        Public Property Enabled As Boolean = True

        Public ReadOnly Property Dir As String
            Get
                Return System.IO.Path.Combine(ServerDir, Name)
            End Get
        End Property

        Public ReadOnly Property JarPath As String
            Get
                Return System.IO.Path.Combine(Dir, "server.jar")
            End Get
        End Property

        Public ReadOnly Property EulaPath As String
            Get
                Return System.IO.Path.Combine(Dir, "eula.txt")
            End Get
        End Property

        Public ReadOnly Property ConfigPath As String
            Get
                Return System.IO.Path.Combine(Dir, "pcl-server.json")
            End Get
        End Property

        Public ReadOnly Property ReadyText As String
            Get
                If Not Installed Then Return "未下载服务端"
                If Not EulaAccepted Then Return "已下载，需同意 EULA"
                Return "可以启动"
            End Get
        End Property
    End Class

    '==== 本地服务端版本 ====

    Public ReadOnly LocalVersions As New List(Of ServerVersion)

    ''' <summary>扫描本地的服务端版本目录。</summary>
    Public Sub ScanLocal()
        LocalVersions.Clear()
        Try
            DirectoryUtils.Create(ServerDir)
            For Each SubDir In Directory.GetDirectories(ServerDir)
                Try
                    Dim Name = PathUtils.GetLastPart(SubDir)
                    Dim Jar = System.IO.Path.Combine(SubDir, "server.jar")
                    Dim Cfg = System.IO.Path.Combine(SubDir, "pcl-server.json")
                    Dim V As New ServerVersion With {.Name = Name}

                    If FileUtils.Exists(Cfg) Then
                        Try
                            Dim Loaded = Newtonsoft.Json.JsonConvert.DeserializeObject(Of ServerVersion)(FileUtils.ReadAsString(Cfg))
                            If Loaded IsNot Nothing Then
                                V.Port = Loaded.Port
                                V.MemoryMB = Loaded.MemoryMB
                                V.OnlineMode = Loaded.OnlineMode
                                V.Motd = Loaded.Motd
                                V.Enabled = Loaded.Enabled
                            End If
                        Catch ex As Exception
                            Logger.Warn(ex, $"读取服务端配置失败：{Name}")
                        End Try
                    End If

                    If FileUtils.Exists(Jar) Then
                        V.Installed = True
                        V.JarSize = New FileInfo(Jar).Length
                    End If
                    V.EulaAccepted = FileUtils.Exists(System.IO.Path.Combine(SubDir, "eula.txt")) AndAlso
                                     FileUtils.ReadAsString(System.IO.Path.Combine(SubDir, "eula.txt")).Contains("eula=true")

                    LocalVersions.Add(V)
                Catch ex As Exception
                    Logger.Warn(ex, $"扫描服务端目录失败：{SubDir}")
                End Try
            Next
            Logger.Info($"服务端扫描完成：{LocalVersions.Count} 个版本")
        Catch ex As Exception
            Logger.Error(ex, "扫描服务端失败")
        End Try
    End Sub

    Public Sub SaveConfig(V As ServerVersion)
        Try
            If V Is Nothing OrElse String.IsNullOrWhiteSpace(V.Name) Then Return
            DirectoryUtils.Create(V.Dir)
            Dim Json = Newtonsoft.Json.JsonConvert.SerializeObject(V, Newtonsoft.Json.Formatting.Indented)
            FileUtils.Write(V.ConfigPath, Json, Encoding.UTF8)
        Catch ex As Exception
            Logger.Error(ex, "保存服务端配置失败")
        End Try
    End Sub

    ''' <summary>删除一个服务端（连同世界存档）。</summary>
    Public Sub DeleteVersion(V As ServerVersion)
        Try
            If V Is Nothing OrElse Not DirectoryUtils.Exists(V.Dir) Then Return
            Directory.Delete(V.Dir, True)
            LocalVersions.Remove(V)
            Logger.Info($"已删除服务端：{V.Name}")
        Catch ex As Exception
            Logger.Error(ex, "删除服务端失败")
        End Try
    End Sub

    '==== 可下载版本 ====

    ''' <summary>可下载的服务端版本（来自 Mojang 版本清单）。</summary>
    Public Class RemoteVersion
        Public Property Id As String = ""
        Public Property Type As String = "release"
        Public Property Url As String = ""
        Public Property Size As Long = 0
        Public Property Sha1 As String = ""
        Public Property ReleaseTime As String = ""
    End Class

    Public ReadOnly RemoteVersions As New List(Of RemoteVersion)

    ''' <summary>
    ''' 拉取可下载的服务端版本列表。
    ''' <para>
    ''' 只保留 <c>downloads.server</c> 存在的版本——早期版本（如 1.2.5 之前）
    ''' 没有独立服务端，强行列出会让玩家下载失败。
    ''' </para>
    ''' </summary>
    Public Function FetchRemote(Optional IncludeSnapshot As Boolean = False) As Boolean
        Try
            RemoteVersions.Clear()
            Dim Manifest As String = NetGetString("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json")
            If String.IsNullOrWhiteSpace(Manifest) Then Return False
            Dim Root As JObject = Newtonsoft.Json.JsonConvert.DeserializeObject(Of JObject)(Manifest)
            If Root Is Nothing Then Return False

            Dim All As JArray = Root("versions")
            If All Is Nothing Then Return False

            Dim Count As Integer = 0
            For Each Item In All
                Dim T = Item("type")?.ToString()
                If T <> "release" AndAlso Not (IncludeSnapshot AndAlso T = "snapshot") Then Continue For
                '最多取 60 个，够用且不至于每次都拉几百次
                If Count >= 60 Then Exit For

                Dim Url = Item("url")?.ToString()
                If String.IsNullOrWhiteSpace(Url) Then Continue For
                Try
                    Dim Detail As String = NetGetString(Url)
                    If String.IsNullOrWhiteSpace(Detail) Then Continue For
                    Dim DJ As JObject = Newtonsoft.Json.JsonConvert.DeserializeObject(Of JObject)(Detail)
                    Dim Srv As JObject = DJ("downloads")?.Item("server")
                    If Srv Is Nothing Then Continue For

                    Dim Size = If(Srv("size")?.ToObject(Of Long)(), 0L)
                    If Size <= 0 Then Continue For

                    RemoteVersions.Add(New RemoteVersion With {
                        .Id = Item("id")?.ToString(),
                        .Type = T,
                        .Url = Srv("url")?.ToString(),
                        .Size = Size,
                        .Sha1 = Srv("sha1")?.ToString(),
                        .ReleaseTime = If(Item("releaseTime")?.ToString(), "").Split("T"c)(0)
                    })
                    Count += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"读取版本详情失败：{Item("id")}")
                End Try
            Next
            Logger.Info($"服务端版本列表：拉到 {RemoteVersions.Count} 个可下载版本")
            Return RemoteVersions.Count > 0
        Catch ex As Exception
            Logger.Error(ex, "拉取服务端版本列表失败")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 下载服务端 jar 到指定版本目录。
    ''' </summary>
    Public Function DownloadServer(JarUrl As String, VersionName As String, ExpectedSize As Long,
                                   Optional Loader As LoaderBase = Nothing) As Boolean
        Try
            If String.IsNullOrWhiteSpace(VersionName) Then Return False
            Dim Dir = System.IO.Path.Combine(ServerDir, VersionName)
            DirectoryUtils.Create(Dir)
            Dim Target = System.IO.Path.Combine(Dir, "server.jar")
            Dim Temp = Target & ".download"

            Logger.Info($"下载服务端 {VersionName}：{JarUrl}")
            Dim Data As Byte() = NetGetBytes(JarUrl, Loader)
            If Data Is Nothing OrElse Data.Length < 1024 * 1024 Then
                Hint("服务端下载失败或文件过小", HintType.Red)
                Return False
            End If

            '校验：jar 是 zip，头两个字节应为 PK
            If Data.Length < 2 OrElse Data(0) <> &H50 OrElse Data(1) <> &H4B Then
                Hint("下载到的不是有效的服务端 jar", HintType.Red)
                Return False
            End If

            IO.File.WriteAllBytes(Temp, Data)
            If FileUtils.Exists(Target) Then FileUtils.Delete(Target, True)
            IO.File.Copy(Temp, Target, True)
            FileUtils.Delete(Temp, True)
            Logger.Info($"服务端已保存：{Target}（{StringUtils.FormatByteSize(Data.Length)}）")

            '写一份配置
            Dim V As New ServerVersion With {.Name = VersionName, .Installed = True, .JarSize = Data.Length}
            SaveConfig(V)
            Return True
        Catch ex As Exception
            Logger.Error(ex, "下载服务端失败")
            Hint($"下载失败：{ex.Message}", HintType.Red)
            Return False
        End Try
    End Function

    '==== EULA 与启动 ====

    ''' <summary>写入 eula.txt。必须由用户明确同意后调用。</summary>
    Public Function AcceptEula(V As ServerVersion) As Boolean
        Try
            If V Is Nothing Then Return False
            DirectoryUtils.Create(V.Dir)
            FileUtils.Write(V.EulaPath,
                "# 由 PCL 生成。同意 Minecraft EULA 后才能开服。" & vbCrLf &
                "# https://aka.ms/MinecraftEULA" & vbCrLf &
                "eula=true" & vbCrLf, Encoding.UTF8)
            V.EulaAccepted = True
            SaveConfig(V)
            Logger.Info($"已同意 EULA：{V.Name}")
            Return True
        Catch ex As Exception
            Logger.Error(ex, "写入 EULA 失败")
            Return False
        End Try
    End Function

    ''' <summary>写入 server.properties（只改我们关心的几项）。</summary>
    Public Sub WriteProperties(V As ServerVersion)
        Try
            If V Is Nothing Then Return
            Dim P = System.IO.Path.Combine(V.Dir, "server.properties")
            Dim Lines As New List(Of String)
            Dim Handled As New HashSet(Of String) From {"server-port", "online-mode", "motd"}

            If FileUtils.Exists(P) Then
                For Each Line In FileUtils.ReadAsString(P).Split({vbCrLf, vbLf}, StringSplitOptions.None)
                    Dim T = Line.Trim()
                    If T = "" OrElse T.StartsWith("#") Then
                        Lines.Add(Line) : Continue For
                    End If
                    Dim Eq = T.IndexOf("="c)
                    If Eq > 0 Then
                        Dim K = T.Substring(0, Eq).Trim()
                        Select Case K
                            Case "server-port"
                                Lines.Add($"server-port={V.Port}") : Handled.Remove(K) : Continue For
                            Case "online-mode"
                                Lines.Add($"online-mode={If(V.OnlineMode, "true", "false")}") : Handled.Remove(K) : Continue For
                            Case "motd"
                                Lines.Add($"motd={If(String.IsNullOrWhiteSpace(V.Motd), V.Name, V.Motd)}") : Handled.Remove(K) : Continue For
                        End Select
                    End If
                    Lines.Add(Line)
                Next
            End If

            If Handled.Contains("server-port") Then Lines.Add($"server-port={V.Port}")
            If Handled.Contains("online-mode") Then Lines.Add($"online-mode={If(V.OnlineMode, "true", "false")}")
            If Handled.Contains("motd") Then Lines.Add($"motd={If(String.IsNullOrWhiteSpace(V.Motd), V.Name, V.Motd)}")

            FileUtils.Write(P, String.Join(vbCrLf, Lines) & vbCrLf, Encoding.UTF8)
        Catch ex As Exception
            Logger.Error(ex, "写入 server.properties 失败")
        End Try
    End Sub

    ''' <summary>找可用的 Java（1.17+ 服务器需要 Java 17）。</summary>
    Public Function FindJava() As String
        Try
            Dim Raw As String = Settings.Get(Of String)("LaunchArgumentJavaAll")
            If Not String.IsNullOrWhiteSpace(Raw) Then
                Dim Arr As JArray = JArray.Parse(Raw)
                For Each Item In Arr
                    Dim P As String = If(Item("Path")?.ToString(), "")
                    If Not String.IsNullOrWhiteSpace(P) AndAlso FileUtils.Exists(P) Then Return P
                Next
            End If
            Dim Sel As String = Settings.Get(Of String)("LaunchArgumentJavaSelect")
            If Not String.IsNullOrWhiteSpace(Sel) AndAlso Sel.StartsWith("{", StringComparison.Ordinal) Then
                Dim P As String = JObject.Parse(Sel)("Path")?.ToString()
                If Not String.IsNullOrWhiteSpace(P) AndAlso FileUtils.Exists(P) Then Return P
            End If
        Catch ex As Exception
            Logger.Warn(ex, "从设置读取 Java 列表失败")
        End Try
        Try
            Dim Env = Environment.GetEnvironmentVariable("JAVA_HOME")
            If Not String.IsNullOrWhiteSpace(Env) Then
                Dim P = System.IO.Path.Combine(Env, "bin", "java.exe")
                If FileUtils.Exists(P) Then Return P
            End If
        Catch
        End Try
        Return Nothing
    End Function

    ''' <summary>构造启动参数。内存下限 1024，避免填 0 后起不来。</summary>
    Public Function BuildArguments(V As ServerVersion) As String
        Dim Mem = Math.Max(1024, V.MemoryMB)
        Dim Xms = Math.Min(512, Mem)
        Return $"-Xms{Xms}M -Xmx{Mem}M -jar ""server.jar"" nogui"
    End Function

    '==== 运行状态 ====

    Public Property Running As Process = Nothing
    Public Property RunningVersion As ServerVersion = Nothing

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Running IsNot Nothing AndAlso Not Running.HasExited
        End Get
    End Property

    ''' <summary>启动服务端。</summary>
    Public Function StartServer(V As ServerVersion) As Boolean
        Try
            If IsRunning Then
                Hint("已有服务端在运行，请先停止", HintType.Red)
                Return False
            End If
            If V Is Nothing Then Return False
            If Not V.EulaAccepted Then
                Hint("需要先同意 Minecraft EULA 才能开服", HintType.Red)
                Return False
            End If
            If Not FileUtils.Exists(V.JarPath) Then
                Hint("找不到 server.jar，请先下载服务端", HintType.Red)
                Return False
            End If

            Dim Java = FindJava()
            If Java Is Nothing Then
                Hint("没有可用的 Java，请先在设置里安装 Java", HintType.Red)
                Return False
            End If

            WriteProperties(V)

            Dim Psi As New ProcessStartInfo With {
                .FileName = Java,
                .Arguments = BuildArguments(V),
                .WorkingDirectory = V.Dir,
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardInput = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }
            Dim Proc As New Process With {.StartInfo = Psi, .EnableRaisingEvents = True}
            AddHandler Proc.OutputDataReceived, Sub(sender, e)
                                                    If e.Data IsNot Nothing Then Logger.Info($"[服务端 {V.Name}] {e.Data}")
                                                End Sub
            AddHandler Proc.ErrorDataReceived, Sub(sender, e)
                                                   If e.Data IsNot Nothing Then Logger.Warn($"[服务端 {V.Name}] {e.Data}")
                                               End Sub
            Proc.Start()
            Proc.BeginOutputReadLine()
            Proc.BeginErrorReadLine()

            Running = Proc
            RunningVersion = V
            Logger.Info($"服务端已启动：{V.Name}，内存 {V.MemoryMB}MB，端口 {V.Port}")
            Hint($"服务端 {V.Name} 已启动", HintType.Green)
            Return True
        Catch ex As Exception
            Logger.Error(ex, "启动服务端失败")
            Hint($"启动失败：{ex.Message}", HintType.Red)
            Return False
        End Try
    End Function

    ''' <summary>优雅停止：先发 stop，15 秒超时再强杀。</summary>
    Public Sub StopServer()
        Try
            If Not IsRunning Then Return
            Dim Proc = Running
            Try
                Proc.StandardInput.WriteLine("stop")
                Proc.StandardInput.Flush()
                Logger.Info("已向服务端发送 stop 命令")
            Catch
            End Try
            If Not Proc.WaitForExit(15000) Then
                Logger.Warn("服务端未在 15 秒内退出，强制结束")
                Proc.Kill()
            End If
            Running = Nothing
            RunningVersion = Nothing
            Hint("服务端已停止", HintType.Blue)
        Catch ex As Exception
            Logger.Error(ex, "停止服务端失败")
        End Try
    End Sub

    '==== HTTP ====

    Private Function NetGetString(Url As String) As String
        Try
            'Mojang 接口只支持 TLS 1.2+，.NET Framework 4.8 默认可能不带
            Net.ServicePointManager.SecurityProtocol = Net.ServicePointManager.SecurityProtocol Or
                                                       Net.SecurityProtocolType.Tls12 Or Net.SecurityProtocolType.Tls11
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 20000
            Req.ReadWriteTimeout = 20000
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"请求失败：{Url}")
            Return Nothing
        End Try
    End Function

    Private Function NetGetBytes(Url As String, Optional Loader As LoaderBase = Nothing) As Byte()
        Try
            'Mojang 接口只支持 TLS 1.2+，.NET Framework 4.8 默认可能不带
            Net.ServicePointManager.SecurityProtocol = Net.ServicePointManager.SecurityProtocol Or
                                                       Net.SecurityProtocolType.Tls12 Or Net.SecurityProtocolType.Tls11
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 300000
            Req.ReadWriteTimeout = 300000
            Req.UserAgent = "PCL3/" & VersionBaseName
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Dim Total = Resp.ContentLength
                Using Src = Resp.GetResponseStream()
                    Using Ms As New IO.MemoryStream()
                        Dim Buf(81919) As Byte
                        Dim Read As Integer
                        Do
                            Read = Src.Read(Buf, 0, Buf.Length)
                            If Read <= 0 Then Exit Do
                            Ms.Write(Buf, 0, Read)
                            If Loader IsNot Nothing AndAlso Total > 0 Then
                                Loader.Progress = CDbl(Ms.Length) / Total
                            End If
                        Loop
                        Return Ms.ToArray()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"下载失败：{Url}")
            Return Nothing
        End Try
    End Function

End Module
