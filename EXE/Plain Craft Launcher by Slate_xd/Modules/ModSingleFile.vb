Imports System.Reflection

'单文件打包支持
'把依赖的托管 DLL 作为嵌入资源打进 exe，启动时从内存加载，
'这样交付时只需要一个 Plain Craft Launcher by Slate_xd.exe 文件。
Public Module ModSingleFile

    ''' <summary>
    ''' 需要从嵌入资源中解析的程序集名称（不含扩展名）。
    ''' </summary>
    Private ReadOnly EmbeddedNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "MeloongCore",
        "MeloongCore.Wpf",
        "PCLCS",
        "Newtonsoft.Json",
        "Microsoft.Win32.Registry"
    }

    Private Initialized As Boolean = False
    Private ReadOnly InitLock As New Object()

    ''' <summary>
    ''' 注册程序集解析回调。重复调用安全。
    ''' </summary>
    Public Sub EnsureInitialized()
        If Initialized Then Return
        SyncLock InitLock
            If Initialized Then Return
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf ResolveAssembly
            Initialized = True
        End SyncLock
    End Sub

    ''' <summary>
    ''' 从嵌入资源中解析程序集。找不到时返回 Nothing，交回默认逻辑。
    ''' </summary>
    Private Function ResolveAssembly(sender As Object, Args As ResolveEventArgs) As Assembly
        Try
            Dim Requested As New AssemblyName(Args.Name)
            Dim SimpleName As String = Requested.Name
            If Not EmbeddedNames.Contains(SimpleName) Then Return Nothing
            Return LoadEmbedded(SimpleName)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 从当前程序集的嵌入资源中读取并加载指定名称的 DLL。
    ''' </summary>
    Private Function LoadEmbedded(Name As String) As Assembly
        Dim Asm As Assembly = Assembly.GetExecutingAssembly()
        Dim Candidates As New List(Of String) From {
            $"{Asm.GetName().Name}.{Name}.dll",
            $"{Asm.GetName().Name}.Resources.{Name}.dll",
            $"{Name}.dll"
        }
        For Each ResourceName In Candidates
            Dim Stream As IO.Stream = Asm.GetManifestResourceStream(ResourceName)
            If Stream Is Nothing Then Continue For
            Try
                Using Stream
                    Dim Buffer(CInt(Stream.Length) - 1) As Byte
                    Dim Read As Integer = 0
                    While Read < Buffer.Length
                        Dim Got As Integer = Stream.Read(Buffer, Read, Buffer.Length - Read)
                        If Got <= 0 Then Exit While
                        Read += Got
                    End While
                    Return Assembly.Load(Buffer)
                End Using
            Catch
                '换下一个候选名
            End Try
        Next
        Return Nothing
    End Function

End Module
