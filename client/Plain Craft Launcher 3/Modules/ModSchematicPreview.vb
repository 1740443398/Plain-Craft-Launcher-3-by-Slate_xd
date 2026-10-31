'投影预览集成
'CRL 的投影预览由 PCLCS.SchematicPreview 提供，该类型未包含在本仓库的 PCLCS.dll 中，
'因此这里改为调用外部「投影预览」程序（Standalone Litematic Renderer）。
'该程序支持通过命令行传入投影文件，实测可正确加载 .litematic 并渲染。
Public Module ModSchematicPreview

    '支持的投影文件扩展名
    Public ReadOnly SchematicExtensions As String() = {"litematic", "nbt", "schem"}

    '投影预览的下载页地址（网盘分享页，非直链）
    '网盘直链有时效与防盗链限制，硬编码进程序很快就会失效，
    '因此这里打开分享页让用户手动下载，再由程序引导放置。
    Public Const SchematicViewerShareUrl As String = "https://share.feijipan.com/s/xyyH6IkI?code=LMNB"

    '若配置了直链，则优先直接下载（留空表示走分享页）
    Public Const SchematicViewerUrlDefault As String = ""

    '缓存已找到的程序路径
    Private CachedViewerPath As String = ""
    Private CachedViewerAt As DateTime = DateTime.MinValue
    Private Const CacheMinutes As Integer = 5

    '解压出的顶层目录名（用于定位真正的程序）
    Private Const ViewerFolderName As String = "Litematic-Viewer"

    ''' <summary>
    ''' 判断某个路径是否为受支持的投影文件。
    ''' </summary>
    Public Function IsSchematicFile(Path As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(Path) OrElse Not FileUtils.Exists(Path) Then Return False
            Dim Ext As String = IO.Path.GetExtension(Path).TrimStart("."c).ToLowerInvariant()
            Return SchematicExtensions.Contains(Ext)
        Catch ex As Exception
            Logger.Warn(ex, $"判断投影文件失败：{Path}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 投影预览的预期安装目录。
    ''' </summary>
    Public ReadOnly Property SchematicViewerFolder As String
        Get
            Return IO.Path.Combine(Paths.Base, ViewerFolderName)
        End Get
    End Property

    ''' <summary>
    ''' 下载地址。优先使用设置项，其次使用常量。
    ''' </summary>
    Public ReadOnly Property SchematicViewerUrl As String
        Get
            Dim Custom As String = Settings.Get(Of String)("ToolSchematicViewerUrl")
            If Not String.IsNullOrWhiteSpace(Custom) Then Return Custom.Trim()
            Return SchematicViewerUrlDefault
        End Get
    End Property

    ''' <summary>
    ''' 查找「投影预览」程序。依次检查用户设置、安装目录与常见位置。
    ''' 找不到时返回空字符串。
    ''' </summary>
    Public Function FindSchematicViewer(Optional ForceRefresh As Boolean = False) As String
        Try
            '用户手动指定的优先级最高
            Dim Custom As String = Settings.Get(Of String)("ToolSchematicViewerPath")
            If Not String.IsNullOrWhiteSpace(Custom) AndAlso FileUtils.Exists(Custom) Then Return Custom

            '缓存命中
            If Not ForceRefresh AndAlso CachedViewerPath <> "" AndAlso
               (DateTime.Now - CachedViewerAt).TotalMinutes < CacheMinutes AndAlso
               FileUtils.Exists(CachedViewerPath) Then
                Return CachedViewerPath
            End If

            '优先检查本启动器下的安装目录
            If Directory.Exists(SchematicViewerFolder) Then
                Dim Hit As String = ProbeViewerIn(SchematicViewerFolder, 3)
                If Hit <> "" Then
                    CachedViewerPath = Hit
                    CachedViewerAt = DateTime.Now
                    Logger.Info($"已找到投影预览程序：{Hit}")
                    Return Hit
                End If
            End If

            '再检查程序目录、其父目录、桌面、下载目录
            Dim Bases As New List(Of String) From {
                Paths.Base,
                IO.Path.GetDirectoryName(Paths.Base.TrimEnd("\"c)),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) & "\Downloads"
            }
            For Each BaseDir In Bases
                If String.IsNullOrWhiteSpace(BaseDir) OrElse Not Directory.Exists(BaseDir) Then Continue For
                Dim Hit As String = ProbeViewerIn(BaseDir, 2)
                If Hit <> "" Then
                    CachedViewerPath = Hit
                    CachedViewerAt = DateTime.Now
                    Logger.Info($"已找到投影预览程序：{Hit}")
                    Return Hit
                End If
            Next
            Return ""
        Catch ex As Exception
            Logger.Warn(ex, "查找投影预览程序失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 在指定目录下按限定深度查找投影预览可执行文件。
    ''' </summary>
    Private Function ProbeViewerIn(BaseDir As String, Depth As Integer) As String
        Try
            For Each Name In {"投影预览.exe", "LitematicViewer.exe", "Litematic-Viewer.exe"}
                Dim Full As String = IO.Path.Combine(BaseDir, Name)
                If FileUtils.Exists(Full) Then Return Full
            Next
            If Depth <= 0 Then Return ""
            For Each SubDir In Directory.GetDirectories(BaseDir)
                Try
                    Dim Leaf As String = IO.Path.GetFileName(SubDir).ToLowerInvariant()
                    If Leaf = "node_modules" OrElse Leaf = ".git" OrElse Leaf = "windows" OrElse Leaf = "runtime" Then Continue For
                    Dim Hit As String = ProbeViewerIn(SubDir, Depth - 1)
                    If Hit <> "" Then Return Hit
                Catch
                    '单个目录无权限就跳过
                End Try
            Next
            Return ""
        Catch ex As Exception
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 判断投影预览是否已安装可用。
    ''' </summary>
    Public Function IsSchematicViewerInstalled() As Boolean
        Return FindSchematicViewer(ForceRefresh:=True) <> ""
    End Function

    ''' <summary>
    ''' 用投影预览打开指定文件。找不到程序时会引导用户下载或指定。
    ''' </summary>
    Public Sub OpenSchematicPreview(Path As String)
        Try
            If Not IsSchematicFile(Path) Then
                Hint("这不是受支持的投影文件（.litematic / .nbt / .schem）", HintType.Red)
                Return
            End If

            Dim Viewer As String = FindSchematicViewer()
            If Viewer = "" Then
                If MyMsgBox("尚未安装「投影预览」。" & vbCrLf & vbCrLf &
                            "投影预览是一个独立的 Minecraft 投影查看工具，" & vbCrLf &
                            "可以在主页或设置中一键下载并自动安装。" & vbCrLf & vbCrLf &
                            "是否现在前往下载？",
                            "未找到投影预览", "去下载", "取消") = 1 Then
                    DownloadSchematicViewer()
                End If
                Return
            End If

            Logger.Info($"用投影预览打开：{Path}")
            StartProcess(New ProcessStartInfo With {
                .FileName = Viewer,
                .Arguments = $"""{Path}""",
                .WorkingDirectory = IO.Path.GetDirectoryName(Viewer),
                .UseShellExecute = False
            })
            Hint("已用投影预览打开", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, $"打开投影预览失败：{Path}")
            Hint($"打开投影预览失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 让用户手动选择投影预览程序的位置，并记住选择。
    ''' </summary>
    Public Sub SelectSchematicViewer()
        Try
            Dim Dialog As New Microsoft.Win32.OpenFileDialog With {
                .Title = "选择投影预览程序",
                .Filter = "可执行文件 (*.exe)|*.exe",
                .CheckFileExists = True
            }
            If Dialog.ShowDialog() = True Then
                Settings.Set("ToolSchematicViewerPath", Dialog.FileName)
                CachedViewerPath = Dialog.FileName
                CachedViewerAt = DateTime.Now
                Hint("已记住投影预览程序位置", HintType.Green)
            End If
        Catch ex As Exception
            Logger.Error(ex, "选择投影预览程序失败")
            Hint($"选择失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 下载并解压投影预览到启动器目录下。
    ''' </summary>
    ''' <param name="OnProgress">进度回调，参数为 0-1 的比例。</param>
    ''' <param name="OnState">状态回调，参数为当前阶段的描述文字。</param>
    Public Sub DownloadSchematicViewer(Optional OnProgress As Action(Of Double) = Nothing, Optional OnState As Action(Of String) = Nothing)
        Dim TempZip As String = PathTemp & "SchematicViewer.zip"
        Try
            Dim Url As String = SchematicViewerUrl
            If String.IsNullOrWhiteSpace(Url) Then
                '没有配置直链时，打开网盘分享页并给出操作指引
                OnState?.Invoke("请在浏览器中下载")
                OpenWebsite(SchematicViewerShareUrl)
                MyMsgBox("投影预览需要从网盘手动下载。" & vbCrLf & vbCrLf &
                         "已为你打开下载页面，请：" & vbCrLf &
                         "1. 下载 Litematic-Viewer-1.2.0-win-x64.zip" & vbCrLf &
                         $"2. 解压后把整个文件夹放到：{Paths.Base}" & vbCrLf &
                         "3. 回到本启动器，点击「打开一个投影」即可" & vbCrLf & vbCrLf &
                         "如果已经解压到别处，也可以在设置中用「指定程序位置」告诉启动器它在哪。",
                         "下载投影预览", "我知道了")
                OnState?.Invoke("已打开下载页")
                Return
            End If

            OnState?.Invoke("正在下载投影预览……")
            Logger.Info($"开始下载投影预览：{Url}")
            Directory.CreateDirectory(IO.Path.GetDirectoryName(TempZip))
            NetDownloadByClient(Url, TempZip)
            OnProgress?.Invoke(0.5)
            If Not FileUtils.Exists(TempZip) Then Throw New Exception("下载后未找到文件")

            OnState?.Invoke("正在解压……")
            Dim ExtractDir As String = SchematicViewerFolder & "_temp"
            DirectoryUtils.Delete(ExtractDir)
            FileUtils.ExtractToDirectory(TempZip, ExtractDir, CancellationToken.None, Nothing)
            OnProgress?.Invoke(0.9)

            '把解压出的内容并入正式目录
            OnState?.Invoke("正在安装……")
            DirectoryUtils.Delete(SchematicViewerFolder)
            DirectoryUtils.Create(SchematicViewerFolder)
            Dim InnerDirs As String() = Directory.GetDirectories(ExtractDir)
            If InnerDirs.Length = 1 AndAlso Directory.GetFiles(ExtractDir).Length = 0 Then
                '压缩包内只有一层目录，直接搬内容
                DirectoryUtils.Copy(InnerDirs(0), SchematicViewerFolder)
            Else
                DirectoryUtils.Copy(ExtractDir, SchematicViewerFolder)
            End If
            DirectoryUtils.Delete(ExtractDir)
            FileUtils.Delete(TempZip)

            '清掉旧的路径记忆，让它重新探测
            Settings.Set("ToolSchematicViewerPath", "")
            CachedViewerPath = ""
            CachedViewerAt = DateTime.MinValue

            OnProgress?.Invoke(1)
            OnState?.Invoke("安装完成")
            Dim Viewer As String = FindSchematicViewer(ForceRefresh:=True)
            If Viewer = "" Then
                MyMsgBox("投影预览已下载并解压，但没有找到可执行文件。" & vbCrLf &
                         $"请检查目录：{SchematicViewerFolder}", "安装异常", "我知道了", IsWarn:=True)
            Else
                Logger.Info($"投影预览安装完成：{Viewer}")
                Hint("投影预览已安装完成！", HintType.Green)
            End If
        Catch ex As Exception
            Logger.Error(ex, "下载投影预览失败")
            OnState?.Invoke("下载失败")
            FileUtils.Delete(TempZip)
            Hint($"下载投影预览失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

End Module
