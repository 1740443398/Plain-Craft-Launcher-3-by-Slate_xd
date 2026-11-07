Imports System.IO
Imports System.IO.Compression
Imports System.Text

''' <summary>
''' 极简 tar.gz 解压。
''' <para>
''' .NET Framework 4.8 没有 <c>System.Formats.Tar</c>（那是 .NET 7+ 才有的），
''' 但 tar 格式本身很简单：每个文件一个 512 字节头 + 内容，按 512 对齐。
''' 这里只实现解压包需要的部分：普通文件与目录，足够处理 Terracotta 的发布包。
''' </para>
''' <para>
''' **安全**：所有条目路径都会被校验，拒绝绝对路径与 <c>..</c>，
''' 防止恶意包把文件写到目标目录之外。
''' </para>
''' </summary>
Public Module ModTarGz

    Private Const BlockSize As Integer = 512

    ''' <summary>
    ''' 把 tar.gz 解压到指定目录。
    ''' </summary>
    ''' <returns>解压出的文件数。</returns>
    Public Function Extract(ArchivePath As String, TargetDir As String) As Integer
        If String.IsNullOrWhiteSpace(ArchivePath) OrElse Not FileUtils.Exists(ArchivePath) Then
            Throw New FileNotFoundException("压缩包不存在", ArchivePath)
        End If
        DirectoryUtils.Create(TargetDir)

        Dim Count As Integer = 0
        '先解 gz 到内存流，再按 tar 解析。
        'Terracotta 包约 8 MB，解压后约 20 MB，放内存没问题。
        Using Gz As New GZipStream(File.OpenRead(ArchivePath), CompressionMode.Decompress)
            Using Buffer As New MemoryStream()
                Gz.CopyTo(Buffer)
                Buffer.Position = 0
                Count = ExtractTarStream(Buffer, TargetDir)
            End Using
        End Using
        Return Count
    End Function

    ''' <summary>从已解压的 tar 数据流里提取文件。</summary>
    Private Function ExtractTarStream(Tar As Stream, TargetDir As String) As Integer
        Dim Header(BlockSize - 1) As Byte
        Dim Count As Integer = 0
        Dim LongName As String = Nothing

        Do
            '读一个头块；读不满说明结束
            If Not ReadFull(Tar, Header, BlockSize) Then Exit Do
            '全零块表示归档结束
            If IsAllZero(Header) Then Exit Do

            Dim Name = ReadString(Header, 0, 100)
            Dim Prefix = ReadString(Header, 345, 155)
            Dim SizeField = ReadString(Header, 124, 12)
            Dim TypeFlag = ChrW(Header(156))

            'UStar 的 prefix 字段用来放长路径的前半段
            If Not String.IsNullOrEmpty(Prefix) Then Name = Prefix.TrimEnd("/"c) & "/" & Name

            Dim Size As Long = ParseOctal(SizeField)

            'GNU 长文件名扩展（TypeFlag 'L'）：下一个块是完整路径
            If TypeFlag = "L"c Then
                Dim NameBuf(Size - 1) As Byte
                If Size > 0 Then ReadFull(Tar, NameBuf, CInt(Size))
                SkipPadding(Tar, Size)
                LongName = Encoding.UTF8.GetString(NameBuf).TrimEnd(ChrW(0))
                Continue Do
            End If

            If TypeFlag = "x"c Then
                'PAX 扩展头，跳过内容；文件名仍用基础头里的
                SkipBytes(Tar, Size)
                Continue Do
            End If

            If LongName IsNot Nothing Then
                Name = LongName
                LongName = Nothing
            End If

            '目录
            If TypeFlag = "5"c Then
                Dim DirPath = SafeCombine(TargetDir, Name)
                If DirPath IsNot Nothing Then DirectoryUtils.Create(DirPath)
                Continue Do
            End If

            '只处理普通文件（'0' 或 NUL）
            If TypeFlag = "0"c OrElse TypeFlag = ChrW(0) Then
                If Size > 0 Then
                    Dim FilePath = SafeCombine(TargetDir, Name)
                    If FilePath IsNot Nothing Then
                        DirectoryUtils.Create(PathUtils.RemoveLastPart(FilePath))
                        Using Out As New FileStream(FilePath, FileMode.Create, FileAccess.Write)
                            CopyExactly(Tar, Out, Size)
                        End Using
                        Count += 1
                    Else
                        '路径不安全：丢弃内容但仍要跳过
                        SkipBytes(Tar, Size)
                    End If
                End If
                SkipPadding(Tar, Size)
                Continue Do
            End If

            '其它类型（符号链接等）直接跳过
            SkipBytes(Tar, Size)
        Loop
        Return Count
    End Function

    ''' <summary>
    ''' 把 tar 内的相对路径安全地拼到目标目录下。
    ''' 路径非法（绝对路径、含 ..、为空）时返回 Nothing。
    ''' </summary>
    Private Function SafeCombine(TargetDir As String, Name As String) As String
        If String.IsNullOrWhiteSpace(Name) Then Return Nothing
        '统一分隔符，去掉前导斜杠
        Dim Clean = Name.Replace("/"c, "\"c).TrimStart("\"c)
        If Clean = "" Then Return Nothing
        '拒绝任何向上跳的段
        For Each Part In Clean.Split("\"c)
            If Part = ".." Then Return Nothing
        Next
        '拒绝盘符
        If Clean.Length >= 2 AndAlso Clean(1) = ":"c Then Return Nothing

        Dim Full = System.IO.Path.Combine(TargetDir, Clean)
        '最后再确认结果仍在目标目录内
        Dim NormTarget = System.IO.Path.GetFullPath(TargetDir).TrimEnd("\"c) & "\"
        Dim NormFull = System.IO.Path.GetFullPath(Full)
        If Not NormFull.StartsWith(NormTarget, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        Return Full
    End Function

    Private Function ReadFull(Stream As Stream, Buffer As Byte(), Count As Integer) As Boolean
        Dim Read As Integer = 0
        While Read < Count
            Dim N = Stream.Read(Buffer, Read, Count - Read)
            If N <= 0 Then Return False
            Read += N
        End While
        Return True
    End Function

    Private Sub CopyExactly(From As Stream, [To] As Stream, Count As Long)
        Dim Buf(8191) As Byte
        Dim Left As Long = Count
        While Left > 0
            Dim Want = CInt(Math.Min(Left, Buf.Length))
            Dim N = From.Read(Buf, 0, Want)
            If N <= 0 Then Exit While
            [To].Write(Buf, 0, N)
            Left -= N
        End While
    End Sub

    Private Sub SkipBytes(Stream As Stream, Count As Long)
        If Count <= 0 Then Return
        SkipPadding(Stream, Count)
    End Sub

    ''' <summary>跳过内容并补齐到 512 字节边界。</summary>
    Private Sub SkipPadding(Stream As Stream, Size As Long)
        If Size <= 0 Then Return
        Dim Padded = ((Size + BlockSize - 1) \ BlockSize) * BlockSize
        Dim Buf(8191) As Byte
        Dim Left As Long = Padded
        While Left > 0
            Dim Want = CInt(Math.Min(Left, Buf.Length))
            Dim N = Stream.Read(Buf, 0, Want)
            If N <= 0 Then Exit While
            Left -= N
        End While
    End Sub

    Private Function IsAllZero(Buffer As Byte()) As Boolean
        For Each B In Buffer
            If B <> 0 Then Return False
        Next
        Return True
    End Function

    ''' <summary>读 NUL 结尾的字符串字段。</summary>
    Private Function ReadString(Buffer As Byte(), Offset As Integer, Length As Integer) As String
        Dim EndPos = Offset
        While EndPos < Offset + Length AndAlso Buffer(EndPos) <> 0
            EndPos += 1
        End While
        If EndPos <= Offset Then Return ""
        Return Encoding.UTF8.GetString(Buffer, Offset, EndPos - Offset)
    End Function

    ''' <summary>解析八进制数字字段。tar 里也可能用「base-256」，这里只处理八进制。</summary>
    Private Function ParseOctal(Field As String) As Long
        Dim Clean = Field.Trim(ChrW(0), " "c)
        If Clean = "" Then Return 0
        '个别实现会在高位设 0x80 表示 base-256，这里保守地返回 0
        Dim Result As Long = 0
        For Each C In Clean
            If C < "0"c OrElse C > "7"c Then Continue For
            Result = Result * 8 + (AscW(C) - 48)
        Next
        Return Result
    End Function

End Module
