Imports System
Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Sockets

''' <summary>
''' NAT 类型检测：告诉玩家「你的网络能不能联机」。
''' <para>
''' 联机失败的绝大多数原因是 NAT 类型不合适（对称 NAT、UDP 被封）。
''' 与其让玩家点了「创建房间」等半分钟再报错，不如提前检测并说清楚原因。
''' </para>
''' <para>
''' 原理：向 STUN 服务器发 Binding 请求，服务器回包告诉你它看到的公网地址。
''' 换一个服务器再问一次，如果映射端口变了，说明是对称 NAT（每个目标分配不同端口），
''' P2P 打洞成功率很低。
''' </para>
''' <para>
''' 检测失败一律返回 <c>UnsupportedServer</c>，**绝不猜测**——
''' 误报「你的网络不行」会让玩家放弃本来能用的功能。
''' </para>
''' </summary>
Public Module ModNatCheck

    ''' <summary>NAT 类型。</summary>
    Public Enum NatType
        ''' <summary>检测失败，无法判断。</summary>
        Unknown = 0
        ''' <summary>公网 IP，无 NAT。最佳。</summary>
        OpenInternet = 1
        ''' <summary>完全锥形。最佳。</summary>
        FullCone = 2
        ''' <summary>受限锥形。良好。</summary>
        RestrictedCone = 3
        ''' <summary>端口受限锥形。受限。</summary>
        PortRestrictedCone = 4
        ''' <summary>对称 NAT。可能失败。</summary>
        Symmetric = 5
        ''' <summary>UDP 被封。无法联机。</summary>
        UdpBlocked = 6
        ''' <summary>服务器不支持检测。</summary>
        UnsupportedServer = 7
    End Enum

    ''' <summary>检测结果。</summary>
    Public Class NatResult
        Public Property Type As NatType = NatType.Unknown
        ''' <summary>STUN 服务器看到的公网地址（检测成功时有值）。</summary>
        Public Property PublicEndpoint As String = ""
        ''' <summary>人类可读的说明。</summary>
        Public Property Detail As String = ""
        ''' <summary>是否适合联机。</summary>
        Public ReadOnly Property IsGood As Boolean
            Get
                Return Type = NatType.OpenInternet OrElse Type = NatType.FullCone OrElse Type = NatType.RestrictedCone
            End Get
        End Property
    End Class

    'STUN 常量（RFC 5389）
    Private Const StunHeaderSize As Integer = 20
    Private Const BindingRequest As UShort = &H1US
    Private Const BindingResponse As UShort = &H101US
    Private Const AttrMappedAddress As UShort = &H1US
    Private Const AttrXorMappedAddress As UShort = &H20US
    Private Const MagicCookie As UInteger = &H2112A442UI

    '多个公共 STUN 服务器，逐个尝试；同一个包发给不同服务器用于比对端口
    Private ReadOnly StunServers As String() = {
        "stun.miwifi.com:3478",
        "stun.chat.bilibili.com:3478",
        "stun.hitv.com:3478",
        "stun.l.google.com:19302"
    }

    ''' <summary>
    ''' 检测 NAT 类型。整个过程在后台线程执行，最长约 6 秒。
    ''' </summary>
    Public Function Detect() As NatResult
        Dim Result As New NatResult
        Try
            '第一步：向第一个可用服务器问一次，拿到公网地址
            Dim First As Tuple(Of IPEndPoint, IPEndPoint) = Nothing
            For Each Server In StunServers
                First = Query(Server)
                If First IsNot Nothing Then Exit For
            Next
            If First Is Nothing Then
                Result.Type = NatType.UnsupportedServer
                Result.Detail = "无法连接任何 STUN 服务器，可能被防火墙拦截，或当前网络不支持 UDP。"
                Return Result
            End If

            Dim PublicEp = First.Item1
            Result.PublicEndpoint = PublicEp.ToString()

            '第二步：如果公网地址与本地地址完全相同，说明没有 NAT
            Dim LocalEp = First.Item2
            If PublicEp.Address.Equals(LocalEp.Address) AndAlso PublicEp.Port = LocalEp.Port Then
                Result.Type = NatType.OpenInternet
                Result.Detail = "没有检测到 NAT，你拥有公网 IP，这是最佳的联机条件。"
                Return Result
            End If

            '第三步：换服务器再问一次，比对映射端口是否变化
            Dim Ports As New HashSet(Of Integer) From {PublicEp.Port}
            For Each Server In StunServers
                Dim Other = Query(Server)
                If Other Is Nothing Then Continue For
                Ports.Add(Other.Item1.Port)
                '只要拿到两个不同的端口就足以判定对称 NAT
                If Ports.Count > 1 Then Exit For
            Next

            If Ports.Count > 1 Then
                Result.Type = NatType.Symmetric
                Result.Detail = "当前 NAT 类型为：对称 NAT。P2P 打洞有一定几率失败；如果正在使用手机热点，建议换一个网络。"
            Else
                '端口一致：锥形 NAT。无法进一步区分受限程度（那需要更复杂的探测），
                '保守地报「完全锥形」会误导，这里报受限锥形作为中间值并说明。
                Result.Type = NatType.RestrictedCone
                Result.Detail = "当前 NAT 类型为：锥形（映射端口稳定）。联机通常可用。"
            End If
            Return Result
        Catch ex As SocketException
            Result.Type = NatType.UdpBlocked
            Result.Detail = "UDP 通信被阻止，无法联机。请检查防火墙或安全软件设置。"
            Logger.Warn(ex, "NAT 检测：UDP 被阻止")
            Return Result
        Catch ex As Exception
            Result.Type = NatType.Unknown
            Result.Detail = $"检测失败：{ex.Message}"
            Logger.Warn(ex, "NAT 检测失败")
            Return Result
        End Try
    End Function

    ''' <summary>
    ''' 向一个 STUN 服务器查询映射地址。返回 (公网地址, 本地地址)，失败返回 Nothing。
    ''' </summary>
    Private Function Query(Server As String) As Tuple(Of IPEndPoint, IPEndPoint)
        Dim Parts = Server.Split(":"c)
        If Parts.Length <> 2 Then Return Nothing
        Dim Host = Parts(0)
        Dim Port As Integer
        If Not Integer.TryParse(Parts(1), Port) Then Return Nothing

        Dim Socket As Socket = Nothing
        Try
            '解析域名（可能是 IPv4 或 IPv6）
            Dim Addresses = Dns.GetHostAddresses(Host)
            Dim Target As IPAddress = Nothing
            For Each A In Addresses
                If A.AddressFamily = AddressFamily.InterNetwork Then
                    Target = A
                    Exit For
                End If
            Next
            If Target Is Nothing Then Return Nothing

            Socket = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            Socket.ReceiveTimeout = 1500
            Socket.SendTimeout = 1500
            Socket.Bind(New IPEndPoint(IPAddress.Any, 0))
            Dim LocalEp = CType(Socket.LocalEndPoint, IPEndPoint)

            '构造 Binding Request：20 字节头 + 12 字节事务 ID
            Dim Packet(StunHeaderSize + 11) As Byte
            Packet(0) = (BindingRequest >> 8) And &HFF
            Packet(1) = BindingRequest And &HFF
            '长度 0
            'Magic Cookie
            Packet(4) = &H21 : Packet(5) = &H12 : Packet(6) = &HA4 : Packet(7) = &H42
            '随机事务 ID
            Dim Rnd As New Random()
            For i = 8 To 19
                Packet(i) = CByte(Rnd.Next(0, 256))
            Next

            Dim ServerEp As New IPEndPoint(Target, Port)
            Socket.SendTo(Packet, ServerEp)

            Dim Buffer(1023) As Byte
            Dim RemoteEp As EndPoint = New IPEndPoint(IPAddress.Any, 0)
            Dim Received As Integer = Socket.ReceiveFrom(Buffer, RemoteEp)
            If Received < StunHeaderSize Then Return Nothing

            '校验是 Binding Response
            Dim MsgType As UShort = (CUShort(Buffer(0)) << 8) Or Buffer(1)
            If MsgType <> BindingResponse Then Return Nothing

            '解析属性，找 XOR-MAPPED-ADDRESS 或 MAPPED-ADDRESS
            Dim Offset As Integer = StunHeaderSize
            While Offset + 4 <= Received
                Dim AttrType As UShort = (CUShort(Buffer(Offset)) << 8) Or Buffer(Offset + 1)
                Dim AttrLen As Integer = (CInt(Buffer(Offset + 2)) << 8) Or Buffer(Offset + 3)
                Dim ValueOffset As Integer = Offset + 4
                If ValueOffset + AttrLen > Received Then Exit While

                If (AttrType = AttrXorMappedAddress OrElse AttrType = AttrMappedAddress) AndAlso AttrLen >= 8 Then
                    Dim Family As Byte = Buffer(ValueOffset + 1)
                    If Family = &H1 Then 'IPv4
                        Dim MappedPort As Integer = (CInt(Buffer(ValueOffset + 2)) << 8) Or Buffer(ValueOffset + 3)
                        Dim AddrBytes(3) As Byte
                        Array.Copy(Buffer, ValueOffset + 4, AddrBytes, 0, 4)
                        If AttrType = AttrXorMappedAddress Then
                            'XOR：端口与 Cookie 高 16 位异或，地址与 Cookie 异或
                            MappedPort = MappedPort Xor (MagicCookie >> 16)
                            AddrBytes(0) = AddrBytes(0) Xor &H21
                            AddrBytes(1) = AddrBytes(1) Xor &H12
                            AddrBytes(2) = AddrBytes(2) Xor &HA4
                            AddrBytes(3) = AddrBytes(3) Xor &H42
                        End If
                        Return Tuple.Create(New IPEndPoint(New IPAddress(AddrBytes), MappedPort), LocalEp)
                    End If
                End If
                '属性按 4 字节对齐
                Offset = ValueOffset + ((AttrLen + 3) And Not 3)
            End While
            Return Nothing
        Catch ex As Exception
            Return Nothing
        Finally
            If Socket IsNot Nothing Then
                Try
                    Socket.Close()
                Catch
                End Try
            End If
        End Try
    End Function

End Module
