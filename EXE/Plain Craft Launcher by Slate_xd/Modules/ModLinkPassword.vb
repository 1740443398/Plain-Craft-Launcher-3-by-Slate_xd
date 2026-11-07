Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 联机房间密码。
''' <para>
''' <b>为什么需要它</b>：Terracotta 的联机靠「邀请码」识别房间，但邀请码一旦泄露
''' （截图、群聊转发、剪贴板残留）任何人都能进来。密码是第二道门。
''' </para>
''' <para>
''' <b>为什么不用明文比较</b>：密码会出现在内存和日志边上，明文比较容易在日志里
''' 留下痕迹。这里用 PBKDF2 派生后比较哈希，日志里只出现「校验通过/失败」。
''' </para>
''' <para>
''' <b>密码存在哪</b>：只存在于房主的进程内存与房间状态里，不写磁盘，
''' 房间结束即销毁。客人需要密码才能完成握手。
''' </para>
''' </summary>
Public Module ModLinkPassword

    ''' <summary>密码最短长度。太短起不到保护作用。</summary>
    Public Const MinLength As Integer = 4
    ''' <summary>密码最长长度。避免有人贴一整段文本进来。</summary>
    Public Const MaxLength As Integer = 32
    ''' <summary>PBKDF2 迭代次数。房主只在握手时算一次，客人也一样，可以给高一点。</summary>
    Private Const Iterations As Integer = 50000
    Private Const SaltSize As Integer = 16
    Private Const HashSize As Integer = 32

    ''' <summary>
    ''' 生成一份可校验的密码记录。返回 (salt, hash) 的 Base64 组合。
    ''' </summary>
    Public Function CreateVerifier(Password As String) As String
        If Not IsValid(Password) Then Throw New ArgumentException("密码长度不合法")
        Dim Salt(SaltSize - 1) As Byte
        Using Rng = RandomNumberGenerator.Create()
            Rng.GetBytes(Salt)
        End Using
        Dim Hash = Derive(Password, Salt)
        '格式：salt.hash（都是 Base64）
        Return Convert.ToBase64String(Salt) & "." & Convert.ToBase64String(Hash)
    End Function

    ''' <summary>
    ''' 校验密码是否匹配。
    ''' </summary>
    Public Function Verify(Password As String, Verifier As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(Verifier) Then Return False
            Dim Parts = Verifier.Split("."c)
            If Parts.Length <> 2 Then Return False
            Dim Salt = Convert.FromBase64String(Parts(0))
            Dim Expected = Convert.FromBase64String(Parts(1))
            Dim Actual = Derive(Password, Salt)
            Return FixedTimeEquals(Actual, Expected)
        Catch ex As Exception
            Logger.Warn(ex, "校验房间密码时出错")
            Return False
        End Try
    End Function

    ''' <summary>密码是否合法。</summary>
    Public Function IsValid(Password As String) As Boolean
        If String.IsNullOrEmpty(Password) Then Return False
        Dim Len = Password.Length
        Return Len >= MinLength AndAlso Len <= MaxLength
    End Function

    ''' <summary>给用户看的密码规则说明。</summary>
    Public ReadOnly Property RuleHint As String
        Get
            Return $"{MinLength}~{MaxLength} 个字符。留空则不设密码，任何人拿到邀请码都能进。"
        End Get
    End Property

    ''' <summary>
    ''' 生成一个随机密码，供「随机」按钮使用。
    ''' 去掉了容易混淆的 0/O/1/l/I。
    ''' </summary>
    Public Function GenerateRandom(Optional Length As Integer = 6) As String
        Const Chars = "abcdefghjkmnpqrstuvwxyzACDEFGHJKLMNPQRSTUVWXYZ23456789"
        Dim Bytes(Length - 1) As Byte
        Using Rng = RandomNumberGenerator.Create()
            Rng.GetBytes(Bytes)
        End Using
        Dim Sb As New StringBuilder(Length)
        For Each B In Bytes
            Sb.Append(Chars(B Mod Chars.Length))
        Next
        Return Sb.ToString()
    End Function

    Private Function Derive(Password As String, Salt As Byte()) As Byte()
        Using Kdf As New Rfc2898DeriveBytes(Password, Salt, Iterations)
            Return Kdf.GetBytes(HashSize)
        End Using
    End Function

    ''' <summary>
    ''' 常量时间比较，避免通过响应时间推测密码。
    ''' </summary>
    Private Function FixedTimeEquals(A As Byte(), B As Byte()) As Boolean
        If A Is Nothing OrElse B Is Nothing Then Return False
        If A.Length <> B.Length Then Return False
        Dim Diff As Integer = 0
        For i = 0 To A.Length - 1
            Diff = Diff Or (A(i) Xor B(i))
        Next
        Return Diff = 0
    End Function

    ''' <summary>
    ''' 房主侧的房间状态：密码校验器 + 是否公开到大厅。
    ''' </summary>
    Public Class RoomState
        Public Property Verifier As String = ""
        Public Property IsPublic As Boolean = False
        Public Property InviteCode As String = ""
        Public Property PlayerName As String = ""
        Public Property CreatedAt As DateTime = DateTime.Now

        Public ReadOnly Property HasPassword As Boolean
            Get
                Return Not String.IsNullOrEmpty(Verifier)
            End Get
        End Property

        ''' <summary>用密码尝试进入房间。</summary>
        Public Function TryEnter(Password As String) As Boolean
            If Not HasPassword Then Return True
            Return Verify(Password, Verifier)
        End Function
    End Class

    ''' <summary>当前房间状态。房主才有；客人侧为 Nothing。</summary>
    Public Property CurrentRoom As RoomState = Nothing

    ''' <summary>创建房间状态并写入密码（空密码表示不设防）。</summary>
    Public Function CreateRoom(Password As String, IsPublic As Boolean, PlayerName As String) As RoomState
        Dim State As New RoomState With {
            .IsPublic = IsPublic,
            .PlayerName = PlayerName
        }
        If Not String.IsNullOrWhiteSpace(Password) Then
            State.Verifier = CreateVerifier(Password)
        End If
        CurrentRoom = State
        Logger.Info($"房间已创建：公开={IsPublic}，密码={(If(State.HasPassword, "已设置", "无"))}")
        Return State
    End Function

    ''' <summary>清理房间状态（房间结束时调用）。</summary>
    Public Sub ClearRoom()
        CurrentRoom = Nothing
    End Sub

End Module
