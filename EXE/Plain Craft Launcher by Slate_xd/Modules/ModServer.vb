Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.IO
Imports System.Diagnostics
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 一键开服器：在启动器里直接开一个带模组加载器的服务器。
''' <para>
''' <b>设计取向</b>：参考了 SeaLantern（Rust 写的开源开服器）的思路——
''' 不追求「第一次就能开全所有配置」，而是把开服这件事拆成三件必须做的事：
''' 选版本、定内存、确认 EULA。其余全部用安全默认值。
''' </para>
''' <para>
''' <b>为什么不做成「下载任意服务端」</b>：那等于给启动器加了一个任意代码下载执行入口。
''' 这里只允许官方 Forge / NeoForge / Fabric / Quilt / 原版服务端，
''' 地址在代码里写死，不接受用户提供的 URL。
''' </para>
''' </summary>
Public Module ModServer

    ''' <summary>服务器根目录。</summary>
    Public ReadOnly Property ServerDir As String
        Get
            Return Paths.Base & "PCL\Servers\"
        End Get
    End Property

    ''' <summary>一个已配置的服务器。</summary>
    Public Class ServerInstance
        Public Property Name As String = ""
        ''' <summary>对应的游戏版本，如 1.20.1。</summary>
        Public Property GameVersion As String = ""
        ''' <summary>加载器：vanilla / forge / neoforge / fabric / quilt。</summary>
        Public Property Loader As String = "vanilla"
        ''' <summary>加载器版本，vanilla 时为空。</summary>
        Public Property LoaderVersion As String = ""
        ''' <summary>分配内存（MB）。</summary>
        Public Property MemoryMB As Integer = 2048
        ''' <summary>已同意 EULA。</summary>
        Public Property EulaAccepted As Boolean = False
        ''' <summary>端口。</summary>
        Public Property Port As Integer = 25565
        ''' <summary>正版验证。</summary>
        Public Property OnlineMode As Boolean = True

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
    End Class

    '==== 已配置的服务器 ====

    Public ReadOnly Servers As New List(Of ServerInstance)

    ''' <summary>扫描服务器目录。</summary>
    Public Sub Scan()
        Servers.Clear()
        Try
            DirectoryUtils.Create(ServerDir)
            For Each SubDir In Directory.GetDirectories(ServerDir)
                Try
                    Dim Cfg = System.IO.Path.Combine(SubDir, "pcl-server.json")
                    If Not FileUtils.Exists(Cfg) Then Continue For
                    Dim S As ServerInstance = Newtonsoft.Json.JsonConvert.DeserializeObject(Of ServerInstance)(FileUtils.ReadAsString(Cfg))
                    If S Is Nothing Then Continue For
                    If String.IsNullOrWhiteSpace(S.Name) Then S.Name = PathUtils.GetLastPart(SubDir)
                    Servers.Add(S)
                Catch ex As Exception
                    Logger.Warn(ex, $"读取服务器配置失败：{SubDir}")
                End Try
            Next
            Logger.Info($"服务器扫描完成：{Servers.Count} 个")
        Catch ex As Exception
            Logger.Error(ex, "扫描服务器失败")
        End Try
    End Sub

    ''' <summary>保存服务器配置。</summary>
    Public Sub Save(S As ServerInstance)
        Try
            If S Is Nothing OrElse String.IsNullOrWhiteSpace(S.Name) Then Return
            DirectoryUtils.Create(S.Dir)
            Dim Json = Newtonsoft.Json.JsonConvert.SerializeObject(S, Newtonsoft.Json.Formatting.Indented)
            FileUtils.Write(System.IO.Path.Combine(S.Dir, "pcl-server.json"), Json, Encoding.UTF8)
        Catch ex As Exception
            Logger.Error(ex, "保存服务器配置失败")
        End Try
    End Sub

    ''' <summary>删除服务器（连同目录）。</summary>
    Public Sub Delete(S As ServerInstance)
        Try
            If S Is Nothing OrElse Not DirectoryUtils.Exists(S.Dir) Then Return
            Directory.Delete(S.Dir, True)
            Servers.Remove(S)
            Logger.Info($"已删除服务器：{S.Name}")
        Catch ex As Exception
            Logger.Error(ex, "删除服务器失败")
        End Try
    End Sub

    ''' <summary>
    ''' 写入 eula.txt。这一步必须由用户明确同意，不能代劳。
    ''' </summary>
    Public Function AcceptEula(S As ServerInstance) As Boolean
        Try
            If S Is Nothing Then Return False
            DirectoryUtils.Create(S.Dir)
            FileUtils.Write(S.EulaPath,
                "# 由 PCL 生成。同意 Minecraft EULA 后才能开服。" & vbCrLf &
                "# https://aka.ms/MinecraftEULA" & vbCrLf &
                "eula=true" & vbCrLf, Encoding.UTF8)
            S.EulaAccepted = True
            Save(S)
            Logger.Info($"已同意 EULA：{S.Name}")
            Return True
        Catch ex As Exception
            Logger.Error(ex, "写入 EULA 失败")
            Return False
        End Try
    End Function

    ''' <summary>写入 server.properties（只改我们关心的几项，其余保留）。</summary>
    Public Sub WriteProperties(S As ServerInstance)
        Try
            If S Is Nothing Then Return
            Dim P = System.IO.Path.Combine(S.Dir, "server.properties")
            Dim Lines As New List(Of String)
            Dim Handled As New HashSet(Of String) From {"server-port", "online-mode", "motd"}

            If FileUtils.Exists(P) Then
                For Each Line In FileUtils.ReadAsString(P).Split({vbCrLf, vbLf}, StringSplitOptions.None)
                    Dim T = Line.Trim()
                    If T = "" OrElse T.StartsWith("#") Then
                        Lines.Add(Line)
                        Continue For
                    End If
                    Dim Eq = T.IndexOf("="c)
                    If Eq > 0 Then
                        Dim K = T.Substring(0, Eq).Trim()
                        Select Case K
                            Case "server-port"
                                Lines.Add($"server-port={S.Port}")
                                Handled.Remove(K) : Continue For
                            Case "online-mode"
                                Lines.Add($"online-mode={If(S.OnlineMode, "true", "false")}")
                                Handled.Remove(K) : Continue For
                            Case "motd"
                                Lines.Add($"motd={S.Name}")
                                Handled.Remove(K) : Continue For
                        End Select
                    End If
                    Lines.Add(Line)
                Next
            End If

            '补齐缺失项
            If Handled.Contains("server-port") Then Lines.Add($"server-port={S.Port}")
            If Handled.Contains("online-mode") Then Lines.Add($"online-mode={If(S.OnlineMode, "true", "false")}")
            If Handled.Contains("motd") Then Lines.Add($"motd={S.Name}")

            FileUtils.Write(P, String.Join(vbCrLf, Lines) & vbCrLf, Encoding.UTF8)
        Catch ex As Exception
            Logger.Error(ex, "写入 server.properties 失败")
        End Try
    End Sub

    ''' <summary>
    ''' 找可用的 Java。服务器需要 Java 17+（1.17 之后）。
    ''' </summary>
    Public Function FindJava() As String
        Try
            '从设置里读 Java 列表：这是 PCL 自己的持久化位置，
            '不依赖闭源库的内部字段名，升级时不会因为改名而崩
            Dim Raw As String = Settings.Get(Of String)("LaunchArgumentJavaAll")
            If Not String.IsNullOrWhiteSpace(Raw) Then
                Dim Arr As Newtonsoft.Json.Linq.JArray = Newtonsoft.Json.Linq.JArray.Parse(Raw)
                For Each Item In Arr
                    Dim P As String = If(Item("Path")?.ToString(), "")
                    If Not String.IsNullOrWhiteSpace(P) AndAlso FileUtils.Exists(P) Then Return P
                Next
            End If

            '再试强制选择的那个
            Dim Sel As String = Settings.Get(Of String)("LaunchArgumentJavaSelect")
            If Not String.IsNullOrWhiteSpace(Sel) AndAlso Sel.StartsWithF("{") Then
                Dim P As String = Newtonsoft.Json.Linq.JObject.Parse(Sel)("Path")?.ToString()
                If Not String.IsNullOrWhiteSpace(P) AndAlso FileUtils.Exists(P) Then Return P
            End If
        Catch ex As Exception
            Logger.Warn(ex, "从设置读取 Java 列表失败")
        End Try

        '最后回退到 JAVA_HOME
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

    ''' <summary>
    ''' 构造启动参数。内存下限 1024，避免用户填 0 之后服务器起不来。
    ''' </summary>
    Public Function BuildArguments(S As ServerInstance) As String
        Dim Mem = Math.Max(1024, S.MemoryMB)
        Return $"-Xms{Math.Min(512, Mem)}M -Xmx{Mem}M -jar ""server.jar"" nogui"
    End Function

    ''' <summary>启动状态。</summary>
    Public Property Running As Process = Nothing
    Public Property RunningServer As ServerInstance = Nothing

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Running IsNot Nothing AndAlso Not Running.HasExited
        End Get
    End Property

    ''' <summary>
    ''' 启动服务器。
    ''' </summary>
    Public Function StartServer(S As ServerInstance) As Boolean
        Try
            If IsRunning Then
                Hint("已有服务器在运行，请先停止", HintType.Red)
                Return False
            End If
            If S Is Nothing Then Return False

            If Not S.EulaAccepted Then
                Hint("需要先同意 Minecraft EULA 才能开服", HintType.Red)
                Return False
            End If

            If Not FileUtils.Exists(S.JarPath) Then
                Hint("找不到 server.jar，请先下载服务端", HintType.Red)
                Return False
            End If

            Dim Java = FindJava()
            If Java Is Nothing Then
                Hint("没有可用的 Java，请先在设置里安装 Java", HintType.Red)
                Return False
            End If

            WriteProperties(S)

            Dim Psi As New ProcessStartInfo With {
                .FileName = Java,
                .Arguments = BuildArguments(S),
                .WorkingDirectory = S.Dir,
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
                                                    If e.Data IsNot Nothing Then Logger.Info($"[服务器] {e.Data}")
                                                End Sub
            AddHandler Proc.ErrorDataReceived, Sub(sender, e)
                                                   If e.Data IsNot Nothing Then Logger.Warn($"[服务器] {e.Data}")
                                               End Sub
            Proc.Start()
            Proc.BeginOutputReadLine()
            Proc.BeginErrorReadLine()

            Running = Proc
            RunningServer = S
            Logger.Info($"服务器已启动：{S.Name}，内存 {S.MemoryMB}MB，端口 {S.Port}")
            Hint($"服务器 {S.Name} 已启动", HintType.Green)
            Return True
        Catch ex As Exception
            Logger.Error(ex, "启动服务器失败")
            Hint($"启动失败：{ex.Message}", HintType.Red)
            Return False
        End Try
    End Function

    ''' <summary>优雅停止：先发 stop 命令，再等；超时后强杀。</summary>
    Public Sub StopServer()
        Try
            If Not IsRunning Then Return
            Dim Proc = Running
            Try
                Proc.StandardInput.WriteLine("stop")
                Proc.StandardInput.Flush()
                Logger.Info("已向服务器发送 stop 命令")
            Catch
            End Try
            If Not Proc.WaitForExit(15000) Then
                Logger.Warn("服务器未在 15 秒内退出，强制结束")
                Proc.Kill()
            End If
            Running = Nothing
            RunningServer = Nothing
            Hint("服务器已停止", HintType.Blue)
        Catch ex As Exception
            Logger.Error(ex, "停止服务器失败")
        End Try
    End Sub

End Module
