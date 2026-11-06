Public Class PageOtherAbout

    Private Shadows IsLoaded As Boolean = False
    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True

        ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", VersionDisplay).Replace("%VERSIONCODE%", VersionCode).Replace("%BRANCH%", CInt(BuildType))
        If BuildType = BuildTypes.Debug Then
            BtnDonateDonate.Visibility = Visibility.Collapsed
            BtnDonateOutput.Visibility = Visibility.Visible
        End If
        RefreshDeveloperAvatar()
        LoadDonorList()

    End Sub

    ''' <summary>
    ''' 从服务端拉取赞助者名单并渲染。失败时显示提示文字。
    ''' </summary>
    Private Sub LoadDonorList()
        Try
            If PanDonorList Is Nothing Then Return
            RunInThread(
            Sub()
                Try
                    Dim Donors As List(Of ModUpdate.DonorEntry) = ModUpdate.FetchDonors()
                    RunInUi(
                    Sub()
                        Try
                            RenderDonorList(Donors)
                        Catch ex As Exception
                            Logger.Warn(ex, "渲染赞助者名单失败")
                        End Try
                    End Sub)
                Catch ex As Exception
                    Logger.Warn(ex, "加载赞助者名单失败")
                End Try
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "刷新赞助者名单失败")
        End Try
    End Sub

    ''' <summary>
    ''' 把赞助者名单渲染成 WrapPanel 里的文字片段。
    ''' </summary>
    Private Sub RenderDonorList(Donors As List(Of ModUpdate.DonorEntry))
        Try
            If PanDonorList Is Nothing Then Return
            PanDonorList.Children.Clear()
            If Donors Is Nothing OrElse Donors.Count = 0 Then
                PanDonorList.Children.Add(New TextBlock With {
                    .Text = "暂时还没有赞助记录，感谢每一位支持者的心意！",
                    .Opacity = 0.6,
                    .Margin = New Thickness(0, 1.5, 6, 0)
                })
                Return
            End If
            '383 个名字逐个建 TextBlock 会卡住 UI 线程，改成单个 TextBlock 拼接
            '按需求不显示悬浮详情，所以不设 ToolTip
            Dim Builder As New Text.StringBuilder()
            For Each Donor In Donors
                If Builder.Length > 0 Then Builder.Append("  ")
                Builder.Append(Donor.Name)
            Next
            PanDonorList.Children.Add(New TextBlock With {
                .Text = Builder.ToString(),
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(0, 1.5, 6, 0)
            })
        Catch ex As Exception
            Logger.Warn(ex, "渲染赞助者名单失败")
        End Try
    End Sub

    ''' <summary>
    ''' 运行时从 QQ 拉取开发者头像。失败时保留内置的备用图片。
    ''' </summary>
    Private Sub RefreshDeveloperAvatar()
        Try
            If ImgXiaoDi Is Nothing Then Return
            RunInThread(
            Sub()
                Try
                    Dim Url As String = "https://q1.qlogo.cn/g?b=qq&nk=1740443398&s=100"
                    RunInUi(
                    Sub()
                        Try
                            ImgXiaoDi.Source = Url
                            ImgXiaoDi.EnableCache = True
                        Catch ex As Exception
                            Logger.Warn(ex, "设置开发者头像失败")
                        End Try
                    End Sub)
                Catch ex As Exception
                    Logger.Warn(ex, "拉取开发者头像失败，将使用内置图片")
                End Try
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "刷新开发者头像失败")
        End Try
    End Sub

    Public Shared Sub CopyIdentify() Handles BtnDonateCopy.Click
        ClipboardSet(Identify)
    End Sub
    Private Sub BtnDonateCodeInput_Click() Handles BtnDonateInput.Click
        InputPotatoCode(False)
    End Sub
    Private Sub BtnDonateOutput_Click(sender As Object, e As EventArgs) Handles BtnDonateOutput.Click
        GeneratePotatoCode()
    End Sub
    Private Sub BtnAboutUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAboutUpdate.Click
        UpdateCheckByButton()
    End Sub

End Class
