'一个万能的自动图片类型转换工具类

Imports System.Drawing.Imaging

Public Class MyBitmap

    ''' <summary>
    ''' 存储的图片
    ''' </summary>
    Public Pic As System.Drawing.Bitmap

    '自动类型转换
    '支持的类：Image，ImageSource，Bitmap，ImageBrush，BitmapSource
    Public Shared Widening Operator CType(Image As System.Drawing.Image) As MyBitmap
        If Image Is Nothing Then Return Nothing
        Return New MyBitmap(Image)
    End Operator
    Public Shared Widening Operator CType(Image As MyBitmap) As System.Drawing.Image
        If Image Is Nothing Then Return Nothing
        Return Image.Pic
    End Operator
    Public Shared Widening Operator CType(Image As ImageSource) As MyBitmap
        If Image Is Nothing Then Return Nothing
        Return New MyBitmap(Image)
    End Operator
    Public Shared Widening Operator CType(Image As MyBitmap) As ImageSource
        If Image Is Nothing Then Return Nothing
        Dim BitmapPic As System.Drawing.Bitmap = Image.Pic
        Dim rect = New System.Drawing.Rectangle(0, 0, BitmapPic.Width, BitmapPic.Height)
        Dim bitmapData = BitmapPic.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim Result = BitmapSource.Create(BitmapPic.Width, BitmapPic.Height, BitmapPic.HorizontalResolution, BitmapPic.VerticalResolution,
                                       PixelFormats.Bgra32, Nothing, bitmapData.Scan0,
                                       rect.Width * rect.Height * 4, bitmapData.Stride)
            Result.Freeze()
            Return Result
        Finally
            BitmapPic.UnlockBits(bitmapData)
        End Try
    End Operator
    Public Shared Widening Operator CType(Image As System.Drawing.Bitmap) As MyBitmap
        If Image Is Nothing Then Return Nothing
        Return New MyBitmap(Image)
    End Operator
    Public Shared Widening Operator CType(Image As MyBitmap) As System.Drawing.Bitmap
        If Image Is Nothing Then Return Nothing
        Return Image.Pic
    End Operator
    Public Shared Widening Operator CType(Image As ImageBrush) As MyBitmap
        If Image Is Nothing Then Return Nothing
        Return New MyBitmap(Image)
    End Operator
    Public Shared Widening Operator CType(Image As MyBitmap) As ImageBrush
        If Image Is Nothing Then Return Nothing
        Return New ImageBrush(New MyBitmap(Image.Pic))
    End Operator

    '构造函数
    Public Sub New()
    End Sub
    Public Sub New(FilePathOrResourceName As String)
        Try
            FilePathOrResourceName = FilePathOrResourceName.Replace("pack://application:,,,/images/", PathImage)
            If FilePathOrResourceName.StartsWithF(PathImage) Then
                '使用缓存
                Static Cache As New Concurrent.ConcurrentDictionary(Of String, MyBitmap)
                If Cache.ContainsKey(FilePathOrResourceName) Then
                    Pic = Cache(FilePathOrResourceName).Pic
                Else
                    Pic = New MyBitmap(CType((New ImageSourceConverter).ConvertFromString(FilePathOrResourceName), ImageSource))
                    Cache.TryAdd(FilePathOrResourceName, Pic)
                End If
            Else
                '使用这种自己接管 FileStream 的方法加载才能解除文件占用
                Using InputStream = FileUtils.ReadAsStream(FilePathOrResourceName)
                    '修复 #9001：原先只检查前两字节是否为 RI（RIFF 头），但 AVI/WAV 也是 RIFF，会误判为 WebP；
                    '且没有校验第 8-11 字节的 "WEBP" 标识。改为完整校验 RIFF....WEBP 格式。
                    Dim IsWebP As Boolean = False
                    If InputStream.Length >= 12 Then
                        Dim Head(11) As Byte
                        InputStream.Read(Head, 0, 12)
                        InputStream.Seek(0, SeekOrigin.Begin)
                        IsWebP = Head(0) = 82 AndAlso Head(1) = 73 AndAlso Head(2) = 70 AndAlso Head(3) = 70 AndAlso 'RIFF
                                 Head(8) = 87 AndAlso Head(9) = 69 AndAlso Head(10) = 66 AndAlso Head(11) = 80 'WEBP
                    End If
                    If IsWebP Then
                        '读取 WebP
                        Dim FileBytes(InputStream.Length - 1) As Byte
                        InputStream.Read(FileBytes, 0, FileBytes.Length)
                        Try
                            Pic = WebPDecoder.DecodeFromBytes(FileBytes) '将代码隔离在另外一个类中，这样只要不走进这个分支就不会加载 Imazen.WebP.dll
                        Catch ex As Exception
                            '动图 WebP（如 AE2 Logo）或 SimpleDecoder 不支持的特性：
                            '退化为用 WPF 解码第一帧，避免整个图标加载失败变成占位图（#9001）
                            Try
                                Using MS As New MemoryStream(FileBytes)
                                    Dim Decoder As BitmapDecoder = BitmapDecoder.Create(MS, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
                                    Pic = New MyBitmap(Decoder.Frames(0))
                                End Using
                                Logger.Info("WebP 动图或不支持的变体，已回退为静态首帧")
                            Catch ex2 As Exception
                                Logger.Warn(ex2, "WebP 解码失败，使用占位图")
                                Pic = New System.Drawing.Bitmap(1, 1)
                            End Try
                        End Try
                    Else
                        Pic = New System.Drawing.Bitmap(InputStream)
                    End If
                End Using
            End If
        Catch ex As Exception
            Pic = My.Application.TryFindResource(FilePathOrResourceName)
            If Pic Is Nothing Then
                Pic = New System.Drawing.Bitmap(1, 1)
                If TypeOf ex Is ArgumentException Then
                    Throw New Exception($"图片格式不支持，或图片文件损坏（{FilePathOrResourceName}）", ex)
                Else
                    Throw New Exception($"加载 MyBitmap 意外失败（{FilePathOrResourceName}）", ex)
                End If
            Else
                Logger.Warn(ex, $"指定类型有误的 MyBitmap 加载（{FilePathOrResourceName}）")
                Exit Try
            End If
        End Try
    End Sub
    Public Sub New(Image As ImageSource)
        Using MS = New MemoryStream()
            Dim Encoder = New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(Image))
            Encoder.Save(MS)
            Pic = New System.Drawing.Bitmap(MS)
        End Using
    End Sub
    Public Sub New(Image As System.Drawing.Image)
        Pic = Image
    End Sub
    Public Sub New(Image As System.Drawing.Bitmap)
        Pic = Image
    End Sub
    Public Sub New(Image As ImageBrush)
        Using MS = New MemoryStream()
            Dim Encoder = New BmpBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(Image.ImageSource))
            Encoder.Save(MS)
            Pic = New System.Drawing.Bitmap(MS)
        End Using
    End Sub
    Private Class WebPDecoder '将代码隔离在另外一个类中，这样只要不调用这个方法就不会加载 Imazen.WebP.dll
        Public Shared Function DecodeFromBytes(Bytes As Byte()) As System.Drawing.Bitmap
            Return New Imazen.WebP.SimpleDecoder().DecodeFromBytes(Bytes, Bytes.Length)
        End Function
    End Class

    ''' <summary>
    ''' 获取裁切的图片，这个方法不会导致原对象改变且会返回一个新的对象。
    ''' </summary>
    Public Function Clip(X As Integer, Y As Integer, Width As Integer, Height As Integer) As MyBitmap
        Dim bmp As New System.Drawing.Bitmap(Width, Height, Pic.PixelFormat)
        bmp.SetResolution(Pic.HorizontalResolution, Pic.VerticalResolution)
        Using g As System.Drawing.Graphics = System.Drawing.Graphics.FromImage(bmp)
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
            g.TranslateTransform(-X, -Y)
            g.DrawImage(Pic, New System.Drawing.Rectangle(0, 0, Pic.Width, Pic.Height))
        End Using
        Return bmp
    End Function

    ''' <summary>
    ''' 获取旋转或翻转后的图片，这个方法不会导致原对象改变且会返回一个新的对象。
    ''' </summary>
    Public Function RotateFlip(Type As System.Drawing.RotateFlipType) As MyBitmap
        Dim bmp As New System.Drawing.Bitmap(Pic)
        bmp.SetResolution(Pic.HorizontalResolution, Pic.VerticalResolution)
        bmp.RotateFlip(Type)
        Return bmp
    End Function

    ''' <summary>
    ''' 将图像保存到文件。
    ''' </summary>
    Public Sub Save(FilePath As String)
        Dim encoder As BitmapEncoder = New PngBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(Me))
        Using fileStream = FileUtils.CreateAsStream(FilePath)
            encoder.Save(fileStream)
        End Using
    End Sub

End Class
