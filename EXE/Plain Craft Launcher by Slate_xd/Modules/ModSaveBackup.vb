Imports System.IO
Imports System.IO.Compression
Imports Newtonsoft.Json

''' <summary>
''' 存档自动备份。
''' <para>
''' 游戏正常退出后，把 <c>saves\</c> 下有变化的世界打包到
''' <c>.pclbackup\</c>，玩家误删或世界损坏时可以还原。
''' </para>
''' <para>
''' 只备份**有变化**的世界：用 level.dat 的大小 + 修改时间做指纹，
''' 与上次备份记录比对，没变化就跳过，避免每次退出都白写一遍大文件。
''' </para>
''' <para>
''' 所有操作失败只写日志，绝不打断游戏退出流程——备份是附赠能力，
''' 不能因为它出错就影响玩家正常使用。
''' </para>
''' </summary>
Public Module ModSaveBackup

    ''' <summary>每个世界默认保留的自动备份份数。</summary>
    Public Const DefaultKeepCount As Integer = 3

    ''' <summary>单次备份的大小上限（字节）。超过则跳过并提示，避免写爆磁盘。</summary>
    Public Const MaxWorldSize As Long = 800L * 1024 * 1024

    ''' <summary>一条备份记录。</summary>
    Public Class BackupEntry
        ''' <summary>世界文件夹名。</summary>
        Public Property WorldName As String = ""
        ''' <summary>备份文件名（在 .pclbackup 下）。</summary>
        Public Property FileName As String = ""
        ''' <summary>备份时间（UTC ISO 8601）。</summary>
        Public Property CreatedAt As String = ""
        ''' <summary>压缩包大小（字节）。</summary>
        Public Property Size As Long = 0
        ''' <summary>备份时 level.dat 的指纹，用于判断之后是否有变化。</summary>
        Public Property LevelStamp As String = ""

        <JsonIgnore>
        Public ReadOnly Property CreatedTime As DateTime
            Get
                Dim T As DateTime
                If DateTime.TryParse(CreatedAt, T) Then Return T
                Return DateTime.MinValue
            End Get
        End Property

        <JsonIgnore>
        Public ReadOnly Property DisplayText As String
            Get
                Dim TimeText As String = If(CreatedTime = DateTime.MinValue, "未知时间", CreatedTime.ToLocalTime().ToString("MM-dd HH:mm"))
                Return $"{WorldName}　{StringUtils.FormatByteSize(Size)}　{TimeText}"
            End Get
        End Property
    End Class

    Private ReadOnly JsonOpt As New JsonSerializerSettings With {.Formatting = Formatting.Indented}

    Private Function BackupDir(Instance As McInstance) As String
        If Instance Is Nothing Then Return Nothing
        Return Instance.PathIndie & ".pclbackup\"
    End Function

    Private Function MetaFile(Instance As McInstance) As String
        Dim Dir = BackupDir(Instance)
        If Dir Is Nothing Then Return Nothing
        Return Dir & "index.json"
    End Function

    ''' <summary>读取备份索引。损坏时返回空列表。</summary>
    Public Function ListBackups(Instance As McInstance) As List(Of BackupEntry)
        Dim Result As New List(Of BackupEntry)
        Try
            Dim Meta = MetaFile(Instance)
            If Meta Is Nothing OrElse Not FileUtils.Exists(Meta) Then Return Result
            Dim Raw As String = FileUtils.ReadAsString(Meta)
            If String.IsNullOrWhiteSpace(Raw) Then Return Result
            Dim Parsed = JsonConvert.DeserializeObject(Of List(Of BackupEntry))(Raw)
            If Parsed IsNot Nothing Then Result = Parsed
            Dim Dir = BackupDir(Instance)
            Result = Result.Where(Function(e) e IsNot Nothing AndAlso
                                              Not String.IsNullOrWhiteSpace(e.FileName) AndAlso
                                              FileUtils.Exists(Dir & e.FileName)).ToList()
        Catch ex As Exception
            Logger.Warn(ex, "读取备份索引失败，按空处理")
            Return New List(Of BackupEntry)
        End Try
        Return Result
    End Function

    Private Sub SaveBackups(Instance As McInstance, Entries As List(Of BackupEntry))
        Try
            Dim Meta = MetaFile(Instance)
            If Meta Is Nothing Then Return
            DirectoryUtils.Create(BackupDir(Instance))
            FileUtils.Write(Meta, JsonConvert.SerializeObject(Entries, JsonOpt), Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "保存备份索引失败")
        End Try
    End Sub

    ''' <summary>
    ''' 计算世界的指纹：level.dat 的大小与修改时间。取不到时返回空串（视为未知）。
    ''' </summary>
    Private Function WorldStamp(WorldDir As String) As String
        Try
            Dim LevelDat = WorldDir & "level.dat"
            If Not FileUtils.Exists(LevelDat) Then Return ""
            Dim Info As New FileInfo(LevelDat)
            Return $"{Info.Length}@{Info.LastWriteTimeUtc.Ticks}"
        Catch ex As Exception
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 游戏退出后调用：为有变化的世界创建备份。
    ''' </summary>
    ''' <param name="Instance">当前实例。</param>
    ''' <param name="KeepCount">每个世界保留的份数。</param>
    ''' <returns>实际创建的备份数。</returns>
    Public Function BackupChangedWorlds(Instance As McInstance, Optional KeepCount As Integer = DefaultKeepCount) As Integer
        If Instance Is Nothing Then Return 0
        Try
            Dim SavesDir = Instance.PathIndie & "saves\"
            If Not DirectoryUtils.Exists(SavesDir) Then Return 0

            Dim Dir = BackupDir(Instance)
            DirectoryUtils.Create(Dir)
            Dim Entries = ListBackups(Instance)
            Dim Created As Integer = 0

            For Each WorldDir In Directory.GetDirectories(SavesDir)
                Try
                    Dim WorldName = PathUtils.GetLastPart(WorldDir)
                    '跳过明显不是世界的目录
                    If Not FileUtils.Exists(WorldDir & "\level.dat") Then Continue For

                    Dim Stamp = WorldStamp(WorldDir)
                    If String.IsNullOrEmpty(Stamp) Then Continue For

                    '与最近一次该世界的备份比对，没变化就跳过
                    Dim Last = Entries.Where(Function(e) e.WorldName = WorldName).
                                       OrderByDescending(Function(e) e.CreatedTime).FirstOrDefault()
                    If Last IsNot Nothing AndAlso Last.LevelStamp = Stamp Then Continue For

                    '体积过大时跳过，避免写爆磁盘
                    Dim TotalSize As Long = 0
                    Try
                        TotalSize = New DirectoryInfo(WorldDir).EnumerateFiles("*", SearchOption.AllDirectories).
                                                                  Sum(Function(f) f.Length)
                    Catch ex As Exception
                        Logger.Warn(ex, $"统计世界大小时失败，跳过备份：{WorldName}")
                        Continue For
                    End Try
                    If TotalSize > MaxWorldSize Then
                        Logger.Warn($"世界 {WorldName} 超过 {StringUtils.FormatByteSize(MaxWorldSize)}，跳过自动备份")
                        Continue For
                    End If

                    '写压缩包
                    Dim SafeName = String.Join("_", WorldName.Split(Path.GetInvalidFileNameChars()))
                    Dim FileName = $"{SafeName}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip"
                    Dim Target = Dir & FileName
                    Try
                        If FileUtils.Exists(Target) Then FileUtils.Delete(Target, True)
                        ZipFile.CreateFromDirectory(WorldDir, Target, CompressionLevel.Optimal, False)
                    Catch ex As Exception
                        Logger.Warn(ex, $"打包世界失败，跳过：{WorldName}")
                        Continue For
                    End Try

                    Entries.Add(New BackupEntry With {
                        .WorldName = WorldName,
                        .FileName = FileName,
                        .CreatedAt = DateTime.UtcNow.ToString("o"),
                        .Size = If(FileUtils.Exists(Target), New FileInfo(Target).Length, 0L),
                        .LevelStamp = Stamp
                    })
                    Created += 1

                    '清理超出份数的旧备份
                    Dim SameWorld = Entries.Where(Function(e) e.WorldName = WorldName).
                                            OrderByDescending(Function(e) e.CreatedTime).ToList()
                    If SameWorld.Count > KeepCount Then
                        For Each Old In SameWorld.Skip(KeepCount)
                            Try
                                Dim OldPath = Dir & Old.FileName
                                If FileUtils.Exists(OldPath) Then FileUtils.Delete(OldPath, True)
                            Catch ex As Exception
                                Logger.Warn(ex, $"清理旧备份失败：{Old.FileName}")
                            End Try
                            Entries.Remove(Old)
                        Next
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "处理单个世界备份时出错")
                End Try
            Next

            If Created > 0 Then SaveBackups(Instance, Entries)
            Return Created
        Catch ex As Exception
            Logger.Warn(ex, "自动备份存档失败")
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 把一份备份解压到指定目录。目标已存在时不覆盖。
    ''' </summary>
    Public Function RestoreBackup(Instance As McInstance, Entry As BackupEntry, TargetDir As String) As Boolean
        If Instance Is Nothing OrElse Entry Is Nothing OrElse String.IsNullOrWhiteSpace(TargetDir) Then Return False
        Try
            Dim Source = BackupDir(Instance) & Entry.FileName
            If Not FileUtils.Exists(Source) Then
                Logger.Warn($"备份文件不存在：{Entry.FileName}")
                Return False
            End If
            If DirectoryUtils.Exists(TargetDir) AndAlso Directory.GetFileSystemEntries(TargetDir).Length > 0 Then
                Logger.Warn($"还原目标非空，拒绝覆盖：{TargetDir}")
                Return False
            End If
            DirectoryUtils.Create(TargetDir)
            ZipFile.ExtractToDirectory(Source, TargetDir)
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"还原备份失败：{Entry.FileName}")
            Return False
        End Try
    End Function

End Module
