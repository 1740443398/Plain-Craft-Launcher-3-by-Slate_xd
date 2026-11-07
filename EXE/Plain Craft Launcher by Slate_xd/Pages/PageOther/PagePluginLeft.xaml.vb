''' <summary>
''' 插件页的左侧栏：已安装 / 创建插件 / 开发文档。
''' </summary>
Public Class PagePluginLeft

    Public PageID As Integer = 0

    Public Event PageChanged As EventHandler(Of Integer)

    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemInstalled.Check, ItemCreate.Check, ItemStore.Check, ItemDoc.Check
        If sender.Tag Is Nothing Then Return
        PageID = CInt(Val(sender.Tag))
        RaiseEvent PageChanged(Me, PageID)
    End Sub

    Public Sub SetPage(ID As Integer)
        PageID = ID
        Dim Items = {ItemInstalled, ItemCreate, ItemStore, ItemDoc}
        For Each It In Items
            If CInt(Val(It.Tag)) = ID Then
                It.SetChecked(True, False, False)
                Exit For
            End If
        Next
    End Sub

End Class
