Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

''' <summary>
''' 插件广场：从 GitHub 发现并安装社区插件。
''' <para>
''' <b>为什么不建自己的服务器</b>：插件本质是代码，最可信的来源是代码托管平台本身——
''' 谁写的、什么时候改的、有多少人 star，这些都是公开可查的。
''' 自建服务器反而要求用户信任我们，而且还要维护。
''' </para>
''' <para>
''' <b>发现机制</b>：作者在 GitHub 仓库上打一个约定的 topic（<see cref="Topic"/>），
''' 就会被广场收录。发布流程就是 <c>git push</c> + 加 topic，不需要提交表单、不需要审核队列。
''' </para>
''' <para>
''' <b>安装前必须审计</b>：从广场装的东西默认不可信。下载后先跑
''' <see cref="ModPluginAudit"/>，危险的直接拒绝，并让用户看到依据。
''' </para>
''' </summary>
Public Module ModPluginStore

    ''' <summary>收录约定：GitHub 仓库需要打的 topic。</summary>
    Public Const Topic As String = "pcl-plugin"
    ''' <summary>兼容的第二个 topic（更容易被搜到）。</summary>
    Public Const TopicAlt As String = "pcl3-plugin"

    ''' <summary>广场里的一个插件。</summary>
    Public Class StoreEntry
        Public Property RepoFullName As String = ""
        Public Property Name As String = ""
        Public Property Description As String = ""
        Public Property Author As String = ""
        Public Property Homepage As String = ""
        Public Property Stars As Integer = 0
        Public Property UpdatedAt As String = ""
        Public Property DefaultBranch As String = ""
        ''' <summary>清单里声明的最低版本要求（若仓库有 plugin.json）。</summary>
        Public Property PluginId As String = ""
        Public Property PluginVersion As String = ""

        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace(PluginId), Name, PluginId)
            End Get
        End Property
    End Class

    ''' <summary>最近一次拉取到的广场条目。</summary>
    Public ReadOnly Entries As New List(Of StoreEntry)
    Public Property LastFetchOK As Boolean = False
    Public Property LastError As String = ""

    ''' <summary>
    ''' 从 GitHub 搜索打了约定 topic 的仓库。
    ''' </summary>
    Public Function Fetch() As Boolean
        Try
            Entries.Clear()
            Dim Query = $"https://api.github.com/search/repositories?q=topic:{Topic}&sort=stars&order=desc&per_page=50"
            Dim Raw = HttpGet(Query)
            If String.IsNullOrWhiteSpace(Raw) Then Return Fail("GitHub 没有响应，可能是网络或频率限制")

            Dim Root As JToken = JToken.Parse(Raw)
            If Root("message") IsNot Nothing Then
                Return Fail($"GitHub 返回：{Root("message")}")
            End If

            Dim Items As JArray = TryCast(Root("items"), JArray)
            If Items Is Nothing Then Return Fail("返回内容格式不对")

            For Each It In Items
                Try
                    Dim Full = It("full_name")?.ToString()
                    If String.IsNullOrWhiteSpace(Full) Then Continue For
                    Entries.Add(New StoreEntry With {
                        .RepoFullName = Full,
                        .Name = If(It("name")?.ToString(), ""),
                        .Description = If(It("description")?.ToString(), ""),
                        .Author = If(It("owner")?("login")?.ToString(), ""),
                        .Homepage = If(It("html_url")?.ToString(), ""),
                        .Stars = If(It("stargazers_count")?.ToObject(Of Integer)(), 0),
                        .UpdatedAt = If(It("updated_at")?.ToString(), "").Split("T"c)(0),
                        .DefaultBranch = If(It("default_branch")?.ToString(), "main")
                    })
                Catch ex As Exception
                    Logger.Warn(ex, "解析广场条目失败")
                End Try
            Next

            LastFetchOK = True
            LastError = ""
            Logger.Info($"插件广场：找到 {Entries.Count} 个插件")
            Return Entries.Count > 0
        Catch ex As Exception
            Logger.Warn(ex, "拉取插件广场失败")
            Return Fail(ex.Message)
        End Try
    End Function

    Private Function Fail(Msg As String) As Boolean
        LastFetchOK = False
        LastError = If(Msg, "未知错误")
        Logger.Warn($"插件广场：{LastError}")
        Return False
    End Function

    ''' <summary>
    ''' 下载一个仓库的 zip 并解压到插件目录。
    ''' </summary>
    ''' <returns>安装后的插件目录；失败返回 Nothing。</returns>
    Public Function Install(Entry As StoreEntry, Optional Loader As LoaderBase = Nothing) As String
        Try
            If Entry Is Nothing OrElse String.IsNullOrWhiteSpace(Entry.RepoFullName) Then Return Nothing

            '目录名用仓库名，冲突时加后缀
            Dim BaseName = Entry.Name
            For Each C In System.IO.Path.GetInvalidFileNameChars()
                BaseName = BaseName.Replace(C, "_"c)
            Next
            Dim TargetDir = System.IO.Path.Combine(ModPlugin.PluginDir, BaseName)
            If DirectoryUtils.Exists(TargetDir) Then TargetDir = TargetDir & "-" & Date.Now.ToString("HHmmss")

            'GitHub 的 zip 下载地址
            Dim Url = $"https://github.com/{Entry.RepoFullName}/archive/refs/heads/{Entry.DefaultBranch}.zip"
            Logger.Info($"下载插件：{Url}")
            Dim Data = HttpGetBytes(Url, Loader)
            If Data Is Nothing OrElse Data.Length < 1024 Then
                Hint("插件下载失败或文件过小", HintType.Red)
                Return Nothing
            End If
            If Data.Length < 2 OrElse Data(0) <> &H50 OrElse Data(1) <> &H4B Then
                Hint("下载到的不是有效的 zip", HintType.Red)
                Return Nothing
            End If

            '解压到临时目录
            Dim TempDir = TargetDir & ".tmp"
            If DirectoryUtils.Exists(TempDir) Then System.IO.Directory.Delete(TempDir, True)
            DirectoryUtils.Create(TempDir)
            Try
                '先落地成临时 zip（ZipFile 需要真实路径），用完立刻删，不留在 Temp 里
                Dim TempZip As String = ToTempZip(Data)
                Try
                    System.IO.Compression.ZipFile.ExtractToDirectory(TempZip, TempDir)
                Catch ex As Exception
                    Logger.Error(ex, "解压插件失败")
                    Hint($"解压失败：{ex.Message}", HintType.Red)
                    Try
                        System.IO.Directory.Delete(TempDir, True)
                    Catch
                    End Try
                    Return Nothing
                Finally
                    '临时 zip 无论成败都要删，否则每装一次就留一份在 Temp
                    Try
                        If FileUtils.Exists(TempZip) Then FileUtils.Delete(TempZip, True)
                    Catch
                    End Try
                End Try
            Catch ex As Exception
                Logger.Error(ex, "解压插件失败")
                Hint($"解压失败：{ex.Message}", HintType.Red)
                Try
                    System.IO.Directory.Delete(TempDir, True)
                Catch
                End Try
                Return Nothing
            End Try

            'GitHub 的 zip 里有一层 仓库名-分支名 的目录，要剥掉
            Dim Inner = TempDir
            Dim Subs = System.IO.Directory.GetDirectories(TempDir)
            If Subs.Length = 1 AndAlso System.IO.Directory.GetFiles(TempDir).Length = 0 Then
                Inner = Subs(0)
            End If

            '检查清单是否存在
            If Not FileUtils.Exists(System.IO.Path.Combine(Inner, "plugin.json")) Then
                Hint("这个仓库里没有 plugin.json，不是有效的插件", HintType.Red)
                Try
                    System.IO.Directory.Delete(TempDir, True)
                Catch
                End Try
                Return Nothing
            End If

            '移动到位
            Try
                If System.IO.Directory.GetParent(TargetDir) IsNot Nothing Then
                    DirectoryUtils.Create(System.IO.Path.GetDirectoryName(TargetDir))
                End If
                System.IO.Directory.Move(Inner, TargetDir)
            Catch ex As Exception
                Logger.Error(ex, "放置插件目录失败")
                Hint($"安装失败：{ex.Message}", HintType.Red)
                Return Nothing
            Finally
                Try
                    If DirectoryUtils.Exists(TempDir) Then System.IO.Directory.Delete(TempDir, True)
                Catch
                End Try
            End Try

            '装完立刻审计
            ModPlugin.Scan()
            Dim Installed = ModPlugin.Plugins.FirstOrDefault(Function(p) p.Directory = TargetDir)
            If Installed IsNot Nothing Then
                Dim Findings = ModPluginAudit.Audit(Installed)
                If ModPluginAudit.HighestRisk(Findings) = ModPluginAudit.RiskLevel.Dangerous Then
                    '危险就删掉，不留在磁盘上
                    Try
                        System.IO.Directory.Delete(TargetDir, True)
                    Catch
                    End Try
                    ModPlugin.Scan()
                    Hint("插件含危险内容，已拒绝安装并删除", HintType.Red)
                    Logger.Warn($"拒绝安装插件 {Entry.RepoFullName}：{ModPluginAudit.Summarize(Findings)}")
                    Return Nothing
                End If
            End If

            Logger.Info($"插件已安装到 {TargetDir}")
            Hint($"插件已安装：{Entry.Name}", HintType.Green)
            Return TargetDir
        Catch ex As Exception
            Logger.Error(ex, "安装插件失败")
            Hint($"安装失败：{ex.Message}", HintType.Red)
            Return Nothing
        End Try
    End Function

    ''' <summary>把内存里的 zip 写成临时文件（ZipFile 需要路径）。</summary>
    Private Function ToTempZip(Data As Byte()) As String
        Dim P = System.IO.Path.Combine(PathTemp, "plugin-" & Guid.NewGuid().ToString("N") & ".zip")
        DirectoryUtils.Create(PathTemp)
        IO.File.WriteAllBytes(P, Data)
        Return P
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

    Private Function HttpGetBytes(Url As String, Optional Loader As LoaderBase = Nothing) As Byte()
        Try
            Dim Req = Prepare(Url)
            Req.Timeout = 120000
            Req.ReadWriteTimeout = 120000
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
                            If Loader IsNot Nothing AndAlso Total > 0 Then Loader.Progress = CDbl(Ms.Length) / Total
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
        Req.Accept = "application/vnd.github+json"
        Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
        Return Req
    End Function

End Module
