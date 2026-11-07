Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' 从 Mod 的 JAR 里读出它自己声明的身份与依赖。
''' <para>
''' 不同加载器的声明格式完全不同，必须分开读，不能混着解析：
''' </para>
''' <list type="bullet">
''' <item>Fabric / Quilt：根目录 <c>fabric.mod.json</c>（或 <c>quilt.mod.json</c>）</item>
''' <item>Forge 1.13+ / NeoForge：<c>META-INF/mods.toml</c> 或 <c>META-INF/neoforge.mods.toml</c></item>
''' <item>旧版 Forge：<c>mcmod.info</c></item>
''' </list>
''' <para>
''' 设计原则：**读不出来就说读不出来**。文件名和平台分类只作参考，不能代替实际声明；
''' 解析失败一律返回 Unknown，绝不因为解析不出来就报「缺前置」。
''' </para>
''' </summary>
Public Module ModMeta

    ''' <summary>加载器类型。</summary>
    Public Enum ModLoaderKind
        Unknown = 0
        Fabric = 1
        Quilt = 2
        Forge = 3
        NeoForge = 4
    End Enum

    ''' <summary>一条依赖声明。</summary>
    Public Class ModDependency
        ''' <summary>依赖的 Mod ID。</summary>
        Public Property ModId As String = ""
        ''' <summary>要求的版本范围，空表示不限制。</summary>
        Public Property Requirement As String = ""
        ''' <summary>是否必需；False 表示可选。</summary>
        Public Property Required As Boolean = True
        ''' <summary>是否只影响客户端。</summary>
        Public Property ClientSide As Boolean = True
    End Class

    ''' <summary>从一个 JAR 里读到的 Mod 元数据。</summary>
    Public Class ModMetaInfo
        ''' <summary>Mod ID（可能有多个，取第一个为主）。</summary>
        Public Property Ids As New List(Of String)
        ''' <summary>显示名。</summary>
        Public Property Name As String = ""
        ''' <summary>版本。</summary>
        Public Property Version As String = ""
        ''' <summary>声明的加载器。</summary>
        Public Property Loader As ModLoaderKind = ModLoaderKind.Unknown
        ''' <summary>依赖列表。</summary>
        Public Property Dependencies As New List(Of ModDependency)
        ''' <summary>是否成功读到声明。False 时上面各字段都不可信。</summary>
        Public Property Parsed As Boolean = False
        ''' <summary>解析失败的原因，用于日志与提示。</summary>
        Public Property FailureReason As String = ""

        ''' <summary>主 ID。</summary>
        Public ReadOnly Property PrimaryId As String
            Get
                Return If(Ids.Count > 0, Ids(0), "")
            End Get
        End Property

        Public ReadOnly Property DisplayName As String
            Get
                If Not String.IsNullOrWhiteSpace(Name) Then Return Name
                If Not String.IsNullOrWhiteSpace(PrimaryId) Then Return PrimaryId
                Return "（未知 Mod）"
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 读取一个 Mod 文件的元数据。任何异常都被吞掉并转为 Parsed=False。
    ''' </summary>
    Public Function ReadMeta(FilePath As String) As ModMetaInfo
        Dim Result As New ModMetaInfo
        Try
            If String.IsNullOrWhiteSpace(FilePath) OrElse Not FileUtils.Exists(FilePath) Then
                Result.FailureReason = "文件不存在"
                Return Result
            End If
            Using Zip As ZipArchive = ZipFile.OpenRead(FilePath)
                '按优先级依次尝试：Fabric → Quilt → NeoForge → Forge 新 → Forge 旧
                If TryReadFabric(Zip, Result, "fabric.mod.json", ModLoaderKind.Fabric) Then Return Result
                If TryReadFabric(Zip, Result, "quilt.mod.json", ModLoaderKind.Quilt) Then Return Result
                If TryReadForgeToml(Zip, Result, "META-INF/neoforge.mods.toml", ModLoaderKind.NeoForge) Then Return Result
                If TryReadForgeToml(Zip, Result, "META-INF/mods.toml", ModLoaderKind.Forge) Then Return Result
                If TryReadLegacyForge(Zip, Result) Then Return Result
            End Using
            Result.FailureReason = "JAR 内没有可识别的 Mod 声明"
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, $"读取 Mod 元数据失败：{FilePath}")
            Result.FailureReason = ex.Message
            Result.Parsed = False
            Return Result
        End Try
    End Function

    ''' <summary>
    ''' 把 Fabric 的依赖声明统一展开成 (id, 版本范围) 列表。
    ''' <para>
    ''' Fabric 允许 <c>{ "modid": ">=1.0" }</c> 与 <c>[{ "id": "modid", "version": ">=1.0" }]</c>
    ''' 两种写法，必须都支持；遍历 JObject 拿到的是 JProperty，不能直接 ToString。
    ''' </para>
    ''' </summary>
    Private Iterator Function EnumerateDependencyPairs(Node As JToken) As IEnumerable(Of Tuple(Of String, String))
        If Node Is Nothing Then Return
        If TypeOf Node Is JObject Then
            For Each Prop In CType(Node, JObject).Properties()
                Dim Ver As String = ""
                If Prop.Value IsNot Nothing AndAlso TypeOf Prop.Value Is JValue Then Ver = Prop.Value.ToString()
                Yield Tuple.Create(Prop.Name, Ver)
            Next
        ElseIf TypeOf Node Is JArray Then
            For Each Item In CType(Node, JArray)
                Dim Id As String = ""
                Dim Ver As String = ""
                If TypeOf Item Is JValue Then
                    Id = Item.ToString()
                ElseIf TypeOf Item Is JObject Then
                    Id = Item("id")?.ToString()
                    Ver = Item("version")?.ToString()
                End If
                Yield Tuple.Create(If(Id, ""), If(Ver, ""))
            Next
        End If
    End Function

    ''' <summary>读 ZIP 内某个条目的文本内容，不存在返回 Nothing。</summary>
    Private Function ReadEntryText(Zip As ZipArchive, EntryName As String) As String
        Try
            Dim Entry = Zip.GetEntry(EntryName)
            If Entry Is Nothing Then Return Nothing
            '防御：声明文件不该超过 2 MB，超了多半是恶意构造
            If Entry.Length > 2L * 1024 * 1024 Then Return Nothing
            Using EntryStream = Entry.Open()
                Using Reader As New System.IO.StreamReader(EntryStream, System.Text.Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>解析 Fabric / Quilt 的 JSON 声明。</summary>
    Private Function TryReadFabric(Zip As ZipArchive, Result As ModMetaInfo, EntryName As String, Kind As ModLoaderKind) As Boolean
        Dim Raw = ReadEntryText(Zip, EntryName)
        If Raw Is Nothing Then Return False
        Try
            Dim Root = JObject.Parse(Raw)
            'Quilt 把内容包在 quilt_loader 下
            Dim Container As JToken = Root
            If Root("quilt_loader") IsNot Nothing Then Container = Root("quilt_loader")

            Dim Id = Container("id")?.ToString()
            If String.IsNullOrWhiteSpace(Id) Then
                Result.FailureReason = $"{EntryName} 缺少 id 字段"
                Return False
            End If
            Result.Ids.Add(Id)
            Result.Version = Container("version")?.ToString()
            Result.Name = Container("name")?.ToString()
            Result.Loader = Kind

            'provides：同一个 JAR 还提供了哪些 ID
            Dim Provides = Container("provides")
            If Provides IsNot Nothing Then
                For Each P In Provides
                    Dim ProvidedId = If(TypeOf P Is JValue, P.ToString(), P("id")?.ToString())
                    If Not String.IsNullOrWhiteSpace(ProvidedId) Then Result.Ids.Add(ProvidedId)
                Next
            End If

            'depends：必需依赖。Fabric 支持两种写法：
            '  { "modid": ">=1.0" }              ← 最常见
            '  [ { "id": "modid", "version": ">=1.0" }, ... ]
            Dim Depends = Container("depends")
            If Depends IsNot Nothing Then
                For Each Pair In EnumerateDependencyPairs(Depends)
                    If String.IsNullOrWhiteSpace(Pair.Item1) Then Continue For
                    '跳过加载器自身与 Minecraft：这些由启动器负责，不是玩家该补的
                    If Pair.Item1 = "fabricloader" OrElse Pair.Item1 = "fabric-api" OrElse
                       Pair.Item1 = "minecraft" OrElse Pair.Item1 = "java" Then Continue For
                    Result.Dependencies.Add(New ModDependency With {
                        .ModId = Pair.Item1, .Requirement = Pair.Item2, .Required = True})
                Next
            End If

            'breaks：明确不兼容的版本，用 Required=False + ClientSide=False 标记
            Dim Breaks = Container("breaks")
            If Breaks IsNot Nothing Then
                For Each Pair In EnumerateDependencyPairs(Breaks)
                    If String.IsNullOrWhiteSpace(Pair.Item1) Then Continue For
                    Result.Dependencies.Add(New ModDependency With {
                        .ModId = Pair.Item1, .Requirement = Pair.Item2,
                        .Required = False, .ClientSide = False})
                Next
            End If

            Result.Parsed = True
            Return True
        Catch ex As Exception
            Result.FailureReason = $"{EntryName} 解析失败：{ex.Message}"
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 解析 Forge / NeoForge 的 TOML 声明。
    ''' <para>
    ''' 只做够用的子集解析：定位 <c>[[mods]]</c> 与 <c>[[dependencies.xxx]]</c> 段，
    ''' 不引入完整 TOML 解析器。看不懂的结构按未知处理，不猜。
    ''' </para>
    ''' </summary>
    Private Function TryReadForgeToml(Zip As ZipArchive, Result As ModMetaInfo, EntryName As String, Kind As ModLoaderKind) As Boolean
        Dim Raw = ReadEntryText(Zip, EntryName)
        If Raw Is Nothing Then Return False
        Try
            Dim Lines = Raw.Split({vbCrLf, vbLf}, StringSplitOptions.None)

            '---- [[mods]] 段：取第一个 modId / version / displayName ----
            Dim InMods As Boolean = False
            Dim GotMod As Boolean = False
            For Each Line In Lines
                Dim Trimmed = Line.Trim()
                If Trimmed.StartsWith("[[") Then
                    InMods = Trimmed.Equals("[[mods]]", StringComparison.OrdinalIgnoreCase)
                    Continue For
                End If
                If Not InMods Then Continue For
                Dim Kv = SplitTomlPair(Trimmed)
                If Kv Is Nothing Then Continue For
                Select Case Kv.Item1
                    Case "modId"
                        If Not String.IsNullOrWhiteSpace(Kv.Item2) Then
                            Result.Ids.Add(Kv.Item2)
                            GotMod = True
                        End If
                    Case "version"
                        If String.IsNullOrWhiteSpace(Result.Version) Then Result.Version = Kv.Item2
                    Case "displayName"
                        If String.IsNullOrWhiteSpace(Result.Name) Then Result.Name = Kv.Item2
                End Select
            Next

            If Not GotMod Then
                Result.FailureReason = $"{EntryName} 里没有 [[mods]] modId"
                Return False
            End If
            Result.Loader = Kind

            '---- [[dependencies.<modid>]] 段 ----
            Dim InDeps As Boolean = False
            For Each Line In Lines
                Dim Trimmed = Line.Trim()
                If Trimmed.StartsWith("[[") Then
                    InDeps = Trimmed.StartsWith("[[dependencies.", StringComparison.OrdinalIgnoreCase)
                    Continue For
                End If
                If Not InDeps Then Continue For
                Dim Kv = SplitTomlPair(Trimmed)
                If Kv Is Nothing Then Continue For
                If Kv.Item1 <> "modId" Then Continue For
                Dim DepId = Kv.Item2
                If String.IsNullOrWhiteSpace(DepId) Then Continue For
                '跳过加载器自身与 Minecraft
                If DepId = "forge" OrElse DepId = "neoforge" OrElse DepId = "minecraft" OrElse DepId = "java" Then Continue For
                Result.Dependencies.Add(New ModDependency With {.ModId = DepId, .Required = True})
            Next

            Result.Parsed = True
            Return True
        Catch ex As Exception
            Result.FailureReason = $"{EntryName} 解析失败：{ex.Message}"
            Return False
        End Try
    End Function

    ''' <summary>把 <c>key = "value"</c> 拆成两段。不是键值行返回 Nothing。</summary>
    'VB 里 "#"c 会被当成预处理指令，用常量绕开
    Private ReadOnly HashChar As Char = ChrW(35)
    Private ReadOnly QuoteChar As Char = ChrW(34)

    Private Function SplitTomlPair(Line As String) As Tuple(Of String, String)
        If String.IsNullOrWhiteSpace(Line) OrElse Line.StartsWith(HashChar) Then Return Nothing
        Dim Pos = Line.IndexOf("="c)
        If Pos <= 0 Then Return Nothing
        Dim Key = Line.Substring(0, Pos).Trim()
        Dim Val = Line.Substring(Pos + 1).Trim()
        '去掉行尾注释
        Dim HashPos = Val.IndexOf(HashChar)
        If HashPos >= 0 Then Val = Val.Substring(0, HashPos).Trim()
        '去掉引号
        Val = Val.Trim(QuoteChar)
        Return Tuple.Create(Key, Val)
    End Function

    ''' <summary>解析旧版 Forge 的 mcmod.info。</summary>
    Private Function TryReadLegacyForge(Zip As ZipArchive, Result As ModMetaInfo) As Boolean
        Dim Raw = ReadEntryText(Zip, "mcmod.info")
        If Raw Is Nothing Then Return False
        Try
            Dim Root = JToken.Parse(Raw)
            'mcmod.info 可能是数组，也可能是 {modList:[...]}
            Dim First As JToken = Nothing
            If TypeOf Root Is JArray Then
                First = CType(Root, JArray).FirstOrDefault()
            ElseIf Root("modList") IsNot Nothing Then
                First = Root("modList").FirstOrDefault()
            End If
            If First Is Nothing Then
                Result.FailureReason = "mcmod.info 内容为空"
                Return False
            End If
            Dim Id = First("modid")?.ToString()
            If String.IsNullOrWhiteSpace(Id) Then
                Result.FailureReason = "mcmod.info 缺少 modid"
                Return False
            End If
            Result.Ids.Add(Id)
            Result.Name = First("name")?.ToString()
            Result.Version = First("version")?.ToString()
            Result.Loader = ModLoaderKind.Forge
            '旧版 mcmod.info 的依赖信息不可靠，不解析，留空
            Result.Parsed = True
            Return True
        Catch ex As Exception
            Result.FailureReason = $"mcmod.info 解析失败：{ex.Message}"
            Return False
        End Try
    End Function

End Module
