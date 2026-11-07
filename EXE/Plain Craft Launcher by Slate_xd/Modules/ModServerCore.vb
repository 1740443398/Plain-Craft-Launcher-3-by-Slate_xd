Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

''' <summary>
''' 服务端核心来源。
''' <para>
''' <b>为什么要分类</b>：Minecraft 服务端核心不是「一个 jar 通吃」。
''' 原版不能装模组；插件核心（Paper 系）能装插件但装不了模组；
''' 模组核心（Forge/Fabric 系）反过来；还有基岩互通和代理端。
''' 选错了就是白下载、白开服，所以界面上必须先把这件事讲清楚。
''' </para>
''' <para>
''' <b>数据来源</b>：主源是 FastMirror（无极镜像）的公开 API，
''' 它聚合了 16 个核心且每个核心都带完整的版本列表与 SHA1。
''' 原版（Vanilla）额外保留 Mojang 官方源作为直连通道。
''' </para>
''' </summary>
Public Module ModServerCore

    ''' <summary>FastMirror API 根地址。</summary>
    Public Const ApiBase As String = "https://download.fastmirror.net/api/v3"
    ''' <summary>FastMirror 下载根地址。</summary>
    Public Const DownloadBase As String = "https://download.fastmirror.net/download"

    ''' <summary>核心类别。与 FastMirror 的 tag 对应。</summary>
    Public Enum CoreKind
        ''' <summary>原版。</summary>
        Vanilla = 0
        ''' <summary>插件服（Bukkit/Paper 系）。</summary>
        Plugin = 1
        ''' <summary>模组服（Forge/Fabric 系）。</summary>
        Modded = 2
        ''' <summary>基岩版服务端（Nukkit/PocketMine）。</summary>
        Bedrock = 3
        ''' <summary>代理端（BungeeCord/Velocity）。</summary>
        Proxy = 4
    End Enum

    ''' <summary>一个服务端核心。</summary>
    Public Class CoreOption
        Public Property Name As String = ""
        Public Property Kind As CoreKind = CoreKind.Vanilla
        ''' <summary>FastMirror 的原始 tag。</summary>
        Public Property RawTag As String = ""
        Public Property Description As String = ""
        Public Property Homepage As String = ""
        ''' <summary>支持的 Minecraft 版本（来自 API）。</summary>
        Public Property Versions As New List(Of String)
        ''' <summary>是否官方推荐。</summary>
        Public Property Recommend As Boolean = False

        Public ReadOnly Property KindText As String
            Get
                Select Case Kind
                    Case CoreKind.Vanilla : Return "原版"
                    Case CoreKind.Plugin : Return "插件服"
                    Case CoreKind.Modded : Return "模组服"
                    Case CoreKind.Bedrock : Return "基岩版"
                    Case CoreKind.Proxy : Return "代理端"
                    Case Else : Return "其他"
                End Select
            End Get
        End Property

        ''' <summary>能力说明，直接展示给玩家，避免选错。</summary>
        Public ReadOnly Property AbilityText As String
            Get
                Select Case Kind
                    Case CoreKind.Vanilla : Return "只能玩原版，不能装插件或模组"
                    Case CoreKind.Plugin : Return "能装插件，不能装模组"
                    Case CoreKind.Modded : Return "能装模组，不能装插件"
                    Case CoreKind.Bedrock : Return "给手机/主机玩家用的基岩版服务端"
                    Case CoreKind.Proxy : Return "群组服转发，不承载游戏世界"
                    Case Else : Return ""
                End Select
            End Get
        End Property
    End Class

    ''' <summary>已拉取到的核心列表。</summary>
    Public ReadOnly Cores As New List(Of CoreOption)

    ''' <summary>每个核心的中文说明（API 只给英文名）。</summary>
    Private ReadOnly Descriptions As New Dictionary(Of String, String) From {
        {"Vanilla", "Mojang 官方原版服务端。纯净生存用这个。"},
        {"Paper", "最主流的插件服务端，性能比原版好，插件生态最大。"},
        {"Purpur", "基于 Paper，额外提供大量可调项（末影龙、刷怪、坐骑等）。"},
        {"Folia", "Paper 的多线程实验分支，适合大型服务器，插件兼容性差。"},
        {"Leaves", "Paper 的分支，偏向性能优化与部分原版特性恢复。"},
        {"SpongeVanilla", "Sponge 的原版实现，用 Sponge 插件。"},
        {"Fabric", "轻量模组服务端，启动快，只装 Fabric 模组。"},
        {"Forge", "老牌模组服务端，大量整合包基于它。"},
        {"NeoForge", "Forge 的社区分支，新版本模组越来越多用它。"},
        {"Quilt", "Fabric 的分支，兼容大部分 Fabric 模组。"},
        {"Arclight", "让 Forge/Fabric 模组服也能装 Bukkit 插件。"},
        {"CatServer", "国产混合端，Forge 模组 + 插件同时支持。"},
        {"SpongeForge", "Sponge 的 Forge 实现。"},
        {"SpongeNeo", "Sponge 的 NeoForge 实现。"},
        {"Nukkit", "基岩版服务端（Java 编写），手机/主机玩家进这个。"},
        {"PocketMine", "基岩版服务端（PHP 编写），插件生态与 Java 版不同。"},
        {"BungeeCord", "老牌群组服代理，把多个子服串成一个入口。"},
        {"Velocity", "现代群组服代理，性能与安全性比 BungeeCord 好。"}
    }

    ''' <summary>
    ''' 拉取核心列表。失败时保留上次结果。
    ''' </summary>
    Public Function FetchCores() As Boolean
        Try
            Dim Raw = HttpGet(ApiBase)
            If String.IsNullOrWhiteSpace(Raw) Then Return False
            Dim Root As JToken = JToken.Parse(Raw)
            Dim Arr As JArray = TryCast(Root("data"), JArray)
            If Arr Is Nothing Then Return False

            Cores.Clear()
            For Each Item In Arr
                Try
                    Dim Name = Item("name")?.ToString()
                    If String.IsNullOrWhiteSpace(Name) Then Continue For
                    Dim Tag = If(Item("tag")?.ToString(), "")

                    Dim Kind As CoreKind
                    Select Case Tag
                        Case "vanilla" : Kind = CoreKind.Vanilla
                        Case "pure" : Kind = CoreKind.Plugin
                        Case "mod" : Kind = CoreKind.Modded
                        Case "bedrock" : Kind = CoreKind.Bedrock
                        Case "proxy" : Kind = CoreKind.Proxy
                        Case Else : Kind = CoreKind.Plugin
                    End Select

                    Dim C As New CoreOption With {
                        .Name = Name,
                        .Kind = Kind,
                        .RawTag = Tag,
                        .Homepage = If(Item("homepage")?.ToString(), ""),
                        .Recommend = If(Item("recommend")?.ToObject(Of Boolean)(), False),
                        .Description = If(Descriptions.ContainsKey(Name), Descriptions(Name), "")
                    }
                    Dim Vs As JArray = TryCast(Item("mc_versions"), JArray)
                    If Vs IsNot Nothing Then
                        For Each V In Vs
                            Dim S = V?.ToString()
                            If Not String.IsNullOrWhiteSpace(S) Then C.Versions.Add(S)
                        Next
                    End If
                    Cores.Add(C)
                Catch ex As Exception
                    Logger.Warn(ex, "解析核心项失败")
                End Try
            Next

            '推荐的排前面
            Dim Sorted = Cores.OrderByDescending(Function(c) c.Recommend).
                                ThenBy(Function(c) CInt(c.Kind)).
                                ThenBy(Function(c) c.Name).ToList()
            Cores.Clear()
            Cores.AddRange(Sorted)

            Logger.Info($"服务端核心列表：拉到 {Cores.Count} 个核心")
            Return Cores.Count > 0
        Catch ex As Exception
            Logger.Error(ex, "拉取服务端核心列表失败")
            Return False
        End Try
    End Function

    ''' <summary>按类别取核心。</summary>
    Public Function GetByKind(Kind As CoreKind) As List(Of CoreOption)
        Return Cores.Where(Function(c) c.Kind = Kind).ToList()
    End Function

    ''' <summary>一个可下载的具体文件。</summary>
    Public Class CoreFile
        Public Property CoreName As String = ""
        Public Property McVersion As String = ""
        Public Property Build As String = ""
        Public Property Url As String = ""
        Public Property Sha1 As String = ""
        Public Property UpdateTime As String = ""
        Public Property Size As Long = 0
    End Class

    ''' <summary>
    ''' 拉取某核心在某游戏版本下的可下载构建。
    ''' </summary>
    Public Function FetchBuilds(CoreName As String, McVersion As String) As List(Of CoreFile)
        Dim Out As New List(Of CoreFile)
        Try
            If String.IsNullOrWhiteSpace(CoreName) OrElse String.IsNullOrWhiteSpace(McVersion) Then Return Out
            Dim Url = $"{ApiBase}/{Uri.EscapeDataString(CoreName)}/{Uri.EscapeDataString(McVersion)}"
            Dim Raw = HttpGet(Url)
            If String.IsNullOrWhiteSpace(Raw) Then Return Out
            Dim Root As JToken = JToken.Parse(Raw)
            Dim Builds As JArray = TryCast(Root("data")?("builds"), JArray)
            If Builds Is Nothing Then Return Out

            For Each B In Builds
                Try
                    Dim BuildVer = B("core_version")?.ToString()
                    If String.IsNullOrWhiteSpace(BuildVer) Then Continue For
                    Out.Add(New CoreFile With {
                        .CoreName = CoreName,
                        .McVersion = If(B("mc_version")?.ToString(), McVersion),
                        .Build = BuildVer,
                        .Sha1 = If(B("sha1")?.ToString(), ""),
                        .UpdateTime = If(B("update_time")?.ToString(), ""),
                        .Url = $"{DownloadBase}/{Uri.EscapeDataString(CoreName)}/{Uri.EscapeDataString(McVersion)}/{Uri.EscapeDataString(BuildVer)}"
                    })
                Catch ex As Exception
                    Logger.Warn(ex, "解析构建项失败")
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"取 {CoreName} {McVersion} 构建失败")
        End Try
        Return Out
    End Function

    ''' <summary>
    ''' Mojang 官方原版服务端地址（作为 Vanilla 的直连通道保留）。
    ''' </summary>
    Public Function FetchVanillaOfficial(McVersion As String) As CoreFile
        Try
            Dim Manifest = HttpGet("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json")
            If String.IsNullOrWhiteSpace(Manifest) Then Return Nothing
            Dim Root As JToken = JToken.Parse(Manifest)
            Dim Target As JToken = Nothing
            For Each Item In Root("versions")
                If Item("id")?.ToString() = McVersion Then
                    Target = Item
                    Exit For
                End If
            Next
            If Target Is Nothing Then Return Nothing

            Dim Detail = HttpGet(Target("url")?.ToString())
            If String.IsNullOrWhiteSpace(Detail) Then Return Nothing
            Dim DJ = JToken.Parse(Detail)
            Dim Srv As JToken = DJ("downloads")?("server")
            If Srv Is Nothing Then Return Nothing

            Return New CoreFile With {
                .CoreName = "Vanilla（Mojang 官方）",
                .McVersion = McVersion,
                .Build = "release",
                .Url = Srv("url")?.ToString(),
                .Sha1 = If(Srv("sha1")?.ToString(), ""),
                .Size = If(Srv("size")?.ToObject(Of Long)(), 0L)
            }
        Catch ex As Exception
            Logger.Warn(ex, $"取 Mojang 官方服务端失败：{McVersion}")
            Return Nothing
        End Try
    End Function

    Private Function HttpGet(Url As String) As String
        Try
            Net.ServicePointManager.SecurityProtocol = Net.ServicePointManager.SecurityProtocol Or
                                                       Net.SecurityProtocolType.Tls12 Or Net.SecurityProtocolType.Tls11
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 20000
            Req.ReadWriteTimeout = 20000
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.Accept = "application/json"
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
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

End Module
