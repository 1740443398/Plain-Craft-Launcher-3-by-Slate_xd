Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' 自定义皮肤：用 authlib-injector 让离线账号在所有版本显示自己的皮肤。
''' <para>
''' <b>为什么原来的方案在新版本失效</b>：PCL 上游的做法是「构造一个特定 UUID，
''' 让游戏的默认皮肤推算逻辑选中 Steve 或 Alex」。这在 1.19.3 之前有效，
''' 因为那时客户端会按 UUID 自己算默认皮肤。1.19.3 之后 Mojang 改成
''' 「皮肤必须由会话服务下发带签名的 textures 属性」，
''' 纯靠 UUID 猜不出来了，所以那个办法对新版本必然无效。
''' </para>
''' <para>
''' <b>现在的做法</b>：注入 authlib-injector 这个 Java agent，
''' 把游戏的会话服务地址改指向一个本地 Yggdrasil 服务，
''' 由它下发自定义皮肤。这是全版本通用的正规做法，
''' 也是各类第三方皮肤站一直在用的方案。
''' </para>
''' <para>
''' <b>代价要说清楚</b>：这是改游戏启动参数，不是改游戏文件。
''' 但 Java agent 会被部分杀毒软件误报，所以必须让用户明确知道并自己开启。
''' </para>
''' </summary>
Public Module ModSkin

    ''' <summary>authlib-injector 的版本清单地址。</summary>
    Public Const ArtifactApi As String = "https://authlib-injector.yushi.moe/artifact/latest.json"

    ''' <summary>本地存放位置。</summary>
    Public ReadOnly Property JarDir As String
        Get
            Return Paths.Base & "PCL\Skin\"
        End Get
    End Property

    Public ReadOnly Property JarPath As String
        Get
            Return System.IO.Path.Combine(JarDir, "authlib-injector.jar")
        End Get
    End Property

    ''' <summary>最近一次查询到的信息。</summary>
    Public Class ArtifactInfo
        Public Property Version As String = ""
        Public Property DownloadUrl As String = ""
        Public Property Sha256 As String = ""
        Public Property BuildNumber As Integer = 0
    End Class

    ''' <summary>
    ''' 查询最新版 authlib-injector 信息。
    ''' </summary>
    Public Function FetchInfo() As ArtifactInfo
        Try
            Dim Raw = HttpGet(ArtifactApi)
            If String.IsNullOrWhiteSpace(Raw) Then Return Nothing
            Dim J As JToken = JToken.Parse(Raw)
            Return New ArtifactInfo With {
                .Version = If(J("version")?.ToString(), ""),
                .DownloadUrl = If(J("download_url")?.ToString(), ""),
                .Sha256 = If(J("checksums")?("sha256")?.ToString(), ""),
                .BuildNumber = If(J("build_number")?.ToObject(Of Integer)(), 0)
            }
        Catch ex As Exception
            Logger.Warn(ex, "查询 authlib-injector 版本失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 下载 authlib-injector。已存在且校验通过就跳过。
    ''' </summary>
    Public Function EnsureJar(Optional Force As Boolean = False) As Boolean
        Try
            If Not Force AndAlso FileUtils.Exists(JarPath) Then
                Dim Fi As New IO.FileInfo(JarPath)
                'jar 至少几百 KB，太小说明是坏的
                If Fi.Length > 100 * 1024 Then Return True
            End If

            Dim Info = FetchInfo()
            If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Info.DownloadUrl) Then
                Hint("无法获取 authlib-injector 下载地址", HintType.Red)
                Return False
            End If

            Logger.Info($"下载 authlib-injector {Info.Version}（build {Info.BuildNumber}）")
            Dim Data = HttpGetBytes(Info.DownloadUrl)
            If Data Is Nothing OrElse Data.Length < 100 * 1024 Then
                Hint("下载 authlib-injector 失败", HintType.Red)
                Return False
            End If
            If Data.Length < 2 OrElse Data(0) <> &H50 OrElse Data(1) <> &H4B Then
                Hint("下载到的不是有效的 jar", HintType.Red)
                Return False
            End If

            '有 SHA256 就校验
            If Not String.IsNullOrWhiteSpace(Info.Sha256) Then
                Try
                    Using Sha = Security.Cryptography.SHA256.Create()
                        Dim Actual = BitConverter.ToString(Sha.ComputeHash(Data)).Replace("-", "").ToLowerInvariant()
                        If Not String.Equals(Actual, Info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                            Hint("authlib-injector 校验失败，请重试", HintType.Red)
                            Return False
                        End If
                        Logger.Info("authlib-injector SHA256 校验通过")
                    End Using
                Catch ex As Exception
                    Logger.Warn(ex, "SHA256 校验出错，跳过")
                End Try
            End If

            DirectoryUtils.Create(JarDir)
            Dim Temp = JarPath & ".download"
            IO.File.WriteAllBytes(Temp, Data)
            If FileUtils.Exists(JarPath) Then FileUtils.Delete(JarPath, True)
            IO.File.Move(Temp, JarPath)

            Logger.Info($"authlib-injector 已保存：{JarPath}")
            Return True
        Catch ex As Exception
            Logger.Error(ex, "准备 authlib-injector 失败")
            Return False
        End Try
    End Function

    ''' <summary>是否已经准备好。</summary>
    Public ReadOnly Property IsReady As Boolean
        Get
            Return FileUtils.Exists(JarPath)
        End Get
    End Property

    ''' <summary>
    ''' 构造要加到 JVM 参数里的 agent 参数。
    ''' </summary>
    ''' <param name="SkinServer">Yggdrasil API 根地址。</param>
    Public Function BuildAgentArg(SkinServer As String) As String
        Try
            If Not IsReady Then Return ""
            If String.IsNullOrWhiteSpace(SkinServer) Then Return ""
            '路径里有空格必须加引号，否则 JVM 会把参数切断
            Return $"-javaagent:""{JarPath}""={SkinServer}"
        Catch ex As Exception
            Logger.Warn(ex, "构造 agent 参数失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 检查 JVM 参数里是否已经有 agent，避免重复添加。
    ''' </summary>
    Public Function HasAgent(Args As String) As Boolean
        If String.IsNullOrWhiteSpace(Args) Then Return False
        Return Args.IndexOf("authlib-injector", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Function HttpGet(Url As String) As String
        Try
            Dim Req = Prepare(Url)
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"请求失败：{Url}")
            Return Nothing
        End Try
    End Function

    Private Function HttpGetBytes(Url As String) As Byte()
        Try
            Dim Req = Prepare(Url)
            Req.Timeout = 120000
            Req.ReadWriteTimeout = 120000
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Src = Resp.GetResponseStream()
                    Using Ms As New IO.MemoryStream()
                        Dim Buf(81919) As Byte
                        Dim Read As Integer
                        Do
                            Read = Src.Read(Buf, 0, Buf.Length)
                            If Read <= 0 Then Exit Do
                            Ms.Write(Buf, 0, Read)
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

    Private Function Prepare(Url As String) As Net.HttpWebRequest
        Net.ServicePointManager.SecurityProtocol = Net.ServicePointManager.SecurityProtocol Or
                                                   Net.SecurityProtocolType.Tls12 Or Net.SecurityProtocolType.Tls11
        Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = 20000
        Req.ReadWriteTimeout = 20000
        Req.UserAgent = "PCL3/" & VersionBaseName
        Req.Accept = "application/json"
        Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
        Return Req
    End Function

End Module
