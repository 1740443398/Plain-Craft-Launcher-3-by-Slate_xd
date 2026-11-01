'启动时的静默数据同步
'从自建服务端预拉更新信息、公告、赞助者名单，
'失败完全不打扰用户（不弹窗、不提示条），但每一次结果都写日志。
'调试模式下额外写 Trace 级明细，方便排查。
Public Module ModSync

    ''' <summary>最近一次同步是否成功（供界面判断，不影响功能）。</summary>
    Public ReadOnly SyncState As New SyncStateData()

    Public Class SyncStateData
        Public LastCheck As Date = Date.MinValue
        Public LastSuccess As Date = Date.MinValue
        Public LastError As String = ""
        Public HasUpdate As Boolean = False
        Public NoticeCount As Integer = 0
        Public DonorCount As Integer = 0
        Public Completed As Boolean = False
    End Class

    Private ReadOnly SyncingGate As New Object()
    Private Syncing As Boolean = False

    ''' <summary>
    ''' 启动时后台静默同步。必须异步调用，绝不阻塞界面。
    ''' </summary>
    Public Sub StartSilentSync()
        SyncLock SyncingGate
            If Syncing Then
                Logger.Trace("静默同步：已有任务在跑，跳过本次")
                Return
            End If
            Syncing = True
        End SyncLock

        Logger.Info("静默同步：开始")
        If ModeDebug Then Logger.Trace("静默同步：调试模式，将输出完整明细")

        RunInThread(
        Sub()
            Try
                DoSync()
            Catch ex As Exception
                '兜底：任何未预期异常都不许冒泡到界面
                SyncState.LastError = ex.Message
                Logger.Warn(ex, "静默同步：未预期的异常（已忽略）")
            Finally
                SyncLock SyncingGate
                    Syncing = False
                End SyncLock
                SyncState.Completed = True
                SyncState.LastCheck = Date.Now
                Logger.Info($"静默同步：结束，成功={SyncState.LastSuccess <> Date.MinValue}")
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 实际执行同步：更新检查 + 公告 + 赞助者。
    ''' 每一项独立 try，互不影响。
    ''' </summary>
    Private Sub DoSync()
        Dim SuccessCount As Integer = 0
        Dim FailCount As Integer = 0

        '1. 检查更新（同时带回公告）
        Try
            Logger.Trace($"静默同步：请求检查更新，当前版本码 {VersionCode}")
            Dim Result As ModUpdate.UpdateCheckResult = ModUpdate.CheckUpdate()
            If Result Is Nothing Then
                FailCount += 1
                SyncState.LastError = "检查更新返回空"
                Logger.Warn("静默同步：检查更新返回空结果")
            Else
                SuccessCount += 1
                SyncState.HasUpdate = Result.HasUpdate
                SyncState.NoticeCount = If(Result.Notices, New List(Of ModUpdate.UpdateNotice)).Count
                SyncState.LastSuccess = Date.Now
                Logger.Info($"静默同步：检查更新成功，有新版本={Result.HasUpdate}，最新={If(Result.LatestVersion, "无")}，公告 {SyncState.NoticeCount} 条")
                If ModeDebug Then
                    For Each N In Result.Notices
                        Logger.Trace($"  公告：{N.Title}")
                    Next
                End If
            End If
        Catch ex As Exception
            FailCount += 1
            SyncState.LastError = ex.Message
            Logger.Warn(ex, "静默同步：检查更新失败（已忽略）")
        End Try

        '2. 赞助者名单（失败不影响任何功能，只记录）
        Try
            Logger.Trace("静默同步：请求赞助者名单")
            Dim Donors As List(Of ModUpdate.DonorEntry) = ModUpdate.FetchDonors()
            If Donors Is Nothing OrElse Donors.Count = 0 Then
                Logger.Warn("静默同步：赞助者名单为空")
            Else
                SuccessCount += 1
                SyncState.DonorCount = Donors.Count
                Logger.Info($"静默同步：赞助者名单成功，共 {Donors.Count} 位")
                If ModeDebug AndAlso Donors.Count > 0 Then
                    Logger.Trace($"  首位：{Donors(0).Name}")
                End If
            End If
        Catch ex As Exception
            FailCount += 1
            Logger.Warn(ex, "静默同步：赞助者名单失败（已忽略）")
        End Try

        '3. 温和预热一言缓存池（顺带把今日一言提前备好）
        Try
            Logger.Trace("静默同步：预热一言缓存")
            ModHitokoto.WarmUp()
        Catch ex As Exception
            Logger.Warn(ex, "静默同步：一言预热失败（已忽略）")
        End Try

        Logger.Info($"静默同步：完成，成功 {SuccessCount} 项，失败 {FailCount} 项")
    End Sub

End Module
