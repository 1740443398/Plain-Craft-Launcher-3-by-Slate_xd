''' <summary>
''' 开服器的左侧栏：服务端 / 下载核心 / 控制台 / 状态 / 模板。
''' </summary>
Public Class PageServerLeft

    ''' <summary>当前子页面编号。</summary>
    Public PageID As Integer = 0

    ''' <summary>切换子页面。</summary>
    Public Event PageChanged As EventHandler(Of Integer)

    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemVersions.Check, ItemDownload.Check, ItemConsole.Check, ItemStatus.Check, ItemTemplates.Check
        If sender.Tag Is Nothing Then Return
        PageID = CInt(Val(sender.Tag))
        RaiseEvent PageChanged(Me, PageID)
    End Sub

    ''' <summary>外部设置当前页（不会触发事件）。</summary>
    Public Sub SetPage(ID As Integer)
        PageID = ID
        Dim Items = {ItemVersions, ItemDownload, ItemConsole, ItemStatus, ItemTemplates}
        For Each It In Items
            If CInt(Val(It.Tag)) = ID Then
                It.SetChecked(True, False, False)
                Exit For
            End If
        Next
    End Sub

End Class
