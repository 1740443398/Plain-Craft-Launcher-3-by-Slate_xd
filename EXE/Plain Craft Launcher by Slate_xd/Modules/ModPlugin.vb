Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.IO
Imports Newtonsoft.Json.Linq

''' <summary>
''' 插件系统：让任何人都能给启动器加功能，而不必重新编译整个程序。
''' <para>
''' 设计目标与约束：
''' </para>
''' <list type="bullet">
''' <item><b>零重编译</b>：插件是独立目录下的 <c>.xaml</c> + <c>plugin.json</c>，放进 Plugins 目录即可生效。</item>
''' <item><b>统一规范</b>：所有插件遵循同一套清单格式与生命周期，见 <see cref="PluginManifest"/>。</item>
''' <item><b>安全边界</b>：插件只能声明自己需要的权限，敏感能力（读写玩家数据、执行外部程序、访问账号）
''' 默认关闭，必须显式声明且由用户确认。</item>
''' <item><b>失败隔离</b>：单个插件加载失败不影响其它插件，也不影响启动器本身。</item>
''' </list>
''' <para>
''' <b>为什么用 XAML 而不是 DLL</b>：DLL 插件需要反射加载任意代码，等于给插件完全的系统权限，
''' 一旦有人投毒就是灾难。XAML 只能描述界面与声明式事件，能做的事情有明确边界，
''' 这和我们已有的 <c>Custom.xaml</c> 主页机制一致，玩家也更容易上手。
''' </para>
''' </summary>
Public Module ModPlugin

    ''' <summary>插件根目录。</summary>
    Public ReadOnly Property PluginDir As String
        Get
            Return Paths.Base & "PCL\Plugins\"
        End Get
    End Property

    ''' <summary>
    ''' 插件清单。文件名固定为 <c>plugin.json</c>，与插件入口 <c>main.xaml</c> 同目录。
    ''' </summary>
    Public Class PluginManifest
        ''' <summary>插件唯一标识，建议用反域名格式（如 <c>com.example.myplugin</c>）。必填。</summary>
        Public Property Id As String = ""
        ''' <summary>显示名称。必填。</summary>
        Public Property Name As String = ""
        ''' <summary>版本号，如 <c>1.0.0</c>。必填。</summary>
        Public Property Version As String = ""
        ''' <summary>作者。</summary>
        Public Property Author As String = ""
        ''' <summary>一句话说明这个插件做什么。</summary>
        Public Property Description As String = ""
        ''' <summary>入口 XAML 文件名，默认 <c>main.xaml</c>。</summary>
        Public Property Entry As String = "main.xaml"
        ''' <summary>是否需要网络权限。</summary>
        Public Property NeedNetwork As Boolean = False
        ''' <summary>是否需要读取玩家数据（存档、模组、日志）。</summary>
        Public Property NeedPlayerData As Boolean = False
        ''' <summary>是否需要执行外部程序。</summary>
        Public Property NeedExecute As Boolean = False
        ''' <summary>最低启动器版本码，低于此值不加载。</summary>
        Public Property MinVersionCode As Integer = 0

        ''' <summary>
        ''' 头像文件名（放在插件目录里）。留空则用自动生成的字母头像。
        ''' 支持 png / jpg / bmp / ico，建议 64x64。
        ''' </summary>
        Public Property Icon As String = ""

        ''' <summary>
        ''' 自定义配置页的 XAML 文件名（放在插件目录里）。
        ''' 留空则不显示「设置」按钮。
        ''' </summary>
        Public Property ConfigPage As String = ""

        ''' <summary>
        ''' 插件分类，用于在列表里分组。留空归入「其他」。
        ''' </summary>
        Public Property Category As String = ""

        ''' <summary>
        ''' 主页地址（可选）。列表里提供一个「主页」按钮。
        ''' </summary>
        Public Property Homepage As String = ""

        ''' <summary>
        ''' 是否在标题栏加一个入口。为 True 时插件的 main.xaml 会作为一个独立页面。
        ''' <para>
        ''' 这是插件给启动器加新功能的主要方式：声明 titlebar 后，
        ''' 玩家点标题栏按钮就能进入插件页面，和内置页面一样。
        ''' </para>
        ''' </summary>
        Public Property Titlebar As Boolean = False

        ''' <summary>标题栏按钮上的文字。留空则用 name。</summary>
        Public Property TitlebarText As String = ""

        ''' <summary>
        ''' 标题栏按钮的图标（SVG 路径数据，24x24 视口）。
        ''' 留空则用一个通用方块图标。
        ''' </summary>
        Public Property TitlebarIcon As String = ""

        ''' <summary>
        ''' 运行时类型："js"（默认，XAML+脚本）或 "java"（独立 jar 进程）。
        ''' <para>
        ''' Java 插件能写真正的代码，但只能通过 JSON 消息请求启动器执行操作，
        ''' 拿不到文件系统权限。
        ''' </para>
        ''' </summary>
        Public Property Runtime As String = "js"

        ''' <summary>
        ''' 申请的权限列表。可选值见 <see cref="ModPluginPermission"/>：
        ''' network / fileRead / fileWrite / readSettings / writeSettings /
        ''' readInstances / notify / clipboard / titlebar。
        ''' <para>
        ''' 首次启用时会把这里列的权限展示给用户，同意后才生效。
        ''' 没申请的能力一律拒绝，不会静默放行。
        ''' </para>
        ''' </summary>
        Public Property Permissions As String() = {}

        ''' <summary>清单是否具备加载所需的最少字段。</summary>
        Public ReadOnly Property IsValid As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(Id) AndAlso
                       Not String.IsNullOrWhiteSpace(Name) AndAlso
                       Not String.IsNullOrWhiteSpace(Version)
            End Get
        End Property

        ''' <summary>声明的敏感权限，用于向用户展示。</summary>
        Public ReadOnly Property DeclaredPermissions As List(Of String)
            Get
                Dim List As New List(Of String)
                If NeedNetwork Then List.Add("网络访问")
                If NeedPlayerData Then List.Add("读取玩家数据")
                If NeedExecute Then List.Add("执行外部程序")
                Return List
            End Get
        End Property
    End Class

    ''' <summary>一个已发现的插件。</summary>
    Public Class PluginEntry
        Public Property Manifest As PluginManifest
        ''' <summary>插件所在目录。</summary>
        Public Property Directory As String = ""
        ''' <summary>入口文件的完整路径。</summary>
        Public Property EntryPath As String = ""
        ''' <summary>加载失败时的原因；为空表示可用。</summary>
        Public Property FailureReason As String = ""
        ''' <summary>用户是否已启用。</summary>
        Public Property Enabled As Boolean = True

        ''' <summary>头像文件的绝对路径；未提供时为空。</summary>
        Public Property IconPath As String = ""

        ''' <summary>自定义配置页的绝对路径；未提供时为空。</summary>
        Public Property ConfigPath As String = ""

        ''' <summary>是否是 Java 插件（跑独立进程）。</summary>
        Public ReadOnly Property IsJava As Boolean
            Get
                Return Manifest IsNot Nothing AndAlso
                       String.Equals(Manifest.Runtime, "java", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        ''' <summary>Java 插件的 jar 路径。</summary>
        Public ReadOnly Property JarPath As String
            Get
                If String.IsNullOrWhiteSpace(Directory) Then Return ""
                Return System.IO.Path.Combine(Directory, "plugin.jar")
            End Get
        End Property

        ''' <summary>Java 插件是否具备运行条件。</summary>
        Public ReadOnly Property JavaReady As Boolean
            Get
                Return IsJava AndAlso FileUtils.Exists(JarPath)
            End Get
        End Property

        ''' <summary>是否提供了自定义头像。</summary>
        Public ReadOnly Property HasIcon As Boolean
            Get
                Return Not String.IsNullOrEmpty(IconPath) AndAlso FileUtils.Exists(IconPath)
            End Get
        End Property

        ''' <summary>是否提供了自定义配置页。</summary>
        Public ReadOnly Property HasConfigPage As Boolean
            Get
                Return Not String.IsNullOrEmpty(ConfigPath) AndAlso FileUtils.Exists(ConfigPath)
            End Get
        End Property

        ''' <summary>用于自动生成字母头像的文字（名字首字）。</summary>
        Public ReadOnly Property IconInitial As String
            Get
                Dim N = DisplayName
                If String.IsNullOrWhiteSpace(N) Then Return "?"
                Return N.Substring(0, 1).ToUpperInvariant()
            End Get
        End Property

        Public ReadOnly Property IsUsable As Boolean
            Get
                Return FailureReason = "" AndAlso Manifest IsNot Nothing AndAlso Manifest.IsValid
            End Get
        End Property

        Public ReadOnly Property DisplayName As String
            Get
                Return If(Manifest IsNot Nothing, Manifest.Name, PathUtils.GetLastPart(Directory))
            End Get
        End Property
    End Class

    ''' <summary>已扫描到的全部插件，按 Id 索引。</summary>
    Public ReadOnly Plugins As New List(Of PluginEntry)

    ''' <summary>
    ''' 扫描插件目录。可重复调用，会重建列表。
    ''' </summary>
    Public Sub Scan()
        Plugins.Clear()
        Try
            DirectoryUtils.Create(PluginDir)
            For Each SubDir In Directory.GetDirectories(PluginDir)
                Try
                    Dim Entry As New PluginEntry With {.Directory = SubDir}
                    Dim ManifestPath = System.IO.Path.Combine(SubDir, "plugin.json")
                    If Not FileUtils.Exists(ManifestPath) Then
                        Entry.FailureReason = "缺少 plugin.json"
                        Plugins.Add(Entry)
                        Continue For
                    End If

                    Dim Raw As String = FileUtils.ReadAsString(ManifestPath)
                    Dim M As PluginManifest = Nothing
                    Try
                        M = Newtonsoft.Json.JsonConvert.DeserializeObject(Of PluginManifest)(Raw)
                    Catch ex As Exception
                        Entry.FailureReason = $"plugin.json 格式错误：{ex.Message}"
                        Plugins.Add(Entry)
                        Continue For
                    End Try
                    Entry.Manifest = M

                    If M Is Nothing OrElse Not M.IsValid Then
                        Entry.FailureReason = "plugin.json 缺少必填字段（id / name / version）"
                        Plugins.Add(Entry)
                        Continue For
                    End If

                    '版本门槛
                    If M.MinVersionCode > VersionCode Then
                        Entry.FailureReason = $"需要启动器版本码 ≥ {M.MinVersionCode}，当前为 {VersionCode}"
                        Plugins.Add(Entry)
                        Continue For
                    End If

                    '入口文件
                    Entry.EntryPath = System.IO.Path.Combine(SubDir, M.Entry)

                    '头像与配置页：都在插件目录内，越界路径一律忽略
                    If Not String.IsNullOrWhiteSpace(M.Icon) Then
                        Dim IconFull = ResolveInsidePlugin(SubDir, M.Icon)
                        If IconFull IsNot Nothing AndAlso FileUtils.Exists(IconFull) Then Entry.IconPath = IconFull
                    End If
                    If Not String.IsNullOrWhiteSpace(M.ConfigPage) Then
                        Dim CfgFull = ResolveInsidePlugin(SubDir, M.ConfigPage)
                        If CfgFull IsNot Nothing AndAlso FileUtils.Exists(CfgFull) Then Entry.ConfigPath = CfgFull
                    End If
                    If Not FileUtils.Exists(Entry.EntryPath) Then
                        Entry.FailureReason = $"找不到入口文件 {M.Entry}"
                        Plugins.Add(Entry)
                        Continue For
                    End If

                    '用户开关：动态键可能尚未注册，Settings.Get 会抛 KeyNotFoundException，
                    '不能让它把整个插件判为不可用
                    Try
                        Entry.Enabled = Settings.Get(Of Boolean)($"PluginEnabled.{M.Id}")
                    Catch
                        Entry.Enabled = True '默认启用
                    End Try
                    Plugins.Add(Entry)
                Catch ex As Exception
                    Logger.Warn(ex, $"扫描插件目录失败：{SubDir}")
                End Try
            Next
            Dim Ok = Plugins.Where(Function(p) p.IsUsable).Count()
            Logger.Info($"插件扫描完成：共 {Plugins.Count} 个，可用 {Ok} 个")
        Catch ex As Exception
            Logger.Error(ex, "扫描插件失败")
        End Try
    End Sub

    ''' <summary>读取某个插件的 XAML 内容。失败返回 Nothing。</summary>
    ''' <summary>
    ''' 读插件入口内容。Java 插件没有 XAML 入口，返回空字符串。
    ''' </summary>
    Public Function ReadEntry(Entry As PluginEntry) As String
        '如果是 Java 插件，没有 XAML 入口
        If Entry IsNot Nothing AndAlso Entry.IsJava Then Return ""
        Try
            If Entry Is Nothing OrElse Not Entry.IsUsable OrElse Not Entry.Enabled Then Return Nothing
            Return FileUtils.TryReadAsString(Entry.EntryPath)
        Catch ex As Exception
            Logger.Warn(ex, $"读取插件入口失败：{Entry?.DisplayName}")
            Return Nothing
        End Try
    End Function

    ''' <summary>设置插件开关并持久化。</summary>
    Public Sub SetEnabled(Entry As PluginEntry, Enabled As Boolean)
        Try
            If Entry Is Nothing OrElse Entry.Manifest Is Nothing Then Return
            Entry.Enabled = Enabled
            Try
                Settings.Set($"PluginEnabled.{Entry.Manifest.Id}", Enabled)
            Catch ex As Exception
                Logger.Warn(ex, $"保存插件开关失败：{Entry.DisplayName}")
            End Try
            Logger.Info($"插件 {Entry.DisplayName} 已{(If(Enabled, "启用", "禁用"))}")
        Catch ex As Exception
            Logger.Warn(ex, "设置插件开关失败")
        End Try
    End Sub

    ''' <summary>
    ''' 生成一份插件模板，方便作者快速开始。
    ''' </summary>
    Public Function CreateTemplate(Name As String) As Boolean
        Try
            Dim SafeName = String.Join("_", Name.Split(Path.GetInvalidFileNameChars()))
            If String.IsNullOrWhiteSpace(SafeName) Then Return False
            Dim TargetDir = System.IO.Path.Combine(PluginDir, SafeName)
            If DirectoryUtils.Exists(TargetDir) Then
                Hint($"目录已存在：{SafeName}", HintType.Red)
                Return False
            End If
            DirectoryUtils.Create(TargetDir)

            Dim Manifest As New JObject From {
                {"id", $"com.example.{SafeName.ToLowerInvariant()}"},
                {"name", Name},
                {"version", "1.0.0"},
                {"author", "你的名字"},
                {"description", "这个插件做什么"},
                {"entry", "main.xaml"},
                {"needNetwork", False},
                {"needPlayerData", False},
                {"needExecute", False},
                {"minVersionCode", 100}
            }
            FileUtils.Write(System.IO.Path.Combine(TargetDir, "plugin.json"),
                            Manifest.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8)

            Dim Template =
                "<local:MyCard Title=""" & Name & """ Margin=""0,0,0,15"">" & vbCrLf &
                "    <StackPanel Margin=""25,38,25,15"">" & vbCrLf &
                "        <TextBlock Text=""Hello, 这是一个插件！"" TextWrapping=""Wrap"" />" & vbCrLf &
                "        <TextBlock Margin=""0,10,0,0"" Text=""编辑 main.xaml 修改这段内容。""" & vbCrLf &
                "                   Foreground=""{DynamicResource ColorBrushGray3}"" TextWrapping=""Wrap"" />" & vbCrLf &
                "    </StackPanel>" & vbCrLf &
                "</local:MyCard>"
            FileUtils.Write(System.IO.Path.Combine(TargetDir, "main.xaml"), Template, Encoding.UTF8)

            Hint($"已创建插件模板：{SafeName}", HintType.Green)
            Scan()
            Return True
        Catch ex As Exception
            Logger.Error(ex, "创建插件模板失败")
            Hint($"创建失败：{ex.Message}", HintType.Red)
            Return False
        End Try
    End Function


    ''' <summary>
    ''' 把清单里的相对路径解析到插件目录内。越界返回 Nothing。
    ''' </summary>
    Private Function ResolveInsidePlugin(Root As String, RelativePath As String) As String
        Try
            If String.IsNullOrWhiteSpace(RelativePath) Then Return Nothing
            Dim Clean = RelativePath.Replace("/"c, "\"c).TrimStart("\"c)
            If Clean = "" Then Return Nothing
            For Each Part In Clean.Split("\"c)
                If Part = ".." Then Return Nothing
            Next
            If Clean.Length >= 2 AndAlso Clean(1) = ":"c Then Return Nothing
            If Clean.StartsWith("\\") Then Return Nothing
            Dim Full = System.IO.Path.Combine(Root, Clean)
            Dim RootFull = System.IO.Path.GetFullPath(Root).TrimEnd("\"c) & "\"
            Dim Norm = System.IO.Path.GetFullPath(Full)
            If Not Norm.StartsWith(RootFull, StringComparison.OrdinalIgnoreCase) Then Return Nothing
            Return Full
        Catch
            Return Nothing
        End Try
    End Function
End Module
