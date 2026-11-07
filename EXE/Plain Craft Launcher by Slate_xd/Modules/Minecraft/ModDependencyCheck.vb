Imports System
Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' 模组依赖检查：找出「装了 A，但 A 要求的前置 B 没装或版本不对」的情况。
''' <para>
''' 判断链条：<c>ModMeta.ReadMeta</c> 读出每个 Mod 声明的依赖 →
''' 与本地已安装的 Mod ID 表比对 → 缺的记为缺失，有的用
''' <c>ModVersionRange.MatchVersion</c> 校验版本。
''' </para>
''' <para>
''' **三态原则**：只有「确定不满足」才算问题；「无法确认」的单独归类，
''' 不当作错误提示玩家。版本范围写法太杂，猜错会让玩家删掉本来能用的 Mod。
''' </para>
''' <para>
''' 检查只读文件、不执行 JAR 里的任何代码、不自动下载或删除。
''' </para>
''' </summary>
Public Module ModDependencyCheck

    ''' <summary>单个 Mod 的检查结果。</summary>
    Public Class ModIssue
        ''' <summary>出问题的 Mod 文件名。</summary>
        Public Property FileName As String = ""
        ''' <summary>出问题的 Mod 显示名。</summary>
        Public Property ModName As String = ""
        ''' <summary>缺失或版本不符的前置 ID。</summary>
        Public Property DependencyId As String = ""
        ''' <summary>要求的版本范围。</summary>
        Public Property Requirement As String = ""
        ''' <summary>问题类型。</summary>
        Public Property Kind As IssueKind = IssueKind.Missing
        ''' <summary>本地已装版本的说明（仅版本不符时有值）。</summary>
        Public Property FoundVersion As String = ""

        Public ReadOnly Property DisplayText As String
            Get
                Select Case Kind
                    Case IssueKind.Missing
                        Return $"{ModName} 需要前置 {DependencyId}"
                    Case IssueKind.VersionMismatch
                        Return $"{ModName} 需要 {DependencyId} {Requirement}，本地是 {FoundVersion}"
                    Case IssueKind.Unparsable
                        Return $"{FileName} 无法解析，跳过检查"
                    Case Else
                        Return $"{ModName} 与 {DependencyId} 冲突"
                End Select
            End Get
        End Property
    End Class

    ''' <summary>问题类型。</summary>
    Public Enum IssueKind
        ''' <summary>前置完全没装。</summary>
        Missing = 0
        ''' <summary>装了但版本不在要求范围内。</summary>
        VersionMismatch = 1
        ''' <summary>声明里明确写的不兼容。</summary>
        Conflict = 2
        ''' <summary>JAR 读不出声明，无法判断。</summary>
        Unparsable = 3
    End Enum

    ''' <summary>一次检查的完整结果。</summary>
    Public Class CheckResult
        ''' <summary>确定的问题。</summary>
        Public Property Issues As New List(Of ModIssue)
        ''' <summary>无法确认的条目（不当作错误）。</summary>
        Public Property Uncertain As New List(Of String)
        ''' <summary>参与检查的 Mod 数。</summary>
        Public Property CheckedCount As Integer = 0
        ''' <summary>解析失败的 Mod 数。</summary>
        Public Property UnparsableCount As Integer = 0

        Public ReadOnly Property HasIssues As Boolean
            Get
                Return Issues.Count > 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 检查一个实例的 mods 目录。
    ''' </summary>
    Public Function Check(Instance As McInstance) As CheckResult
        Dim Result As New CheckResult
        Try
            If Instance Is Nothing Then Return Result
            Dim ModsDir = Instance.PathIndie & "mods\"
            If Not DirectoryUtils.Exists(ModsDir) Then Return Result

            '---- 第一步：读出所有已启用 Mod 的元数据 ----
            Dim Installed As New Dictionary(Of String, ModMeta.ModMetaInfo)(StringComparer.OrdinalIgnoreCase)
            Dim AllMetas As New List(Of Tuple(Of String, ModMeta.ModMetaInfo))
            For Each Path In Directory.GetFiles(ModsDir)
                Dim Ext = PathUtils.GetExtension(Path).ToLowerInvariant()
                '只检查会真正加载的扩展名
                If Ext <> ".jar" AndAlso Ext <> ".litemod" Then Continue For
                Try
                    Dim Meta = ModMeta.ReadMeta(Path)
                    Result.CheckedCount += 1
                    If Not Meta.Parsed Then
                        Result.UnparsableCount += 1
                        Result.Uncertain.Add($"{PathUtils.GetLastPart(Path)}：{Meta.FailureReason}")
                        Continue For
                    End If
                    '同一个 JAR 可能提供多个 ID，全部登记
                    For Each Id In Meta.Ids
                        If Not String.IsNullOrWhiteSpace(Id) AndAlso Not Installed.ContainsKey(Id) Then
                            Installed(Id) = Meta
                        End If
                    Next
                    AllMetas.Add(Tuple.Create(Path, Meta))
                Catch ex As Exception
                    Logger.Warn(ex, $"检查 Mod 失败：{Path}")
                    Result.UnparsableCount += 1
                End Try
            Next

            '---- 第二步：逐个核对依赖 ----
            For Each Item In AllMetas
                Dim Path = Item.Item1
                Dim Meta = Item.Item2
                For Each Dep In Meta.Dependencies
                    If String.IsNullOrWhiteSpace(Dep.ModId) Then Continue For

                    Dim HasLocal = Installed.ContainsKey(Dep.ModId)
                    Dim LocalMeta As ModMeta.ModMetaInfo = Nothing
                    If HasLocal Then LocalMeta = Installed(Dep.ModId)

                    '声明里 breaks 的项（Required=False + ClientSide=False）
                    If Not Dep.Required AndAlso Not Dep.ClientSide Then
                        If HasLocal AndAlso Not String.IsNullOrWhiteSpace(Dep.Requirement) Then
                            Dim M = ModVersionRange.MatchVersion(LocalMeta.Version, Dep.Requirement)
                            If M = ModVersionRange.VersionMatch.Satisfied Then
                                Result.Issues.Add(New ModIssue With {
                                    .FileName = PathUtils.GetLastPart(Path),
                                    .ModName = Meta.DisplayName,
                                    .DependencyId = Dep.ModId,
                                    .Requirement = Dep.Requirement,
                                    .FoundVersion = LocalMeta.Version,
                                    .Kind = IssueKind.Conflict
                                })
                            End If
                        End If
                        Continue For
                    End If

                    If Not Dep.Required Then Continue For

                    '必需依赖：没装就是缺失
                    If Not HasLocal Then
                        Result.Issues.Add(New ModIssue With {
                            .FileName = PathUtils.GetLastPart(Path),
                            .ModName = Meta.DisplayName,
                            .DependencyId = Dep.ModId,
                            .Requirement = Dep.Requirement,
                            .Kind = IssueKind.Missing
                        })
                        Continue For
                    End If

                    '装了：有版本要求就核对
                    If String.IsNullOrWhiteSpace(Dep.Requirement) Then Continue For
                    Dim Match = ModVersionRange.MatchVersion(LocalMeta.Version, Dep.Requirement)
                    Select Case Match
                        Case ModVersionRange.VersionMatch.Unsatisfied
                            Result.Issues.Add(New ModIssue With {
                                .FileName = PathUtils.GetLastPart(Path),
                                .ModName = Meta.DisplayName,
                                .DependencyId = Dep.ModId,
                                .Requirement = Dep.Requirement,
                                .FoundVersion = LocalMeta.Version,
                                .Kind = IssueKind.VersionMismatch
                            })
                        Case ModVersionRange.VersionMatch.Unknown
                            '无法确认：单独归类，不当作错误
                            Result.Uncertain.Add(
                                $"{Meta.DisplayName} → {Dep.ModId} {Dep.Requirement}（本地 {LocalMeta.Version}）")
                    End Select
                Next
            Next

            Return Result
        Catch ex As Exception
            Logger.Error(ex, "依赖检查失败")
            Return Result
        End Try
    End Function

End Module
