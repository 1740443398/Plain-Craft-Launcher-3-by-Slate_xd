Imports System.Text.Json
Imports System.IO

''' <summary>
''' 启动器自己的 Mod 回收站。
''' <para>
''' PCL 删除 Mod 时默认丢进 Windows 回收站，但玩家在启动器里没有任何入口
''' 能把它们找回来；系统回收站里文件名还被改写过，认不出是哪个 Mod。
''' </para>
''' <para>
''' 这里在实例目录下维护一个 <c>.pclrecycle</c> 文件夹：删除时把文件移进去并记一条
''' JSON 元数据（原始文件名、原路径、删除时间、大小），还原时按记录移回去。
''' 每个实例独立，互不干扰；超过上限时按时间清掉最旧的。
''' </para>
''' <para>
''' 所有操作失败只写日志，不抛异常打断调用方。元数据损坏时按空回收站处理，
''' 不让一个坏文件卡死整个 Mod 管理页。
''' </para>
''' </summary>
Public Module ModRecycleBin

    ''' <summary>回收站最多保留多少条记录。超出后清理最旧的。</summary>
    Public Const MaxItems As Integer = 60

    ''' <summary>回收站里的一条记录。</summary>
    Public Class RecycleEntry
        ''' <summary>原始完整路径。</summary>
        Public Property OriginalPath As String = ""
        ''' <summary>原始文件名。</summary>
        Public Property OriginalName As String = ""
        ''' <summary>在回收站目录中的文件名（去重后的唯一名）。</summary>
        Public Property StoredName As String = ""
        ''' <summary>删除时间（UTC ISO 8601）。</summary>
        Public Property DeletedAt As String = ""
        ''' <summary>文件大小（字节）。</summary>
        Public Property Size As Long = 0

        <JsonIgnore>
        Public ReadOnly Property DeletedTime As DateTime
            Get
                Dim T As DateTime
                If DateTime.TryParse(DeletedAt, T) Then Return T
                Return DateTime.MinValue
            End Get
        End Property

        <JsonIgnore>
        Public ReadOnly Property DisplayText As String
            Get
                Dim TimeText As String = If(DeletedTime = DateTime.MinValue, "未知时间", DeletedTime.ToLocalTime().ToString("MM-dd HH:mm"))
                Dim SizeText As String = If(Size > 0, StringUtils.FormatByteSize(Size), "")
                Return $"{OriginalName}　{SizeText}　{TimeText}"
            End Get
        End Property
    End Class

    Private ReadOnly JsonOpt As New JsonSerializerSettings With {.Formatting = Formatting.Indented}

    ''' <summary>某个实例的回收站目录。</summary>
    Private Function RecycleDir(Instance As McInstance) As String
        If Instance Is Nothing Then Return Nothing
        Return Instance.PathIndie & ".pclrecycle\"
    End Function

    Private Function MetaFile(Instance As McInstance) As String
        Dim Dir = RecycleDir(Instance)
        If Dir Is Nothing Then Return Nothing
        Return Dir & "index.json"
    End Function

    ''' <summary>
    ''' 读取某个实例的回收站记录。目录或索引不存在、内容损坏时返回空列表。
    ''' </summary>
    Public Function ListEntries(Instance As McInstance) As List(Of RecycleEntry)
        Dim Result As New List(Of RecycleEntry)
        Try
            Dim Meta = MetaFile(Instance)
            If Meta Is Nothing OrElse Not FileUtils.Exists(Meta) Then Return Result
            Dim Raw As String = FileUtils.ReadAsString(Meta)
            If String.IsNullOrWhiteSpace(Raw) Then Return Result
            Dim Parsed = JsonConvert.DeserializeObject(Of List(Of RecycleEntry))(Raw)
            If Parsed IsNot Nothing Then Result = Parsed
            '只保留文件仍在回收站目录里的记录
            Dim Dir = RecycleDir(Instance)
            Result = Result.Where(Function(e) e IsNot Nothing AndAlso
                                              Not String.IsNullOrWhiteSpace(e.StoredName) AndAlso
                                              FileUtils.Exists(Dir & e.StoredName)).ToList()
        Catch ex As Exception
            Logger.Warn(ex, "读取回收站记录失败，按空处理")
            Return New List(Of RecycleEntry)
        End Try
        Return Result
    End Function

    Private Sub SaveEntries(Instance As McInstance, Entries As List(Of RecycleEntry))
        Try
            Dim Meta = MetaFile(Instance)
            If Meta Is Nothing Then Return
            DirectoryUtils.Create(RecycleDir(Instance))
            FileUtils.Write(Meta, JsonConvert.SerializeObject(Entries, JsonOpt), Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "保存回收站记录失败")
        End Try
    End Sub

    ''' <summary>
    ''' 把一批文件移进回收站并记录。返回成功移入的条数。
    ''' </summary>
    Public Function MoveToRecycle(Instance As McInstance, Files As IEnumerable(Of String)) As Integer
        If Instance Is Nothing OrElse Files Is Nothing Then Return 0
        Dim Dir = RecycleDir(Instance)
        If Dir Is Nothing Then Return 0
        Dim Entries = ListEntries(Instance)
        Dim Count As Integer = 0
        Try
            DirectoryUtils.Create(Dir)
            For Each Path In Files
                Try
                    If Not FileUtils.Exists(Path) Then Continue For
                    Dim Info As New FileInfo(Path)
                    '用时间戳 + 原名保证唯一，避免同名覆盖
                    Dim Stored As String = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Info.Name}"
                    Dim Target As String = Dir & Stored
                    FileUtils.Move(Path, Target)
                    Entries.Add(New RecycleEntry With {
                        .OriginalPath = Path,
                        .OriginalName = Info.Name,
                        .StoredName = Stored,
                        .DeletedAt = DateTime.UtcNow.ToString("o"),
                        .Size = Info.Length
                    })
                    Count += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"移入回收站失败：{Path}")
                End Try
            Next
            '超出上限时清理最旧的
            If Entries.Count > MaxItems Then
                Dim Drop = Entries.OrderBy(Function(e) e.DeletedTime).Take(Entries.Count - MaxItems).ToList()
                For Each Old In Drop
                    Try
                        Dim OldPath = Dir & Old.StoredName
                        If FileUtils.Exists(OldPath) Then FileUtils.Delete(OldPath, True)
                    Catch ex As Exception
                        Logger.Warn(ex, $"清理旧回收站文件失败：{Old.StoredName}")
                    End Try
                    Entries.Remove(Old)
                Next
            End If
            SaveEntries(Instance, Entries)
        Catch ex As Exception
            Logger.Warn(ex, "移入回收站时出错")
        End Try
        Return Count
    End Function

    ''' <summary>
    ''' 把一条记录还原到原位置。目标已存在时返回 False，不覆盖。
    ''' </summary>
    Public Function Restore(Instance As McInstance, Entry As RecycleEntry) As Boolean
        If Instance Is Nothing OrElse Entry Is Nothing Then Return False
        Try
            Dim Dir = RecycleDir(Instance)
            Dim Source = Dir & Entry.StoredName
            If Not FileUtils.Exists(Source) Then
                Logger.Warn($"回收站文件已不存在：{Entry.StoredName}")
                Return False
            End If
            Dim Target = Entry.OriginalPath
            If String.IsNullOrWhiteSpace(Target) Then Return False
            If FileUtils.Exists(Target) Then
                Logger.Warn($"还原目标已存在，跳过：{Target}")
                Return False
            End If
            DirectoryUtils.Create(PathUtils.RemoveLastPart(Target))
            FileUtils.Move(Source, Target)
            Dim Entries = ListEntries(Instance)
            Entries.RemoveAll(Function(e) e.StoredName = Entry.StoredName)
            SaveEntries(Instance, Entries)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"还原失败：{Entry.OriginalName}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 从回收站彻底删除一条记录。
    ''' </summary>
    Public Function DeletePermanently(Instance As McInstance, Entry As RecycleEntry) As Boolean
        If Instance Is Nothing OrElse Entry Is Nothing Then Return False
        Try
            Dim Source = RecycleDir(Instance) & Entry.StoredName
            If FileUtils.Exists(Source) Then FileUtils.Delete(Source, True)
            Dim Entries = ListEntries(Instance)
            Entries.RemoveAll(Function(e) e.StoredName = Entry.StoredName)
            SaveEntries(Instance, Entries)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"彻底删除失败：{Entry.OriginalName}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 清空某个实例的回收站。
    ''' </summary>
    Public Function ClearAll(Instance As McInstance) As Integer
        If Instance Is Nothing Then Return 0
        Dim Entries = ListEntries(Instance)
        Dim Count As Integer = 0
        For Each Entry In Entries.ToList()
            If DeletePermanently(Instance, Entry) Then Count += 1
        Next
        Return Count
    End Function

End Module
