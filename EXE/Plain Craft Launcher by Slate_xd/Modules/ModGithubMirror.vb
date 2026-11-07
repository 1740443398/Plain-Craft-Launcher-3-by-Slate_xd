'GitHub 下载加速
'把 github.com / raw.githubusercontent.com 的链接改写成公共加速镜像，
'并在下载时按「原链接 + 多个镜像」的顺序自动重试。
Public Module ModGithubMirror

    ''' <summary>
    ''' 加速镜像模板。{0} 会被替换为原始完整 URL。
    ''' 顺序按实测响应速度排列（2026-10 实测）：
    '''   gh-proxy.com 约 1.8s、ghproxy.cn 约 1.9s、gitproxy.click 约 2.8s
    ''' 其余常见镜像（ghfast.top / ghproxy.net / mirror.ghproxy.com 等）实测超时，已剔除。
    ''' </summary>
    Public ReadOnly GithubMirrors As String() = {
        "https://gh-proxy.com/{0}",
        "https://ghproxy.cn/{0}",
        "https://gitproxy.click/{0}"
    }

    ''' <summary>
    ''' 是否启用 GitHub 加速（可在设置中关闭）。
    ''' </summary>
    Public ReadOnly Property EnableMirror As Boolean
        Get
            Return Settings.Get(Of Boolean)("ToolGithubMirror")
        End Get
    End Property

    ''' <summary>
    ''' 判断链接是否为 GitHub 资源。
    ''' </summary>
    Public Function IsGithubUrl(Url As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(Url) Then Return False
            Dim Host As String = New Uri(Url).Host.ToLowerInvariant()
            Return Host = "github.com" OrElse Host = "raw.githubusercontent.com" OrElse
                   Host = "objects.githubusercontent.com" OrElse Host = "codeload.github.com" OrElse
                   Host.EndsWithF(".githubusercontent.com")
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 为 GitHub 链接生成候选下载地址列表（原始地址在最后作为兜底）。
    ''' 非 GitHub 链接原样返回。
    ''' </summary>
    Public Function GetDownloadUrls(Url As String, Optional EnableMirror As Boolean = True) As String()
        If Not EnableMirror OrElse Not IsGithubUrl(Url) Then Return {Url}
        Dim Result As New List(Of String)
        '镜像优先：国内直连 github.com 往往很慢或超时
        For Each Template In GithubMirrors
            Result.Add(String.Format(Template, Url))
        Next
        '原始地址作为最后兜底
        Result.Add(Url)
        Return Result.ToArray()
    End Function

    ''' <summary>
    ''' 探测各镜像的可用性，返回按响应速度排序的镜像模板。
    ''' 结果缓存 10 分钟。
    ''' </summary>
    Private CachedMirrors As String() = Nothing
    Private CachedMirrorsAt As DateTime = DateTime.MinValue

    Public Function GetFastestMirrors(Optional ForceRefresh As Boolean = False) As String()
        If Not ForceRefresh AndAlso CachedMirrors IsNot Nothing AndAlso
           (DateTime.Now - CachedMirrorsAt).TotalMinutes < 10 Then
            Return CachedMirrors
        End If
        Try
            '用一个体积很小的公开文件做探测
            Const ProbeUrl As String = "https://raw.githubusercontent.com/github/gitignore/main/README.md"
            Dim Scored As New List(Of (Template As String, Ms As Long))
            For Each Template In GithubMirrors
                Try
                    Dim Sw = Diagnostics.Stopwatch.StartNew()
                    NetRequestByClient(String.Format(Template, ProbeUrl), HttpMethod.Get, Timeout:=6000)
                    Sw.Stop()
                    Scored.Add((Template, Sw.ElapsedMilliseconds))
                Catch ex As Exception
                    Logger.Warn(ex, $"GitHub 镜像不可用：{Template}")
                End Try
            Next
            If Scored.Count = 0 Then
                '全都不通就退回默认顺序
                CachedMirrors = GithubMirrors
            Else
                CachedMirrors = Scored.OrderBy(Function(x) x.Ms).Select(Function(x) x.Template).ToArray()
                Logger.Info($"GitHub 加速镜像测速结果：{String.Join(" > ", CachedMirrors)}")
            End If
            CachedMirrorsAt = DateTime.Now
            Return CachedMirrors
        Catch ex As Exception
            Logger.Warn(ex, "GitHub 镜像测速失败，使用默认顺序")
            Return GithubMirrors
        End Try
    End Function

End Module
