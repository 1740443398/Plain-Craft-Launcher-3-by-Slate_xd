Imports System.Security.Cryptography

'从 PCL 2.x 的注册表数据中导入微软登录信息
'PCL 用 DES 加密存储登录凭据，密钥由「PCL + Identify」的稳定哈希派生
'本模块复刻该算法，使本启动器可以直接接管 PCL 已有的登录状态
Public Module ModPclImport

    'PCL 2.x 常规版使用该注册表位置；开源版会加上 Debug 后缀
    Private Const PclRegFolderNormal As String = "HKEY_CURRENT_USER\Software\PCL"
    Private Const PclRegFolderDebug As String = "HKEY_CURRENT_USER\Software\PCLDebug"

    'PCL 的 DES 初始向量，固定值
    Private ReadOnly PclDesIv As Byte() = Encoding.UTF8.GetBytes("95168702")

    Private ReadOnly PclImportLock As New Object()

    ''' <summary>
    ''' 候选的 Identify 列表。PCL 在不同机器上可能使用不同种子，
    ''' 因此这里把所有可能的形式都试一遍，解密成功即为命中。
    ''' </summary>
    Private Function PclIdentifyCandidates(RegFolder As String) As List(Of String)
        Dim Result As New List(Of String)
        Dim Board As String = ReadPclBoard()
        Dim Raw As String = ReadPclString(RegFolder, "Identify")
        '1) 用注册表里记录的原始种子派生
        If Not String.IsNullOrWhiteSpace(Raw) Then Result.Add(PclIdentifyFrom(Board, Raw))
        '2) 主板不可读时 PCL 会退回 "Unknown"
        Result.Add(PclIdentifyFrom(Board, "Unknown"))
        '3) 无有效硬件标识时 PCL 使用的全零占位
        Result.Add("0000-0000-0000-0000")
        '4) 注册表里可能直接就是明文 Identify
        If Not String.IsNullOrWhiteSpace(Raw) Then Result.Add(Raw)
        Return Result.Where(Function(x) Not String.IsNullOrWhiteSpace(x)).Distinct().ToList()
    End Function

    ''' <summary>
    ''' 读取主板标识；失败时返回 "Unknown"，与 PCL 行为一致。
    ''' </summary>
    Private Function ReadPclBoard() As String
        Try
            Dim Raw As Object = RegistryUtils.TryRead("HKEY_LOCAL_MACHINE\SYSTEM\HardwareConfig", "LastConfig", "Unknown")
            Return If(Raw?.ToString(), "Unknown").ToUpperInvariant().Trim("{"c, "}"c)
        Catch ex As Exception
            Logger.Warn(ex, "读取主板标识失败")
            Return "Unknown"
        End Try
    End Function

    ''' <summary>
    ''' 由主板标识与种子派生出 Identify。
    ''' </summary>
    Private Function PclIdentifyFrom(Board As String, Seed As String) As String
        Try
            Dim H As String = (Board & Seed).GetStableHashCode().ToString("X").EnsureLength("7"c, 16)
            Return $"{H.Substring(4, 4)}-{H.Substring(12, 4)}-{H.Substring(0, 4)}-{H.Substring(8, 4)}"
        Catch ex As Exception
            Logger.Warn(ex, "派生 Identify 失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 读取注册表字符串值；不存在时返回空串。
    ''' </summary>
    Private Function ReadPclString(RegFolder As String, Name As String) As String
        Try
            Dim Raw As Object = RegistryUtils.TryRead(RegFolder, Name, "")
            Return If(Raw?.ToString(), "").Trim()
        Catch ex As Exception
            Logger.Warn(ex, $"读取 PCL 注册表项 {Name} 失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 用 DES 解密 PCL 存储的字符串；失败返回 Nothing。
    ''' </summary>
    Private Function TryPclDecrypt(Encrypted As String, Identify As String) As String
        If String.IsNullOrWhiteSpace(Encrypted) OrElse String.IsNullOrWhiteSpace(Identify) Then Return Nothing
        Try
            Dim KeyText As String = ("PCL" & Identify).GetStableHashCode().ToString().EnsureLength("X"c, 8).Substring(0, 8)
            Dim KeyBytes As Byte() = Encoding.UTF8.GetBytes(KeyText)
            Using Des As New DESCryptoServiceProvider()
                Using Ms As New MemoryStream()
                    Using Cs As New CryptoStream(Ms, Des.CreateDecryptor(KeyBytes, PclDesIv), CryptoStreamMode.Write)
                        Dim Data As Byte() = Convert.FromBase64String(Encrypted)
                        Cs.Write(Data, 0, Data.Length)
                        Cs.FlushFinalBlock()
                    End Using
                    Return Encoding.UTF8.GetString(Ms.ToArray())
                End Using
            End Using
        Catch
            '解密失败是预期情况（种子不匹配），不记录日志避免刷屏
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 判断解密结果是否像正常文本。用于排除「碰巧解密成功但内容是乱码」。
    ''' </summary>
    Private Function IsSaneText(Text As String, Optional MaxLength As Integer = 64) As Boolean
        If String.IsNullOrWhiteSpace(Text) Then Return False
        Text = Text.Trim()
        If Text.Length > MaxLength Then Return False
        Return Text.All(Function(c) Not Char.IsControl(c))
    End Function

    ''' <summary>
    ''' 从某个注册表位置尝试导入。成功返回导入的用户名，失败返回空串。
    ''' </summary>
    Private Function TryImportFrom(RegFolder As String, Silent As Boolean) As String
        '先确认该位置确实存在 PCL 数据
        Dim HasData As Boolean
        Try
            HasData = RegistryUtils.Has(RegFolder, "CacheMsV2OAuthRefresh") OrElse RegistryUtils.Has(RegFolder, "LoginMsJson")
        Catch
            HasData = False
        End Try
        If Not HasData Then Return ""

        '找出能解开 CacheMsV2Name 的 Identify
        Dim EncryptedName As String = ReadPclString(RegFolder, "CacheMsV2Name")
        Dim HitIdentify As String = ""
        Dim UserName As String = ""
        For Each Candidate In PclIdentifyCandidates(RegFolder)
            Dim Plain As String = TryPclDecrypt(EncryptedName, Candidate)
            If Plain IsNot Nothing AndAlso IsSaneText(Plain) Then
                HitIdentify = Candidate
                UserName = Plain.Trim()
                Exit For
            End If
        Next
        If HitIdentify = "" Then
            Logger.Info($"在 {RegFolder} 中未能还原 PCL 的登录数据（种子不匹配或尚未登录）")
            Return ""
        End If

        '用命中的 Identify 解开其余字段
        Dim RefreshToken As String = If(TryPclDecrypt(ReadPclString(RegFolder, "CacheMsV2OAuthRefresh"), HitIdentify), "").Trim()
        If Not IsSaneText(RefreshToken, 4000) Then
            Logger.Info($"PCL 中账号 {UserName} 没有可用的 RefreshToken，无法导入")
            Return ""
        End If
        Dim AccessToken As String = If(TryPclDecrypt(ReadPclString(RegFolder, "CacheMsV2Access"), HitIdentify), "").Trim()
        Dim Uuid As String = If(TryPclDecrypt(ReadPclString(RegFolder, "CacheMsV2Uuid"), HitIdentify), "").Trim()
        Dim ProfileJson As String = If(TryPclDecrypt(ReadPclString(RegFolder, "CacheMsV2ProfileJson"), HitIdentify), "").Trim()

        '写入本启动器自己的设置
        Settings.Set("CacheMsV2OAuthRefresh", RefreshToken)
        Settings.Set("CacheMsV2Access", AccessToken)
        Settings.Set("CacheMsV2Uuid", Uuid)
        Settings.Set("CacheMsV2Name", UserName)
        Settings.Set("CacheMsV2ProfileJson", ProfileJson)
        Settings.Set("LoginType", 5) '5 = 正版登录（Ms）
        Logger.Info($"已从 {RegFolder} 导入微软登录账号：{UserName}")
        If Not Silent Then Hint($"已从 PCL 导入登录账号 {UserName}", HintType.Green)
        Return UserName
    End Function

    ''' <summary>
    ''' 尝试从 PCL 导入微软登录信息。
    ''' 当前已登录时不覆盖；返回导入的用户名，未导入返回空串。
    ''' </summary>
    ''' <param name="Silent">为 True 时不弹提示（用于启动时自动导入）。</param>
    Public Function ImportPclMicrosoftLogin(Optional Silent As Boolean = False) As String
        SyncLock PclImportLock
            Try
                '已经有登录信息就不动它
                If Not String.IsNullOrWhiteSpace(Settings.Get(Of String)("CacheMsV2OAuthRefresh")) Then Return ""
                '依次尝试两个可能的注册表位置
                For Each Folder In {PclRegFolderNormal, PclRegFolderDebug}
                    Dim Name As String = TryImportFrom(Folder, Silent)
                    If Name <> "" Then Return Name
                Next
                Return ""
            Catch ex As Exception
                Logger.Warn(ex, "从 PCL 导入登录信息时出错")
                Return ""
            End Try
        End SyncLock
    End Function

    ''' <summary>
    ''' 供设置页调用的手动导入，带确认与结果反馈。
    ''' </summary>
    Public Sub ImportPclMicrosoftLoginManual()
        If Not String.IsNullOrWhiteSpace(Settings.Get(Of String)("CacheMsV2OAuthRefresh")) Then
            If MyMsgBox("当前已有登录信息。继续导入会覆盖现有登录，是否继续？",
                        "从 PCL 导入", "覆盖并导入", "取消") <> 1 Then
                Return
            End If
        End If
        Dim Name As String = ImportPclMicrosoftLogin(Silent:=True)
        If Name = "" Then
            MyMsgBox("未能从 PCL 导入登录信息。" & vbCrLf &
                     "可能原因：" & vbCrLf &
                     "· PCL 从未在这台电脑上登录过微软账号" & vbCrLf &
                     "· PCL 的登录数据已被清除或重置" & vbCrLf &
                     "· 数据使用了无法识别的加密种子",
                     "从 PCL 导入", "我知道了", IsWarn:=True)
        Else
            MyMsgBox($"已成功导入微软登录账号：{Name}{vbCrLf}重新打开启动页即可生效。",
                     "从 PCL 导入")
        End If
    End Sub

End Module
