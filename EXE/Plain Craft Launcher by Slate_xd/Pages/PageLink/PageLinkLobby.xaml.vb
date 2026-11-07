Imports System.Windows.Input
Imports System.Collections.Generic
Imports System.Threading

''' <summary>
''' 联机大厅页面：列出公开房间，点击即可申请加入。
''' </summary>
Public Class PageLinkLobby
    Inherits MyPageRight
    Implements IRefreshable

    Private IsLoading As Boolean = False

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If IsLoading Then Return
        IsLoading = True
        Try
            LabLobbyStatus.Text = "正在获取大厅列表……"
            BtnLobbyRefresh.IsEnabled = False

            Dim T As New Threading.Thread(
                Sub()
                    Dim FetchOk As Boolean = ModLinkLobby.Fetch()
                    RunInUi(
                        Sub()
                            IsLoading = False
                            BtnLobbyRefresh.IsEnabled = True
                            RenderRooms(FetchOk)
                        End Sub)
                End Sub) With {.IsBackground = True}
            T.Start()
        Catch ex As Exception
            IsLoading = False
            BtnLobbyRefresh.IsEnabled = True
            Logger.Error(ex, "刷新大厅失败")
            LabLobbyStatus.Text = $"刷新失败：{ex.Message}"
        End Try
    End Sub

    Private Sub RenderRooms(Ok As Boolean)
        Try
            PanLobbyList.Children.Clear()

            If Not Ok Then
                LabLobbyStatus.Text = $"获取失败：{ModLinkLobby.LastError}"
                Return
            End If

            Dim Rooms = ModLinkLobby.LastRooms
            LabLobbyStatus.Text = If(Rooms.Count = 0,
                "现在没有公开的房间。你可以自己开一个，勾选「公开」就会出现在这里。",
                $"共 {Rooms.Count} 个公开房间，点击任意房间申请加入。")

            PanLobbyEmpty.Visibility = If(Rooms.Count = 0, Visibility.Visible, Visibility.Collapsed)
            If Rooms.Count = 0 Then Return

            For Each R In Rooms
                PanLobbyList.Children.Add(BuildRoomCard(R))
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染大厅列表失败")
        End Try
    End Sub

    Private Function BuildRoomCard(R As ModLinkLobby.LobbyRoom) As FrameworkElement
        Dim Card As New MyCard With {
            .Title = R.DisplayName,
            .Margin = New Thickness(0, 0, 0, 10)
        }
        Card.Cursor = Cursors.Hand

        Dim Panel As New StackPanel With {.Margin = New Thickness(25, 36, 25, 14)}

        Panel.Children.Add(New TextBlock With {
            .Text = R.SubtitleText, .TextWrapping = TextWrapping.Wrap, .FontSize = 12.5,
            .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
        })

        Dim Row As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .Margin = New Thickness(0, 10, 0, 0)
        }
        Row.Children.Add(New TextBlock With {
            .Text = "人数 " & R.PlayersText, .VerticalAlignment = VerticalAlignment.Center,
            .FontSize = 12.5, .Margin = New Thickness(0, 0, 16, 0)
        })
        If R.HasPassword Then
            Row.Children.Add(New TextBlock With {
                .Text = "需要密码", .VerticalAlignment = VerticalAlignment.Center,
                .FontSize = 12.5,
                .Foreground = New SolidColorBrush(Color.FromRgb(&HC0, &H88, &H20))
            })
        End If
        Panel.Children.Add(Row)

        If Not String.IsNullOrWhiteSpace(R.Motd) Then
            Panel.Children.Add(New TextBlock With {
                .Text = R.Motd, .Margin = New Thickness(0, 8, 0, 0),
                .TextWrapping = TextWrapping.Wrap, .FontSize = 12.5
            })
        End If

        Card.Children.Add(Panel)

        AddHandler Card.MouseLeftButtonUp,
            Sub()
                Try
                    '用房间号加入；有密码时提示向房主索要
                    If R.HasPassword Then
                        Hint("这个房间设置了密码，请向房主索要密码后使用邀请码加入", HintType.Blue)
                        PageLinkMain.Join(R.Code)
                    Else
                        PageLinkMain.Join(R.Code)
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "从大厅加入房间失败")
                End Try
            End Sub

        Return Card
    End Function

    Private Sub BtnLobbyRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLobbyRefresh.MouseLeftButtonUp
        Refresh()
    End Sub

    Private Sub BtnLobbyBack_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLobbyBack.MouseLeftButtonUp
        Try
            FrmMain.PageBack()
        Catch ex As Exception
            Logger.Warn(ex, "返回失败")
        End Try
    End Sub

End Class
