'模组加载器镜像加速
'把 Forge / NeoForge / Fabric / Quilt 的 Maven 地址重写到 ZCSM 镜像，
'官方源作为后备。镜像失效时自动回退，不影响原有逻辑。
'
'ZCSM = ZeroCore Smart Mirror（零核智能转储镜像），由等离子工作室维护。
'它按路径容错转发到上游 Maven，缓存命中后响应极快。
Public Module ModMirror

    ''' <summary>镜像根地址。</summary>
    Private Const MirrorRoot As String = "https://mirror.zcswo.cn"

    ''' <summary>上游 Maven → ZCSM 路径前缀的映射。</summary>
    Private ReadOnly HostMap As (Src As String, Dst As String)() = {
        ("maven.neoforged.net/releases", "/neoforge"),
        ("maven.neoforged.net", "/neoforge"),
        ("maven.minecraftforge.net", "/forge"),
        ("files.minecraftforge.net/maven", "/forge"),
        ("maven.fabricmc.net", "/fabric"),
        ("maven.quiltmc.org/repository/release", "/quilt"),
        ("maven.quiltmc.org", "/quilt"),
        ("maven.architectury.dev", "/architectury")
    }

    ''' <summary>是否启用镜像加速（可在设置中关闭）。</summary>
    Public ReadOnly Property Enabled As Boolean
        Get
            Try
                Return Settings.Get(Of Boolean)("ToolModMirror")
            Catch
                Return True
            End Try
        End Get
    End Property

    ''' <summary>
    ''' 把官方 Maven 地址转换为镜像地址。无法识别时返回空字符串。
    ''' </summary>
    Public Function ToMirror(Url As String) As String
        Try
            If Not Enabled Then Return ""
            If String.IsNullOrWhiteSpace(Url) Then Return ""
            If Url.Contains("mirror.zcswo.cn") Then Return Url

            For Each M In HostMap
                Dim Prefix As String = "https://" & M.Src & "/"
                If Url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return MirrorRoot & M.Dst & "/" & Url.Substring(Prefix.Length)
                End If
                '兼容 http
                Dim HttpPrefix As String = "http://" & M.Src & "/"
                If Url.StartsWith(HttpPrefix, StringComparison.OrdinalIgnoreCase) Then
                    Return MirrorRoot & M.Dst & "/" & Url.Substring(HttpPrefix.Length)
                End If
            Next
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, $"镜像地址转换失败：{Url}")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 为单个 URL 生成「镜像优先、官方后备」的地址列表。
    ''' 镜像不可用时 LoaderDownload 会自动切到下一个源。
    ''' </summary>
    Public Function WithMirror(Url As String) As List(Of String)
        Dim List As New List(Of String) From {Url}
        Try
            Dim Mirrored As String = ToMirror(Url)
            If Mirrored <> "" AndAlso Mirrored <> Url Then
                List.Insert(0, Mirrored)
                Logger.Trace($"镜像优先：{Url} → {Mirrored}")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "生成镜像地址列表失败")
        End Try
        Return List
    End Function

    ''' <summary>
    ''' 批量转换一组 URL（去重，保持顺序）。
    ''' </summary>
    Public Function WithMirror(Urls As IEnumerable(Of String)) As List(Of String)
        Dim Result As New List(Of String)
        Try
            For Each U In Urls
                For Each C In WithMirror(U)
                    If Not Result.Contains(C) Then Result.Add(C)
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "批量镜像转换失败")
            Return New List(Of String)(Urls)
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 探测镜像是否可用（后台线程调用，不阻塞界面）。
    ''' </summary>
    Public Function Probe() As Boolean
        Try
            Dim TestUrl As String = MirrorRoot & "/neoforge/net/neoforged/neoforge/maven-metadata.xml"
            Dim Raw As String = NetRequestByClient(TestUrl, HttpMethod.Get, Timeout:=8000)
            Dim Ok As Boolean = Not String.IsNullOrWhiteSpace(Raw)
            Logger.Info($"模组镜像探测：{(If(Ok, "可用", "不可用"))}")
            Return Ok
        Catch ex As Exception
            Logger.Warn(ex, "模组镜像探测失败")
            Return False
        End Try
    End Function

End Module
